using System;

namespace JmcModLib.Config
{
    /// <summary>
    /// 标记这个变量/字段是一个配置项
    /// </summary>
    /// <remarks>
    /// 标记这个变量/字段是一个配置项
    /// </remarks>
    /// <param name="displayName">显示名（用于放在UI上以及作为json中的key）</param>
    /// <param name="onChanged">配置变更时的额外回调方法名称，需要和字段/变量在同一个类中，接受参数为新赋的值，将会在实际修改值前调用（注：不需要写变更变量的操作）</param>
    /// <param name="group"> 配置所在的分组，默认为 DefaultGroup</param>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class ConfigAttribute(string displayName,
                           string? onChanged = null,
                           string group = ConfigAttribute.DefaultGroup) : Attribute
    {
        /// <summary>
        /// 显示名（用于放在UI上以及作为json中的key）
        /// </summary>
        public string DisplayName { get; } = displayName;

        /// <summary>
        /// 配置变更时的额外回调方法名称，需要和字段/变量在同一个类中，接受参数为新赋的值，将会在实际修改值前调用（注：不需要写变更变量的操作）
        /// </summary>
        public string? OnChanged { get; } = onChanged;

        /// <summary>
        /// 配置所在的分组，默认为 DefaultGroup
        /// </summary>
        public string Group { get; } = group;

        /// <summary>
        /// 默认分组保留字，值为 "DefaultGroup"
        /// </summary>
        public const string DefaultGroup = "DefaultGroup";
    }
}