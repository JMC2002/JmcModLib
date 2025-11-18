using System;

namespace JmcModLib.Config
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class ConfigAttribute : Attribute
    {
        public string DisplayName { get; }
        public string? Description { get; }
        public string? OnChanged { get; }

        public ConfigAttribute(string displayName, string? description = null, string? onChanged = null)
        {
            DisplayName = displayName;
            Description = description;
            OnChanged = onChanged;
        }
    }

    /// <summary>是否需要持久化保存</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class PreserveConfigAttribute : Attribute { }
}
