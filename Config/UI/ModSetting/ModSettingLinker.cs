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
    /// <remarks>
    /// 两个路径，当此MOD与子MOD在Setting上线前注册配置，在Setting上线时会直接从UIManager处获取Entry并构建所有，
    /// 当Setting上线后才出现配置，则UIManager处读一条这里构建一条，并在扫描结束后构建元信息
    /// </remarks>
    internal static class ModSettingLinker
    {
        private static bool _initialized = false;
        private static bool SettingInit => ModSettingAPI.IsInit;
        internal static Dictionary<Assembly, bool> initialized = [];

        internal static event Action<Assembly>? BeforeRemoveAsm;

        public static void Init()
        {
            if (_initialized)
            {
                ModLogger.Warn("重复初始化ModSettingLinker，拒绝");
                return;
            }

            if (!TryInitModSetting())
                ModLogger.Info("未检测到ModSetting或者初始化失败，将会在ModSetting重新上线时尝试初始化");

            // 当任意 Mod 启用时尝试与 ModSetting 连接
            ModManager.OnModActivated += TryInitModSetting;
            // 当ModSetting离线，清除初始化状态以重新监听重建
            ModManager.OnModWillBeDeactivated += TryUnInitModSetting;
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
            ModManager.OnModWillBeDeactivated -= TryUnInitModSetting;
            ModManager.OnModActivated -= TryInitModSetting;
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
                        ModSettingAPI.SetValue(info, entry.Key, newVal, (flg =>
                        {
                            if (flg)
                                ModLogger.Info($"向Setting 同步 {entry.Key} 的值成功：{savedValue} → {newVal}");
                            else
                                ModLogger.Error($"向Setting 同步 {entry.Key} 的值失败");
                        }));
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


        private static void SyncValue(ConfigEntry entry, object? newVal)
        {
            try
            {
                var info = ModRegistry.GetModInfo(entry.Assembly)?.Info;
                var t = entry.UIType;

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
                    ModLogger.Trace($"{entry.Key}({entry.DisplayName}) 的值向Setting同步");
                }
                else
                {
                    ModLogger.Trace($"{entry.Key}({entry.DisplayName}) 的值不存在或者未更改，跳过同步");
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

        internal static void UnRegister(Assembly asm)
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
            if (SettingInit)
                return;
            ModLogger.Trace($"检测到Mod {info.name}启用");
            // 只在 ModSetting 启动时进行初始化
            if (info.name != ModSettingAPI.MOD_NAME)
                return;
            TryInitModSetting();
        }

        private static bool TryInitModSetting()
        {
            if (SettingInit // 只初始化一次
             || !ModSettingAPI.Init(VersionInfo.modInfo))
                return false;
            ModLogger.Info("检测到 ModSetting 启用，尝试注册配置界面");

            InitAllMod();
            return true;
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
            // 保证只在需要的时候才初始化UI，理论上不会走这条分支，除非操作失误
            if (IsInitialized(asm))
            {
                ModLogger.Warn($"已初始化，退出");
                return;
            }
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

        private static void RemoveAllMod()
        {
            foreach (var asm in initialized.Keys.ToList())  // ToList 存快照
            {
                UnRegister(asm);
            }
        }
    }
}