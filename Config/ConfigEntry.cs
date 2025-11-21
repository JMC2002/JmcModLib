using JmcModLib.Reflection;
using System;
using System.Reflection;
namespace JmcModLib.Config
{
    /// <summary>
    /// 承载配置信息的类。
    /// </summary>
    public sealed class ConfigEntry : BaseEntry<MemberAccessor>
    {
        internal ConfigAttribute Attribute { get; }

        /// <summary>
        /// 字段/属性最初的默认值，用于 Reset。
        /// </summary>
        internal object? DefaultValue { get; }

        internal ConfigEntry(Assembly asm, Type declaringType, MemberAccessor accessor, ConfigAttribute attr, object? defaultValue)
                    : base(asm, attr.Group, declaringType, accessor)
        {
            Attribute = attr;
            DefaultValue = defaultValue;
        }
    }
}
