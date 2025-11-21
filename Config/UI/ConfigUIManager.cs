using JmcModLib.Config.UI.ModSetting;
using System.Collections.Generic;
using System.Reflection;

namespace JmcModLib.Config.UI
{
    internal static class ConfigUIManager
    {
        // Assembly → Group → UIEntry
        private static readonly Dictionary<Assembly, Dictionary<string, List<PendingUIEntry>>> _pending
            = new();

        internal static void Init()
        {
            ModSettingLinker.Init();
        }

        internal static void Dispose()
        {
            ModSettingLinker.Dispose();
        }

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

        internal static void UnregisterAsm(Assembly asm)
        {
            ModSettingLinker.RemoveMod(asm);
            if (_pending.ContainsKey(asm))
            {
                _pending.Remove(asm);
            }
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
