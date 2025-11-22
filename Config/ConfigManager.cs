using JmcModLib.Config.UI;
using JmcModLib.Core;
using JmcModLib.Reflection;
using JmcModLib.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace JmcModLib.Config
{
    /// <summary>
    /// Config 管理器，负责注册、加载、保存配置项
    /// </summary>
    public static class ConfigManager
    {
        // Config 文件根目录（默认）
        private static readonly string ConfigDir = Path.Combine(Application.persistentDataPath, "Saves/JmcModLibConfig");

        // Assembly -> group -> key -> ConfigEntry
        private static readonly ConcurrentDictionary<Assembly,
            ConcurrentDictionary<string, ConcurrentDictionary<string, ConfigEntry>>> _entries
            = new();

        // Assembly -> Type -> instance (用于 instance 字段的读写)
        private static readonly ConcurrentDictionary<Assembly, ConcurrentDictionary<Type, object>> _typeInstances
            = new();

        // 存放每一个修改了存储后端的 Assembly对应的存储实现
        private static readonly ConcurrentDictionary<Assembly, IConfigStorage> _storages
            = new();

        // 默认存储后端（支持子 MOD 覆盖）
        private static readonly IConfigStorage _defaultStorage =
            new NewtonsoftConfigStorage(ConfigDir);

        /// <summary>
        /// 某个ASM有配置项并且扫描完毕后广播
        /// </summary>
        internal static event Action<Assembly>? OnRegistered;

        internal static event Action<ConfigEntry, object?>? OnValueChanged;

        // -------------- Storage 设置 API ----------------
        /// <summary>
        /// 注册一个 Assembly 的存储实现（子 MOD 可以重写默认存储）
        /// </summary>
        /// <param name="storage">继承自IConfigStorage的类</param>
        /// <param name="asm">默认为调用者asm</param>
        public static void SetStorage(IConfigStorage storage, Assembly? asm)
        {
            _storages[asm ?? Assembly.GetCallingAssembly()] = storage;
        }

        // 获取该 Assembly 当前使用的存储引擎
        private static IConfigStorage GetStorage(Assembly asm) =>
            _storages.TryGetValue(asm, out var st) ? st : _defaultStorage;

        // -------------- Registration --------------------

        internal static void Init()
        {
            ConfigUIManager.Init();
            ModRegistry.OnRegistered += RegisterAllInAssembly; // 尝试自动注册ASM
            ModRegistry.OnUnRegistered += Unregister; // 尝试自动反注册ASM
        }

        internal static void Dispose()
        {
            ConfigUIManager.Dispose();
            UnregisterAll();
            ModRegistry.OnRegistered -= RegisterAllInAssembly;
            ModRegistry.OnUnRegistered -= Unregister;
        }

        /// <summary>
        /// 自动扫描当前 Assembly 内标记了 [Config] 的字段/属性
        /// </summary>
        /// <param name="asm">默认留空表示扫描当前 Assembly</param>
        public static void RegisterAllInAssembly(Assembly? asm = null)
        {
            asm ??= Assembly.GetCallingAssembly();
            if (_entries.ContainsKey(asm))
                return; // 已注册

            var groups = new ConcurrentDictionary<string, ConcurrentDictionary<string, ConfigEntry>>(StringComparer.Ordinal);
            bool hasEntry = false;

            foreach (var type in asm.GetTypes())
            {
                try
                {
                    if (type.ContainsGenericParameters)                                    // 跳过开放泛型类型
                        continue;

                    foreach (var acc in MethodAccessor.GetAll(type))
                    {
                        if (!acc.HasAttribute<UIButtonAttribute>())
                            continue;

                        // 使用 UIButtonAttribute 的验证函数
                        var methodInfo = acc.Member;
                        if (!UIButtonAttribute.IsValidButtonMethod(methodInfo, out var errorMsg))
                        {
                            ModLogger.Error($"方法 {type.FullName}.{acc.Name} 标记了 UIButtonAttribute，但 {errorMsg}，跳过注册");
                            continue;
                        }

                        var attr = acc.GetAttribute<UIButtonAttribute>()!;
                        var entry = new ButtonEntry(asm, type, acc, attr.Group);
                        ConfigUIManager.RegisterEntry(entry, attr);
                    }

                    foreach (var acc in MemberAccessor.GetAll(type))
                    {
                        if (!acc.HasAttribute<ConfigAttribute>())   // 仅处理标记了 ConfigAttribute 的成员
                            continue;

                        // 如果有 instance 需求，创建实例
                        object? instance = null;
                        if (!acc.IsStatic)
                        {
                            var perAsm = _typeInstances.GetOrAdd(asm, _ => new ConcurrentDictionary<Type, object>());
                            instance = perAsm.GetOrAdd(type, t => Activator.CreateInstance(t)!);
                        }

                        var attr = acc.GetAttribute<ConfigAttribute>()!;
                        var entry = new ConfigEntry(asm, type, acc, attr, acc.GetValue(instance));
                        var group = entry.Group;
                        var groupDict = groups.GetOrAdd(group, _ => new ConcurrentDictionary<string, ConfigEntry>(StringComparer.Ordinal));
                        groupDict[entry.Key] = entry;

                        // 找 UI 属性（如果有）
                        var uiAttr = acc.GetAttribute<UIConfigAttribute>();
                        if (uiAttr != null)
                        {
                            // 注册任务，等待后续生成
                            if (!uiAttr.IsValid(entry))
                            {
                                ModLogger.Error($"字段/属性 {entry.Key} 类型为 {acc.MemberType} 与 UIAttribute {uiAttr.GetType().Name} 要求不匹配或值不合法");
                            }
                            else
                            {
                                ConfigUIManager.RegisterEntry(entry, uiAttr);
                            }
                        }

                        hasEntry = true;
                        ModLogger.Trace($"{ModRegistry.GetTag(asm)}发现配置项: {type.FullName}.{acc.Name}, key 为: {entry.Key}");
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.Error($"扫描类型 {type.FullName} 时发生异常", ex);
                }
            }

            if (!hasEntry)
            {
                ModLogger.Trace($"{ModRegistry.GetTag(asm)} 未找到配置项，跳过注册");
                return;
            }

            _entries[asm] = groups;
            // 加载已保存的配置
            SyncConfigInAssembly(asm);
            OnRegistered?.Invoke(asm);
            ModLogger.Info($"{ModRegistry.GetTag(asm)}注册配置成功!");
        }

        /// <summary>
        /// 反注册当前 Assembly 内所有配置项，并保存当前值
        /// </summary>
        /// <param name="asm"></param>
        public static void Unregister(Assembly? asm = null)
        {
            asm ??= Assembly.GetCallingAssembly();
            if (!_entries.ContainsKey(asm))
                return; // 未注册

            ConfigUIManager.Unregister(asm);
            SaveAllInAssembly(asm);
            ClearAssemblyCache(asm);

            ModLogger.Info($"{ModRegistry.GetTag(asm)}卸载配置成功!");
        }

        internal static void UnregisterAll()
        {
            foreach (var asm in _entries.Keys.ToList())
            {
                Unregister(asm);
            }
            ModLogger.Info($"卸载所有配置成功!");
        }

        private static void ClearAssemblyCache(Assembly asm)
        {
            if (asm == null) return;

            // 清除该 Assembly 所有 entry
            _entries.TryRemove(asm, out _);

            // 清除实例缓存
            _typeInstances.TryRemove(asm, out _);

            // 清除自定义存储后端（如果有）
            _storages.TryRemove(asm, out _);

            ModLogger.Debug($"{ModRegistry.GetTag(asm)} 清除配置缓存成功");
        }

        // -------------- Query helpers -------------------
        /// <summary>
        /// 返回某个 key 的 group（public，供 storage 调用）
        /// </summary>
        /// <param name="key">需要查询的key</param>
        /// <param name="asm">Assembly，留空则为当前</param>
        /// <returns>返回与key对应的group，如果没有找到对应的key则返回null</returns>
        public static string? TryGetGroupForKey(string key, Assembly? asm = null)
        {
            asm ??= Assembly.GetCallingAssembly();
            if (!_entries.TryGetValue(asm, out var groups)) return null;

            foreach (var gkv in groups)
            {
                if (gkv.Value.ContainsKey(key))
                    return gkv.Key;
            }
            return null;
        }

        /// <summary>
        /// 列出 Assembly 所有组名
        /// </summary>
        /// <param name="asm">指定程序集，留空则为调用者</param>
        /// <returns>组内所有 group 的集合</returns>
        public static IEnumerable<string> GetGroups(Assembly? asm = null)
        {
            asm ??= Assembly.GetCallingAssembly();
            if (!_entries.TryGetValue(asm, out var groups)) yield break;
            foreach (var key in groups.Keys) yield return key;
        }

        /// <summary>
        /// 列出组内所有 keys
        /// </summary>
        /// <param name="group">要查询的组名</param>
        /// <param name="asm">指定程序集，留空则为调用者</param>
        /// <returns>组内所有 keys 的集合</returns>
        public static IEnumerable<string> GetKeys(string group, Assembly? asm = null)
        {
            asm ??= Assembly.GetCallingAssembly();
            if (!_entries.TryGetValue(asm, out var groups)) yield break;
            if (!groups.TryGetValue(group, out var dict)) yield break;
            foreach (var k in dict.Keys) yield return k;
        }

        // 获取 ConfigEntry（null 如果未找到）
        private static ConfigEntry? GetEntry(string key, Assembly? asm = null)
        {
            asm ??= Assembly.GetCallingAssembly();
            if (!_entries.TryGetValue(asm, out var groups)) return null;
            foreach (var g in groups.Values)
            {
                if (g.TryGetValue(key, out var e)) return e;
            }
            ModLogger.Warn($"{ModRegistry.GetTag(asm)}配置项 Key='{key}' 未注册！");
            return null;
        }

        // 获取entry对应的实例对象（如果是静态成员则返回null），不检查 entry 是否存在
        private static object? GetInstance(ConfigEntry entry)
        {
            if (entry.Accessor.IsStatic)
                return null;

            return entry.Accessor.IsStatic ? null : _typeInstances[entry.Assembly][entry.DeclaringType];
        }

        // 通过 key 获取实例对象（如果是静态成员则返回null），若 key 不存在则抛出异常
        private static object? GetInstance(string Key, Assembly asm)
        {
            return GetInstance(GetEntry(Key, asm) ?? throw new ArgumentNullException(nameof(Key)));
        }

        // -------------------------------------------------------
        // 持久化：Load / Save
        // -------------------------------------------------------
        /// <summary>
        /// 将 Assembly 内所有配置项与存储介质同步，若文件中有保存的值则加载，否则保存当前值
        /// </summary>
        /// <exception cref="ArgumentNullException">asm为空抛出异常</exception>
        private static void SyncConfigInAssembly(Assembly asm)
        {
            if (asm == null) throw new ArgumentNullException(nameof(asm));
            if (!_entries.TryGetValue(asm, out var groups)) return;

            var storage = GetStorage(asm);

            foreach (var g in groups)
            {
                foreach (var kv in g.Value)
                {
                    var entry = kv.Value;
                    // 获取已保存的值
                    if (storage.TryLoad(entry.Attribute.DisplayName, entry.Group, entry.Accessor.MemberType, out var loaded, asm))
                        entry.Accessor.SetValue(GetInstance(entry), loaded);
                    else
                    {
                        // 如果没有保存的值，则保存当前值
                        storage.Save(entry.Attribute.DisplayName, entry.Group, GetValue(entry), asm);
                    }
                }
            }
        }

        private static void SaveAllInAssembly(Assembly asm)
        {
            if (asm == null) throw new ArgumentNullException(nameof(asm));
            if (!_entries.ContainsKey(asm)) return;

            var storage = GetStorage(asm);
            storage.Flush(asm);
        }

        // -------------- Reset --------------------------

        // 重置key对应的值为默认值
        internal static void ResetKey(ConfigEntry entry)
        {
            if (!object.Equals(GetValue(entry), entry.DefaultValue))
                SetValue(entry, entry.DefaultValue);    // 仅在值不相等时设置，避免触发不必要的 OnChanged 回调
        }

        // 重置组内所有entry
        private static void ResetGroup(string group, Assembly asm)
        {
            if (!_entries.TryGetValue(asm, out var groups)) return;
            if (!groups.TryGetValue(group, out var dict)) return;
            foreach (var kv in dict)
                ResetKey(kv.Value);
        }

        /// <summary>
        /// 重置当前 Assembly 内所有配置项为默认值
        /// </summary>
        internal static void ResetAsm(Assembly? asm = null)
        {
            asm ??= Assembly.GetCallingAssembly();
            if (!_entries.TryGetValue(asm, out var groups)) return;
            foreach (var g in groups.Keys)
                ResetGroup(g, asm);
        }

        // -------------- Direct GetValue/SetValue ------------------
        internal static object? GetValue(ConfigEntry modEntry)
        {
            return modEntry.Accessor.GetValue(GetInstance(modEntry));
        }

        /// <summary>
        /// 获取Key对应变量的值，如果Key不存在，会输出一条Warn
        /// </summary>
        /// <param name="key">目标变量的Key，可以通过BaseEntry.GetKey构造</param>
        /// <param name="asm">指定程序集，留空则为调用者</param>
        /// <returns>如果Key不存在，会输出一条Warn并返回空，否则返回对应值</returns>
        public static object? GetValue(string key, Assembly? asm = null)
        {
            asm ??= Assembly.GetCallingAssembly();
            var entry = GetEntry(key, asm);
            if (entry == null) return null;
            return GetValue(entry);
        }

        internal static void SetValue(ConfigEntry modEntry, object? value)
        {
            // 如果注册有额外的 OnChanged 回调，调用它
            if (!string.IsNullOrEmpty(modEntry.Attribute.OnChanged))
            {
                var mAcc = MethodAccessor.Get(modEntry.DeclaringType, modEntry.Attribute.OnChanged);
                if (mAcc.IsStatic == modEntry.Accessor.IsStatic)
                    mAcc.Invoke(GetInstance(modEntry), value);
                else if (mAcc.IsStatic)
                    mAcc.Invoke(null, value);
                else
                {
                    var perAsm = _typeInstances.GetOrAdd(modEntry.Assembly, _ => new ConcurrentDictionary<Type, object>());
                    var instance = perAsm.GetOrAdd(modEntry.DeclaringType, t => Activator.CreateInstance(t)!);
                    mAcc.Invoke(instance, value);
                }
            }

            modEntry.Accessor.SetValue(GetInstance(modEntry), value);
            var asm = modEntry.Assembly;
            // 在设置值后调用 Save 方法以保持数据一致性
            var storage = GetStorage(asm);
            storage.Save(modEntry.Attribute.DisplayName, modEntry.Group, value, asm);
            OnValueChanged?.Invoke(modEntry, value);
        }

        /// <summary>
        /// 为Key代表的配置的值设置新的值，如果Key不存在，会输出一条Warn
        /// </summary>
        /// <param name="key">目标变量的Key，可以通过ConfigEntry.GetKey构造</param>
        /// <param name="value">新的值</param>
        /// <param name="asm">指定程序集，留空则为调用者</param>
        /// <returns>如果Key不存在，会输出一条Warn并返回空，否则返回对应值</returns>
        public static void SetValue(string key, object? value, Assembly? asm = null)
        {
            asm ??= Assembly.GetCallingAssembly();
            var entry = GetEntry(key, asm);
            if (entry == null) return;

            SetValue(entry, value);
        }
    }
}