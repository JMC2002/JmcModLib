using System;

namespace JmcModLib.Config
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class ConfigAttribute : Attribute
    {
        public string DisplayName { get; }
        public string? Description { get; }
        public string? OnChanged { get; }

        public string Group { get; }
        public const string DefaultGroup = "DefaultGroup";

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