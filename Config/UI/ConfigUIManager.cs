using Duckov.Modding;
using JmcModLib.Config.UI.ModSetting;
using JmcModLib.Reflection;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace JmcModLib.Config.UI
{
    internal static class ConfigUIManager
    {
        // Assembly → Group → UIEntry
        private static readonly Dictionary<Assembly, Dictionary<string, List<PendingUIEntry<BaseEntry, UIBaseAttribute>>>> _pending
            = new();

        internal static event Action<Assembly>? OnRegistered;

        internal static void Init()
        {
            ConfigManager.OnRegistered += Register; // 单个MOD扫描完后再决定是否广播
            ModSettingLinker.Init();
            
        }

        internal static void Dispose()
        {
            ConfigManager.OnRegistered -= Register;
            ModSettingLinker.Dispose();
        }

        public static void RegisterEntry(BaseEntry entry, UIBaseAttribute ui)
        {
            var asm = entry.assembly;
            if (!_pending.TryGetValue(asm, out var groups))
            {
                groups = new Dictionary<string, List<PendingUIEntry<BaseEntry, UIBaseAttribute>>>();
                _pending.Add(asm, groups);
            }

            var group = entry.Group;

            if (!groups.TryGetValue(group, out var list))
            {
                list = new List<PendingUIEntry<BaseEntry, UIBaseAttribute>>();
                groups.Add(group, list);
            }

            list.Add(new PendingUIEntry<BaseEntry, UIBaseAttribute>(entry, ui));
            //// ModSettingLinker.initialized[asm] = false; 
            //ModSettingLinker.initialized.TryAdd(asm, false);
            OnRegistered?.Invoke(asm);
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
            ModSettingLinker.UnRegister(asm);
            if (_pending.ContainsKey(asm))
            {
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
                            ConfigManager.ResetKey(cfg);
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
