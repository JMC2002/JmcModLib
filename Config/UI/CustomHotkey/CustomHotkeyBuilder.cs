using Duckov.Modding;
using JmcModLib.Config.Entry;
using JmcModLib.Core;
using JmcModLib.Utils;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace JmcModLib.Config.UI.CustomHotkey
{
    internal class CustomHotkeyBuilder
    {
        private static bool TryGetModInfo(Assembly asm, out ModInfo info, [CallerMemberName] string caller = "")
        {
            var mod = ModRegistry.GetModInfo(asm);
            if (mod?.Info == null)
            {
                info = default!;
                ModLogger.Warn($"{ModRegistry.GetTag(asm)} 无法执行 {caller}: 未初始化 modinfo");
                return false;
            }

            info = mod.Info;
            return true;
        }

        internal static void KeyBindBuild(ConfigEntry<KeyCode> entry)
        {
            var asm = entry.Assembly;
            if (!TryGetModInfo(asm, out var info))
                return;

            CustomHotkeyHelper.AddNewHotkey(info.name,
                                        entry.Key,
                                        entry.DefaultValue,
                                        L10n.Get(entry.DisplayName, asm));
            //,
            //                            entry.GetTypedValue(),
            //                            entry.SetTypedValue);
            CustomHotkeyHelper.SetKey(info.name, entry.Key, entry.GetTypedValue());
            CustomHotkeyHelper.TryAddEvent2OnCustomHotkeyChangedEvent(info.name, entry.SyncFromData);
        }
    }
}
