using JmcModLib.Config.Entry;
using JmcModLib.Config.UI.ModSetting;
using JmcModLib.Utils;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace JmcModLib.Config.UI
{
    internal static class ConfigUIManager
    {
        // Assembly → GroupName → UIEntry
        private static readonly Dictionary<Assembly, Dictionary<string, List<PendingUIEntry<BaseEntry, UIBaseAttribute>>>> _pending
            = [];

		// 扫描完成后基于程序集的广播
		internal static event Action<Assembly>? OnRegistered;
		// 每次注册单条 UI 时的广播
		internal static event Action<PendingUIEntry<BaseEntry, UIBaseAttribute>>? OnEntryRegistered;

        internal static void Init()
        {
            ConfigManager.OnRegistered += Register; // 单个MOD扫描完后再决定是否广播
            ModSettingLinker.Init();
        }

        internal static void Dispose()
        {
            ConfigManager.OnRegistered -= Register;
            ModSettingLinker.Dispose();
            _pending.Clear();
        }

        public static void RegisterEntry(BaseEntry entry, UIBaseAttribute ui)
        {
            var asm = entry.Assembly;
            if (!_pending.TryGetValue(asm, out var groups))
            {
                groups = [];
                _pending.Add(asm, groups);
            }

            var group = entry.Group;

            if (!groups.TryGetValue(group, out var list))
            {
                list = [];
                groups.Add(group, list);
            }

			var pending = new PendingUIEntry<BaseEntry, UIBaseAttribute>(entry, ui);
			list.Add(pending);
			// 每条 UI 注册都单独广播
			OnEntryRegistered?.Invoke(pending);
        }

        private static bool IsRegistered(Assembly asm)
        {
            return _pending.ContainsKey(asm);
        }

        /// <summary>
        /// 若ASM存在条目，广播此ASM
        /// </summary>
        private static void Register(Assembly asm)
        {
            if (IsRegistered(asm))
            {
                OnRegistered?.Invoke(asm);
            }
        }

        internal static void Unregister(Assembly asm)
        {
            if (_pending.ContainsKey(asm))
            {
                ModSettingLinker.UnRegister(asm);
                _pending.Remove(asm);
            }
        }

        internal static void ResetAsm(Assembly asm)
        {
            if (IsRegistered(asm))
            {
                foreach (var group in _pending[asm].Values)
                {
                    foreach (var entry in group)
                    {
                        if (entry.Entry is ConfigEntry cfg)
                        {
                            cfg.Reset();
                        }
                    }
                }
            }
        }

        internal static Dictionary<Assembly, Dictionary<string, List<PendingUIEntry<BaseEntry, UIBaseAttribute>>>> GetPending()
            => _pending;

        internal static Dictionary<string, List<PendingUIEntry<BaseEntry, UIBaseAttribute>>>? GetGroups(Assembly asm)
            => _pending.TryGetValue(asm, out var g) ? g : null;

        internal static IEnumerable<Assembly> GetAllAssemblies()
            => _pending.Keys;

        internal static bool ContainsAssembly(Assembly asm)
            => _pending.ContainsKey(asm);
    }
}