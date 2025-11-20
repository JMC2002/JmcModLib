using Duckov.Modding;
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

        /// <summary>
        /// 调用Register注册元信息
        /// </summary>
        /// <param name="name">MOD的名称</param>
        /// <param name="version">MOD的版本号，留空或填null则会被默认置为1.0.0</param>
        /// <param name="level">期待显示的默认打印级别，留空则打印Info及以上</param>
        /// <param name="assembly">程序集，留空自动获取</param>
        public static void Register(ModInfo info, string? name = null, string? version = null, LogLevel level = LogLevel.Info, Assembly? assembly = null)
        {
            
            assembly ??= Assembly.GetCallingAssembly();
            _mods[assembly] = new Modinfo(info, name ?? info.name, version ?? "1.0.0", level);
            ModLogger.Debug($"[{GetTag(assembly)??"错误"}] 注册成功，info.displayName: {info.displayName}, name: {info.name}");

            OnRegistered?.Invoke(assembly);
        }

        /// <summary>
        /// 获取程序集的MOD信息，留空则返回调用者的信息
        /// </summary>
        public static Modinfo? GetModInfo(Assembly? assembly = null)
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
        public static string? GetTag(Assembly? assembly = null)
        {
            var info = GetModInfo(assembly);
            return info is null ? null : $"[{info.Name} v{info.Version}]";
        }

        /// <summary>
        /// Mod的元信息
        /// </summary>
        /// <param name="Name">Mod名</param>
        /// <param name="Version">Mod版本号</param>
        /// <param name="Level">Mod打印级别</param>
        public record Modinfo(ModInfo Info, string Name, string Version, LogLevel Level);
    }
}
