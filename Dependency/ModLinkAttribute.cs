using System;
using System.Collections.Generic;
using System.Text;

namespace JmcModLib.Dependency
{
    /// <summary>
    /// 标记方法是MOD启用时还是禁用时的回调
    /// </summary>
    public enum ModLinkEvent {
        /// <summary>标记方法在MOD启用时调用 </summary>
        Activated,
        /// <summary>标记方法在MOD禁用时调用 </summary>
        Deactivated
    }

    /// <summary>
    /// 标记一个方法在指定MOD启用或禁用时调用
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class ModLinkAttribute(string name, ModLinkEvent ev) : Attribute
    {
        /// <summary>
        /// 查找的MOD名
        /// </summary>
        public string Name { get; } = name;
        /// <summary>
        /// 方法调用的时机
        /// </summary>
        public ModLinkEvent Event { get; } = ev;
    }

}
