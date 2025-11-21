using Duckov.Modding;
using JmcModLib.Config;
using JmcModLib.Utils;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace JmcModLib.Core
{
    /// <summary>
    /// 使用前应该先注册类的信息，
    /// <example>
    /// 示例：
    /// <code>
    /// ModRegistry.Register(VersionInfo.Name, VersionInfo.Version);
    /// </code>
    /// </example>
    /// </summary>
    public static class ModRegistry
    {
        private static readonly Dictionary<Assembly, Modinfo> _mods = new();

        /// <summary>
        /// 当一个 MOD 完成注册后触发。
        /// 参数：Assembly（唯一标识MOD）（该MOD元信息）
        /// </summary>
        internal static event Action<Assembly>? OnRegistered;

        internal static void Init()
        {
            ConfigManager.Init();
            L10n.Init();
        }

        internal static void Dispose()
        {
            ConfigManager.Dispose();
            L10n.Dispose();
            _mods.Clear();
            OnRegistered = null;
        }

        /// <summary>
        /// 调用Register注册元信息，至少需要在OnAfterSetup及以后调用
        /// </summary>
        /// <param name="info"> MOD的info信息，可以在OnAfterSetup及以后取得，OnEnable阶段该值未初始化，不可用 </param>
        /// <param name="name">MOD的名称，留空将在modinfo中取得，若也为空将在assembly中取得</param>
        /// <param name="version">MOD的版本号，留空或填null则会被默认置为1.0.0</param>
        /// <param name="level">期待显示的默认打印级别，留空则打印Info及以上</param>
        /// <param name="assembly">程序集，留空自动获取</param>
        public static void Register(ModInfo info, string? name = null, string? version = null, LogLevel level = LogLevel.Info, Assembly? assembly = null)
        {
            if (string.IsNullOrEmpty(info.displayName))
            {
                ModLogger.Warn("ModInfo未初始化，应当在OnAfterSetup及以后注册，而非OnEnable及以前");
            }
            assembly ??= Assembly.GetCallingAssembly();
            _mods[assembly] = new Modinfo(info, name ?? info.displayName ?? assembly.FullName, version ?? "1.0.0", level);
            // ConfigManager.RegisterAllInAssembly(assembly);

            ModLogger.Debug($"{GetTag(assembly)??"错误"} 注册成功");
            OnRegistered?.Invoke(assembly);
        }

        /// <summary>
        /// 获取程序集的MOD信息，留空则返回调用者的信息
        /// </summary>
        internal static Modinfo? GetModInfo(Assembly? assembly = null)
        {
            assembly ??= Assembly.GetCallingAssembly();
            return _mods.TryGetValue(assembly, out var info) ? info : null;
        }

        /// <summary>
        /// 设置程序集的打印等级，默认为调用者
        /// </summary>
        public static void SetLogLevel(LogLevel level, Assembly? assembly = null)
        {
            assembly ??= Assembly.GetCallingAssembly();
            if (_mods.TryGetValue(assembly, out var info))
            {
                _mods[assembly] = info with { Level = level };
            }
        }

        /// <summary>
        /// 获取由程序集Mod名与版本号拼接成的标签，留空则返回调用者的Tag
        /// </summary>
        /// <returns> 返回$"[{info.Name} v{info.Version}]"，若未注册，返回空 </returns>
        public static string? GetTag(Assembly? assembly = null)
        {
            var info = GetModInfo(assembly);
            return info is null ? null : $"[{info.Name} v{info.Version}]";
        }

        /// <summary>
        /// Mod的元信息
        /// </summary>
        /// <param name="Info">Mod的详细信息</param>
        /// <param name="Name">Mod名</param>
        /// <param name="Version">Mod版本号</param>
        /// <param name="Level">Mod打印级别</param>
        internal record Modinfo(ModInfo Info, string Name, string Version, LogLevel Level);
    }
}
