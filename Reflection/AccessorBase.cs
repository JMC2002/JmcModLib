using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;

namespace JmcModLib.Reflection
{
    /// <summary>
    /// 所有访问器的基类
    /// </summary>
    public abstract class ReflectionAccessorBase
    {
        /// <summary>
        /// 默认搜索所有静态、实例、公有、私有，不搜索继承
        /// </summary>
        public const BindingFlags DefaultFlags =
            BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>
        /// 成员名称
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// 声明该成员的类型
        /// </summary>
        public abstract Type DeclaringType { get; }

        /// <summary>
        /// 该成员是否为静态
        /// </summary>
        public virtual bool IsStatic { get; protected set; }

        // =============================================
        //   Attribute 访问部分（统一实现）
        // =============================================

        /// <summary>
        /// 访问器缓存
        /// </summary>
        protected readonly ConcurrentDictionary<Type, Attribute[]> _attrCache = new();

        /// <summary>
        /// 获取指定类型的第一个 Attribute。如果不存在返回 null。
        /// </summary>
        public T? GetAttribute<T>() where T : Attribute =>
            GetAttributes(typeof(T)).Cast<T>().FirstOrDefault();

        /// <summary>
        /// 判断是否具有某个 Attribute。
        /// </summary>
        public bool HasAttribute<T>() where T : Attribute =>
            GetAttribute<T>() != null;

        /// <summary>
        /// 获取指定类型的所有 Attribute。如果 type 为 null，返回所有 Attribute。
        /// </summary>
        public abstract Attribute[] GetAttributes(Type? type = null);

        /// <summary>
        /// 获取所有 Attribute（等价于 GetAttributes(null)）
        /// </summary>
        public Attribute[] GetAllAttributes() => GetAttributes(null);
    }

    /// <summary>
    /// MemberAccessor 和 MethodAccessor 的派生基类
    /// </summary>
    /// <remarks>
    /// 构造基类
    /// </remarks>
    /// <param name="member">成员信息</param>
    /// <exception cref="ArgumentNullException"> 若 member 为 null </exception>
    public abstract class ReflectionAccessorBase<TMemberInfo, TAccessor>(TMemberInfo member) 
        : ReflectionAccessorBase
        where TMemberInfo : MemberInfo
        where TAccessor : ReflectionAccessorBase<TMemberInfo, TAccessor>
    {
        // =============================================
        //   静态缓存（所有子类共享相同的缓存模式）
        // =============================================

        private static readonly ConcurrentDictionary<TMemberInfo, TAccessor> _cache = new();

        /// <summary>
        /// 获取当前缓存的条目数量
        /// </summary>
        public static int CacheCount => _cache.Count;

        /// <summary>
        /// 从 MemberInfo 获取 TAccessor 并缓存（由子类实现具体的创建逻辑）
        /// </summary>
        protected static TAccessor GetOrCreate(TMemberInfo member, Func<TMemberInfo, TAccessor> factory)
        {
            return _cache.GetOrAdd(member, factory);
        }

        /// <summary>
        /// 清空缓存（用于测试或内存管理）
        /// </summary>
        public static void ClearCache() => _cache.Clear();

        // =============================================
        //   实例属性和字段
        // =============================================

        /// <summary>
        /// 底层的 MemberInfo（FieldInfo/PropertyInfo/MethodInfo 等）
        /// </summary>
        public TMemberInfo Member { get; } = member 
                                          ?? throw new ArgumentNullException(nameof(member), "member 不能为 null");

        /// <summary>
        /// 成员名称
        /// </summary>
        public override string Name => Member.Name;

        /// <summary>
        /// 声明该成员的类型
        /// </summary>
        public override Type DeclaringType => Member.DeclaringType!;

        // =============================================
        //   Attribute 访问部分（统一实现）
        // =============================================
        /// <summary>
        /// 获取指定类型的所有 Attribute。如果 type 为 null，返回所有 Attribute。
        /// </summary>
        public override Attribute[] GetAttributes(Type? type = null)
        {
            type ??= typeof(object); // 用 typeof(object) 表示"获取全部 Attribute"

            return _attrCache.GetOrAdd(type, t =>
            {
                if (t == typeof(object))
                {
                    // 获取所有 attribute
                    return [.. Member.GetCustomAttributes(inherit: true).Cast<Attribute>()];
                }
                else
                {
                    // 获取特定类型
                    return [.. Member.GetCustomAttributes(t, inherit: true).Cast<Attribute>()];
                }
            });
        }
    }
}