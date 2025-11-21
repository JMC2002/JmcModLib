using JmcModLib.Core;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace JmcModLib.Utils
{
    /// <summary>
    /// 打印级别
    /// </summary>
    public enum LogLevel
    {
        /// <summary>
        /// 主要用于打印出函数入函数
        /// </summary>
        Trace = 0,

        /// <summary>
        /// Debug
        /// </summary>
        Debug = 1,

        /// <summary>
        /// Info
        /// </summary>
        Info = 2,

        /// <summary>
        /// Warn
        /// </summary>
        Warn = 3,

        /// <summary>
        /// Error
        /// </summary>
        Error = 4,

        /// <summary>
        /// None
        /// </summary>
        None = 5
    }

    /// <summary>
    /// 一个打印类
    /// </summary>
    public static class ModLogger
    {
        private static bool ShouldLog(Assembly asm, LogLevel level)
        {
            var info = ModRegistry.GetModInfo(asm);
            if (info is null) return true; // 未注册的库默认输出
            return level >= info.Level;
        }

        private static string Format(Assembly asm, string level, string message, string caller, string file, int line)
        {
            string tag = ModRegistry.GetTag(asm) ?? "[UnknownMod]";
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            return $"{tag} [{timestamp}] [{level}] {caller} (L{line}): {message}";
        }

        private static void Log(LogLevel level, string message,
            Assembly asm,
            [CallerMemberName] string caller = "",
            [CallerFilePath] string file = "",
            [CallerLineNumber] int line = 0)
        {
            if (!ShouldLog(asm, level)) return;

            string text = Format(asm, level.ToString().ToUpper(), message, caller, file, line);
            switch (level)
            {
                case LogLevel.Trace:
                case LogLevel.Debug:
                case LogLevel.Info:
                    UnityEngine.Debug.Log(text);
                    break;

                case LogLevel.Warn:
                    UnityEngine.Debug.LogWarning(text);
                    break;

                case LogLevel.Error:
                    UnityEngine.Debug.LogError(text);
                    break;
            }
        }

        /// <summary>
        /// 使用Trace输出
        /// </summary>
        public static void Trace(string msg, Assembly? asm = null, [CallerMemberName] string caller = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
            => Log(LogLevel.Trace, msg, asm ?? Assembly.GetCallingAssembly(), caller, file, line);

        /// <summary>
        /// 使用Debug输出
        /// </summary>
        public static void Debug(string msg, Assembly? asm = null, [CallerMemberName] string caller = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
            => Log(LogLevel.Debug, msg, asm ?? Assembly.GetCallingAssembly(), caller, file, line);

        /// <summary>
        /// Info输出
        /// </summary>
        public static void Info(string msg, Assembly? asm = null, [CallerMemberName] string caller = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
            => Log(LogLevel.Info, msg, asm ?? Assembly.GetCallingAssembly(), caller, file, line);

        /// <summary>
        /// Warn输出
        /// </summary>
        public static void Warn(string msg, Exception? ex = null, Assembly? asm = null, [CallerMemberName] string caller = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
            => Log(LogLevel.Warn, msg + (ex != null ? $"\n{ex}" : ""), asm ?? Assembly.GetCallingAssembly(), caller, file, line);

        /// <summary>
        /// Error输出，其中若传递异常，会换行并输出异常
        /// </summary>
        public static void Error(string msg, Exception? ex = null, Assembly? asm = null, [CallerMemberName] string caller = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
            => Log(LogLevel.Error, msg + (ex != null ? $"\n{ex}" : ""), asm ?? Assembly.GetCallingAssembly(), caller, file, line);
    }
}