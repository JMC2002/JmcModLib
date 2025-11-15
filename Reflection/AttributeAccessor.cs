using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;

namespace JmcModLib.Reflection
{
    public sealed class AttributeAccessor
    {
        private static readonly ConcurrentDictionary<(MemberInfo member, Type attrType), AttributeAccessor> _cache = new();

        public static int CacheCount => _cache.Count;

        public MemberInfo Member { get; }
        public Type AttributeType { get; }
        public Attribute? Instance { get; }

        private AttributeAccessor(MemberInfo member, Type attrType)
        {
            Member = member;
            AttributeType = attrType;
            Instance = member.GetCustomAttributes(attrType, inherit: true).FirstOrDefault() as Attribute;
        }

        /// <summary>
        /// 获取指定成员上的某种 attribute（缓存）
        /// </summary>
        public static AttributeAccessor Get(MemberInfo member, Type attrType)
        {
            return _cache.GetOrAdd((member, attrType), key => new AttributeAccessor(key.member, key.attrType));
        }

        /// <summary>
        /// 泛型版本：获取 attribute 实例
        /// </summary>
        public static AttributeAccessor Get<TAttribute>(MemberInfo member) where TAttribute : Attribute
        {
            return Get(member, typeof(TAttribute));
        }

        /// <summary>
        /// 当前成员是否包含此 attribute
        /// </summary>
        public bool Exists => Instance != null;
    }
}
