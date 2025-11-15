using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace JmcModLib.Reflection
{
    /// <summary>
    /// 字段 / 属性 的统一高性能访问器。
    /// </summary>
    public sealed class MemberAccessor
    {
        private static readonly ConcurrentDictionary<(Type, string), MemberAccessor> _cache = new();
        public static int CacheCount => _cache.Count;
        public string Name { get; }
        public Type MemberType { get; }
        public bool IsStatic { get; }

        private readonly Func<object?, object?>? getter;
        private readonly Action<object?, object?>? setter;

        private MemberAccessor(MemberInfo member)
        {
            Name = member.Name;

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
            return _cache.GetOrAdd((type, memberName), key =>
            {
                var (t, name) = key;

                var member = ((MemberInfo?)t.GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? t.GetProperty(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)) 
                    ?? throw new MissingMemberException(t.FullName, name);
                return new MemberAccessor(member);
            });
        }

        // ======================
        //   Getter / Setter 构造
        // ======================

        private static Func<object?, object?> CreateFieldGetter(FieldInfo f)
        {
            try
            {
                var target = Expression.Parameter(typeof(object));
                Expression instance = f.IsStatic
                    ? null!
                    : Expression.Convert(target, f.DeclaringType!);

                var fieldAccess = f.IsStatic
                    ? Expression.Field(null, f)
                    : Expression.Field(instance, f);

                var convert = Expression.Convert(fieldAccess, typeof(object));
                return Expression.Lambda<Func<object?, object?>>(convert, target).Compile();
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
                var target = Expression.Parameter(typeof(object));
                var value = Expression.Parameter(typeof(object));

                var instance = f.IsStatic
                    ? null!
                    : Expression.Convert(target, f.DeclaringType!);

                var valueCast = Expression.Convert(value, f.FieldType);

                var assign = f.IsStatic
                    ? Expression.Assign(Expression.Field(null, f), valueCast)
                    : Expression.Assign(Expression.Field(instance, f), valueCast);

                return Expression.Lambda<Action<object?, object?>>(assign, target, value).Compile();
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
                var method = p.GetGetMethod(true)!;
                var target = Expression.Parameter(typeof(object));

                var instance = method.IsStatic
                    ? null!
                    : Expression.Convert(target, p.DeclaringType!);

                var call = method.IsStatic
                    ? Expression.Call(method)
                    : Expression.Call(instance, method);

                var convert = Expression.Convert(call, typeof(object));
                return Expression.Lambda<Func<object?, object?>>(convert, target).Compile();
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
                var method = p.GetSetMethod(true)!;
                var target = Expression.Parameter(typeof(object));
                var value = Expression.Parameter(typeof(object));

                var instance = method.IsStatic
                    ? null!
                    : Expression.Convert(target, p.DeclaringType!);

                var valueCast = Expression.Convert(value, p.PropertyType);

                var call = method.IsStatic
                    ? Expression.Call(method, valueCast)
                    : Expression.Call(instance, method, valueCast);

                return Expression.Lambda<Action<object?, object?>>(call, target, value).Compile();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"创建属性 {p.Name} 的 Setter 时发生错误", ex);
            }
        }
    }
}
