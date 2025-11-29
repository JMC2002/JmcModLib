using Duckov.Modding;
using JmcModLib.Config.Entry;
using JmcModLib.Config.UI.ModSetting;
using JmcModLib.Core;
using JmcModLib.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace JmcModLib.Config.UI.CustomHotkey
{
    internal class CustomHotkeyLinker
    {
        private static bool _initialized = false;
        private static bool SettingInit => CustomHotkeyHelper.IsInited;
        internal static Dictionary<Assembly, bool> initialized = [];
        internal static event Action<Assembly>? BeforeRemoveAsm;

        public static void Init()
        {
            if (_initialized)
            {
                ModLogger.Warn("重复初始化CustomHotkeyLinker，拒绝");
                return;
            }

            if (!CustomHotkeyHelper.TryInit())
                ModLogger.Info("未检测到CustomHotkey或者初始化失败，将会在CustomHotkey重新上线时尝试初始化");

            ConfigUIManager.OnRegistered += Register;
            // 每当有一个Entry被注册，直接构建
            ConfigUIManager.OnEntryRegistered += BuildEntry;
            // 当一个Entry的配置扫描完毕后，构建元信息（维护组+重置按钮等）
            ConfigUIManager.OnRegistered += BuildMeta;
            // 当改变语言或者ModSetting后启用，需要重建所有Entry
            L10n.LanguageChanged += OnLangChanged;
            _initialized = true;
        }

        internal static void Dispose()
        {
            L10n.LanguageChanged -= OnLangChanged;
            ConfigUIManager.OnRegistered -= BuildMeta;
            ConfigUIManager.OnEntryRegistered -= BuildEntry;
            ConfigUIManager.OnRegistered -= Register;
            // ModManager.OnModWillBeDeactivated -= TryUnInitModSetting;
            // ModManager.OnModActivated -= TryInitModSetting;
            RemoveAllMod();
            _initialized = false;
        }

        internal static void SyncValue<T>(ConfigEntry<T> entry, T newVal)
        {
            try
            {
                var info = (ModInfo)ModRegistry.GetModInfo(entry.Assembly)?.Info!;
                if (!ModSettingAPI.GetValue(info, entry.Key, (T savedValue) =>
                {
                    if (!Equals(savedValue, newVal))
                    {
                        ModSettingAPI.SetValue(info, entry.Key, newVal, flg =>
                        {
                            if (flg)
                                ModLogger.Info($"向Setting 同步 {entry.Key} 的值成功：{savedValue} → {newVal}");
                            else
                                ModLogger.Error($"向Setting 同步 {entry.Key} 的值失败");
                        });
                    }
                }))
                {
                    ModLogger.Error($"{entry.Key} 传入的Key不正确，跳过同步");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"{ModRegistry.GetTag(entry.Assembly)} 在同步 {entry.Key} 条目时抛出异常", ex);
            }
        }

        /// <summary>
        /// 判断有没有注册过 Asm到Linker
        /// </summary>
        internal static bool IsRegistered(Assembly asm)
        {
            return initialized.ContainsKey(asm);
        }

        /// <summary>
        /// 判断有没有将 ASM注册到 ModSetting UI 并且已经初始化完成。
        /// </summary>
        internal static bool IsInitialized(Assembly asm)
        {
            return IsRegistered(asm) && initialized[asm];
        }

        private static void RemoveUI(Assembly asm)
        {
            if (IsInitialized(asm))
            {
                var info = ModRegistry.GetModInfo(asm)?.Info;
                if (info == null)
                {
                    ModLogger.Warn($"尝试移除未注册info信息的ModSetting条目，asm：{asm.FullName}");
                    return;
                }
                else
                {
                    ModSettingAPI.RemoveMod((ModInfo)info);
                }
                BeforeRemoveAsm?.Invoke(asm);
            }
        }

        internal static void UnRegister(Assembly asm)
        {
            RemoveUI(asm);
            if (IsRegistered(asm))
                initialized.Remove(asm);
        }

        private static void OnLangChanged(SystemLanguage lang)
        {
            RemoveAllUI();
            InitAllMod();
        }

        // [ModLink(ModSettingAPI.MOD_NAME, ModLinkEvent.Activated)]
        private static bool TryInitModSetting()
        {
            if (SettingInit // 只初始化一次
             || !ModSettingAPI.Init(VersionInfo.modInfo))
                return false;
            ModLogger.Info("检测到 ModSetting 启用，尝试注册配置界面");

            InitAllMod();
            return true;
        }

        // [ModLink(ModSettingAPI.MOD_NAME, ModLinkEvent.Deactivated)]
        private static void TryUnInitModSetting(ModInfo info, Duckov.Modding.ModBehaviour behaviour)
        {
            ModLogger.Trace($"检测到Mod {info.name}停用");
            if (info.name != ModSettingAPI.MOD_NAME || !SettingInit)
                return;     // 只在 ModSetting 停用且已初始化完毕时进行移除
            ModLogger.Info("检测到 ModSetting 停用，所有配置恢复为未初始化状态");
            ModSettingAPI.IsInit = false;
            foreach (var key in initialized.Keys.ToList())
            {
                initialized[key] = false;   // 重置为未初始化状态
            }
        }

        private static void Register(Assembly asm)
        {
            if (!IsRegistered(asm))
                initialized[asm] = false;
        }

        internal static void BuildEntry(PendingUIEntry<BaseEntry, UIBaseAttribute> pending)
        {
            // 当Setting未连接时不操作
            if (!SettingInit)
                return;
            Register(pending.Entry.Assembly);
            ModSettingBuilder.BuildEntry(pending);
        }

        /// <summary>
        /// 当一个ASM配置扫描完毕，注册元信息
        /// </summary>
        /// <param name="asm"></param>
        private static void BuildMeta(Assembly asm)
        {
            if (!SettingInit)
                return;
            ModSettingBuilder.BuildGroup(asm);
            ModSettingBuilder.BuildReset(asm);
            ModSettingBuilder.BuildCopy(asm);
            initialized[asm] = true;        // 触发BuildMeta时标记初始化完成
        }

        /// <summary>
        /// 将指定程序集的 ModSetting 注册到 ModSetting UI。
        /// </summary>
        private static void InitMod(Assembly asm)
        {
            if (!SettingInit)
            {
                ModLogger.Trace("当前没有初始化ModSetting，退出初始化");
                return;  // 还没有初始化ModSetting
            }
            ModLogger.Trace($"注册 {ModRegistry.GetTag(asm)} UI");
            ModSettingBuilder.BuildEntries(asm);
            BuildMeta(asm);
            ModLogger.Debug($"注册 {ModRegistry.GetTag(asm)} UI成功");
        }

        private static void InitAllMod()
        {
            ModLogger.Trace($"进入BuildAll, cnt = {initialized.Count}");
            initialized.Keys
                       .ToList()
                       .ForEach(InitMod);
        }

        private static void RemoveAllUI()
        {
            foreach (var asm in initialized.Keys)
            {
                RemoveUI(asm);
            }
        }

        private static void RemoveAllMod()
        {
            foreach (var asm in initialized.Keys.ToList())  // ToList 存快照
            {
                UnRegister(asm);
            }
        }
    }
}
