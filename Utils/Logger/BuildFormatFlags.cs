using JmcModLib.Config;
using JmcModLib.Config.UI;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace JmcModLib.Utils
{
    internal partial class BuildLoggerUI
    {
        private static class BuildFormatFlags
        {
            private static readonly Dictionary<LogFormatFlags, string> dict = new()
            {
                { LogFormatFlags.Timestamp,  "显示时间戳" },
                { LogFormatFlags.Tag,        "显示Mod标签" },
                { LogFormatFlags.Level,      "显示日志等级" },
                { LogFormatFlags.FilePath,   "显示文件路径" },
                { LogFormatFlags.Caller,     "显示调用函数" },
                { LogFormatFlags.LineNumber, "显示行号" }
            };

            private static void Factory(Assembly asm, LogFormatFlags flag)
            {
                var flagName = dict[flag];
                ConfigManager.RegisterConfig(new UIToggleAttribute(),
                                             flagName,
                                             () => ModLogger.HasFormatFlag(flag, asm),
                                             flg =>
                                             {
                                                 // 判断当前状态，避免用户调用API切换时与UI界面冲突
                                                 if (flg != ModLogger.HasFormatFlag(flag, asm))
                                                     ModLogger.ToggleFormatFlag(flag, asm);
                                             },
                                             DefaultGroup, null, asm);
            }

            internal static void BuildUI(Assembly asm)
            {
                foreach (var key in dict.Keys.OrderBy(k => (int)k))
                    Factory(asm, key);
            }
        }
    }
}
