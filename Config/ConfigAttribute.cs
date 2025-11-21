using System;

namespace JmcModLib.Config
{
    /// <summary>
    /// 标记这个变量/字段是一个配置项
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class ConfigAttribute : Attribute
    {
        /// <summary>
        /// 显示名（用于放在UI上以及作为json中的key）
        /// </summary>
        public string DisplayName { get; }
        /// <summary>
        /// 配置项描述（暂时未使用）
        /// </summary>
        public string? Description { get; }
        /// <summary>
        /// 配置变更时的额外回调方法名称，需要和字段/变量在同一个类中，接受参数为新赋的值（注：不需要写变更变量的操作）
        /// </summary>
        public string? OnChanged { get; }
        /// <summary>
        /// 配置所在的分组，默认为 DefaultGroup
        /// </summary>
        public string Group { get; }
        /// <summary>
        /// 默认分组保留字，值为 "DefaultGroup"
        /// </summary>
        public const string DefaultGroup = "DefaultGroup";

        /// <summary>
        /// 标记这个变量/字段是一个配置项
        /// </summary>
        /// <param name="displayName">显示名（用于放在UI上以及作为json中的key）</param>
        /// <param name="description">配置项描述（暂时未使用）</param>
        /// <param name="onChanged">配置变更时的额外回调方法名称，需要和字段/变量在同一个类中，接受参数为新赋的值（注：不需要写变更变量的操作）</param>
        /// <param name="group"> 配置所在的分组，默认为 DefaultGroup</param>
        public ConfigAttribute(string displayName,
                               string? description = null,
                               string? onChanged = null,
                               string group = DefaultGroup)
        {
            DisplayName = displayName;
            Description = description;
            OnChanged = onChanged;
            Group = group;
        }
    }
}