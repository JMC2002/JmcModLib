using JmcModLib.Config.Entry;
using JmcModLib.Core;
using JmcModLib.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace JmcModLib.Config.UI.ModConfig
{
    internal static class ModConfigBuilder
    {
        // 辅助获取 mod name
        private static string GetModName(Assembly asm) => ModRegistry.GetModInfo(asm)?.Name ?? asm.GetName().Name;

        internal static void FloatSliderBuild(ConfigEntry<float> entry, UIFloatSliderAttribute uiAttr)
        {
            var modName = GetModName(entry.Assembly);
            ModConfigAPI.AddInputWithSlider(
                modName,
                entry.Key,
                L10n.Get(entry.DisplayName, entry.L10nAssembly),
                typeof(float),
                entry.GetTypedValue(),
                new Vector2(uiAttr.Min, uiAttr.Max)
            );
        }

        internal static void IntSliderBuild(ConfigEntry<int> entry, UIIntSliderAttribute uiAttr)
        {
            var modName = GetModName(entry.Assembly);
            // IntSlider 在 ModConfig 也是用 AddInputWithSlider，只是类型传 int，Range 也是 Vector2
            ModConfigAPI.AddInputWithSlider(
                modName,
                entry.Key,
                L10n.Get(entry.DisplayName, entry.L10nAssembly),
                typeof(int),
                entry.GetTypedValue(),
                new Vector2(uiAttr.Min, uiAttr.Max)
            );
        }

        internal static void ToggleBuild(ConfigEntry<bool> entry)
        {
            var modName = GetModName(entry.Assembly);
            // ModConfig 没有原生 Toggle，用 BoolDropdown 代替
            ModConfigAPI.AddBoolDropdownList(
                modName,
                entry.Key,
                L10n.Get(entry.DisplayName, entry.L10nAssembly),
                entry.GetTypedValue()
            );
        }

        internal static void InputBuild(ConfigEntry<string> entry, UIInputAttribute uiAttr)
        {
            var modName = GetModName(entry.Assembly);
            // 纯文本输入，range 传 null
            ModConfigAPI.AddInputWithSlider(
                modName,
                entry.Key,
                L10n.Get(entry.DisplayName, entry.L10nAssembly),
                typeof(string),
                entry.GetTypedValue(),
                null
            );
        }

        internal static void DropdownBuild(ConfigEntry<string> entry, UIDropdownAttribute uiAttr)
        {
            var modName = GetModName(entry.Assembly);
            var type = entry.LogicalType;

            if (type.IsEnum)
            {
                // 构建 ModConfig 需要的 SortedDictionary<string, object>
                // Key 是显示文本，Value 是存储值
                var options = new SortedDictionary<string, object>();

                // 处理排除项
                HashSet<string>? exclude = uiAttr.Exclude is { Length: > 0 }
                    ? new HashSet<string>(uiAttr.Exclude, StringComparer.OrdinalIgnoreCase)
                    : null;

                foreach (var enumVal in Enum.GetValues(type))
                {
                    string name = enumVal.ToString()!;
                    if (exclude != null && exclude.Contains(name)) continue;

                    // 为了兼容性，Value 我们也存 string，因为 ConfigEntry<string> 期望 string
                    // 这样 ModConfig 读取时拿到的是 string，传给 Jmc 正好
                    options[name] = name;
                }

                ModConfigAPI.AddDropdownList(
                    modName,
                    entry.Key,
                    L10n.Get(entry.DisplayName, entry.L10nAssembly),
                    options,
                    typeof(string),
                    entry.GetTypedValue()
                );
            }
        }

        internal static void BuildEntry(PendingUIEntry<BaseEntry, UIBaseAttribute> pending)
        {
            if (pending.Entry is not ConfigEntry configEntry) return;

            try
            {
                // 构建 UI
                pending.UIAttr.BuildUI(configEntry);

                // 注册同步 (重要：这步在 ModSettingLinker 里叫 RegisterUISync)
                // 但因为我们是独立的 Linker，我们需要在这里通知 Linker 去注册事件
                ModConfigLinker.RegisterEntrySync(configEntry);

                ModLogger.Debug($"构建 UI 条目 {configEntry.Key} 到 ModConfig.");
            }
            catch (Exception ex)
            {
                ModLogger.Error($"为 {configEntry.Key} 构建 ModConfig UI 时异常", ex);
            }
        }
    }
}