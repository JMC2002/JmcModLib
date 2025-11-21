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
                    MemberType = p.PropertyType;
                    IsStatic = (p.GetGetMethod(true)?.IsStatic ?? false) ||
                               (p.GetSetMethod(true)?.IsStatic ?? false);

                    if (p.CanRead)
                        getter = CreatePropertyGetter(p);

                    if (p.CanWrite)
                        setter = CreatePropertySetter(p);
                    break;

                default:
                    throw new ArgumentException($"不支持的成员类型: {member.MemberType}");
            }
        }

        /// <summary>
        /// 获取值。
        /// </summary>
        public object? GetValue(object? target)
        {
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
        public void SetValue(object? target, object? value)
        {
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
                                new[] { typeof(object) },
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
                                new[] { typeof(object), typeof(object) },
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
                                new[] { typeof(object) },
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
                                new[] { typeof(object), typeof(object) },
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
    }
}