using System;
using System.Reflection;

namespace JmcModLib.Utils
{
    [Flags]
    public enum LogConfigUIFlags
    {
        None = 0,
        LogLevel = 1 << 0,
        FormatFlags = 1 << 1,
        TestButtons = 1 << 2,
        Default = LogLevel | FormatFlags,
        All = LogLevel | FormatFlags | TestButtons
    }

    internal partial class BuildLoggerUI
    {
        private const string DefaultGroup = "调试选项";
        internal static void BuildUI(Assembly asm, LogConfigUIFlags flags)
        {
            if (flags.HasFlag(LogConfigUIFlags.LogLevel))
            {
                BuildLogLevelSettings.BuildUI(asm);
            }
            if (flags.HasFlag(LogConfigUIFlags.FormatFlags))
            {
                BuildFormatFlags.BuildUI(asm);
            }
            if (flags.HasFlag(LogConfigUIFlags.TestButtons))
            {
                BuildTestButtons.BuildUI(asm);
            }
        }
    }
}
