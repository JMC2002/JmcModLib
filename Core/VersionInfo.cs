using Duckov.Modding;

namespace JmcModLib.Core
{
    internal static class VersionInfo
    {
        internal const string Name = "JmcModLib";
        internal const string Version = "1.4.0";

        internal static ModInfo modInfo;
        internal static string Tag => $"[{Name} v{Version}]";
    }
}