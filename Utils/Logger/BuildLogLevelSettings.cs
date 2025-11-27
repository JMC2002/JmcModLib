using JmcModLib.Config;
using JmcModLib.Config.UI;
using System.Reflection;

namespace JmcModLib.Utils
{

    internal partial class BuildLoggerUI
    {
        private static class BuildLogLevelSettings
        {
            internal static void BuildUI(Assembly asm)
            {
                //ConfigManager.RegisterConfig(new UIDropdownAttribute(),
                //                             "最低打印等级",
                //                             () => { return ModLogger.GetLogLevel(asm); },
                //                             lvl => { ModLogger.SetLogLevel(lvl, asm); },
                //                             DefaultGroup,
                //                             asm: asm);
                ConfigManager.RegisterConfig(new UIDropdownAttribute(),
                                             "最低打印等级",
                                             ModLogger.GetLogLevel(asm),
                                             DefaultGroup,
                                             lvl => { ModLogger.SetLogLevel(lvl, asm); },
                                             asm);
            }
        }
    }
}
