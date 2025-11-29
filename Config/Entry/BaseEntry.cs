using System;
using System.Reflection;

namespace JmcModLib.Config.Entry
{
    /// <summary>
    /// 所有配置条目的基类
    /// </summary>
    internal abstract class BaseEntry(Assembly asm, string group, string displayName, Assembly? l10nAsm)
    {
        internal Assembly Assembly { get; } = asm;
        internal Assembly L10nAssembly { get;  } = l10nAsm ?? asm;
        internal virtual string Key { get; } = GetKey(displayName, group);
        internal string Group { get; } = group;
        internal string DisplayName { get; } = displayName;

        /// <summary>
        /// 通过 DeclaringType 和 Name 生成唯一 Key（当前asm下唯一）
        /// </summary>
        /// <param name="declaringType">变量所在的类的类型</param>
        /// <param name="Name">变量的名称</param>
        /// <returns>返回一个形如{declaringType.FullName}.{Name}的唯一Key</returns>
        public static string GetKey(Type declaringType, string Name) =>
            $"{declaringType.FullName}.{Name}";

        public static string GetKey(string DisplayName, string Group = ConfigAttribute.DefaultGroup) =>
            $"{Group}.{DisplayName}";
    }
}