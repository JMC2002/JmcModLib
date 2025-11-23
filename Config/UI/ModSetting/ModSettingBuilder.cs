using Duckov.Modding;
using JmcModLib.Core;
using JmcModLib.Utils;
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace JmcModLib.Config.UI.ModSetting
{
    internal static class ModSettingBuilder
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

        internal static void FloatSliderBuild(ConfigEntry entry, UIFloatSliderAttribute uiAttr)
        {
            var asm = entry.Assembly;
            if (!TryGetModInfo(asm, out var info))
                return;

            Vector2 range = new(uiAttr.Min, uiAttr.Max);
            ModSettingAPI.AddSlider(info!,
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
            var asm = entry.Assembly;
            if (!TryGetModInfo(asm, out var info))
                return;

            ModSettingAPI.AddSlider(info,
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
            var asm = entry.Assembly;
            if (!TryGetModInfo(asm, out var info))
                return;

            ModSettingAPI.AddToggle(info,
                                    entry.Key,
                                    L10n.Get(entry.Attribute.DisplayName),
                                    (bool)ConfigManager.GetValue(entry)!,
                                    v => ConfigManager.SetValue(entry, v));
        }

        internal static void DropdownBuild(ConfigEntry entry)
        {
            var asm = entry.Assembly;
            if (!TryGetModInfo(asm, out var info))
                return;

            var enumType = entry.Accessor.MemberType;

            // 枚举的所有选项
            var options = Enum.GetNames(enumType).ToList();

            // 当前值
            var currentValue = ConfigManager.GetValue(entry)!;
            string defaultValue = Enum.GetName(enumType, currentValue)!;

            // 添加 UI
            ModSettingAPI.AddDropdownList(info,
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
            var asm = entry.Assembly;
            if (!TryGetModInfo(asm, out var info))
                return;

            ModSettingAPI.AddKeybinding(info,
                                        entry.Key,
                                        L10n.Get(entry.Attribute.DisplayName),
                                        (KeyCode)ConfigManager.GetValue(entry)!,
                                        (KeyCode)entry.DefaultValue!,
                                        v => ConfigManager.SetValue(entry, v));
        }

        internal static void InputBuild(ConfigEntry entry, UIInputAttribute uiAttr)
        {
            var asm = entry.Assembly;
            if (!TryGetModInfo(asm, out var info))
                return;

            ModSettingAPI.AddInput(info,
                                    entry.Key,
                                    L10n.Get(entry.Attribute.DisplayName),
                                    (string)ConfigManager.GetValue(entry)!,
                                    uiAttr.CharacterLimit,
                                    v => ConfigManager.SetValue(entry, v));
        }

        internal static void ButtonBuild(ButtonEntry entry, UIButtonAttribute uiAttr)
        {
            var asm = entry.Assembly;
            if (!TryGetModInfo(asm, out var info))
                return;
            
            ModSettingAPI.AddButton(info,
                                    entry.Key,
                                    L10n.Get(uiAttr.Description, asm),
                                    L10n.Get(uiAttr.ButtonText, asm),
                                    entry.Accessor.InvokeStaticVoid);   // 前期已经验证过是静态无参方法
        }

        internal static void BuildGroup(Assembly asm)
        {
            if (!TryGetModInfo(asm, out var info))
                return;

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
                        info,
                        g.UiGroupKey,
                        g.Description,
                        g.Keys
                    );

                    ModLogger.Trace(
                        $"已为 {ModRegistry.GetTag(asm)} 创建设置组 {g.UiGroupKey}，包含 {g.Keys.Count} 项"
                    );
                });
        }

        internal static void BuildEntry(PendingUIEntry<BaseEntry, UIBaseAttribute> pending)
        {
            try
            {
                pending.UIAttr.BuildUI(pending.Entry);
                ModLogger.Debug($"构建 UI 条目 {pending.Entry.Key} 到 ModSetting. " );
            }
            catch (Exception ex)
            {
                ModLogger.Error($"为 {pending.Entry.Key} 构建 UI 时异常", ex);
            }
        }

        internal static void BuildEntries(Assembly asm)
        {
            ModLogger.Trace($"{ModRegistry.GetTag(asm)} 进入BuildEntries");
            
            if (ConfigUIManager.GetGroups(asm) is not { } groups)
                return;

            groups
                .SelectMany(g => g.Value)
                .ToList()
                .ForEach(BuildEntry);
            ModLogger.Trace($"{ModRegistry.GetTag(asm)} 退出BuildEntries");
        }

        internal static void BuildReset(Assembly asm)
        {
            var modinfo = ModRegistry.GetModInfo(asm);
            if (!TryGetModInfo(asm, out var info))
                return;

            ModSettingAPI.AddButton(info,
                                    $"JmcModLibGen.{modinfo!.Name}.Reset",
                                    $"重置所有选项到默认值（重启游戏或）",
                                    L10n.Get("重置", Assembly.GetExecutingAssembly()),
                                    () => ConfigUIManager.ResetAsm(asm));   // 只重置注册了UI的Config
        }
    }
}