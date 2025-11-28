using Duckov.Modding;
using JmcModLib.Core;
using JmcModLib.Reflection;
using JmcModLib.Utils;
using System;
using System.Collections.Concurrent;
using System.Reflection;
using UnityEngine.UIElements;

namespace JmcModLib.Dependency
{
    /// <summary>
    /// 当某个 MOD 激活/停用时，执行相关联的操作。
    /// 现代化、线程安全实现。
    /// </summary>
    /// <remarks>
    /// 使用[ModLinker(MODNAME, ModLinkEvent.Activated)]等属性注册回调，回调函数必须是静态方法，参数可以是 (ModInfo info, ModBehaviour behaviour)、(ModInfo info) 或无参数，返回值将被忽略。
    /// </remarks>
    internal static class ModLinker
    {
        // 每个 modName -> (Assembly -> ModActions)
        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<Assembly, ModActions>> _modActions
            = new(StringComparer.OrdinalIgnoreCase);

        // 反向映射：Assembly -> 注册过的 modName 列表（用于卸载时清理）
        private static readonly ConcurrentDictionary<Assembly, ConcurrentBag<string>> _assemblyModMap
            = new();

        // 缓存事件委托，以保证能够正确解绑
        private static Action<ModInfo, Duckov.Modding.ModBehaviour>? _onActivatedHandler;
        private static Action<ModInfo, Duckov.Modding.ModBehaviour>? _onDeactivatedHandler;

        private static volatile bool _initialized = false;

        private sealed class ModActions(MethodAccessor? onActivated, MethodAccessor? onDeactivated)
        {

            public MethodAccessor? OnActivated { get; set; } = onActivated;
            public MethodAccessor? OnDeactivated { get; set; } = onDeactivated;

            public bool HasAccessor(ModLinkEvent linkEvent)
            {
                return linkEvent switch
                {
                    ModLinkEvent.Activated => OnActivated is not null,
                    ModLinkEvent.Deactivated => OnDeactivated is not null,
                    _ => throw new ArgumentException(nameof(linkEvent), "未知的 ModLinkEvent 类型"),
                };
            }

            public void SetAccessor(ModLinkEvent linkEvent, MethodAccessor? method)
            {
                if (linkEvent == ModLinkEvent.Activated)
                    OnActivated = method;
                else if (linkEvent == ModLinkEvent.Deactivated)
                    OnDeactivated = method;
                else
                    throw new ArgumentException(nameof(linkEvent), "未知的 ModLinkEvent 类型");
            }

            public void Invoke(ModLinkEvent linkEvent, ModInfo info, Duckov.Modding.ModBehaviour behaviour)
            {
                var method = linkEvent switch
                {
                    ModLinkEvent.Activated => OnActivated,
                    ModLinkEvent.Deactivated => OnDeactivated,
                    _ => null,
                };
                if (method is null) return;

                object?[] args;
                if (method.MemberInfo.GetParameters().Length == 2)
                {
                    args = [info, behaviour];
                }
                else if (method.MemberInfo.GetParameters().Length == 1)
                {
                    args = [info];
                }
                else
                {
                    args = [];
                }

                method.Invoke(null, args);
            }
        }

        internal static void Init()
        {
            if (_initialized)
            {
                ModLogger.Warn("ModLinker: 已经初始化，忽略重复 Init()");
                return;
            }

            _onActivatedHandler = CheckerBuilder(ModLinkEvent.Activated);
            _onDeactivatedHandler = CheckerBuilder(ModLinkEvent.Deactivated);

            ModManager.OnModActivated += _onActivatedHandler;
            ModManager.OnModWillBeDeactivated += _onDeactivatedHandler;

            _initialized = true;
            ModLogger.Info("ModLinker: 初始化完成");
        }

        internal static void Dispose()
        {
            if (!_initialized)
            {
                return;
            }

            if (_onActivatedHandler != null)
                ModManager.OnModActivated -= _onActivatedHandler;
            if (_onDeactivatedHandler != null)
                ModManager.OnModWillBeDeactivated -= _onDeactivatedHandler;

            // 清理本地缓存（不强制触发注销回调）
            _modActions.Clear();
            _assemblyModMap.Clear();

            _initialized = false;
            ModLogger.Info("ModLinker: 已释放");
        }

        internal static bool TryRegister(string modName, MethodAccessor method, ModLinkEvent linkEvent, Assembly? asm = null, bool allowOverwrite = true)
        {
            asm ??= Assembly.GetCallingAssembly();

            var asmMap = _modActions.GetOrAdd(modName, _ => new ConcurrentDictionary<Assembly, ModActions>());
            if (!asmMap.TryGetValue(asm, out var modActions))
            {
                modActions = new ModActions(null, null);
                asmMap[asm] = modActions;
            }

            if (modActions.HasAccessor(linkEvent) && !allowOverwrite)
            {
                ModLogger.Warn($"在 {ModRegistry.GetTag(asm)} 尝试重复为 MOD {modName} 注册 {(linkEvent == ModLinkEvent.Activated ? "激活" : "停用")} 方法（未覆盖）");
                return false;
            }

            modActions.SetAccessor(linkEvent, method);


            // 更新反向映射
            var bag = _assemblyModMap.GetOrAdd(asm, _ => []);
            bag.Add(modName);

            ModLogger.Debug($"{ModRegistry.GetTag(asm)} 为 MOD {modName} 注册了 {linkEvent} 回调");

            return true;
        }

        /// <summary>
        /// 移除特定 (modName, asm) 注册。
        /// </summary>
        internal static bool Unregister(string modName, Assembly? asm = null)
        {
            if (string.IsNullOrWhiteSpace(modName))
                return false;

            asm ??= Assembly.GetCallingAssembly();

            if (!_modActions.TryGetValue(modName, out var asmMap))
                return false;

            if (!asmMap.TryRemove(asm, out _))
                return false;

            // 反向映射：从 bag 中无法直接移除单一元素（ConcurrentBag 不支持移除），
            // 所以我们重建该 assembly 的映射（把剩余的 modName 重新写入）
            _ = RebuildAssemblyModMap(asm);

            // 如果 asmMap 变空，移除 modName 条目
            if (asmMap.IsEmpty)
                _modActions.TryRemove(modName, out _);

            ModLogger.Debug($"ModLinker: 已为 {ModRegistry.GetTag(asm)} 取消注册 MOD {modName}");
            return true;
        }

        /// <summary>
        /// 卸载某个 Assembly（例如 MOD 卸载时调用），清理所有与该 Assembly 相关的注册。
        /// </summary>
        internal static void UnregisterAssembly(Assembly asm)
        {
            if (asm == null) return;

            // 获取该 asm 曾注册过的 modNames 快照
            if (!_assemblyModMap.TryRemove(asm, out var modNamesBag))
                return;

            var modNames = modNamesBag.ToArray(); // Bag snapshot

            foreach (var modName in modNames)
            {
                if (_modActions.TryGetValue(modName, out var asmMap))
                {
                    asmMap.TryRemove(asm, out _);
                    if (asmMap.IsEmpty)
                    {
                        _modActions.TryRemove(modName, out _);
                    }
                }
            }

            ModLogger.Info($"ModLinker: 已清理 Assembly {ModRegistry.GetTag(asm)} 的所有注册项");
        }

        /// <summary>
        /// 构造事件回调（会返回缓存的委托实例供 Init/Dispose 使用）
        /// </summary>
        private static Action<ModInfo, Duckov.Modding.ModBehaviour> CheckerBuilder(ModLinkEvent linkEvent)
        {
            return (info, behaviour) => Checker(info, behaviour, linkEvent);
        }

        /// <summary>
        /// 当 ModManager 触发激活/停用事件时调用。
        /// 注意：遍历时使用 snapshot 以保持线程安全与一致性。
        /// </summary>
        private static void Checker(ModInfo info, Duckov.Modding.ModBehaviour behaviour, ModLinkEvent linkEvent)
        {
            var modName = info.name;
            if (string.IsNullOrWhiteSpace(modName)) return;

            if (!_modActions.TryGetValue(modName, out var asmMap))
                return;

            // snapshot 当前 asmMap 的键值对，避免并发修改问题
            var snapshot = asmMap.ToArray();

            foreach (var kv in snapshot)
            {
                var asm = kv.Key;
                var actions = kv.Value;
                try
                {
                    actions.Invoke(linkEvent, info, behaviour);
                }
                catch (Exception ex)
                {
                    // 捕获并记录，防止单个回调抛异常影响其他回调
                    ModLogger.Error($"ModLinker: 在为 {ModRegistry.GetTag(asm)} 执行 {(linkEvent == ModLinkEvent.Activated ? "激活" : "停用")} 回调时发生异常", ex);
                }
            }
        }

        /// <summary>
        /// 由于 ConcurrentBag 不支持移除单个元素，这里 rebuild 指定 assembly 的 modName 列表。
        /// 该方法会遍历 _modActions 重新生成该 assembly 的反向映射 Bag（保守重建）。
        /// </summary>
        private static bool RebuildAssemblyModMap(Assembly asm)
        {
            if (asm == null) return false;

            var newBag = new ConcurrentBag<string>();

            foreach (var (modName, asmMap) in _modActions)
            {
                if (asmMap.ContainsKey(asm))
                    newBag.Add(modName);
            }


            // 替换或移除（如果为空则不保留）
            if (newBag.IsEmpty)
            {
                _assemblyModMap.TryRemove(asm, out _);
            }
            else
            {
                _assemblyModMap[asm] = newBag;
            }

            return true;
        }

        /// <summary>
        /// 调试用：导出当前注册状态（小心调用，不要在高频路径使用）。
        /// </summary>
        internal static string DumpState()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("ModLinker Dump:");
            foreach (var kv in _modActions)
            {
                sb.AppendLine($" MOD: {kv.Key}");
                foreach (var asmKv in kv.Value)
                {
                    sb.AppendLine($"   - ASM: {ModRegistry.GetTag(asmKv.Key)} (HasActivate:{asmKv.Value.OnActivated != null}, HasDeactivate:{asmKv.Value.OnDeactivated != null})");
                }
            }
            return sb.ToString();
        }
    }
}
