using System;

namespace JmcModLib.Config
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class ConfigAttribute : Attribute
    {
        public string DisplayName { get; }
        public string? Description { get; }
        public string? OnChanged { get; }

        // 新增 Group 可选参数（来自你的选择 5:B）
        public string? Group { get; }

        public ConfigAttribute(string displayName,
                               string? description = null,
                               string? onChanged = null,
                               string? group = null)
        {
            DisplayName = displayName;
            Description = description;
            OnChanged = onChanged;
            Group = group;
        }
    }
}