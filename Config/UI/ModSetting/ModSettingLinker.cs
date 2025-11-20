using Duckov.Modding;
using JmcModLib;
using JmcModLib.Core;
using JmcModLib.Utils;
using SodaCraft.Localizations;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;

namespace JmcModLib.Config.UI.ModSetting
{
    /// <summary>
    /// 负责将 DuckSort 的 ModConfig 注册到 ModSetting。
    /// </summary>
    internal static class ModSettingLinker
    {
        private static bool _initialized = false;
        private static bool SettingInit => ModSetting.ModSettingAPI.IsInit;
        internal static Dictionary<Assembly, bool> initialized = new();

        public static void Init()
        {
            if (_initialized) return;
            if (ModSettingAPI.Init(VersionInfo.modInfo)) BuildAll();
            // 当任意 Mod 启用时尝试与 ModSetting 连接
            ModManager.OnModActivated += TryInitModSetting;
            ConfigManager.OnRegistered += BuildAsm;
            L10n.LanguageChanged += OnLangChanged;
            _initialized = true;
        }

        internal static void Dispose()
        {
            L10n.LanguageChanged -= OnLangChanged;
            ModManager.OnModActivated -= TryInitModSetting;
            ConfigManager.OnRegistered -= BuildAsm;
            RemoveAllMod();
            _initialized = false;
        }

        internal static void RemoveMod(Assembly asm)
        {
            if (initialized.ContainsKey(asm) && initialized[asm])
            {
                ModSettingAPI.RemoveMod(ModRegistry.GetModInfo(asm)!.Info);
            }
            if (initialized.ContainsKey(asm))
                initialized.Remove(asm);
        }

        private static void RemoveAllMod()
        {
            foreach (var asm in initialized.Keys.ToList())  // ToList 存快照
            {
                RemoveMod(asm);
            }
        }

        private static void OnLangChanged(SystemLanguage lang)
        {
            RemoveAllMod();
            BuildAll();
        }

        private static void TryInitModSetting(ModInfo info, Duckov.Modding.ModBehaviour behaviour)
        {
            ModLogger.Debug($"检测到Mod {info.name}启用");
            // 只在 ModSetting 启动时进行初始化
            if (info.name != ModSettingAPI.MOD_NAME || !ModSettingAPI.Init(VersionInfo.modInfo))
                return;

            ModLogger.Info("检测到 ModSetting 启用，尝试注册配置界面");

            BuildAll();
        }

        internal static void BuildAsm(Assembly asm)
        {
            if (!SettingInit)
            {
                ModLogger.Trace("当前没有初始化ModSetting，退出初始化");
                return;  // 还没有初始化ModSetting
            }
            ModLogger.Trace($"注册 {ModRegistry.GetTag(asm)} UI");
            if (initialized.TryGetValue(asm, out bool isInit) && isInit)
            {
                ModLogger.Trace($"已初始化，退出, isInit: {isInit}");
                return;
            }
            ModSettingBuilder.BuildEntries(asm);
            ModSettingBuilder.BuildGroup(asm);
            initialized[asm] = true;
            ModLogger.Trace($"注册 {ModRegistry.GetTag(asm)} UI成功");
        }

        internal static void BuildAll()
        {
            ModLogger.Trace($"进入BuildAll, cnt = {initialized.Count}");
            initialized.Keys
                       .ToList()
                       .ForEach(BuildAsm);
        }
    }
}
