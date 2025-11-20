namespace JmcModLib.Config.UI
{
    /// <summary>
    /// 一个用于存储待处理 UI 配置项的类。
    /// </summary>
    internal sealed class PendingUIEntry
    {
        public ConfigEntry Entry { get; }
        public UIConfigAttribute UIAttr { get; }

        public PendingUIEntry(ConfigEntry entry, UIConfigAttribute uiAttr)
        {
            Entry = entry;
            UIAttr = uiAttr;
        }
    }

}
