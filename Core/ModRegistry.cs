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
        private static readonly Dictionary<string, Assembly> _pathToAssembly = new();

        /// <summary>
        /// 当一个 MOD 完成注册后触发。
        /// 参数：Assembly（唯一标识MOD）（该MOD元信息）
        /// </summary>
        internal static event Action<Assembly>? OnRegistered;

        /// <summary>
        /// 反注册 MOD 时触发。
        /// </summary>
        internal static event Action<Assembly>? OnUnRegistered;

        internal static void Init()
        {
            ConfigManager.Init();
            L10n.Init();
            ModManager.OnModWillBeDeactivated += TryUnRegistered;
        }

        internal static void Dispose()
        {
            ModManager.OnModWillBeDeactivated -= TryUnRegistered;
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
        public static void Register(ModInfo info, string? name = null, string? version = null,
                                    LogLevel level = ModLogger.DefaultLogLevel,
                                    LogFormatFlags tagFlags = LogFormatFlags.Default,
                                    LogConfigUIFlags uIFlags = LogConfigUIFlags.Default,
                                    Assembly? assembly = null)
        {
            assembly ??= Assembly.GetCallingAssembly();
            if (IsRegistered(assembly))
            {
                ModLogger.Warn($"{GetTag(assembly)} 重复注册");
                return;
            }

            if (string.IsNullOrEmpty(info.displayName))
            {
                ModLogger.Warn("ModInfo未初始化，应当在OnAfterSetup及以后注册，而非OnEnable及以前");
            }
            else
            {
                _pathToAssembly[info.path] = assembly;
            }
            _mods[assembly] = new Modinfo(info, name ?? info.displayName ?? assembly.FullName, version ?? "1.0.0");
            // ConfigManager.RegisterAllInAssembly(assembly);

            ModLogger.RegisterAssembly(assembly, level, tagFlags, uIFlags);
            OnRegistered?.Invoke(assembly);
            ModLogger.Debug($"{GetTag(assembly)} 注册成功");
        }

        /// <summary>
        /// 判断是否已注册
        /// </summary>
        public static bool IsRegistered()
            => IsRegistered(Assembly.GetCallingAssembly());

        /// <summary>
        /// 判断是否已注册
        /// </summary>
        public static bool IsRegistered(Assembly assembly)
            => _mods.ContainsKey(assembly);

        /// <summary>
        /// 反注册程序集的MOD信息，留空则反注册调用者的程序集
        /// </summary>
        public static void UnRegister(Assembly? assembly = null)
        {
            assembly ??= Assembly.GetCallingAssembly();
            if (!IsRegistered(assembly))
            {
                return; // 未注册则不进行任何操作
            }

            OnUnRegistered?.Invoke(assembly);
            if (GetModInfo(assembly) != null)
            {
                _pathToAssembly.Remove(GetModInfo(assembly)!.Info.path);
            }

            _mods.Remove(assembly);
        }

        private static void TryUnRegistered(ModInfo info, Duckov.Modding.ModBehaviour modBehaviour)
        {
            if (_pathToAssembly.TryGetValue(info.path, out var assembly))
            {
                UnRegister(assembly);
            }
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
            ModLogger.SetMinLevel(level, assembly);
        }

        /// <summary>
        /// 获取由程序集Mod名与版本号拼接成的标签，留空则返回调用者的Tag
        /// </summary>
        /// <returns> 返回$"[{info.Name} v{info.Version}]"，若未注册，则由assembly的Name与Version拼接 </returns>
        public static string GetTag(Assembly? assembly = null)
        {
            assembly ??= Assembly.GetCallingAssembly();
            var info = GetModInfo(assembly);
            return info is null ? $"[{assembly.GetName().Name} v{assembly.GetName().Version}]" 
                                : $"[{info.Name} v{info.Version}]";
        }

        /// <summary>
        /// Mod的元信息
        /// </summary>
        /// <param name="Info">Mod的详细信息</param>
        /// <param name="Name">Mod名</param>
        /// <param name="Version">Mod版本号</param>
        internal record Modinfo(ModInfo Info, string Name, string Version);
    }
}