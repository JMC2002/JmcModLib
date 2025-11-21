using Duckov.Modding;
using JmcModLib.Core;
using JmcModLib.Reflection;
using JmcModLib.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
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
            if (ModSettingAPI.Init(VersionInfo.modInfo)) InitAllMod();
            // 当任意 Mod 启用时尝试与 ModSetting 连接
            ModManager.OnModActivated += TryInitModSetting;
            ModManager.OnModWillBeDeactivated += TryUnInitModSetting;
            ConfigUIManager.OnRegistered += Register;
            L10n.LanguageChanged += OnLangChanged;
            ConfigManager.OnValueChanged += SyncValue;
            _initialized = true;
        }

        internal static void Dispose()
        {
            ConfigManager.OnValueChanged -= SyncValue;
            L10n.LanguageChanged -= OnLangChanged;
            ModManager.OnModWillBeDeactivated -= TryUnInitModSetting;
            ModManager.OnModActivated -= TryInitModSetting;
            ConfigUIManager.OnRegistered -= Register;
            RemoveAllMod();
            _initialized = false;
        }

        private static void SyncValue(ConfigEntry entry, object? newVal)
        {
            try
            {
                var info = ModRegistry.GetModInfo(entry.assembly)?.Info;
                var t = entry.Accessor.MemberType;

                if (t.IsEnum && t != typeof(KeyCode))   // Enum作为下拉列表时是parse到string的，应该特殊处理
                {
                    newVal = Enum.GetName(t, newVal);
                    t = typeof(string);
                }

                object? valueHolder = t.IsValueType ? Activator.CreateInstance(t) : null;
                if ((bool)MethodAccessor.Get(typeof(ModSettingAPI), "GetSavedValue")
                                        .MakeGeneric(t)
                                        .Invoke(null, info, entry.Key, valueHolder)!
                                        && valueHolder != newVal)
                {
                    MethodAccessor.Get(typeof(ModSettingAPI), "SetValue")
                                        .MakeGeneric(t)
                                        .Invoke(null, info, entry.Key, newVal);
                    ModLogger.Trace($"{entry.Key}({entry.Attribute.DisplayName}) 的值向Setting同步");
                }
                else
                {
                    ModLogger.Trace($"{entry.Key}({entry.Attribute.DisplayName}) 的值不存在或者未更改，跳过同步");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"{ModRegistry.GetTag(entry.assembly)} 在同步 {entry.Key} 条目时抛出异常", ex);
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

        internal static void UnRegister(Assembly asm)
        {
            if (IsInitialized(asm))
            {
                var info = ModRegistry.GetModInfo(asm)?.Info;
                if (info == null)
                {
                    ModLogger.Error($"尝试移除未注册info信息的ModSetting条目，asm：{asm.FullName}");
                    return;
                }
                else
                {
                    ModSettingAPI.RemoveMod((ModInfo)info);
                }
            }
            if (IsRegistered(asm))
                initialized.Remove(asm);
        }

        private static void OnLangChanged(SystemLanguage lang)
        {
            RemoveAllMod();
            InitAllMod();
        }

        private static void TryInitModSetting(ModInfo info, Duckov.Modding.ModBehaviour behaviour)
        {
            ModLogger.Trace($"检测到Mod {info.name}启用");
            // 只在 ModSetting 启动时进行初始化
            if (info.name != ModSettingAPI.MOD_NAME || !ModSettingAPI.Init(VersionInfo.modInfo))
                return;

            ModLogger.Info("检测到 ModSetting 启用，尝试注册配置界面");

            InitAllMod();
        }

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
            initialized[asm] = false;
            InitMod(asm);
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
            if (initialized.TryGetValue(asm, out bool isInit) && isInit)
            {
                ModLogger.Trace($"已初始化，退出, isInit: {isInit}");
                return;
            }
            ModSettingBuilder.BuildEntries(asm);
            ModSettingBuilder.BuildGroup(asm);
            ModSettingBuilder.BuildReset(asm);
            initialized[asm] = true;
            ModLogger.Trace($"注册 {ModRegistry.GetTag(asm)} UI成功");
        }

        private static void InitAllMod()
        {
            ModLogger.Trace($"进入BuildAll, cnt = {initialized.Count}");
            initialized.Keys
                       .ToList()
                       .ForEach(InitMod);
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