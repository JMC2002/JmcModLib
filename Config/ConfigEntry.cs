using JmcModLib.Reflection;
using System;
namespace JmcModLib.Config
{
    public sealed class ConfigEntry
    {
        internal string Key => GetKey(DeclaringType, Accessor.Name);
        internal string Group { get; }
        internal Type DeclaringType { get; }
        internal MemberAccessor Accessor { get; }
        internal ConfigAttribute Attribute { get; }

        /// <summary>
        /// 字段/属性最初的默认值，用于 Reset。
        /// </summary>
        internal object? DefaultValue { get; }

        /// <summary>
        /// 通过 DeclaringType 和 Name 生成唯一 Key（当前asm下唯一）
        /// </summary>
        /// <param name="declaringType">变量所在的类的类型</param>
        /// <param name="Name">变量的名称</param>
        /// <returns>返回一个形如{declaringType.FullName}.{Name}的唯一Key</returns>
        public static string GetKey(Type declaringType, string Name) =>
            $"{declaringType.FullName}.{Name}";

        internal ConfigEntry(Type declaringType, MemberAccessor accessor, ConfigAttribute attr, object? defaultValue)
        {
            DeclaringType = declaringType;
            Accessor = accessor;
            Attribute = attr;
            Group = attr.Group;
            DefaultValue = defaultValue;
        }
    }
}
