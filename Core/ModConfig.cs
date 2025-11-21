using JmcModLib.Config;
using JmcModLib.Config.UI;
using JmcModLib.Utils;

namespace JmcModLib.Core
{
    internal static class ModConfig
    {
        [UIDropdown]
        [Config("打印等级", onChanged: nameof(onLogLevelChanged))]
        internal static LogLevel logLevel = LogLevel.Trace;

        internal static void onLogLevelChanged(LogLevel newValue)
        {
            ModRegistry.SetLogLevel(newValue);
            ModLogger.Info($"打印等级更改为 {newValue}");
        }
    }
}