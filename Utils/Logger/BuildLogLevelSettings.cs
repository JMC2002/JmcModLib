using JmcModLib.Config;
using System.Reflection;
using JmcModLib.Config.UI;

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
