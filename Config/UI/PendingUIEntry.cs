namespace JmcModLib.Config.UI
{
    public sealed class PendingUIEntry
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
