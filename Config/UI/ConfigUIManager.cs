using JmcModLib.Config.UI.ModSetting;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace JmcModLib.Config.UI
{
    public static class ConfigUIManager
    {
        // Assembly → Group → UIEntry
        private static readonly Dictionary<Assembly, Dictionary<string, List<PendingUIEntry>>> _pending
            = new();

        public static void Register(ConfigEntry entry, UIConfigAttribute ui)
        {
            var asm = entry.assembly;
            if (!_pending.TryGetValue(asm, out var groups))
            {
                groups = new Dictionary<string, List<PendingUIEntry>>();
                _pending.Add(asm, groups);
            }

            var group = entry.Group;

            if (!groups.TryGetValue(group, out var list))
            {
                list = new List<PendingUIEntry>();
                groups.Add(group, list);
            }

            list.Add(new PendingUIEntry(entry, ui));
            // ModSettingLinker.initialized[asm] = false; 
            ModSettingLinker.initialized.TryAdd(asm, false);
        }


        internal static Dictionary<Assembly, Dictionary<string, List<PendingUIEntry>>> GetPending()
            => _pending;

        internal static Dictionary<string, List<PendingUIEntry>>? GetGroups(Assembly asm)
            => _pending.TryGetValue(asm, out var g) ? g : null;

        internal static IEnumerable<Assembly> GetAllAssemblies()
            => _pending.Keys;

        internal static bool ContainsAssembly(Assembly asm)
            => _pending.ContainsKey(asm);
    }


}
