using Duckov.Modding;
using JmcModLib.Config.Entry;
using JmcModLib.Core;
using JmcModLib.Utils;
using System;
using System.Collections.Generic;
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

        internal static void FloatSliderBuild(ConfigEntry<float> entry, UIFloatSliderAttribute uiAttr)
        {
            var asm = entry.Assembly;
            if (!TryGetModInfo(asm, out var info))
                return;

            Vector2 range = new(uiAttr.Min, uiAttr.Max);
            ModSettingAPI.AddSlider(info!,
                                    entry.Key,
                                    L10n.Get(entry.DisplayName, asm),
                                    entry.GetTypedValue(),
                                    range,
                                    entry.SetTypedValue,
                                    uiAttr.DecimalPlaces,
                                    uiAttr.CharacterLimit);
        }

        internal static void IntSliderBuild(ConfigEntry<int> entry, UIIntSliderAttribute uiAttr)
        {
            var asm = entry.Assembly;
            if (!TryGetModInfo(asm, out var info))
                return;

            ModSettingAPI.AddSlider(info,
                                    entry.Key,
                                    L10n.Get(entry.DisplayName),
                                    entry.GetTypedValue(),
                                    uiAttr.Min,
                                    uiAttr.Max,
                                    entry.SetTypedValue,
                                    uiAttr.CharacterLimit);
        }

        internal static void ToggleBuild(ConfigEntry<bool> entry)
        {
            var asm = entry.Assembly;
            if (!TryGetModInfo(asm, out var info))
                return;

            ModSettingAPI.AddToggle(info,
                                    entry.Key,
                                    L10n.Get(entry.DisplayName),
                                    entry.GetTypedValue(),
                                    entry.SetTypedValue);
        }

        internal static void DropdownBuild(ConfigEntry<string> entry, UIDropdownAttribute uiAttr)
        {
            var asm = entry.Assembly;
            if (!TryGetModInfo(asm, out var info))
                return;

            var type = entry.LogicalType;
            if (type.IsEnum)
            {
                // 获取枚举所有值（按数值排序）转成名称列表
                List<string> options = [.. Enum.GetValues(type)
                                               .Cast<object>()
                                               .OrderBy(v => (IComparable)Convert.ChangeType(v, Enum.GetUnderlyingType(type)))
                                               .Select(v => v.ToString()!)];

                // 如果有排除列表 → 过滤
                if (uiAttr.Exclude is { Length: > 0 })
                {
                    var exclude = new HashSet<string>(uiAttr.Exclude, StringComparer.OrdinalIgnoreCase);
                    options = [.. options.Where(o => !exclude.Contains(o))];
                }

                // 添加 UI
                ModSettingAPI.AddDropdownList(info,
                                              entry.Key,
                                              L10n.Get(entry.DisplayName),
                                              options,
                                              entry.GetTypedValue(),
                                              entry.SetTypedValue);
            }
            else
            {
                ModLogger.Error($"{ModRegistry.GetTag(asm)} 构造下拉列表 {entry.Key}时失败：现在仅支持从 enum 构造下拉列表");
            }
        }

        internal static void DropdownBuild<TEnum>(ConfigEntry<TEnum> entry)
            where TEnum : Enum
        {
            var asm = entry.Assembly;
            if (!TryGetModInfo(asm, out var info))
                return;

            var enumType = typeof(TEnum);

            // 枚举的所有选项
            var options = Enum.GetNames(enumType).ToList();

            // 当前值
            TEnum currentValue = entry.GetTypedValue();
            string currentStr = currentValue.ToString();

            // 添加 UI
            ModSettingAPI.AddDropdownList(info,
                                          entry.Key,
                                          L10n.Get(entry.DisplayName),
                                          options,
                                          currentStr,
                                          selected =>
                                          {
                                              // 用户改值 → 解析回 enum
                                              var parsed = (TEnum)Enum.Parse(enumType, selected, ignoreCase: true);
                                              entry.SetTypedValue(parsed);
                                          }
            );
        }

        internal static void KeyBindBuild(ConfigEntry<KeyCode> entry)
        {
            var asm = entry.Assembly;
            if (!TryGetModInfo(asm, out var info))
                return;

            ModSettingAPI.AddKeybinding(info,
                                        entry.Key,
                                        L10n.Get(entry.DisplayName),
                                        entry.GetTypedValue(),
                                        entry.DefaultValue,
                                        entry.SetTypedValue);
        }

        internal static void InputBuild(ConfigEntry<string> entry, UIInputAttribute uiAttr)
        {
            var asm = entry.Assembly;
            if (!TryGetModInfo(asm, out var info))
                return;

            ModSettingAPI.AddInput(info,
                                    entry.Key,
                                    L10n.Get(entry.DisplayName),
                                    entry.GetTypedValue(),
                                    uiAttr.CharacterLimit,
                                    entry.SetTypedValue);
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
                                    entry.Invoke);
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
                    Description = L10n.Get(g.Key, L10n.ExistKey(g.Key) ? null : asm),   // 优先用本MOD的本地化组名，防止冲突
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
                if (pending.Entry is ConfigEntry configEntry)
                    configEntry.RegisterUISync();
                ModLogger.Debug($"构建 UI 条目 {pending.Entry.Key} 到 ModSetting. ");
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
                                    L10n.Get("重置所有选项到默认值"),
                                    L10n.Get("重置"),
                                    () => ConfigUIManager.ResetAsm(asm));   // 只重置注册了UI的Config
        }

        internal static void BuildCopy(Assembly asm)
        {
            var modinfo = ModRegistry.GetModInfo(asm);
            if (!TryGetModInfo(asm, out var info))
                return;

            ModSettingAPI.AddButton(info,
                                    $"JmcModLibGen.{modinfo!.Name}.Copy",
                                    L10n.Get("复制配置文件夹地址到剪贴板"),
                                    L10n.Get("复制"),
                                    ConfigManager.CopyConfigPathToClipboard);
        }
    }
}