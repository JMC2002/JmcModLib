using JmcModLib.Config.UI;
using JmcModLib.Reflection;
using JmcModLib.Utils;
using System;
using System.Reflection;

namespace JmcModLib.Config.Entry
{
    /// <summary>
    /// Button 类型的配置项条目
    /// </summary>
    internal sealed class ButtonEntry : BaseEntry
    {
        private Action action1;
        public ButtonEntry(Assembly asm,
                           MethodAccessor method,
                           string group,
                           string displayName)
            : base(asm, group, displayName)
        {
            if (!UIButtonAttribute.IsValidMethod(method.Member, out var lvl, out var errorMessage))
                throw new ArgumentException($"方法不符合 UIButtonAttribute 的要求: {errorMessage}");
            ModLogger.Log(lvl, errorMessage);
            if (method.TypedDelegate is Action action)
                action1 = action;
            else
                action1 = method.InvokeStaticVoid;
        }

        /// <summary>
        /// Action 版本用于手动构建按钮
        /// </summary>
        public ButtonEntry(Assembly asm,
                           Action action,
                           string group,
                           string displayName)
            : base(asm, group, displayName)
        {
            action1 = action;
        }

        public void Invoke()
        {
            action1.Invoke();
        }
    }
}