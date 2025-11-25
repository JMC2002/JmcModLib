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

        [UIButton("复制配置文件夹路径到剪贴板", "复制")]
        private static void CopyConfigPathToClipboard()
        {
            GUIUtility.systemCopyBuffer = ConfigDir;
            ModLogger.Info($"已复制路径到剪贴板: {ConfigDir}");
        }

        // Assembly -> group -> key -> ConfigEntry
        private static readonly ConcurrentDictionary<Assembly,
            ConcurrentDictionary<string, ConcurrentDictionary<string, ConfigEntry>>> _entries = [];

        // Assembly -> key -> ConfigEntry
        private static readonly Dictionary<Assembly, Dictionary<string, ConfigEntry>> _lookup = [];

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
        internal static IConfigStorage GetStorage(Assembly asm) =>
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
            ModRegistry.OnRegistered -= RegisterAllInAssembly;
            ModRegistry.OnUnRegistered -= Unregister;
            ConfigUIManager.Dispose();
            UnregisterAll();
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

                        var attr = acc.GetAttribute<UIButtonAttribute>()!;
                        var entry = new ButtonEntry(asm, acc, attr.Group, attr.Description);
                        ConfigUIManager.RegisterEntry(entry, attr);
                    }

                    foreach (var acc in MemberAccessor.GetAll(type))
                    {
                        if (!acc.HasAttribute<ConfigAttribute>())   // 仅处理标记了 ConfigAttribute 的成员
                            continue;

                        var entry = BuildConfigEntry(asm, type, acc);
                        if (entry == null!)  // 验证失败，跳过
                            continue;

                        ModLogger.Trace($"{ModRegistry.GetTag(asm)}发现配置项: {type.FullName}.{acc.Name}, key 为: {entry.Key}");
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.Error($"扫描类型 {type.FullName} 时发生异常", ex);
                }
            }

            // 由于现在要给每个 ASM 注册元信息配置，就不判断是否扫出来配置项了
            OnRegistered?.Invoke(asm);
            ModLogger.Info($"{ModRegistry.GetTag(asm)}注册配置成功!");
        }

        internal static void RegisterEntry(ConfigEntry entry)
        {
            var groupDict = _entries.GetOrAdd(
                entry.Assembly,
                _ => new ConcurrentDictionary<string, ConcurrentDictionary<string, ConfigEntry>>(StringComparer.Ordinal)
            );

            var entryDict = groupDict.GetOrAdd(
                entry.Group,
                _ => new ConcurrentDictionary<string, ConfigEntry>(StringComparer.Ordinal)
            );

            entryDict[entry.Key] = entry;

            // 注册 key 的缓存
            if (!_lookup.TryGetValue(entry.Assembly, out var lookupDict))
            {
                lookupDict = new Dictionary<string, ConfigEntry>(StringComparer.Ordinal);
                _lookup[entry.Assembly] = lookupDict;
            }

            lookupDict[entry.Key] = entry;

            // 同步配置项（文件中有就读文件中的值，没有就写入）
            entry.Sync();
        }

        private static ConfigEntry? BuildConfigEntry(Assembly asm, Type type, MemberAccessor acc)
        {
            var attr = acc.GetAttribute<ConfigAttribute>()!;

            MethodAccessor? onChangedMethod = null;
            // OnChanged 方法验证
            if (!string.IsNullOrEmpty(attr.OnChanged))
            {
                onChangedMethod = MethodAccessor.Get(type, attr.OnChanged); // 现在校验环节放在构造函数里了
            }
            try
            {
                var uiAttr = acc.GetAttribute<UIConfigAttribute>();
                var entry = ConfigEntryFactory.Create(asm, acc, onChangedMethod, attr, uiAttr);
                RegisterEntry(entry);

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

                return entry;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"{ModRegistry.GetTag(asm)} 创建配置项 {type.FullName}.{acc.Name} 时发生异常，跳过注册", ex);
                return null;
            }
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

            // 清除自定义存储后端（如果有）
            _storages.TryRemove(asm, out _);

            _lookup.Remove(asm);

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
            return _lookup.ContainsKey(asm) && _lookup[asm].ContainsKey(key) ? _lookup[asm][key] : null;
        }

        // -------------------------------------------------------
        // 持久化：Load / Save
        // -------------------------------------------------------

        private static void SaveAllInAssembly(Assembly asm)
        {
            if (asm == null) throw new ArgumentNullException(nameof(asm));

            if (_lookup.TryGetValue(asm, out var entries))  
                foreach (var entry in entries.Values)       
                    entry.SyncFromData();   // 遍历所有entry，检查getter得到的值与文件内容是否一致
                                            // 不一致则写入文件，防止子MOD处修改了值但是未保存
            else
                return;                     // 若asm无配置项，直接返回

            var storage = GetStorage(asm);  // 否则，获取存储后端
            storage.Flush(asm);             // 并将缓冲区写入文件
        }

        // -------------- Reset --------------------------

        // 重置组内所有entry
        private static void ResetGroup(string group, Assembly asm)
        {
            if (!_entries.TryGetValue(asm, out var groups)) return;
            if (!groups.TryGetValue(group, out var dict)) return;
            foreach (var kv in dict)
                kv.Value.Reset();
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
            return entry.GetValue();
        }

        internal static void SetValue(ConfigEntry modEntry, object? value)
        {
            modEntry.SetValue(value);

            //// 在设置值后调用 Save 方法以保持数据一致性
            var asm = modEntry.Assembly;
            var storage = GetStorage(asm);
            storage.Save(modEntry.DisplayName, modEntry.Group, value, asm);
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

        public static void Register<T>(string DisplayName, 
                                       string? Group = ConfigAttribute.DefaultGroup, 
                                       UIConfigAttribute<T>? uiAttr = null, 
                                       Action<T>? action = null, Assembly? asm = null)
        {
            asm ??= Assembly.GetCallingAssembly();
            // var acc = new ConfigAccessor(action,)
        }

        public static void Register(string DisplayName,
                                    Action action,
                                    string buttonText = "按钮",
                                    string group = ConfigAttribute.DefaultGroup,
                                    Assembly? asm = null)
        {
            asm ??= Assembly.GetCallingAssembly();
//             var buttonEntry = new ButtonEntry(asm, null!, MethodAccessor.FromDelegate(action), group);
        }

        public static string Register<T>(string DisplayName, 
                                       T defaultValue,
                                       string Group = ConfigAttribute.DefaultGroup,
                                       UIConfigAttribute<T>? uiAttr = null,
                                       Action<T>? action = null, Assembly? asm = null)
        {
            asm ??= Assembly.GetCallingAssembly();
            var storage = GetStorage(asm);
            var key = BaseEntry.GetKey(DisplayName, Group);

            T getter()
            {
                if (storage.TryLoad(DisplayName, Group, typeof(T), out object? value, asm))
                    return (T)value!;
                ModLogger.Warn($"尝试读取 {key} 值失败，返回默认值");
                return defaultValue;
            }

            void setter(T v)
            {
                storage.Save(DisplayName, Group, v, asm);
            }

            // 若首次建立，则写入默认值
            if (!storage.TryLoad(DisplayName, Group, typeof(T), out object? nowValue, asm))
            {
                setter(defaultValue);
            }
            var entry = new ConfigEntry<T>(asm, DisplayName, Group, defaultValue, getter, setter, action, typeof(T));

            if (uiAttr != null)
                ConfigUIManager.RegisterEntry(entry, uiAttr);

            return key;
        }
    }
}