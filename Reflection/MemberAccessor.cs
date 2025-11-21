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
        /// 成员数据类型（不是MemberTypes）
        /// </summary>
        public Type MemberType { get; }

        private readonly Func<object?, object?>? getter;
        private readonly Action<object?, object?>? setter;

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
                    }
                    // 如果是 const 字段，直接使用 GetRawConstantValue 获取值
                    else if (f.IsLiteral && !f.IsInitOnly) // const 字段
                    {
                        getter = _ => f.GetRawConstantValue();
                        setter = null; // const 字段没有 setter
                    }
                    else
                    {
                        getter = CreateFieldGetter(f);
                        setter = CreateFieldSetter(f);
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
                    }
                    else
                    {
                        indexParams = null;

                        if (p.CanRead)
                            getter = CreatePropertyGetter(p);
                        if (p.CanWrite)
                            setter = CreatePropertySetter(p);
                    }

                    break;

                default:
                    throw new ArgumentException($"不支持的成员类型: {member.MemberType}");
            }
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
        /// 按MemberInfo获取访问去
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

                    // ref struct / Span<T> / ReadOnlySpan<T> 会崩
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
                var dm = new DynamicMethod(
                                $"get_{f.Name}",
                                typeof(object),
                                [typeof(object)],
                                f.DeclaringType!,
                                true);

                ILGenerator il = dm.GetILGenerator();

                if (!f.IsStatic)
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Castclass, f.DeclaringType!);
                    il.Emit(OpCodes.Ldfld, f);
                }
                else
                {
                    il.Emit(OpCodes.Ldsfld, f);
                }

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
                var dm = new DynamicMethod(
                                $"set_{f.Name}",
                                null,
                                [typeof(object), typeof(object)],
                                f.DeclaringType!,
                                true);

                ILGenerator il = dm.GetILGenerator();

                if (!f.IsStatic)
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Castclass, f.DeclaringType!);
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

                var dm = new DynamicMethod(
                                $"get_{p.Name}",
                                typeof(object),
                                [typeof(object)],
                                p.DeclaringType!,
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

                var dm = new DynamicMethod(
                                $"set_{p.Name}",
                                null,
                                [typeof(object), typeof(object)],
                                p.DeclaringType!,
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
            var dm = new DynamicMethod(
                $"idx_get_{p.Name}",
                typeof(object),
                [typeof(object), typeof(object?[])],
                p.DeclaringType!,
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