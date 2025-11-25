using JmcModLib.Core;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using static UnityEngine.Rendering.DebugUI;

namespace JmcModLib.Utils
{

        

    /// <summary>
    /// 打印级别
    /// </summary>
    public enum LogLevel
    {
        /// <summary> 不打印任何，低于任何等级 </summary>
        None = int.MinValue,

        /// <summary> 主要用于打印出函数入函数 </summary>
        Trace = 1,

        /// <summary> Debug </summary>
        Debug = 2,

        /// <summary> Info </summary>
        Info = 3,

        /// <summary> Warn </summary>
        Warn = 4,

        /// <summary> Error </summary>
        Error = 5,

        /// <summary> Fatal 错误，在Debug模式下会抛出异常，在Release模式下会打印信息 </summary>
        Fatal = 6,

        /// <summary> 默认等级 </summary>
        Default = Info,

        /// <summary> 高于所有等级 </summary>
        All = int.MaxValue
    }

    /// <summary>
    /// 日志格式配置项（位标志）
    /// </summary>
    [Flags]
    public enum LogFormatFlags : uint
    {
        /// <summary> 不显示任何东西，占位 </summary>
        None = 0,
        /// <summary>显示时间戳</summary>
        Timestamp = 1 << 0,
        /// <summary>显示日志等级</summary>
        Level = 1 << 1,
        /// <summary>显示调用方法名</summary>
        Caller = 1 << 2,
        /// <summary>显示行号</summary>
        LineNumber = 1 << 3,
        /// <summary>显示文件路径</summary>
        FilePath = 1 << 4,
        /// <summary>显示 TAG（从 ModRegistry 获取）</summary>
        Tag = 1 << 5,

        /// <summary>默认格式：TAG + 时间戳 + 等级 + 调用方法 + 行号</summary>
        Default = Tag | Timestamp | Level | Caller | LineNumber,
        /// <summary>完整格式：包含所有信息</summary>
        All = Tag | Timestamp | Level | Caller | LineNumber | FilePath,
        /// <summary>精简格式：只有等级和消息</summary>
        Minimal = Level
    }

    /// <summary>
    /// 单个 Assembly 的日志配置
    /// </summary>
    public class AssemblyLoggerConfig
    {
        /// <summary>
        /// 该 Assembly 的最低输出等级
        /// </summary>
        public LogLevel MinLevel { get; set; } = LogLevel.Info;

        /// <summary>
        /// 日志格式配置
        /// </summary>
        public LogFormatFlags FormatFlags { get; set; } = LogFormatFlags.Default;
    }

    /// <summary>
    /// 一个打印类
    /// </summary>
    public static partial class ModLogger
    {
        // 全局配置
        private static readonly LogLevel _globalMinLevel = LogLevel.Info;
        private static readonly LogFormatFlags _globalFormatFlags = LogFormatFlags.Default;
        private static readonly Dictionary<Assembly, AssemblyLoggerConfig> _assemblyConfigs = [];
        private static readonly Dictionary<Assembly, bool> DebugCache = [];

        private static bool IsAssemblyDebugBuild(Assembly asm)
        {
            if (DebugCache.TryGetValue(asm, out var result))
                return result;

            var attr = asm.GetCustomAttribute<System.Diagnostics.DebuggableAttribute>();
            result = attr != null && (attr.IsJITTrackingEnabled || !attr.IsJITOptimizerDisabled);
            DebugCache[asm] = result;
            return result;
        }

        /// <summary>
        /// 获取或创建指定 Assembly 的配置
        /// </summary>
        private static AssemblyLoggerConfig GetOrCreateConfig(Assembly asm)
        {
            if (!_assemblyConfigs.TryGetValue(asm, out var config))
            {
                config = new AssemblyLoggerConfig();
                _assemblyConfigs[asm] = config;
                Debug($"为 {ModRegistry.GetTag(asm)} 新建日志配置成功");
            }
            return config;
        }

        /// <summary>
        /// 注册 Assembly 的元信息（供 ModRegistry 调用）
        /// </summary>
        internal static void RegisterAssembly(Assembly assembly, LogLevel minLevel,
                                              LogFormatFlags logFormat,
                                              LogConfigUIFlags buildFlags)
        {
            Fatal(new ArgumentNullException(nameof(assembly)), "尝试为 null Assembly 注册日志配置");
            var config = GetOrCreateConfig(assembly);
            config.MinLevel = minLevel;
            SetFormatFlags(logFormat, assembly);
            BuildLoggerUI.BuildUI(assembly, buildFlags);
        }

        /// <summary>
        /// 反注册 Assembly（供 ModRegistry 调用）
        /// </summary>
        internal static void UnregisterAssembly(Assembly assembly)
        {
            if (assembly == null) return;
            _assemblyConfigs.Remove(assembly);
        }

        /// <summary>
        /// 设置当前调用 Assembly 的最低日志等级
        /// </summary>
        public static void SetMinLevel(LogLevel level, Assembly? asm = null)
        {
            asm ??= Assembly.GetCallingAssembly();
            var config = GetOrCreateConfig(asm);
            config.MinLevel = level;
        }

        /// <summary>
        /// 设置当前调用 Assembly 的日志格式
        /// </summary>
        public static void SetFormatFlags(LogFormatFlags flags, Assembly? asm = null)
        {
            asm ??= Assembly.GetCallingAssembly();
            var config = GetOrCreateConfig(asm);
            config.FormatFlags = flags;
        }

        /// <summary>
        /// 判断当前调用 Assembly 是否包含指定日志格式标志
        /// </summary>
        public static bool HasFormatFlag(LogFormatFlags flag, Assembly? asm = null)
        {
            asm ??= Assembly.GetCallingAssembly();
            var config = GetOrCreateConfig(asm);
            return (config.FormatFlags & flag) != 0;
        }

        /// <summary>
        /// 将当前调用 Assembly 的指定日志格式标志取反
        /// </summary>
        public static void ToggleFormatFlag(LogFormatFlags flag, Assembly? asm = null)
        {
            asm ??= Assembly.GetCallingAssembly();
            var config = GetOrCreateConfig(asm);
            config.FormatFlags ^= flag;
        }

        /// <summary>
        /// 获取当前调用 Assembly 的最低日志等级，若不存在，则设为默认并返回
        /// </summary>
        /// <param name="asm"> 留空则获取调用者 Assembly 的配置 </param>
        /// <returns>
        /// 返回日志等级，若未注册则设置为默认等级并返回
        /// </returns>
        public static LogLevel GetLogLevel(Assembly? asm = null)
        {
            asm ??= Assembly.GetCallingAssembly();
            if (_assemblyConfigs.TryGetValue(asm, out var config))
            {
                SetMinLevel(LogLevel.Default, asm);
            }
            return config.MinLevel;
        }

        /// <summary>
        /// 获取当前调用 Assembly 的日志格式配置
        /// </summary>
        /// <param name="asm"> 留空则获取调用者 Assembly 的配置 </param>
        /// <returns>
        /// 返回日志格式配置，若未注册则返回全局配置
        /// </returns>
        public static LogFormatFlags GetFormatFlags(Assembly? asm = null)
        {
            asm ??= Assembly.GetCallingAssembly();
            if (_assemblyConfigs.TryGetValue(asm, out var config))
            {
                return config.FormatFlags;
            }
            return _globalFormatFlags;
        }

        /// <summary>
        /// 判断是否应该输出日志
        /// </summary>
        private static bool ShouldLog(Assembly asm, LogLevel level, out LogFormatFlags formatFlags)
        {
            // 获取该 Assembly 的配置（如果没有则使用全局配置）
            bool hasConfig = _assemblyConfigs.TryGetValue(asm, out var config);

            // 获取格式配置
            formatFlags = hasConfig ? config.FormatFlags : _globalFormatFlags;

            // 获取有效的日志等级
            LogLevel effectiveLevel = hasConfig ? config.MinLevel : _globalMinLevel;

            return level >= effectiveLevel;
        }

        /// <summary>
        /// 根据格式配置格式化输出内容
        /// </summary>
        private static string Format(Assembly asm, LogFormatFlags formatFlags, string level, string? message, string caller, string file, int line)
        {
            var parts = new System.Text.StringBuilder();

            // TAG
            if ((formatFlags & LogFormatFlags.Tag) != 0)
            {
                parts.Append(ModRegistry.GetTag(asm));
                parts.Append(' ');
            }

            // 时间戳
            if ((formatFlags & LogFormatFlags.Timestamp) != 0)
            {
                parts.Append('[');
                parts.Append(DateTime.Now.ToString("HH:mm:ss"));
                parts.Append("] ");
            }

            // 日志等级
            if ((formatFlags & LogFormatFlags.Level) != 0)
            {
                parts.Append('[');
                parts.Append(level);
                parts.Append("] ");
            }

            // 文件路径
            if ((formatFlags & LogFormatFlags.FilePath) != 0 && !string.IsNullOrEmpty(file))
            {
                parts.Append(System.IO.Path.GetFileName(file));
                parts.Append(" -> ");
            }

            // 调用方法名
            if ((formatFlags & LogFormatFlags.Caller) != 0 && !string.IsNullOrEmpty(caller))
            {
                parts.Append(caller);
            }

            // 行号
            if ((formatFlags & LogFormatFlags.LineNumber) != 0 && line > 0)
            {
                parts.Append(" (L");
                parts.Append(line);
                parts.Append(')');
            }

            // 分隔符和消息
            if (parts.Length > 0)
            {
                parts.Append(": ");
            }
            
            if (!string.IsNullOrEmpty(message))
            {
                parts.Append(message);
            }

            return parts.ToString();
        }

        /// <summary>
        /// 手动指定等级输出日志，为空不打印
        /// </summary>
        public static void Log(LogLevel? level = null, string? message = null,
            Assembly? asm = null,
            [CallerMemberName] string caller = "",
            [CallerFilePath] string file = "",
            [CallerLineNumber] int line = 0)
        {
            asm ??= Assembly.GetCallingAssembly();
            if (level == null || !ShouldLog(asm, (LogLevel)level!, out var formatFlags)) return;

            string text = Format(asm, formatFlags, level.ToString().ToUpper(), message, caller, file, line);
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
        /// 使用Trace输出（使用调用 Assembly 的配置）
        /// </summary>
        public static void Trace(string? msg = null, Assembly? asm = null, [CallerMemberName] string caller = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            asm ??= Assembly.GetCallingAssembly();
            Log(LogLevel.Trace, msg, asm, caller, file, line);
        }

        /// <summary>
        /// 使用Debug输出（使用调用 Assembly 的配置）
        /// </summary>
        public static void Debug(string? msg = null, Assembly? asm = null, [CallerMemberName] string caller = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            asm ??= Assembly.GetCallingAssembly();
            Log(LogLevel.Debug, msg, asm, caller, file, line);
        }

        /// <summary>
        /// Info输出（使用调用 Assembly 的配置）
        /// </summary>
        public static void Info(string? msg = null, Assembly? asm = null, [CallerMemberName] string caller = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            asm ??= Assembly.GetCallingAssembly();
            Log(LogLevel.Info, msg, asm, caller, file, line);
        }

        /// <summary>
        /// Warn输出（使用调用 Assembly 的配置）
        /// </summary>
        public static void Warn(string? msg = null, Exception? ex = null, Assembly? asm = null, [CallerMemberName] string caller = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            asm ??= Assembly.GetCallingAssembly();
            Log(LogLevel.Warn, msg + (ex != null ? $"\n{ex}" : ""), asm, caller, file, line);
        }

        /// <summary>
        /// Error输出，其中若传递异常，会换行并输出异常（使用调用 Assembly 的配置）
        /// </summary>
        public static void Error(string? msg = null, Exception? ex = null, Assembly? asm = null, [CallerMemberName] string caller = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            asm ??= Assembly.GetCallingAssembly();
            Log(LogLevel.Error, msg + (ex != null ? $"\n{ex}" : ""), asm, caller, file, line);
        }

        /// <summary>
        /// Fatal输出，在Debug模式下会直接抛出异常，在Release模式下会打印异常信息
        /// </summary>
        /// <param name="ex"> 待处理的异常 </param>
        /// <param name="msg"> 打印的附加信息 </param>
        /// <param name="asm"> 程序集，留空则为调用者 </param>
        /// <param name="caller"> 调用者函数名，留空自动填充 </param>
        /// <param name="file"> 调用者函数名，留空自动填充 </param>
        /// <param name="line"> 调用者函数名，留空自动填充 </param>
        public static void Fatal(Exception ex, string? msg = null, Assembly? asm = null,
            [CallerMemberName] string caller = "",
            [CallerFilePath] string file = "",
            [CallerLineNumber] int line = 0)
        {
            asm ??= Assembly.GetCallingAssembly();

            if (IsAssemblyDebugBuild(asm))
            {
                Log(LogLevel.Fatal, msg, asm, caller, file, line);
                throw ex;
            }
            else
            {
                Log(LogLevel.Fatal, msg + (ex != null ? $"\n{ex}" : ""), asm, caller, file, line);
            }
        }

    }
}