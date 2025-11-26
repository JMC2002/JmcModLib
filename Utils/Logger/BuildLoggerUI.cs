using System;
using System.Reflection;

namespace JmcModLib.Utils
{
    /// <summary>
    /// 标记要构建的日志配置UI元素
    /// </summary>
    [Flags]
    public enum LogConfigUIFlags
    {
        /// <summary>无 </summary>
        None = 0,
        /// <summary>日志等级选项下拉列表 </summary>
        LogLevel = 1 << 0,
        /// <summary>格式标志复选框 </summary>
        FormatFlags = 1 << 1,
        /// <summary>测试按钮 </summary>
        TestButtons = 1 << 2,
        /// <summary>默认选项（日志等级 + 格式标志） </summary>
        Default = LogLevel | FormatFlags,
        /// <summary>所有选项 </summary>
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
