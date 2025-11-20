using Duckov.Modding;
using JmcModLib.Core;
using JmcModLib.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace JmcModLib.Config.UI.ModSetting
{
    internal static class ModSettingBuilder
    {

        internal static void FloatSliderBuild(ConfigEntry entry, UIFloatSliderAttribute uiAttr)
        {
            var asm = entry.assembly;
            var info = ModRegistry.GetModInfo(asm)?.Info;
            if (info == null)
            {
                ModLogger.Warn($"{ModRegistry.GetTag(asm)} 未初始化modinfo");
                return;
            }

            Vector2 range = new(uiAttr.Min, uiAttr.Max);
            ModSettingAPI.AddSlider((ModInfo)info, 
                                    entry.Key, 
                                    L10n.Get(entry.Attribute.DisplayName, asm), 
                                    (float)ConfigManager.GetValue(entry)!, 
                                    range, 
                                    (v) => ConfigManager.SetValue(entry, v), 
                                    uiAttr.DecimalPlaces, 
                                    uiAttr.CharacterLimit);
        }

        internal static void IntSliderBuild(ConfigEntry entry, UIIntSliderAttribute uiAttr)
        {
            var asm = entry.assembly;
            var info = ModRegistry.GetModInfo(asm)?.Info;
            if (info == null)
            {
                ModLogger.Warn($"{ModRegistry.GetTag(asm)} 未初始化modinfo");
                return;
            }

            ModSettingAPI.AddSlider((ModInfo)info,
                                    entry.Key,
                                    L10n.Get(entry.Attribute.DisplayName),
                                    (int)ConfigManager.GetValue(entry)!,
                                    uiAttr.Min,
                                    uiAttr.Max,
                                    v => ConfigManager.SetValue(entry, v),
                                    uiAttr.CharacterLimit);
        }

        internal static void ToggleBuild(ConfigEntry entry)
        {
            var asm = entry.assembly;
            var info = ModRegistry.GetModInfo(asm)?.Info;
            if (info == null)
            {
                ModLogger.Warn($"{ModRegistry.GetTag(asm)} 未初始化modinfo");
                return;
            }

            ModSettingAPI.AddToggle((ModInfo)info,
                                    entry.Key,
                                    L10n.Get(entry.Attribute.DisplayName),
                                    (bool)ConfigManager.GetValue(entry)!,
                                    v => ConfigManager.SetValue(entry, v));
        }

        internal static void DropdownBuild(ConfigEntry entry)
        {
            var asm = entry.assembly;
            var info = ModRegistry.GetModInfo(asm)?.Info;
            if (info == null)
            {
                ModLogger.Warn($"{ModRegistry.GetTag(asm)} 未初始化 modinfo");
                return;
            }

            var enumType = entry.Accessor.MemberType;

            // 枚举的所有选项
            var options = Enum.GetNames(enumType).ToList();

            // 当前值
            var currentValue = ConfigManager.GetValue(entry)!;
            string defaultValue = Enum.GetName(enumType, currentValue)!;

            // 添加 UI
            ModSettingAPI.AddDropdownList((ModInfo)info,
                                          entry.Key,
                                          L10n.Get(entry.Attribute.DisplayName),
                                          options,
                                          currentValue.ToString(),
                                          selected =>
                                          {
                                              // 用户改值 → 解析回 enum
                                              var parsed = Enum.Parse(enumType, selected, ignoreCase: true);
                                              ConfigManager.SetValue(entry, parsed);
                                          }
            );
        }

        internal static void KeyBindBuild(ConfigEntry entry)
        {
            var asm = entry.assembly;
            var info = ModRegistry.GetModInfo(asm)?.Info;
            if (info == null)
            {
                ModLogger.Warn($"{ModRegistry.GetTag(asm)} 未初始化modinfo");
                return;
            }

            ModSettingAPI.AddKeybinding((ModInfo)info,
                                        entry.Key,
                                        L10n.Get(entry.Attribute.DisplayName),
                                        (KeyCode)ConfigManager.GetValue(entry)!,
                                        (KeyCode)entry.DefaultValue!,
                                        v => ConfigManager.SetValue(entry, v));
        }

        internal static void InputBuild(ConfigEntry entry, UIInputAttribute uiAttr)
        {
            var asm = entry.assembly;
            var info = ModRegistry.GetModInfo(asm)?.Info;
            if (info == null)
            {
                ModLogger.Warn($"{ModRegistry.GetTag(asm)} 未初始化modinfo");
                return;
            }

            ModSettingAPI.AddInput((ModInfo)info,
                                    entry.Key,
                                    L10n.Get(entry.Attribute.DisplayName),
                                    (string)ConfigManager.GetValue(entry)!,
                                    uiAttr.CharacterLimit,
                                    v => ConfigManager.SetValue(entry, v));
        }

        internal static void BuildGroup(Assembly asm)
        {
            var modInfo = ModRegistry.GetModInfo(asm)?.Info;
            if (modInfo == null)
            {
                ModLogger.Warn($"{ModRegistry.GetTag(asm)} 无法建立组：modInfo 尚未注册");
                return;
            }

            var groups = ConfigUIManager.GetGroups(asm);
            if (groups == null)
                return;

            groups
                .Where(g => g.Key != ConfigAttribute.DefaultGroup && g.Value.Count > 0)
                .Select(g => new
                {
                    GroupName = g.Key,
                    Entries = g.Value,
                    UiGroupKey = $"{asm.GetName().Name}.{g.Key}",
                    Description = g.Key,
                    Keys = g.Value.Select(p => p.Entry.Key).ToList()
                })
                .ToList()
                .ForEach(g =>
                {
                    ModSettingAPI.AddGroup(
                        (ModInfo)modInfo,
                        g.UiGroupKey,
                        g.Description,
                        g.Keys
                    );

                    ModLogger.Trace(
                        $"已为 {ModRegistry.GetTag(asm)} 创建设置组 {g.UiGroupKey}，包含 {g.Keys.Count} 项"
                    );
                });
        }

        internal static void BuildEntries(Assembly asm)
        {
            ModLogger.Trace($"{ModRegistry.GetTag(asm)} 进入BuildEntries");
            var groups = ConfigUIManager.GetGroups(asm);
            if (groups == null)
                return;

            groups
                .SelectMany(g => g.Value)
                .ToList()
                .ForEach(p =>
                {
                    try
                    {
                        p.UIAttr.BuildUI(p.Entry);
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Error($"为 {p.Entry.Key} 构建 UI 时异常", ex);
                    }
                });
            ModLogger.Trace($"{ModRegistry.GetTag(asm)} 退出BuildEntries");
        }

    }
}
