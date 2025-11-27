using JmcModLib.Config.Entry;

namespace JmcModLib.Config.UI
{
    /// <summary>
    /// 一个用于存储待处理 UI 配置项的类。
    /// </summary>
    internal sealed class PendingUIEntry<TEntry, TUIAttribute>
        where TEntry : BaseEntry
        where TUIAttribute : UIBaseAttribute
    {
        public TEntry Entry { get; }
        public TUIAttribute UIAttr { get; }

        public PendingUIEntry(TEntry entry, TUIAttribute uiAttr)
        {
            Entry = entry;
            UIAttr = uiAttr;
        }
    }
}