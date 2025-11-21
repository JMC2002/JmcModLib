using JmcModLib.Config;
using JmcModLib.Config.UI;
using JmcModLib.Utils;

namespace JmcModLib.Core
{
    internal static class ModConfig
    {
        [UIDropdown]
        [Config("打印等级", onChanged: nameof(OnLogLevelChanged))]
        internal static LogLevel logLevel = LogLevel.Info;

        internal static void OnLogLevelChanged(LogLevel newValue)
        {
            ModRegistry.SetLogLevel(newValue);
            ModLogger.Info($"打印等级更改为 {newValue}");
        }
    }
}