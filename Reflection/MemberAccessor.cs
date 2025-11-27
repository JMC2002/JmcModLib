using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace JmcModLib.Reflection
{
    /// <summary>
    /// 字段 / 属性 的统一高性能访问器。
    /// </summary>
    public sealed class MemberAccessor : ReflectionAccessorBase<MemberInfo, MemberAccessor>
    {
        // 用于 Name 查找加速（Type, string）→ MemberInfo
        private static readonly ConcurrentDictionary<(Type, string), MemberInfo?> _lookupCache = new();

        /// <summary>
        /// 可读
        /// </summary>
        public bool CanRead => getter != null;

        /// <summary>
        /// 可写
        /// </summary>
        public bool CanWrite => setter != null;

        /// <summary>
        /// 成员数据类型（不是MemberTypes）
        /// </summary>
        public Type MemberType { get; }

        private readonly Func<object?, object?>? getter;
        private readonly Action<object?, object?>? setter;

        // 强类型委托（非索引器）：
        // 实例成员：Getter=Func<TTarget,TValue>  Setter=Action<TTarget,TValue>
        // 静态成员：Getter=Func<TValue>         Setter=Action<TValue>

        /// <summary>
        /// 强类型委托，当ref/ref-like/索引器/不可读时为空
        /// </summary>
        public Delegate? TypedGetter { get; }

        /// <summary>
        /// 强类型委托，当ref/ref-like/索引器/不可写时为空
        /// </summary>
        public Delegate? TypedSetter { get; }

        // 如果是索引器，这里会持有 index 参数
        private readonly ParameterInfo[]? indexParams;

        // 专门给索引器创建的 getter/setter
        private readonly Func<object?, object?[], object?>? indexGetter;
        private readonly Action<object?, object?, object?[]>? indexSetter;

        private MemberAccessor(MemberInfo member)
             : base(member)
        {
            switch (member)
            {
                case FieldInfo f:
                    MemberType = f.FieldType;
                    IsStatic = f.IsStatic;

                    //readonly
                    if (!f.IsLiteral && f.IsInitOnly)
                    {
                        getter = CreateFieldGetter(f);
                        setter = null;
                        // readonly：仅生成 Getter
                        TypedGetter = CreateTypedFieldGetter(f);
                        TypedSetter = null;
                    }
                    // 如果是 const 字段，直接使用 GetRawConstantValue 获取值
                    else if (f.IsLiteral && !f.IsInitOnly) // const 字段
                    {
                        getter = _ => f.GetRawConstantValue();
                        setter = null; // const 字段没有 setter
                        // const 一定是静态字段：生成 Func<TValue>
                        TypedGetter = CreateTypedFieldGetter(f);
                        TypedSetter = null;
                    }
                    else
                    {
                        getter = CreateFieldGetter(f);
                        setter = CreateFieldSetter(f);
                        // 普通字段：Getter/Setter 都生成
                        TypedGetter = CreateTypedFieldGetter(f);
                        TypedSetter = CreateTypedFieldSetter(f);
                    }

                    break;

                case PropertyInfo p:
                    var indices = p.GetIndexParameters();
                    bool isIndexer = indices.Length > 0;

                    MemberType = p.PropertyType;
                    IsStatic = (p.GetGetMethod(true)?.IsStatic ?? false) ||
                               (p.GetSetMethod(true)?.IsStatic ?? false);

                    if (isIndexer)
                    {
                        // 记录 index 参数
                        indexParams = indices;

                        var getterMethod = p.GetGetMethod(true);
                        if (getterMethod != null)
                            indexGetter = CreateIndexerGetter(p, getterMethod);

                        var setterMethod = p.GetSetMethod(true);
                        if (setterMethod != null)
                            indexSetter = CreateIndexerSetter(p, setterMethod);

                        // 普通 getter/setter 清空
                        getter = null;
                        setter = null;
                        TypedGetter = null;
                        TypedSetter = null;
                    }
                    else
                    {
                        indexParams = null;

                        if (p.CanRead)
                            getter = CreatePropertyGetter(p);
                        if (p.CanWrite)
                            setter = CreatePropertySetter(p);

                        // 生成强类型委托
                        if (p.CanRead)
                            TypedGetter = CreateTypedPropertyGetter(p);
                        if (p.CanWrite)
                            TypedSetter = CreateTypedPropertySetter(p);
                    }

                    break;

                default:
                    throw new ArgumentException($"不支持的成员类型: {member.MemberType}");
            }

        }

        // ======================
        // 强类型委托构造（Expression）
        // ======================
        private static Delegate CreateTypedFieldGetter(FieldInfo f)
        {
            if (f.IsStatic)
            {
                // () => StaticField
                var fieldExpr = System.Linq.Expressions.Expression.Field(null, f);
                var lambda = System.Linq.Expressions.Expression.Lambda(GetFuncType([f.FieldType]), fieldExpr);
                return lambda.Compile();
            }
            else
            {
                // (TTarget obj) => obj.Field
                var objParam = System.Linq.Expressions.Expression.Parameter(f.DeclaringType!, "obj");
                var fieldExpr = System.Linq.Expressions.Expression.Field(objParam, f);
                var lambda = System.Linq.Expressions.Expression.Lambda(GetFuncType([f.DeclaringType!, f.FieldType]), fieldExpr, objParam);
                return lambda.Compile();
            }
        }

        private static Delegate CreateTypedFieldSetter(FieldInfo f)
        {
            if (f.IsStatic)
            {
                // (TValue v) => StaticField = v
                var valParam = System.Linq.Expressions.Expression.Parameter(f.FieldType, "value");
                var assign = System.Linq.Expressions.Expression.Assign(System.Linq.Expressions.Expression.Field(null, f), valParam);
                var lambda = System.Linq.Expressions.Expression.Lambda(GetActionType([f.FieldType]), assign, valParam);
                return lambda.Compile();
            }
            else
            {
                // (TTarget obj, TValue v) => obj.Field = v
                var objParam = System.Linq.Expressions.Expression.Parameter(f.DeclaringType!, "obj");
                var valParam = System.Linq.Expressions.Expression.Parameter(f.FieldType, "value");
                var assign = System.Linq.Expressions.Expression.Assign(System.Linq.Expressions.Expression.Field(objParam, f), valParam);
                var lambda = System.Linq.Expressions.Expression.Lambda(GetActionType([f.DeclaringType!, f.FieldType]), assign, objParam, valParam);
                return lambda.Compile();
            }
        }

        private static Delegate CreateTypedPropertyGetter(PropertyInfo p)
        {
            var m = p.GetGetMethod(true)!;
            if (m.IsStatic)
            {
                // () => get_Prop()
                var call = System.Linq.Expressions.Expression.Call(m);
                var lambda = System.Linq.Expressions.Expression.Lambda(GetFuncType([p.PropertyType]), call);
                return lambda.Compile();
            }
            else
            {
                var objParam = System.Linq.Expressions.Expression.Parameter(p.DeclaringType!, "obj");
                var call = System.Linq.Expressions.Expression.Call(objParam, m);
                var lambda = System.Linq.Expressions.Expression.Lambda(GetFuncType([p.DeclaringType!, p.PropertyType]), call, objParam);
                return lambda.Compile();
            }
        }

        private static Delegate CreateTypedPropertySetter(PropertyInfo p)
        {
            var m = p.GetSetMethod(true)!;
            if (m.IsStatic)
            {
                var valParam = System.Linq.Expressions.Expression.Parameter(p.PropertyType, "value");
                var call = System.Linq.Expressions.Expression.Call(m, valParam);
                var lambda = System.Linq.Expressions.Expression.Lambda(GetActionType([p.PropertyType]), call, valParam);
                return lambda.Compile();
            }
            else
            {
                var objParam = System.Linq.Expressions.Expression.Parameter(p.DeclaringType!, "obj");
                var valParam = System.Linq.Expressions.Expression.Parameter(p.PropertyType, "value");
                var call = System.Linq.Expressions.Expression.Call(objParam, m, valParam);
                var lambda = System.Linq.Expressions.Expression.Lambda(GetActionType([p.DeclaringType!, p.PropertyType]), call, objParam, valParam);
                return lambda.Compile();
            }
        }

        private static Type GetActionType(Type[] types)
        {
            return types.Length switch
            {
                1 => typeof(Action<>).MakeGenericType(types),
                2 => typeof(Action<,>).MakeGenericType(types),
                _ => throw new NotSupportedException("参数过多，无法生成 Action 委托")
            };
        }

        private static Type GetFuncType(Type[] types)
        {
            return types.Length switch
            {
                1 => typeof(Func<>).MakeGenericType(types),
                2 => typeof(Func<,>).MakeGenericType(types),
                _ => throw new NotSupportedException("参数过多，无法生成 Func 委托")
            };
        }

        /// <summary>
        /// 获取值。
        /// </summary>
        /// <param name="target">实例对象，静态则为null</param>
        /// <returns>属性值</returns>
        /// <exception cref="InvalidOperationException">如果是索引器属性或成员不可读</exception>
        /// <exception cref="ArgumentNullException">如果非静态情况下 target 为空</exception>
        public object? GetValue(object? target)
        {
            if (indexParams != null)
                throw new InvalidOperationException($"属性 {Name} 是索引器，请使用 GetValue(target, indexArgs[])");

            // 判断静态与实例的 target 传递
            if (!IsStatic && target == null)
                throw new ArgumentNullException($"对于非静态成员 {Name}，target 不能为空");

            if (getter == null)
                throw new InvalidOperationException($"成员 {Name} 不可读");

            try
            {
                return getter(target);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"获取成员 {Name} 时发生错误", ex);
            }
        }

        /// <summary>
        /// 设置值。
        /// </summary>
        /// <param name="target"> 实例对象，静态则为null </param>
        /// <param name="value"> 待设置的值 </param>
        /// <exception cref="InvalidOperationException">如果是索引器属性或成员不可写</exception>
        /// <exception cref="ArgumentNullException">如果非静态情况下 target 为空</exception>
        public void SetValue(object? target, object? value)
        {
            if (indexParams != null)
                throw new InvalidOperationException($"属性 {Name} 是索引器，请使用 SetValue(target, value, indexArgs[])");

            // 判断静态与实例的 target 传递
            if (!IsStatic && target == null)
                throw new ArgumentNullException($"对于非静态成员 {Name}，target 不能为空");

            if (setter == null)
                throw new InvalidOperationException($"成员 {Name} 不可写");

            try
            {
                setter(target, value);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"设置成员 {Name} 时发生错误", ex);
            }
        }

        /// <summary>
        /// 为索引器属性获取值。
        /// </summary>
        /// <param name="target">实例对象，静态则为null</param>
        /// <param name="indexArgs">索引参数</param>
        /// <returns>属性值</returns>
        /// <exception cref="InvalidOperationException">如果不是索引器属性或成员不可读</exception>
        /// <exception cref="ArgumentNullException">如果非静态情况下 target 为空</exception>
        /// <exception cref="ArgumentException">如果索引器参数数量不匹配</exception>
        public object? GetValue(object? target, params object?[] indexArgs)
        {
            if (indexGetter == null)
                throw new InvalidOperationException($"{Name} 不是索引器属性");

            if (!IsStatic && target == null)
                throw new ArgumentNullException($"对于非静态索引器 {Name}，target 不能为空");

            if (indexGetter == null)
                throw new InvalidOperationException($"成员 {Name} 不可读");

            if (indexParams!.Length != indexArgs.Length)
                throw new ArgumentException("索引器参数数量不匹配");

            return indexGetter(target, indexArgs);
        }

        /// <summary>
        /// 为索引器属性设置值
        /// </summary>
        /// <param name="target"> 实例对象，静态则为null </param>
        /// <param name="value"> 待设置的值 </param>
        /// <param name="indexArgs"> 索引参数 </param>
        /// <exception cref="InvalidOperationException">如果不是索引器属性或成员不可写</exception>
        /// <exception cref="ArgumentNullException">如果非静态情况下 target 为空</exception>
        /// <exception cref="ArgumentException">如果索引器参数数量不匹配</exception>
        public void SetValue(object? target, object? value, params object?[] indexArgs)
        {
            if (indexSetter == null)
                throw new InvalidOperationException($"{Name} 不是索引器属性");

            if (!IsStatic && target == null)
                throw new ArgumentNullException($"对于非静态索引器 {Name}，target 不能为空");

            if (indexSetter == null)
                throw new InvalidOperationException($"成员 {Name} 不可写");

            if (indexParams!.Length != indexArgs.Length)
                throw new ArgumentException("索引器参数数量不匹配");

            indexSetter(target, value, indexArgs);
        }

        // =============================================
        // 泛型语法糖（非索引器）
        // =============================================
        /// <summary>
        /// 泛型特化
        /// </summary>
        public TValue GetValue<TTarget, TValue>(TTarget target)
        {
            if (indexParams != null)
                throw new InvalidOperationException($"属性 {Name} 是索引器，不能使用泛型 GetValue<TTarget,TValue>(...) 语法");
            if (!IsStatic && target == null)
                throw new ArgumentNullException(nameof(target), $"对于非静态成员 {Name}，target 不能为空");
            if (getter == null)
                throw new InvalidOperationException($"成员 {Name} 不可读");
            if (TypedGetter is Func<TTarget, TValue> g)
                return g(target);
            if (IsStatic && TypedGetter is Func<TValue> sg)
                return sg();

            var raw = getter(target);
            return raw is null ? default! : (TValue)raw;
        }

        /// <summary>
        /// 泛型特化
        /// </summary>
        public void SetValue<TTarget, TValue>(TTarget target, TValue value)
        {
            if (indexParams != null)
                throw new InvalidOperationException($"属性 {Name} 是索引器，不能使用泛型 SetValue<TTarget,TValue>(...) 语法");
            if (!IsStatic && target == null)
                throw new ArgumentNullException(nameof(target), $"对于非静态成员 {Name}，target 不能为空");
            if (setter == null)
                throw new InvalidOperationException($"成员 {Name} 不可写");
            if (TypedSetter is Action<TTarget, TValue> s)
                s(target, value);
            else if (IsStatic && TypedSetter is Action<TValue> ss)
                ss(value);
            else
                setter(target, value);
        }

        /// <summary>
        /// 静态重载特化
        /// </summary>
        public TValue GetValue<TValue>()
        {
            if (!IsStatic)
                throw new InvalidOperationException($"成员 {Name} 不是静态成员，不能使用 GetValue<TValue>() 语法");
            if (getter == null)
                throw new InvalidOperationException($"成员 {Name} 不可读");
            if (TypedGetter is Func<TValue> g)
                return g();
            var raw = getter(null);
            return raw is null ? default! : (TValue)raw;
        }

        /// <summary>
        /// 静态成员设置值特化
        /// </summary>
        public void SetValue<TValue>(TValue value)
        {
            if (!IsStatic)
                throw new InvalidOperationException($"成员 {Name} 不是静态成员，不能使用 SetValue<TValue>(...) 语法");
            if (setter == null)
                throw new InvalidOperationException($"成员 {Name} 不可写");
            if (TypedSetter is Action<TValue> s)
                s(value);
            else
                setter(null, value);
        }


        /// <summary>
        /// 获得一个成员访问器（自动缓存）。
        /// </summary>
        public static MemberAccessor Get(Type type, string memberName)
        {
            var memberInfo = _lookupCache.GetOrAdd((type, memberName), key =>
            {
                var (t, name) = key;

                return (MemberInfo?)t.GetField(
                            name,
                            DefaultFlags)
                    ?? t.GetProperty(
                            name,
                            DefaultFlags);
            }) ?? throw new MissingMemberException(type.FullName, memberName);
            return Get(memberInfo);
        }

        /// <summary>
        /// 索引器访问器获取，主要用于具有多个索引重载的情况。
        /// 
        /// <example>
        /// 示例：
        /// <code>
        ///class MyList
        ///{
        ///    private string[] _data = { "Apple", "Banana", "Cat" };
        ///
        ///    public string this[int index]
        ///    {
        ///        get => _data[index];
        ///        set => _data[index] = value;
        ///    }
        ///
        ///    public string this[int x, int y]
        ///    {
        ///        get => $"{x},{y}";
        ///        set { }
        ///    }
        ///}
        /// 
        /// var acc1 = MemberAccessor.GetIndexer(typeof(MyList), typeof(int));
        /// var value1 = acc1.GetValue(list, 1); // "Banana"
        ///
        /// var acc2 = MemberAccessor.GetIndexer(typeof(MyList), typeof(int), typeof(int));
        /// var value2 = acc2.GetValue(list, 3, 5); // "3,5"
        /// </code>
        /// </example>
        /// 
        /// </summary>
        /// <param name="type"></param>
        /// <param name="parameterTypes"></param>
        /// <returns></returns>
        /// <exception cref="MissingMemberException"></exception>
        public static MemberAccessor GetIndexer(Type type, params Type[] parameterTypes)
        {
            var prop = type.GetProperty(
                "Item",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                returnType: null,
                types: parameterTypes,
                modifiers: null) ?? throw new MissingMemberException($"找不到匹配参数的索引器");
            return Get(prop);
        }

        /// <summary>
        /// 按MemberInfo获取访问器
        /// </summary>
        public static MemberAccessor Get(MemberInfo member)
            => GetOrCreate(member, m => new MemberAccessor(m));

        /// <summary>
        /// 不处理的成员类型
        /// </summary>
        /// <returns>若不处理，返回false</returns>
        private static bool IsSupportedMember(MemberInfo member)
        {
            switch (member)
            {
                case FieldInfo f:

                    // ref struct / Span<TUI> / ReadOnlySpan<TUI> 会崩
                    if (f.FieldType.IsByRefLike)
                        return false;

                    // 指针类型
                    if (f.FieldType.IsPointer)
                        return false;

                    return true;

                case PropertyInfo p:

                    // 返回 ref struct 的属性
                    if (p.PropertyType.IsByRefLike)
                        return false;

                    return true;
            }

            return false;
        }



        // -------------------------
        //   扫描所有成员
        // -------------------------
        /// <summary>
        /// 获取某类型的所有方法（可选择包含继承方法）
        /// </summary>
        public static IEnumerable<MemberAccessor> GetAll(Type type, BindingFlags flags = DefaultFlags)
        {
            // 跳过扫描 enum 定义本身，否则扫描到 value__ 会炸
            if (type.IsEnum)
                return [];

            return type.GetMembers(flags)
                       .Where(m => m is FieldInfo or PropertyInfo)
                       .Where(IsSupportedMember)
                       .Select(Get);
        }

        /// <summary>
        /// 泛型版本
        /// </summary>
        public static IEnumerable<MemberAccessor> GetAll<T>(BindingFlags flags = DefaultFlags)
            => GetAll(typeof(T), flags);

        // ======================
        //   Getter / Setter 构造
        // ======================

        private static Func<object?, object?> CreateFieldGetter(FieldInfo f)
        {
            try
            {
                var dm = IsSaveOwner(f.DeclaringType) ?
                    new DynamicMethod($"get_{f.Name}",
                                      typeof(object),
                                      [typeof(object)],
                                      f.DeclaringType,
                                      true) :
                    new DynamicMethod($"get_{f.Name}",
                                      typeof(object),
                                      [typeof(object)],
                                      f.Module,
                                      true);

                ILGenerator il = dm.GetILGenerator();

                if (!f.IsStatic)
                {
                    // 非静态字段：
                    // 特殊处理：如果是在枚举类型内的隐藏字段 value__（即 f.DeclaringType 是 enum 且字段名通常是 "value__"）
                    bool declaringIsEnum = f.DeclaringType?.IsEnum ?? false;
                    bool isValueFieldOfEnum = declaringIsEnum && string.Equals(f.Name, "value__", StringComparison.Ordinal);

                    if (isValueFieldOfEnum)
                    {
                        // 读取 boxed enum 的底层值：unbox.any underlyingType，box 并返回
                        var underlying = Enum.GetUnderlyingType(f.DeclaringType!);
                        // 参数 0 是 boxed enum object
                        il.Emit(OpCodes.Ldarg_0);
                        // unbox.any underlyingType （直接把 boxed enum -> underlying value）
                        il.Emit(OpCodes.Unbox_Any, underlying);
                        // underlying 已经是值类型，box 成 object 用于返回
                        il.Emit(OpCodes.Box, underlying);
                        il.Emit(OpCodes.Ret);
                        return (Func<object?, object?>)dm.CreateDelegate(typeof(Func<object?, object?>));
                    }

                    // 一般情形：字段属于某类型（class 或 struct），字段类型可能是 enum 或其他 value-type / ref-type
                    if (f.DeclaringType!.IsValueType)
                    {
                        // 值类型：需要 unbox 后 ldobj
                        il.Emit(OpCodes.Ldarg_0);                     // object
                        il.Emit(OpCodes.Unbox_Any, f.DeclaringType!);
                        il.Emit(OpCodes.Ldfld, f);
                    }
                    else
                    {
                        // 引用类型
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Castclass, f.DeclaringType!);
                        il.Emit(OpCodes.Ldfld, f);
                    }
                }
                else
                {
                    il.Emit(OpCodes.Ldsfld, f);
                }

                // 如果字段本身是值类型（包括 enum），box 之后返回
                if (f.FieldType.IsValueType)
                    il.Emit(OpCodes.Box, f.FieldType);

                il.Emit(OpCodes.Ret);

                return (Func<object?, object?>)dm.CreateDelegate(typeof(Func<object?, object?>));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"创建字段 {f.Name} 的 Getter 时发生错误", ex);
            }
        }

        private static Action<object?, object?> CreateFieldSetter(FieldInfo f)
        {
            try
            {
                var dm = IsSaveOwner(f.DeclaringType) ?
                    new DynamicMethod($"set_{f.Name}",
                                      null,
                                      [typeof(object), typeof(object)],
                                      f.DeclaringType,
                                      true) :
                    new DynamicMethod($"set_{f.Name}",
                                      null,
                                      [typeof(object), typeof(object)],
                                      f.Module,
                                      true);

                ILGenerator il = dm.GetILGenerator();

                bool isValueType = f.DeclaringType!.IsValueType;

                if (!f.IsStatic)
                {
                    bool declaringIsEnum = f.DeclaringType?.IsEnum ?? false;
                    bool isValueFieldOfEnum = declaringIsEnum && string.Equals(f.Name, "value__", StringComparison.Ordinal);

                    if (isValueFieldOfEnum)
                    {
                        // 设置 boxed enum 的底层值：需要把传入的 value 转为 underlyingType，然后重新装箱为 enum / 或生成一个新的 boxed enum
                        // 最安全且简单的做法是：
                        // - 把传入的 object 转为 underlying type (unbox.any)
                        // - 调用 Enum.ToObject( enumType, underlyingValue ) 得到一个 boxed enum
                        // - 将结果覆盖原来传入的 boxed object 的内存 —— 但在托管中无法直接写回装箱对象的内容
                        // 因为直接修改装箱 enum 的内容更复杂（需要 unsafe）,
                        // 为了安全与简单，这里不为 enum 的隐藏 value__ 生成 setter。
                        throw new InvalidOperationException($"无法为 enum 的隐藏字段 value__ 生成 setter：{f.DeclaringType}.{f.Name}");
                    }


                    il.Emit(OpCodes.Ldarg_0);

                    if (isValueType)
                    {
                        // struct：必须 unbox 得到指针
                        il.Emit(OpCodes.Unbox, f.DeclaringType!);  // 得到地址 &DemoStruct
                    }
                    else
                    {
                        // class 维持原逻辑
                        il.Emit(OpCodes.Castclass, f.DeclaringType!);
                    }
                }

                il.Emit(OpCodes.Ldarg_1);
                if (f.FieldType.IsValueType)
                    il.Emit(OpCodes.Unbox_Any, f.FieldType);
                else
                    il.Emit(OpCodes.Castclass, f.FieldType);

                if (f.IsStatic)
                    il.Emit(OpCodes.Stsfld, f);
                else
                    il.Emit(OpCodes.Stfld, f);

                il.Emit(OpCodes.Ret);

                return (Action<object?, object?>)dm.CreateDelegate(typeof(Action<object?, object?>));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"创建字段 {f.Name} 的 Setter 时发生错误", ex);
            }
        }

        private static Func<object?, object?> CreatePropertyGetter(PropertyInfo p)
        {
            try
            {
                var getMethod = p.GetGetMethod(true)!;

                var dm = IsSaveOwner(p.DeclaringType) ?
                    new DynamicMethod(
                                $"get_{p.Name}",
                                typeof(object),
                                [typeof(object)],
                                p.DeclaringType!,
                                true) :
                    new DynamicMethod(
                                $"get_{p.Name}",
                                typeof(object),
                                [typeof(object)],
                                p.Module,
                                true);


                ILGenerator il = dm.GetILGenerator();

                if (!getMethod.IsStatic)
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Castclass, p.DeclaringType!);
                    il.EmitCall(OpCodes.Callvirt, getMethod, null);
                }
                else
                {
                    il.EmitCall(OpCodes.Call, getMethod, null);
                }

                if (p.PropertyType.IsValueType)
                    il.Emit(OpCodes.Box, p.PropertyType);

                il.Emit(OpCodes.Ret);

                return (Func<object?, object?>)dm.CreateDelegate(typeof(Func<object?, object?>));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"创建属性 {p.Name} 的 Getter 时发生错误", ex);
            }
        }

        private static Action<object?, object?> CreatePropertySetter(PropertyInfo p)
        {
            try
            {
                var setMethod = p.GetSetMethod(true)!;

                var dm = IsSaveOwner(p.DeclaringType) ?
                    new DynamicMethod(
                                $"set_{p.Name}",
                                null,
                                [typeof(object), typeof(object)],
                                p.DeclaringType!,
                                true) :
                    new DynamicMethod(
                                $"set_{p.Name}",
                                null,
                                [typeof(object), typeof(object)],
                                p.Module,
                                true);

                ILGenerator il = dm.GetILGenerator();

                if (!setMethod.IsStatic)
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Castclass, p.DeclaringType!);
                }

                il.Emit(OpCodes.Ldarg_1);

                if (p.PropertyType.IsValueType)
                    il.Emit(OpCodes.Unbox_Any, p.PropertyType);
                else
                    il.Emit(OpCodes.Castclass, p.PropertyType);

                if (setMethod.IsStatic)
                    il.EmitCall(OpCodes.Call, setMethod, null);
                else
                    il.EmitCall(OpCodes.Callvirt, setMethod, null);

                il.Emit(OpCodes.Ret);

                return (Action<object?, object?>)dm.CreateDelegate(typeof(Action<object?, object?>));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"创建属性 {p.Name} 的 Setter 时发生错误", ex);
            }
        }

        private static Func<object?, object?[], object?> CreateIndexerGetter(PropertyInfo p, MethodInfo getMethod)
        {
            var dm = IsSaveOwner(p.DeclaringType) ?
                new DynamicMethod(
                        $"idx_get_{p.Name}",
                        typeof(object),
                        [typeof(object), typeof(object?[])],
                        p.DeclaringType!,
                        true) :
                new DynamicMethod(
                        $"idx_get_{p.Name}",
                        typeof(object),
                        [typeof(object), typeof(object?[])],
                        p.Module,
                        true);

            ILGenerator il = dm.GetILGenerator();

            if (!getMethod.IsStatic)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Castclass, p.DeclaringType!);
            }

            // 加载 index 参数
            var idxParams = getMethod.GetParameters();
            for (int i = 0; i < idxParams.Length; i++)
            {
                il.Emit(OpCodes.Ldarg_1);       // indexArgs
                il.Emit(OpCodes.Ldc_I4, i);     // index
                il.Emit(OpCodes.Ldelem_Ref);    // indexArgs[i]

                Type pType = idxParams[i].ParameterType;
                if (pType.IsValueType)
                    il.Emit(OpCodes.Unbox_Any, pType);
                else
                    il.Emit(OpCodes.Castclass, pType);
            }

            il.EmitCall(getMethod.IsStatic ? OpCodes.Call : OpCodes.Callvirt, getMethod, null);

            if (p.PropertyType.IsValueType)
                il.Emit(OpCodes.Box, p.PropertyType);

            il.Emit(OpCodes.Ret);

            return (Func<object?, object?[], object?>)dm.CreateDelegate(typeof(Func<object?, object?[], object?>));
        }
        private static Action<object?, object?, object?[]> CreateIndexerSetter(PropertyInfo p, MethodInfo setMethod)
        {
            var dm = new DynamicMethod(
                $"idx_set_{p.Name}",
                null,
                [typeof(object), typeof(object), typeof(object?[])],
                p.DeclaringType!,
                true);

            ILGenerator il = dm.GetILGenerator();

            if (!setMethod.IsStatic)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Castclass, p.DeclaringType!);
            }

            // 加载 index 参数
            var idxParams = setMethod.GetParameters();
            for (int i = 0; i < idxParams.Length - 1; i++)
            {
                il.Emit(OpCodes.Ldarg_2);       // indexArgs
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldelem_Ref);

                Type pType = idxParams[i].ParameterType;
                if (pType.IsValueType)
                    il.Emit(OpCodes.Unbox_Any, pType);
                else
                    il.Emit(OpCodes.Castclass, pType);
            }

            // 加载 value
            Type valType = p.PropertyType;
            il.Emit(OpCodes.Ldarg_1);
            if (valType.IsValueType)
                il.Emit(OpCodes.Unbox_Any, valType);
            else
                il.Emit(OpCodes.Castclass, valType);

            il.EmitCall(setMethod.IsStatic ? OpCodes.Call : OpCodes.Callvirt, setMethod, null);

            il.Emit(OpCodes.Ret);

            return (Action<object?, object?, object?[]>)dm.CreateDelegate(typeof(Action<object?, object?, object?[]>));
        }

    }
}