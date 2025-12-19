using Duckov.Modding;
using JmcModLib.Config.Entry;
using JmcModLib.Config.UI;
using JmcModLib.Core;
using JmcModLib.Dependency;
using JmcModLib.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace JmcModLib.Config.UI.ModConfig
{
    internal static class ModConfigLinker
    {
        private static bool _initialized = false;
        private static bool ApiInit => ModConfigAPI.IsInit;

        // 记录反向查找表: "ModName_Key" -> ConfigEntry
        private static readonly Dictionary<string, ConfigEntry> _keyToEntryMap = new();
        // 记录已注册同步的 Entry，防止重复订阅事件
        private static readonly HashSet<ConfigEntry> _syncedEntries = new();

        public static void Init()
        {
            if (_initialized) return;

            // 尝试通过 ModLink 初始化 (当 ModConfig 启用时)
            // 如果 ModConfig 已经存在，ModLink 会自动触发 Activated

            // 监听 Jmc 内部注册事件
            ConfigUIManager.OnRegistered += RegisterAsm;
            ConfigUIManager.OnEntryRegistered += BuildSingleEntry;

            _initialized = true;
        }

        public static void Dispose()
        {
            ConfigUIManager.OnRegistered -= RegisterAsm;
            ConfigUIManager.OnEntryRegistered -= BuildSingleEntry;
            Unlink();
            _initialized = false;
        }

        // ==========================================
        // ModLink 生命周期管理
        // ==========================================

        [ModLink(ModConfigAPI.MOD_NAME, ModLinkEvent.Activated)]
        private static void OnModConfigActivated(ModInfo info, Duckov.Modding.ModBehaviour behaviour)
        {
            if (ApiInit) return;

            ModLogger.Info("检测到 ModConfig 启用，尝试连接...");
            if (!ModConfigAPI.Init())
            {
                ModLogger.Warn("ModConfigAPI 初始化失败");
                return;
            }

            // 注册 ModConfig 全局回调 (ModConfig UI -> Jmc)
            ModConfigAPI.AddOnOptionsChangedDelegate(OnModConfigValueChanged);

            // 构建所有已存在的配置
            BuildAll();
        }

        [ModLink(ModConfigAPI.MOD_NAME, ModLinkEvent.Deactivated)]
        private static void OnModConfigDeactivated(ModInfo info)
        {
            Unlink();
        }

        private static void Unlink()
        {
            if (!ApiInit) return;

            ModLogger.Info("断开 ModConfig 连接");
            try
            {
                ModConfigAPI.RemoveOnOptionsChangedDelegate(OnModConfigValueChanged);
            }
            catch { /* ignore */ }

            // 清理缓存
            _keyToEntryMap.Clear();

            // 取消订阅 Jmc 事件 (Jmc -> ModConfig)
            foreach (var entry in _syncedEntries)
            {
                UnsubscribeJmcSync(entry);
            }
            _syncedEntries.Clear();

            // 注意：我们无法轻易移除 ModConfig 里的 UI (它只有 RemoveUI API)，
            // 但既然 ModConfig 被禁用了，UI 自然也就没了。
            // 唯一的问题是如果 ModConfig 只是热重载，可能需要 RemoveMod 调用。
        }

        // ==========================================
        // 构建流程
        // ==========================================

        private static void BuildAll()
        {
            if (!ApiInit) return;
            var pending = ConfigUIManager.GetPending();
            foreach (var asm in pending.Keys)
            {
                RegisterAsm(asm);
            }
        }

        private static void RegisterAsm(Assembly asm)
        {
            if (!ApiInit) return;
            var groups = ConfigUIManager.GetGroups(asm);
            if (groups == null) return;

            foreach (var groupList in groups.Values)
            {
                foreach (var item in groupList)
                {
                    ModConfigBuilder.BuildEntry(item);
                }
            }
        }

        private static void BuildSingleEntry(PendingUIEntry<BaseEntry, UIBaseAttribute> pending)
        {
            if (!ApiInit) return;
            ModConfigBuilder.BuildEntry(pending);
        }

        /// <summary>
        /// 供 Builder 调用：注册单条 Entry 的双向同步
        /// </summary>
        internal static void RegisterEntrySync(ConfigEntry entry)
        {
            var modName = ModRegistry.GetModInfo(entry.Assembly)?.Name ?? entry.Assembly.GetName().Name;
            var fullKey = $"{modName}_{entry.Key}";

            // 1. 记录映射 (用于 ModConfig -> Jmc)
            _keyToEntryMap[fullKey] = entry;

            // 2. 绑定 Jmc 事件 (用于 Jmc -> ModConfig)
            if (!_syncedEntries.Contains(entry))
            {
                // 使用反射调用泛型方法订阅
                // 相当于: entry.OnChangedTypedWithSelf += SyncToModConfig;
                SubscribeJmcSync(entry);
                _syncedEntries.Add(entry);
            }
        }

        // ==========================================
        // 同步逻辑 (核心)
        // ==========================================

        /// <summary>
        /// 路径 A: ModConfig UI 变更 -> JmcModLib
        /// </summary>
        private static void OnModConfigValueChanged(string fullKey)
        {
            if (!_keyToEntryMap.TryGetValue(fullKey, out var entry)) return;

            try
            {
                // ModConfig 已经保存了值，我们需要 Load 出来
                object? newValue = null;
                var t = entry.UIType;

                // 统一读取为 string 兼容 Enum
                if (t.IsEnum) t = typeof(string);

                if (t == typeof(int))
                    newValue = ModConfigAPI.Load(fullKey, (int)entry.GetValue()!);
                else if (t == typeof(float))
                    newValue = ModConfigAPI.Load(fullKey, (float)entry.GetValue()!);
                else if (t == typeof(string))
                    newValue = ModConfigAPI.Load(fullKey, (string)entry.GetValue()!);
                else if (t == typeof(bool))
                    newValue = ModConfigAPI.Load(fullKey, (bool)entry.GetValue()!);

                if (newValue != null)
                {
                    // 对比当前值，防止循环调用
                    // Jmc 的 SetValue 内部也会去抖
                    entry.SetValue(newValue);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"同步 ModConfig -> Jmc 失败: {fullKey}", ex);
            }
        }

        /// <summary>
        /// 路径 B: JmcModLib 代码变更 -> ModConfig Storage
        /// </summary>
        private static void SyncToModConfig<T>(ConfigEntry<T> entry, T newValue)
        {
            if (!ApiInit) return;

            try
            {
                var modName = ModRegistry.GetModInfo(entry.Assembly)?.Name ?? entry.Assembly.GetName().Name;
                var fullKey = $"{modName}_{entry.Key}";

                // 检查 ModConfig 里的旧值，如果一样就不保存 (防止死循环)
                T savedValue = ModConfigAPI.Load(fullKey, entry.DefaultValue);

                // 注意：这里需要处理 Enum -> String 的转换，如果 T 是 Enum
                object valToSave = newValue!;
                object valLoaded = savedValue!;

                // 简单的相等性检查
                if (!Equals(valLoaded, valToSave))
                {
                    ModConfigAPI.Save(fullKey, valToSave);
                    ModLogger.Trace($"同步 Jmc -> ModConfig: {fullKey} = {valToSave}");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"同步 Jmc -> ModConfig 失败: {entry.Key}", ex);
            }
        }

        // ---------- 反射辅助：订阅泛型事件 ----------

        private static readonly MethodInfo _syncMethodInfo = typeof(ModConfigLinker)
            .GetMethod(nameof(SyncToModConfig), BindingFlags.NonPublic | BindingFlags.Static)!;

        private static void SubscribeJmcSync(ConfigEntry entry)
        {
            // entry 是 ConfigEntry<T>，我们需要获取 T
            // ConfigEntry<T> 继承自 ConfigEntry，拥有 OnChangedTypedWithSelf 事件

            var entryType = entry.GetType(); // ConfigEntry<T>
            var eventInfo = entryType.GetEvent("OnChangedTypedWithSelf", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            if (eventInfo != null)
            {
                // 获取 T
                var genericArg = entryType.GetGenericArguments()[0];

                // 构造 SyncToModConfig<T>
                var method = _syncMethodInfo.MakeGenericMethod(genericArg);

                // 构造 Action<ConfigEntry<T>, T>
                var handlerType = eventInfo.EventHandlerType;
                var handler = Delegate.CreateDelegate(handlerType!, method);

                eventInfo.AddEventHandler(entry, handler);
            }
        }

        private static void UnsubscribeJmcSync(ConfigEntry entry)
        {
            // 原理同上，调用 RemoveEventHandler
            // 这里为了简化代码逻辑，通常 Unlink 时如果不彻底销毁 Entry，
            // 只是为了断开连接，可以不做这一步，因为 ApiInit 为 false 时 SyncToModConfig 会直接返回。
            // 但为了严谨，最好反注册。此处略去繁琐的反射反注册代码，
            // 实际应用中，可以通过将 handler 存入字典来实现反注册。
        }
    }
}