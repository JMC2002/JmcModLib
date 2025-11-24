using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace JmcModLib.Reflection.Generic
{
    // =============================================
    //   基类层次结构
    // =============================================

    /// <summary>
    /// 所有泛型访问器的基类
    /// </summary>
    public abstract class GenericAccessorBase
    {
        public const BindingFlags DefaultFlags =
            BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic;

        public abstract string Name { get; }
        public abstract Type DeclaringType { get; }
        public abstract bool IsStatic { get; }

        protected static bool IsSafeOwner(Type? declaringType)
        {
            return declaringType != null &&
                   declaringType.IsVisible &&
                   !declaringType.IsInterface &&
                   !declaringType.IsArray &&
                   !declaringType.IsPointer &&
                   !declaringType.IsByRef &&
                   !declaringType.IsByRefLike &&
                   !declaringType.ContainsGenericParameters;
        }

        // Attribute 访问
        protected readonly ConcurrentDictionary<Type, Attribute[]> _attrCache = new();

        public T? GetAttribute<T>() where T : Attribute =>
            GetAttributes(typeof(T)).Cast<T>().FirstOrDefault();

        public bool HasAttribute<T>() where T : Attribute =>
            GetAttribute<T>() != null;

        public abstract Attribute[] GetAttributes(Type? type = null);

        public Attribute[] GetAllAttributes() => GetAttributes(null);
    }

    /// <summary>
    /// 泛型访问器基类（CRTP 模式）
    /// </summary>
    public abstract class GenericAccessorBase<TMemberInfo, TAccessor> : GenericAccessorBase
        where TMemberInfo : MemberInfo
        where TAccessor : GenericAccessorBase<TMemberInfo, TAccessor>
    {
        private static readonly ConcurrentDictionary<TMemberInfo, TAccessor> _cache = new();

        public static int CacheCount => _cache.Count;

        protected static TAccessor GetOrCreate(TMemberInfo member, Func<TMemberInfo, TAccessor> factory)
        {
            return _cache.GetOrAdd(member, factory);
        }

        public static void ClearCache() => _cache.Clear();

        public TMemberInfo Member { get; }

        public override string Name => Member.Name;
        public override Type DeclaringType => Member.DeclaringType!;

        protected GenericAccessorBase(TMemberInfo member)
        {
            Member = member ?? throw new ArgumentNullException(nameof(member));
        }

        public override Attribute[] GetAttributes(Type? type = null)
        {
            type ??= typeof(object);
            return _attrCache.GetOrAdd(type, t =>
            {
                if (t == typeof(object))
                {
                    return Member.GetCustomAttributes(inherit: true)
                                 .Cast<Attribute>()
                                 .ToArray();
                }
                else
                {
                    return Member.GetCustomAttributes(t, inherit: true)
                                 .Cast<Attribute>()
                                 .ToArray();
                }
            });
        }
    }

    // =============================================
    //   泛型 MemberAccessor<TTarget, TValue>
    // =============================================

    /// <summary>
    /// 强类型成员访问器 - 零开销的字段/属性访问
    /// </summary>
    /// <typeparam name="TTarget">目标对象类型（静态成员使用 object）</typeparam>
    /// <typeparam name="TValue">成员值类型</typeparam>
    public sealed class MemberAccessor<TTarget, TValue> : GenericAccessorBase<MemberInfo, MemberAccessor<TTarget, TValue>>
    {
        public Type MemberType { get; }

        // 强类型委托 - 零装箱
        private readonly Func<TTarget, TValue>? _strongGetter;
        private readonly Action<TTarget, TValue>? _strongSetter;

        // 静态成员的特殊处理
        private readonly Func<TValue>? _staticGetter;
        private readonly Action<TValue>? _staticSetter;

        // 常量值直接缓存
        private readonly TValue? _constValue;
        private readonly bool _isConst;

        public override bool IsStatic { get; }

        private MemberAccessor(MemberInfo member) : base(member)
        {
            switch (member)
            {
                case FieldInfo f:
                    MemberType = f.FieldType;
                    IsStatic = f.IsStatic;

                    ValidateTypes(f.DeclaringType!, f.FieldType);

                    if (f.IsLiteral) // const
                    {
                        _isConst = true;
                        _constValue = (TValue?)f.GetRawConstantValue();
                    }
                    else if (IsStatic)
                    {
                        _staticGetter = CreateStaticFieldGetter(f);
                        if (!f.IsInitOnly)
                            _staticSetter = CreateStaticFieldSetter(f);
                    }
                    else
                    {
                        _strongGetter = CreateFieldGetter(f);
                        if (!f.IsInitOnly)
                            _strongSetter = CreateFieldSetter(f);
                    }
                    break;

                case PropertyInfo p:
                    MemberType = p.PropertyType;
                    IsStatic = (p.GetGetMethod(true)?.IsStatic ?? false) ||
                               (p.GetSetMethod(true)?.IsStatic ?? false);

                    ValidateTypes(p.DeclaringType!, p.PropertyType);

                    // 不支持索引器
                    if (p.GetIndexParameters().Length > 0)
                        throw new ArgumentException($"属性 {p.Name} 是索引器，请使用 IndexerAccessor");

                    if (IsStatic)
                    {
                        if (p.CanRead)
                            _staticGetter = CreateStaticPropertyGetter(p);
                        if (p.CanWrite)
                            _staticSetter = CreateStaticPropertySetter(p);
                    }
                    else
                    {
                        if (p.CanRead)
                            _strongGetter = CreatePropertyGetter(p);
                        if (p.CanWrite)
                            _strongSetter = CreatePropertySetter(p);
                    }
                    break;

                default:
                    throw new ArgumentException($"不支持的成员类型: {member.MemberType}");
            }
        }

        private static void ValidateTypes(Type declaringType, Type memberType)
        {
            // 验证 TTarget
            if (!typeof(TTarget).IsAssignableFrom(declaringType))
            {
                throw new ArgumentException(
                    $"类型参数 TTarget={typeof(TTarget).Name} 与声明类型 {declaringType.Name} 不兼容");
            }

            // 验证 TValue
            if (!memberType.IsAssignableFrom(typeof(TValue)) &&
                !typeof(TValue).IsAssignableFrom(memberType))
            {
                throw new ArgumentException(
                    $"类型参数 TValue={typeof(TValue).Name} 与成员类型 {memberType.Name} 不兼容");
            }

            // 检查不安全类型
            if (memberType.IsByRefLike)
                throw new ArgumentException($"不支持 ref struct 类型: {memberType.Name}");
            if (memberType.IsPointer)
                throw new ArgumentException($"不支持指针类型: {memberType.Name}");
        }

        // ============================================
        //   公共 API - 强类型，零开销
        // ============================================

        public TValue GetValue(TTarget target)
        {
            if (_isConst)
                return _constValue!;

            if (IsStatic)
            {
                if (_staticGetter == null)
                    throw new InvalidOperationException($"成员 {Name} 不可读");
                return _staticGetter();
            }

            if (!IsStatic && target == null!)
                throw new ArgumentNullException(nameof(target));

            if (_strongGetter == null)
                throw new InvalidOperationException($"成员 {Name} 不可读");

            return _strongGetter(target);
        }

        public void SetValue(TTarget target, TValue value)
        {
            if (_isConst)
                throw new InvalidOperationException($"const 字段 {Name} 不可写");

            if (IsStatic)
            {
                if (_staticSetter == null)
                    throw new InvalidOperationException($"成员 {Name} 不可写");
                _staticSetter(value);
                return;
            }

            if (!IsStatic && target == null!)
                throw new ArgumentNullException(nameof(target));

            if (_strongSetter == null)
                throw new InvalidOperationException($"成员 {Name} 不可写");

            _strongSetter(target, value);
        }

        // ============================================
        //   静态工厂方法
        // ============================================

        public static MemberAccessor<TTarget, TValue> Get(string memberName)
        {
            var type = typeof(TTarget);
            var member = (MemberInfo?)type.GetField(memberName, DefaultFlags)
                      ?? type.GetProperty(memberName, DefaultFlags)
                      ?? throw new MissingMemberException(type.FullName, memberName);

            return Get(member);
        }

        public static MemberAccessor<TTarget, TValue> Get(MemberInfo member)
        {
            return GetOrCreate(member, m => new MemberAccessor<TTarget, TValue>(m));
        }

        // ============================================
        //   IL Emit 生成强类型委托
        // ============================================

        private static Func<TTarget, TValue> CreateFieldGetter(FieldInfo field)
        {
            var dm = IsSafeOwner(field.DeclaringType) ?
                new DynamicMethod($"get_{field.Name}", typeof(TValue), new[] { typeof(TTarget) },
                    field.DeclaringType, true) :
                new DynamicMethod($"get_{field.Name}", typeof(TValue), new[] { typeof(TTarget) },
                    field.Module, true);

            var il = dm.GetILGenerator();

            // 处理值类型宿主
            if (field.DeclaringType!.IsValueType)
            {
                // TTarget 必须是装箱的值类型 → unbox 后读取
                il.Emit(OpCodes.Ldarga_S, (byte)0);
                il.Emit(OpCodes.Ldfld, field);
            }
            else
            {
                il.Emit(OpCodes.Ldarg_0);
                if (typeof(TTarget) != field.DeclaringType)
                    il.Emit(OpCodes.Castclass, field.DeclaringType);
                il.Emit(OpCodes.Ldfld, field);
            }

            // 返回值类型转换
            EmitReturn(il, field.FieldType);

            return (Func<TTarget, TValue>)dm.CreateDelegate(typeof(Func<TTarget, TValue>));
        }

        private static Action<TTarget, TValue> CreateFieldSetter(FieldInfo field)
        {
            var dm = IsSafeOwner(field.DeclaringType) ?
                new DynamicMethod($"set_{field.Name}", null, new[] { typeof(TTarget), typeof(TValue) },
                    field.DeclaringType, true) :
                new DynamicMethod($"set_{field.Name}", null, new[] { typeof(TTarget), typeof(TValue) },
                    field.Module, true);

            var il = dm.GetILGenerator();

            if (field.DeclaringType!.IsValueType)
            {
                il.Emit(OpCodes.Ldarga_S, (byte)0);
            }
            else
            {
                il.Emit(OpCodes.Ldarg_0);
                if (typeof(TTarget) != field.DeclaringType)
                    il.Emit(OpCodes.Castclass, field.DeclaringType);
            }

            il.Emit(OpCodes.Ldarg_1);
            EmitConvert(il, typeof(TValue), field.FieldType);

            il.Emit(OpCodes.Stfld, field);
            il.Emit(OpCodes.Ret);

            return (Action<TTarget, TValue>)dm.CreateDelegate(typeof(Action<TTarget, TValue>));
        }

        private static Func<TValue> CreateStaticFieldGetter(FieldInfo field)
        {
            var dm = new DynamicMethod($"get_static_{field.Name}", typeof(TValue), Type.EmptyTypes,
                field.Module, true);
            var il = dm.GetILGenerator();

            il.Emit(OpCodes.Ldsfld, field);
            EmitReturn(il, field.FieldType);

            return (Func<TValue>)dm.CreateDelegate(typeof(Func<TValue>));
        }

        private static Action<TValue> CreateStaticFieldSetter(FieldInfo field)
        {
            var dm = new DynamicMethod($"set_static_{field.Name}", null, new[] { typeof(TValue) },
                field.Module, true);
            var il = dm.GetILGenerator();

            il.Emit(OpCodes.Ldarg_0);
            EmitConvert(il, typeof(TValue), field.FieldType);
            il.Emit(OpCodes.Stsfld, field);
            il.Emit(OpCodes.Ret);

            return (Action<TValue>)dm.CreateDelegate(typeof(Action<TValue>));
        }

        private static Func<TTarget, TValue> CreatePropertyGetter(PropertyInfo property)
        {
            var getMethod = property.GetGetMethod(true)!;
            var dm = IsSafeOwner(property.DeclaringType) ?
                new DynamicMethod($"get_{property.Name}", typeof(TValue), new[] { typeof(TTarget) },
                    property.DeclaringType, true) :
                new DynamicMethod($"get_{property.Name}", typeof(TValue), new[] { typeof(TTarget) },
                    property.Module, true);

            var il = dm.GetILGenerator();

            if (property.DeclaringType!.IsValueType)
            {
                il.Emit(OpCodes.Ldarga_S, (byte)0);
                il.Emit(OpCodes.Constrained, property.DeclaringType);
                il.Emit(OpCodes.Callvirt, getMethod);
            }
            else
            {
                il.Emit(OpCodes.Ldarg_0);
                if (typeof(TTarget) != property.DeclaringType)
                    il.Emit(OpCodes.Castclass, property.DeclaringType);
                il.EmitCall(OpCodes.Callvirt, getMethod, null);
            }

            EmitReturn(il, property.PropertyType);
            return (Func<TTarget, TValue>)dm.CreateDelegate(typeof(Func<TTarget, TValue>));
        }

        private static Action<TTarget, TValue> CreatePropertySetter(PropertyInfo property)
        {
            var setMethod = property.GetSetMethod(true)!;
            var dm = IsSafeOwner(property.DeclaringType) ?
                new DynamicMethod($"set_{property.Name}", null, new[] { typeof(TTarget), typeof(TValue) },
                    property.DeclaringType, true) :
                new DynamicMethod($"set_{property.Name}", null, new[] { typeof(TTarget), typeof(TValue) },
                    property.Module, true);

            var il = dm.GetILGenerator();

            if (property.DeclaringType!.IsValueType)
            {
                il.Emit(OpCodes.Ldarga_S, (byte)0);
            }
            else
            {
                il.Emit(OpCodes.Ldarg_0);
                if (typeof(TTarget) != property.DeclaringType)
                    il.Emit(OpCodes.Castclass, property.DeclaringType);
            }

            il.Emit(OpCodes.Ldarg_1);
            EmitConvert(il, typeof(TValue), property.PropertyType);

            if (property.DeclaringType.IsValueType)
            {
                il.Emit(OpCodes.Constrained, property.DeclaringType);
                il.Emit(OpCodes.Callvirt, setMethod);
            }
            else
            {
                il.EmitCall(OpCodes.Callvirt, setMethod, null);
            }

            il.Emit(OpCodes.Ret);
            return (Action<TTarget, TValue>)dm.CreateDelegate(typeof(Action<TTarget, TValue>));
        }

        private static Func<TValue> CreateStaticPropertyGetter(PropertyInfo property)
        {
            var getMethod = property.GetGetMethod(true)!;
            var dm = new DynamicMethod($"get_static_{property.Name}", typeof(TValue), Type.EmptyTypes,
                property.Module, true);
            var il = dm.GetILGenerator();

            il.EmitCall(OpCodes.Call, getMethod, null);
            EmitReturn(il, property.PropertyType);

            return (Func<TValue>)dm.CreateDelegate(typeof(Func<TValue>));
        }

        private static Action<TValue> CreateStaticPropertySetter(PropertyInfo property)
        {
            var setMethod = property.GetSetMethod(true)!;
            var dm = new DynamicMethod($"set_static_{property.Name}", null, new[] { typeof(TValue) },
                property.Module, true);
            var il = dm.GetILGenerator();

            il.Emit(OpCodes.Ldarg_0);
            EmitConvert(il, typeof(TValue), property.PropertyType);
            il.EmitCall(OpCodes.Call, setMethod, null);
            il.Emit(OpCodes.Ret);

            return (Action<TValue>)dm.CreateDelegate(typeof(Action<TValue>));
        }

        // ============================================
        //   IL 辅助方法
        // ============================================

        private static void EmitConvert(ILGenerator il, Type from, Type to)
        {
            if (from == to)
                return;

            // 处理 enum
            if (to.IsEnum)
            {
                var underlyingType = Enum.GetUnderlyingType(to);
                if (from == underlyingType)
                    return; // 已经是底层类型，无需转换

                // 先转到底层类型
                if (from.IsValueType && from != underlyingType)
                {
                    // 值类型转换
                    EmitNumericConversion(il, from, underlyingType);
                }
                return;
            }

            if (from.IsEnum && !to.IsEnum)
            {
                var underlyingType = Enum.GetUnderlyingType(from);
                EmitNumericConversion(il, underlyingType, to);
                return;
            }

            // 值类型 → 值类型
            if (from.IsValueType && to.IsValueType)
            {
                EmitNumericConversion(il, from, to);
                return;
            }

            // 引用类型转换
            if (!to.IsValueType && !from.IsValueType)
            {
                if (to != typeof(object))
                    il.Emit(OpCodes.Castclass, to);
                return;
            }

            // 装箱/拆箱
            if (from.IsValueType && !to.IsValueType)
            {
                il.Emit(OpCodes.Box, from);
                if (to != typeof(object))
                    il.Emit(OpCodes.Castclass, to);
            }
            else if (!from.IsValueType && to.IsValueType)
            {
                il.Emit(OpCodes.Unbox_Any, to);
            }
        }

        private static void EmitNumericConversion(ILGenerator il, Type from, Type to)
        {
            if (from == to) return;

            // 简化的数值转换
            var toCode = Type.GetTypeCode(to);
            switch (toCode)
            {
                case TypeCode.Int32: il.Emit(OpCodes.Conv_I4); break;
                case TypeCode.Int64: il.Emit(OpCodes.Conv_I8); break;
                case TypeCode.Single: il.Emit(OpCodes.Conv_R4); break;
                case TypeCode.Double: il.Emit(OpCodes.Conv_R8); break;
                case TypeCode.Byte: il.Emit(OpCodes.Conv_U1); break;
                case TypeCode.Int16: il.Emit(OpCodes.Conv_I2); break;
                case TypeCode.UInt32: il.Emit(OpCodes.Conv_U4); break;
                case TypeCode.UInt64: il.Emit(OpCodes.Conv_U8); break;
            }
        }

        private static void EmitReturn(ILGenerator il, Type actualType)
        {
            EmitConvert(il, actualType, typeof(TValue));
            il.Emit(OpCodes.Ret);
        }
    }

    // =============================================
    //   泛型 MethodAccessor - 0到3个参数的强类型版本
    // =============================================

    /// <summary>
    /// 无参数方法访问器
    /// </summary>
    public sealed class MethodAccessor<TTarget, TResult> : GenericAccessorBase<MethodInfo, MethodAccessor<TTarget, TResult>>
    {
        private readonly Func<TTarget, TResult>? _instanceInvoker;
        private readonly Func<TResult>? _staticInvoker;

        public override bool IsStatic { get; }

        private MethodAccessor(MethodInfo method) : base(method)
        {
            IsStatic = method.IsStatic;

            if (method.IsGenericMethodDefinition)
                throw new ArgumentException("泛型方法定义需要先调用 MakeGenericMethod");

            var parameters = method.GetParameters();
            if (parameters.Length != 0)
                throw new ArgumentException($"方法 {method.Name} 有 {parameters.Length} 个参数，应使用 MethodAccessor<...> 重载");

            ValidateTypes(method);

            if (IsStatic)
                _staticInvoker = CreateStaticInvoker(method);
            else
                _instanceInvoker = CreateInstanceInvoker(method);
        }

        private static void ValidateTypes(MethodInfo method)
        {
            if (!typeof(TTarget).IsAssignableFrom(method.DeclaringType))
                throw new ArgumentException($"TTarget 类型不匹配");

            if (!typeof(TResult).IsAssignableFrom(method.ReturnType) &&
                !method.ReturnType.IsAssignableFrom(typeof(TResult)))
                throw new ArgumentException($"TResult 类型不匹配");
        }

        public TResult Invoke(TTarget target)
        {
            if (IsStatic)
            {
                if (_staticInvoker == null)
                    throw new InvalidOperationException("静态方法调用失败");
                return _staticInvoker();
            }

            if (target == null!)
                throw new ArgumentNullException(nameof(target));

            if (_instanceInvoker == null)
                throw new InvalidOperationException("实例方法调用失败");

            return _instanceInvoker(target);
        }

        private static Func<TTarget, TResult> CreateInstanceInvoker(MethodInfo method)
        {
            var dm = IsSafeOwner(method.DeclaringType) ?
                new DynamicMethod($"invoke_{method.Name}", typeof(TResult), new[] { typeof(TTarget) },
                    method.DeclaringType, true) :
                new DynamicMethod($"invoke_{method.Name}", typeof(TResult), new[] { typeof(TTarget) },
                    method.Module, true);

            var il = dm.GetILGenerator();

            if (method.DeclaringType!.IsValueType)
            {
                il.Emit(OpCodes.Ldarga_S, (byte)0);
                il.Emit(OpCodes.Constrained, method.DeclaringType);
                il.Emit(OpCodes.Callvirt, method);
            }
            else
            {
                il.Emit(OpCodes.Ldarg_0);
                if (typeof(TTarget) != method.DeclaringType)
                    il.Emit(OpCodes.Castclass, method.DeclaringType);
                il.EmitCall(method.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, method, null);
            }

            EmitReturnConversion(il, method.ReturnType, typeof(TResult));
            return (Func<TTarget, TResult>)dm.CreateDelegate(typeof(Func<TTarget, TResult>));
        }

        private static Func<TResult> CreateStaticInvoker(MethodInfo method)
        {
            var dm = new DynamicMethod($"invoke_static_{method.Name}", typeof(TResult), Type.EmptyTypes,
                method.Module, true);
            var il = dm.GetILGenerator();

            il.EmitCall(OpCodes.Call, method, null);
            EmitReturnConversion(il, method.ReturnType, typeof(TResult));

            return (Func<TResult>)dm.CreateDelegate(typeof(Func<TResult>));
        }

        private static void EmitReturnConversion(ILGenerator il, Type from, Type to)
        {
            if (from == typeof(void))
            {
                if (to != typeof(void) && to != typeof(object))
                    throw new InvalidOperationException("void 方法不能返回值");

                if (to == typeof(object))
                    il.Emit(OpCodes.Ldnull);
            }
            else
            {
                // 类型转换（简化版本，可以扩展）
                if (from != to)
                {
                    if (from.IsValueType && !to.IsValueType)
                        il.Emit(OpCodes.Box, from);
                    else if (!from.IsValueType && to.IsValueType)
                        il.Emit(OpCodes.Unbox_Any, to);
                    else if (!to.IsAssignableFrom(from))
                        il.Emit(OpCodes.Castclass, to);
                }
            }

            il.Emit(OpCodes.Ret);
        }

        public static MethodAccessor<TTarget, TResult> Get(string methodName)
        {
            var method = typeof(TTarget).GetMethod(methodName, DefaultFlags, null, Type.EmptyTypes, null)
                      ?? throw new MissingMethodException(typeof(TTarget).FullName, methodName);
            return Get(method);
        }

        public static MethodAccessor<TTarget, TResult> Get(MethodInfo method)
        {
            return GetOrCreate(method, m => new MethodAccessor<TTarget, TResult>(m));
        }
    }

    // ============== 1 参数版本 ==============

    /// <summary>
    /// 单参数方法访问器
    /// </summary>
    public sealed class MethodAccessor<TTarget, TArg1, TResult> : GenericAccessorBase<MethodInfo, MethodAccessor<TTarget, TArg1, TResult>>
    {
        private readonly Func<TTarget, TArg1, TResult>? _instanceInvoker;
        private readonly Func<TArg1, TResult>? _staticInvoker;

        public override bool IsStatic { get; }

        private MethodAccessor(MethodInfo method) : base(method)
        {
            IsStatic = method.IsStatic;

            if (method.IsGenericMethodDefinition)
                throw new ArgumentException("泛型方法定义需要先调用 MakeGenericMethod");

            var parameters = method.GetParameters();
            if (parameters.Length != 1)
                throw new ArgumentException($"方法 {method.Name} 参数数量不匹配");

            ValidateTypes(method, parameters);

            if (IsStatic)
                _staticInvoker = CreateStaticInvoker(method, parameters);
            else
                _instanceInvoker = CreateInstanceInvoker(method, parameters);
        }

        private static void ValidateTypes(MethodInfo method, ParameterInfo[] parameters)
        {
            if (!typeof(TTarget).IsAssignableFrom(method.DeclaringType))
                throw new ArgumentException("TTarget 类型不匹配");

            if (!typeof(TArg1).IsAssignableFrom(parameters[0].ParameterType) &&
                !parameters[0].ParameterType.IsAssignableFrom(typeof(TArg1)))
                throw new ArgumentException("TArg1 类型不匹配");

            if (!typeof(TResult).IsAssignableFrom(method.ReturnType) &&
                !method.ReturnType.IsAssignableFrom(typeof(TResult)))
                throw new ArgumentException("TResult 类型不匹配");

            // ref/out 参数检查
            if (parameters[0].ParameterType.IsByRef)
                throw new ArgumentException("泛型 MethodAccessor 不支持 ref/out 参数，请使用非泛型版本");
        }

        public TResult Invoke(TTarget target, TArg1 arg1)
        {
            if (IsStatic)
            {
                if (_staticInvoker == null)
                    throw new InvalidOperationException("静态方法调用失败");
                return _staticInvoker(arg1);
            }

            if (target == null!)
                throw new ArgumentNullException(nameof(target));

            if (_instanceInvoker == null)
                throw new InvalidOperationException("实例方法调用失败");

            return _instanceInvoker(target, arg1);
        }

        private static Func<TTarget, TArg1, TResult> CreateInstanceInvoker(MethodInfo method, ParameterInfo[] parameters)
        {
            var dm = IsSafeOwner(method.DeclaringType) ?
                new DynamicMethod($"invoke_{method.Name}", typeof(TResult),
                    new[] { typeof(TTarget), typeof(TArg1) }, method.DeclaringType, true) :
                new DynamicMethod($"invoke_{method.Name}", typeof(TResult),
                    new[] { typeof(TTarget), typeof(TArg1) }, method.Module, true);

            var il = dm.GetILGenerator();

            if (method.DeclaringType!.IsValueType)
            {
                il.Emit(OpCodes.Ldarga_S, (byte)0);
            }
            else
            {
                il.Emit(OpCodes.Ldarg_0);
                if (typeof(TTarget) != method.DeclaringType)
                    il.Emit(OpCodes.Castclass, method.DeclaringType);
            }

            // 加载参数1
            il.Emit(OpCodes.Ldarg_1);
            EmitParameterConversion(il, typeof(TArg1), parameters[0].ParameterType);

            // 调用方法
            if (method.DeclaringType.IsValueType)
            {
                il.Emit(OpCodes.Constrained, method.DeclaringType);
                il.Emit(OpCodes.Callvirt, method);
            }
            else
            {
                il.EmitCall(method.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, method, null);
            }

            EmitReturnConversion(il, method.ReturnType, typeof(TResult));
            return (Func<TTarget, TArg1, TResult>)dm.CreateDelegate(typeof(Func<TTarget, TArg1, TResult>));
        }

        private static Func<TArg1, TResult> CreateStaticInvoker(MethodInfo method, ParameterInfo[] parameters)
        {
            var dm = new DynamicMethod($"invoke_static_{method.Name}", typeof(TResult),
                new[] { typeof(TArg1) }, method.Module, true);
            var il = dm.GetILGenerator();

            il.Emit(OpCodes.Ldarg_0);
            EmitParameterConversion(il, typeof(TArg1), parameters[0].ParameterType);
            il.EmitCall(OpCodes.Call, method, null);
            EmitReturnConversion(il, method.ReturnType, typeof(TResult));

            return (Func<TArg1, TResult>)dm.CreateDelegate(typeof(Func<TArg1, TResult>));
        }

        private static void EmitParameterConversion(ILGenerator il, Type from, Type to)
        {
            if (from == to) return;

            if (to.IsEnum)
            {
                var underlyingType = Enum.GetUnderlyingType(to);
                if (from != underlyingType && from.IsValueType)
                {
                    EmitNumericConversion(il, from, underlyingType);
                }
                return;
            }

            if (from.IsEnum && !to.IsEnum)
            {
                var underlyingType = Enum.GetUnderlyingType(from);
                EmitNumericConversion(il, underlyingType, to);
                return;
            }

            if (from.IsValueType && to.IsValueType)
            {
                EmitNumericConversion(il, from, to);
            }
            else if (!to.IsValueType && !from.IsValueType)
            {
                if (to != typeof(object))
                    il.Emit(OpCodes.Castclass, to);
            }
            else if (from.IsValueType && !to.IsValueType)
            {
                il.Emit(OpCodes.Box, from);
                if (to != typeof(object))
                    il.Emit(OpCodes.Castclass, to);
            }
            else if (!from.IsValueType && to.IsValueType)
            {
                il.Emit(OpCodes.Unbox_Any, to);
            }
        }

        private static void EmitNumericConversion(ILGenerator il, Type from, Type to)
        {
            if (from == to) return;

            var toCode = Type.GetTypeCode(to);
            switch (toCode)
            {
                case TypeCode.Int32: il.Emit(OpCodes.Conv_I4); break;
                case TypeCode.Int64: il.Emit(OpCodes.Conv_I8); break;
                case TypeCode.Single: il.Emit(OpCodes.Conv_R4); break;
                case TypeCode.Double: il.Emit(OpCodes.Conv_R8); break;
                case TypeCode.Byte: il.Emit(OpCodes.Conv_U1); break;
                case TypeCode.Int16: il.Emit(OpCodes.Conv_I2); break;
                case TypeCode.UInt32: il.Emit(OpCodes.Conv_U4); break;
                case TypeCode.UInt64: il.Emit(OpCodes.Conv_U8); break;
            }
        }

        private static void EmitReturnConversion(ILGenerator il, Type from, Type to)
        {
            if (from == typeof(void))
            {
                if (to == typeof(object) || to == typeof(void))
                {
                    if (to == typeof(object))
                        il.Emit(OpCodes.Ldnull);
                    il.Emit(OpCodes.Ret);
                    return;
                }
                throw new InvalidOperationException("void 方法不能返回非 void 类型");
            }

            EmitParameterConversion(il, from, to);
            il.Emit(OpCodes.Ret);
        }

        public static MethodAccessor<TTarget, TArg1, TResult> Get(string methodName)
        {
            var method = typeof(TTarget).GetMethod(methodName, DefaultFlags, null,
                new[] { typeof(TArg1) }, null)
                ?? throw new MissingMethodException(typeof(TTarget).FullName, methodName);
            return Get(method);
        }

        public static MethodAccessor<TTarget, TArg1, TResult> Get(MethodInfo method)
        {
            return GetOrCreate(method, m => new MethodAccessor<TTarget, TArg1, TResult>(m));
        }
    }

    // ============== 2 参数版本 ==============

    /// <summary>
    /// 双参数方法访问器
    /// </summary>
    public sealed class MethodAccessor<TTarget, TArg1, TArg2, TResult> :
        GenericAccessorBase<MethodInfo, MethodAccessor<TTarget, TArg1, TArg2, TResult>>
    {
        private readonly Func<TTarget, TArg1, TArg2, TResult>? _instanceInvoker;
        private readonly Func<TArg1, TArg2, TResult>? _staticInvoker;

        public override bool IsStatic { get; }

        private MethodAccessor(MethodInfo method) : base(method)
        {
            IsStatic = method.IsStatic;

            if (method.IsGenericMethodDefinition)
                throw new ArgumentException("泛型方法定义需要先调用 MakeGenericMethod");

            var parameters = method.GetParameters();
            if (parameters.Length != 2)
                throw new ArgumentException($"方法 {method.Name} 需要2个参数");

            // 检查 ref/out
            if (parameters.Any(p => p.ParameterType.IsByRef))
                throw new ArgumentException("泛型 MethodAccessor 不支持 ref/out 参数");

            if (IsStatic)
                _staticInvoker = CreateStaticInvoker(method, parameters);
            else
                _instanceInvoker = CreateInstanceInvoker(method, parameters);
        }

        public TResult Invoke(TTarget target, TArg1 arg1, TArg2 arg2)
        {
            if (IsStatic)
            {
                if (_staticInvoker == null)
                    throw new InvalidOperationException("静态方法调用失败");
                return _staticInvoker(arg1, arg2);
            }

            if (target == null!)
                throw new ArgumentNullException(nameof(target));

            if (_instanceInvoker == null)
                throw new InvalidOperationException("实例方法调用失败");

            return _instanceInvoker(target, arg1, arg2);
        }

        private static Func<TTarget, TArg1, TArg2, TResult> CreateInstanceInvoker(
            MethodInfo method, ParameterInfo[] parameters)
        {
            var dm = IsSafeOwner(method.DeclaringType) ?
                new DynamicMethod($"invoke_{method.Name}", typeof(TResult),
                    new[] { typeof(TTarget), typeof(TArg1), typeof(TArg2) },
                    method.DeclaringType, true) :
                new DynamicMethod($"invoke_{method.Name}", typeof(TResult),
                    new[] { typeof(TTarget), typeof(TArg1), typeof(TArg2) },
                    method.Module, true);

            var il = dm.GetILGenerator();

            // 加载目标
            if (method.DeclaringType!.IsValueType)
            {
                il.Emit(OpCodes.Ldarga_S, (byte)0);
            }
            else
            {
                il.Emit(OpCodes.Ldarg_0);
                if (typeof(TTarget) != method.DeclaringType)
                    il.Emit(OpCodes.Castclass, method.DeclaringType);
            }

            // 加载参数1
            il.Emit(OpCodes.Ldarg_1);
            EmitConvert(il, typeof(TArg1), parameters[0].ParameterType);

            // 加载参数2
            il.Emit(OpCodes.Ldarg_2);
            EmitConvert(il, typeof(TArg2), parameters[1].ParameterType);

            // 调用
            if (method.DeclaringType.IsValueType)
            {
                il.Emit(OpCodes.Constrained, method.DeclaringType);
                il.Emit(OpCodes.Callvirt, method);
            }
            else
            {
                il.EmitCall(method.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, method, null);
            }

            EmitReturnConvert(il, method.ReturnType, typeof(TResult));
            return (Func<TTarget, TArg1, TArg2, TResult>)dm.CreateDelegate(
                typeof(Func<TTarget, TArg1, TArg2, TResult>));
        }

        private static Func<TArg1, TArg2, TResult> CreateStaticInvoker(
            MethodInfo method, ParameterInfo[] parameters)
        {
            var dm = new DynamicMethod($"invoke_static_{method.Name}", typeof(TResult),
                new[] { typeof(TArg1), typeof(TArg2) }, method.Module, true);
            var il = dm.GetILGenerator();

            il.Emit(OpCodes.Ldarg_0);
            EmitConvert(il, typeof(TArg1), parameters[0].ParameterType);

            il.Emit(OpCodes.Ldarg_1);
            EmitConvert(il, typeof(TArg2), parameters[1].ParameterType);

            il.EmitCall(OpCodes.Call, method, null);
            EmitReturnConvert(il, method.ReturnType, typeof(TResult));

            return (Func<TArg1, TArg2, TResult>)dm.CreateDelegate(typeof(Func<TArg1, TArg2, TResult>));
        }

        private static void EmitConvert(ILGenerator il, Type from, Type to)
        {
            if (from == to) return;

            if (to.IsEnum)
            {
                var underlyingType = Enum.GetUnderlyingType(to);
                if (from != underlyingType && from.IsValueType)
                {
                    EmitNumericConv(il, from, underlyingType);
                }
                return;
            }

            if (from.IsValueType && to.IsValueType)
                EmitNumericConv(il, from, to);
            else if (!to.IsValueType && !from.IsValueType)
            {
                if (to != typeof(object))
                    il.Emit(OpCodes.Castclass, to);
            }
            else if (from.IsValueType && !to.IsValueType)
            {
                il.Emit(OpCodes.Box, from);
                if (to != typeof(object))
                    il.Emit(OpCodes.Castclass, to);
            }
            else if (!from.IsValueType && to.IsValueType)
                il.Emit(OpCodes.Unbox_Any, to);
        }

        private static void EmitNumericConv(ILGenerator il, Type from, Type to)
        {
            if (from == to) return;
            var toCode = Type.GetTypeCode(to);
            switch (toCode)
            {
                case TypeCode.Int32: il.Emit(OpCodes.Conv_I4); break;
                case TypeCode.Int64: il.Emit(OpCodes.Conv_I8); break;
                case TypeCode.Single: il.Emit(OpCodes.Conv_R4); break;
                case TypeCode.Double: il.Emit(OpCodes.Conv_R8); break;
                case TypeCode.Byte: il.Emit(OpCodes.Conv_U1); break;
                case TypeCode.Int16: il.Emit(OpCodes.Conv_I2); break;
                case TypeCode.UInt32: il.Emit(OpCodes.Conv_U4); break;
                case TypeCode.UInt64: il.Emit(OpCodes.Conv_U8); break;
            }
        }

        private static void EmitReturnConvert(ILGenerator il, Type from, Type to)
        {
            if (from == typeof(void))
            {
                if (to == typeof(object))
                    il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ret);
                return;
            }
            EmitConvert(il, from, to);
            il.Emit(OpCodes.Ret);
        }

        public static MethodAccessor<TTarget, TArg1, TArg2, TResult> Get(string methodName)
        {
            var method = typeof(TTarget).GetMethod(methodName, DefaultFlags, null,
                new[] { typeof(TArg1), typeof(TArg2) }, null)
                ?? throw new MissingMethodException(typeof(TTarget).FullName, methodName);
            return Get(method);
        }

        public static MethodAccessor<TTarget, TArg1, TArg2, TResult> Get(MethodInfo method)
        {
            return GetOrCreate(method, m => new MethodAccessor<TTarget, TArg1, TArg2, TResult>(m));
        }
    }

    // ============== 3 参数版本 ==============

    /// <summary>
    /// 三参数方法访问器
    /// </summary>
    public sealed class MethodAccessor<TTarget, TArg1, TArg2, TArg3, TResult> :
        GenericAccessorBase<MethodInfo, MethodAccessor<TTarget, TArg1, TArg2, TArg3, TResult>>
    {
        private readonly Func<TTarget, TArg1, TArg2, TArg3, TResult>? _instanceInvoker;
        private readonly Func<TArg1, TArg2, TArg3, TResult>? _staticInvoker;

        public override bool IsStatic { get; }

        private MethodAccessor(MethodInfo method) : base(method)
        {
            IsStatic = method.IsStatic;

            if (method.IsGenericMethodDefinition)
                throw new ArgumentException("泛型方法定义需要先调用 MakeGenericMethod");

            var parameters = method.GetParameters();
            if (parameters.Length != 3)
                throw new ArgumentException($"方法 {method.Name} 需要3个参数");

            if (parameters.Any(p => p.ParameterType.IsByRef))
                throw new ArgumentException("泛型 MethodAccessor 不支持 ref/out 参数");

            if (IsStatic)
                _staticInvoker = CreateStaticInvoker(method, parameters);
            else
                _instanceInvoker = CreateInstanceInvoker(method, parameters);
        }

        public TResult Invoke(TTarget target, TArg1 arg1, TArg2 arg2, TArg3 arg3)
        {
            if (IsStatic)
            {
                if (_staticInvoker == null)
                    throw new InvalidOperationException("静态方法调用失败");
                return _staticInvoker(arg1, arg2, arg3);
            }

            if (target == null!)
                throw new ArgumentNullException(nameof(target));

            if (_instanceInvoker == null)
                throw new InvalidOperationException("实例方法调用失败");

            return _instanceInvoker(target, arg1, arg2, arg3);
        }

        private static Func<TTarget, TArg1, TArg2, TArg3, TResult> CreateInstanceInvoker(
            MethodInfo method, ParameterInfo[] parameters)
        {
            var dm = IsSafeOwner(method.DeclaringType) ?
                new DynamicMethod($"invoke_{method.Name}", typeof(TResult),
                    new[] { typeof(TTarget), typeof(TArg1), typeof(TArg2), typeof(TArg3) },
                    method.DeclaringType, true) :
                new DynamicMethod($"invoke_{method.Name}", typeof(TResult),
                    new[] { typeof(TTarget), typeof(TArg1), typeof(TArg2), typeof(TArg3) },
                    method.Module, true);

            var il = dm.GetILGenerator();

            if (method.DeclaringType!.IsValueType)
            {
                il.Emit(OpCodes.Ldarga_S, (byte)0);
            }
            else
            {
                il.Emit(OpCodes.Ldarg_0);
                if (typeof(TTarget) != method.DeclaringType)
                    il.Emit(OpCodes.Castclass, method.DeclaringType);
            }

            il.Emit(OpCodes.Ldarg_1);
            EmitConv(il, typeof(TArg1), parameters[0].ParameterType);

            il.Emit(OpCodes.Ldarg_2);
            EmitConv(il, typeof(TArg2), parameters[1].ParameterType);

            il.Emit(OpCodes.Ldarg_3);
            EmitConv(il, typeof(TArg3), parameters[2].ParameterType);

            if (method.DeclaringType.IsValueType)
            {
                il.Emit(OpCodes.Constrained, method.DeclaringType);
                il.Emit(OpCodes.Callvirt, method);
            }
            else
            {
                il.EmitCall(method.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, method, null);
            }

            EmitRetConv(il, method.ReturnType, typeof(TResult));
            return (Func<TTarget, TArg1, TArg2, TArg3, TResult>)dm.CreateDelegate(
                typeof(Func<TTarget, TArg1, TArg2, TArg3, TResult>));
        }

        private static Func<TArg1, TArg2, TArg3, TResult> CreateStaticInvoker(
            MethodInfo method, ParameterInfo[] parameters)
        {
            var dm = new DynamicMethod($"invoke_static_{method.Name}", typeof(TResult),
                new[] { typeof(TArg1), typeof(TArg2), typeof(TArg3) }, method.Module, true);
            var il = dm.GetILGenerator();

            il.Emit(OpCodes.Ldarg_0);
            EmitConv(il, typeof(TArg1), parameters[0].ParameterType);

            il.Emit(OpCodes.Ldarg_1);
            EmitConv(il, typeof(TArg2), parameters[1].ParameterType);

            il.Emit(OpCodes.Ldarg_2);
            EmitConv(il, typeof(TArg3), parameters[2].ParameterType);

            il.EmitCall(OpCodes.Call, method, null);
            EmitRetConv(il, method.ReturnType, typeof(TResult));

            return (Func<TArg1, TArg2, TArg3, TResult>)dm.CreateDelegate(
                typeof(Func<TArg1, TArg2, TArg3, TResult>));
        }

        private static void EmitConv(ILGenerator il, Type from, Type to)
        {
            if (from == to) return;

            if (from.IsValueType && to.IsValueType)
            {
                var toCode = Type.GetTypeCode(to);
                switch (toCode)
                {
                    case TypeCode.Int32: il.Emit(OpCodes.Conv_I4); break;
                    case TypeCode.Int64: il.Emit(OpCodes.Conv_I8); break;
                    default: break;
                }
            }
            else if (!to.IsValueType && !from.IsValueType)
            {
                if (to != typeof(object))
                    il.Emit(OpCodes.Castclass, to);
            }
            else if (from.IsValueType && !to.IsValueType)
            {
                il.Emit(OpCodes.Box, from);
            }
            else if (!from.IsValueType && to.IsValueType)
            {
                il.Emit(OpCodes.Unbox_Any, to);
            }
        }

        private static void EmitRetConv(ILGenerator il, Type from, Type to)
        {
            if (from == typeof(void))
            {
                if (to == typeof(object))
                    il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ret);
                return;
            }
            EmitConv(il, from, to);
            il.Emit(OpCodes.Ret);
        }

        public static MethodAccessor<TTarget, TArg1, TArg2, TArg3, TResult> Get(string methodName)
        {
            var method = typeof(TTarget).GetMethod(methodName, DefaultFlags, null,
                new[] { typeof(TArg1), typeof(TArg2), typeof(TArg3) }, null)
                ?? throw new MissingMethodException(typeof(TTarget).FullName, methodName);
            return Get(method);
        }

        public static MethodAccessor<TTarget, TArg1, TArg2, TArg3, TResult> Get(MethodInfo method)
        {
            return GetOrCreate(method, m => new MethodAccessor<TTarget, TArg1, TArg2, TArg3, TResult>(m));
        }
    }
}