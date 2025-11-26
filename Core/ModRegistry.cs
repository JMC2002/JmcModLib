using Duckov.Modding;
using JmcModLib.Config;
using JmcModLib.Utils;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace JmcModLib.Core
{


    /// <summary>
    /// MOD 注册管理器。
    /// 使用前应该先注册 MOD 信息。
    /// <example>
    /// 示例：
    /// <code>
    /// // 简单注册（自动完成）
    /// ModRegistry.Register(VersionInfo.ModInfo, "MyMod", "1.0.0");
    /// 
    /// // 链式注册（手动完成）
    /// ModRegistry.Register(true, VersionInfo.ModInfo, "MyMod", "1.0.0")
    ///            .RegisterL10n("Lang")
    ///            .RegisterLogger(LogLevel.Debug)
    ///            .Done();
    /// </code>
    /// </example>
    /// </summary>
    public static class ModRegistry
    {
        private static readonly Dictionary<Assembly, Modinfo> _mods = [];
        private static readonly Dictionary<string, Assembly> _pathToAssembly = [];

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
            _pathToAssembly.Clear();
            OnRegistered = null;
        }

        private static bool RegisterImpl(Assembly assembly, ModInfo info, string? name = null, string? version = null)
        {
            if (IsRegistered(assembly))
            {
                ModLogger.Warn($"{GetTag(assembly)} 重复注册");
                return false;
            }

            if (string.IsNullOrEmpty(info.displayName))
            {
                ModLogger.Warn("ModInfo未初始化，应当在OnAfterSetup及以后注册，而非OnEnable及以前");
            }
            else
            {
                _pathToAssembly[info.path] = assembly;
            }
            _mods[assembly] = new Modinfo(info, name ?? info.displayName ?? assembly.FullName, version ?? info.version ?? assembly.GetName().Version.ToString());
            return true;
        }

        /// <summary>
        /// 调用Register注册元信息并将其他模块按初始化注册
        /// </summary>
        /// <remarks>
        /// <para>至少需要在OnAfterSetup及以后（不能在OnEnable及以前）调用，否则info信息未初始化不可用</para>
        /// <para>当需要手动创建带组的UI条目或需要手动指定某些模块的初始化参数，需要阻塞注册以进行链式调用，请使用重载Register(bool deferredCompletion, ...)</para>
        /// </remarks>
        /// <param name="info"> MOD的info信息，可以在OnAfterSetup及以后取得，OnEnable阶段该值未初始化，不可用 </param>
        /// <param name="name">MOD的名称，留空将在modinfo中取得，若也为空将在assembly中取得</param>
        /// <param name="version">MOD的版本号，留空或填null则会在modinfo中取得，若也为空将在assembly中取得</param>
        /// <param name="assembly">程序集，留空自动获取</param>
        public static void Register(ModInfo info, string? name = null, string? version = null, Assembly? assembly = null)
        {
            assembly ??= Assembly.GetCallingAssembly();
            if (RegisterImpl(assembly, info, name, version))
                Done(assembly);
        }

        /// <summary>
        /// 调用Register注册元信息并阻塞自动注册以进行链式调用
        /// </summary>
        /// <remarks>
        /// <para>至少需要在OnAfterSetup及以后（不能在OnEnable及以前）调用，否则info信息未初始化不可用</para>
        /// <para>当需要手动创建带组的UI条目或需要手动指定某些模块的初始化参数，调用此重载版本，请使用重载Register(Modinfo info, ...)</para>
        /// </remarks>
        /// <param name="deferredCompletion"> 是否阻塞自动注册 </param>
        /// <param name="info"> MOD的info信息，可以在OnAfterSetup及以后取得，OnEnable阶段该值未初始化，不可用 </param>
        /// <param name="name">MOD的名称，留空将在modinfo中取得，若也为空将在assembly中取得</param>
        /// <param name="version">MOD的版本号，留空或填null则会被默认置为1.0.0</param>
        /// <param name="assembly"></param>
        /// <returns> 若已注册或未阻塞（deferredCompletion的值为false）返回空，否则返回一个链式构造器，构造完成后调用Done即可 </returns>
        public static RegistryBuilder? Register(bool deferredCompletion, ModInfo info, string? name = null, string? version = null,
                                                Assembly? assembly = null)
        {
            assembly ??= Assembly.GetCallingAssembly();
            if (deferredCompletion)
            {
                if (RegisterImpl(assembly, info, name, version))
                    return new RegistryBuilder(assembly);
                return null;
            }
            else
            {
                Register(info, name, version, assembly);
                return null;
            }
        }

        internal static void Done(Assembly assembly)
        {
            if (IsRegistered(assembly))
            {
                OnRegistered?.Invoke(assembly);
                ModLogger.Debug($"{GetTag(assembly)} 注册成功");
            }
            else
            {
                // 理论不应该发生
                ModLogger.Fatal(CreateNotRegisteredException(assembly));
            }
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
            return info is not null ? $"[{info.Name} v{info.Version}]"
                                    : GetFallbackTag(assembly);
        }

        private static string GetFallbackTag(Assembly assembly)
        {
            return $"[{assembly.GetName().Name} v{assembly.GetName().Version}]";
        }

        internal static ArgumentOutOfRangeException CreateNotRegisteredException(Assembly assembly, string? name = null,
                                                                                 [CallerMemberName] string caller = "") 
            => new(name ?? nameof(assembly), $"执行 {caller} 时: {GetFallbackTag(assembly)} 未注册");

        /// <summary>
        /// Mod的元信息
        /// </summary>
        /// <param name="Info">Mod的详细信息</param>
        /// <param name="Name">Mod名</param>
        /// <param name="Version">Mod版本号</param>
        internal record Modinfo(ModInfo Info, string Name, string Version);
    }
}