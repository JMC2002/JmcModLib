using JmcModLib.Config.Entry;
using JmcModLib.Config.Storage;
using JmcModLib.Config.UI;
using JmcModLib.Core;
using JmcModLib.Reflection;
using JmcModLib.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
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

        internal static void CopyConfigPathToClipboard()
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

        private static readonly HashSet<Assembly> hadScan = [];
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
            if (hadScan.Contains(asm))
            {
                ModLogger.Debug("跳过重复扫描程序集 " + ModRegistry.GetTag(asm) + " 的配置项。");
                return;
            }
            hadScan.Add(asm);

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
            entry.SyncFromFile();
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
            hadScan.Remove(asm);
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
        /// 通过显示名称和组名获取Key
        /// </summary>
        /// <param name="displayName"> 显示名称 </param>
        /// <param name="group"> 组名，可选 </param>
        /// <returns> 直接构造一个key，不检验是否存在 </returns>
        public static string GetKey(string displayName, string group = ConfigAttribute.DefaultGroup)
            => BaseEntry.GetKey(displayName, group);

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
        public static void SetValue(string key, object? value, Assembly? asm = null)
        {
            asm ??= Assembly.GetCallingAssembly();
            var entry = GetEntry(key, asm);
            if (entry == null) return;

            SetValue(entry, value);
        }

        /// <summary>
        /// 注册一个按钮，相关文本将自动调用本地化文件
        /// </summary>
        /// <param name="description"> 按钮的描述文本 </param>
        /// <param name="action"> 按钮的行为 </param>
        /// <param name="buttonText"> 按钮上的文本 </param>
        /// <param name="group"> 按钮所在的组，留空为默认 </param>
        /// <param name="asm"> 注册的Assembly集，留空为调用者 </param>
        /// <returns> 返回按钮的Key </returns>
        public static string RegisterButton(string description, Action action, string buttonText = "按钮",
                                            string group = ConfigAttribute.DefaultGroup, Assembly? asm = null)
        {
            asm ??= Assembly.GetCallingAssembly();
            var buttonEntry = new ButtonEntry(asm, action, group, description);
            var uiAttr = new UIButtonAttribute(description, buttonText, group);
            ConfigUIManager.RegisterEntry(buttonEntry, uiAttr);
            return buttonEntry.Key;
        }

        /// <summary>
        /// 通过对象注册单条配置信息的实现
        /// </summary>
        private static string RegisterConfigImpl<T>(Assembly asm, string displayName, T defaultValue, Func<T> getter,
                                                    Action<T> setter, UIConfigAttribute? uiAttr, string group,
                                                    Action<T>? action)
        {
            var entry = ConfigEntryFactory.Create(asm, displayName, group, defaultValue, getter, setter, action, typeof(T), uiAttr);
            RegisterEntry(entry);
            if (uiAttr != null)
                ConfigUIManager.RegisterEntry(entry, uiAttr);
            return entry.Key;
        }

        /// <summary>
        /// 通过值注册单条配置信息的实现
        /// </summary>
        private static string RegisterConfigImpl<T>(Assembly asm, string displayName, T defaultValue,
                                                    string Group = ConfigAttribute.DefaultGroup,
                                                    UIConfigAttribute? uiAttr = null, Action<T>? action = null)
        {
            var storage = GetStorage(asm);
            var key = BaseEntry.GetKey(displayName, Group);

            if (defaultValue == null)
                throw new ArgumentNullException(nameof(defaultValue), $"通过值注册 {key} 传入的参数为空，跳过注册");

            T getter()
            {
                if (storage.TryLoad(displayName, Group, typeof(T), out object? value, asm))
                    return (T)value!;
                ModLogger.Warn($"尝试读取 {key} 值失败，返回默认值");
                return defaultValue;
            }

            void setter(T v) { }    // 由于Set包装了Save语义，此处不再Save

            try
            {
                // 若首次建立，则写入默认值
                if (!storage.TryLoad(displayName, Group, typeof(T), out object? nowValue, asm))
                {
                    storage.Save(displayName, Group, nowValue, asm);
                }
                return RegisterConfigImpl(asm, displayName, defaultValue, getter, setter, uiAttr, Group, action);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"{ModRegistry.GetTag(asm)} 注册配置项 {key} 时出现问题", ex);
                return default!;
            }
        }

        /// <summary>
        /// 仅注册配置项，不绑定任何UI，仅用于持久化
        /// </summary>
        /// <remarks> 可为实例对象，但应当自行维护生命周期，保证自注册至MOD卸载期间不被销毁。 </remarks>
        /// <typeparam name="T"> 注册的值的类型 </typeparam>
        /// <param name="displayName"> 用作在持久化文本中的条目名称 </param>
        /// <param name="getter"> 维护的值的getter，在设置初始值以及卸载阶段调用一次以同步文件 </param>
        /// <param name="setter"> 维护的值的setter，用户若需要在代码中改值，最好使用GetValue(key)，但直接修改自己的值也会在最后保存文件时同步保存，只是不能同步UI </param>
        /// <param name="group"> 值所在的组，若不需要分组则留空 </param>
        /// <param name="asm"> 注册的程序集，留空则为调用者本身 </param>
        /// <returns> 返回配置项的Key，可以通过GetValue/SetValue函数查询系统内的值 </returns>
        public static string RegisterConfig<T>(string displayName, Func<T> getter, Action<T> setter,
                                               string group = ConfigAttribute.DefaultGroup, Assembly? asm = null)
            => RegisterConfigImpl(asm ?? Assembly.GetCallingAssembly(), displayName, getter(), getter, setter, null, group, null);

        /// <summary>
        /// 通过getter/setter 注册一个配置项。
        /// </summary>
        /// <remarks> 可为实例对象，但应当自行维护生命周期，保证自注册至MOD卸载期间不被销毁。 </remarks>
        /// <typeparam name="T"> 注册的值的类型 </typeparam>
        /// <param name="uiAttr"> 需要注册UI的Attribute，相关文本将自动调用本地化文件 </param>
        /// <param name="displayName"> 显示文本，用于保存以及显示在UI系统里 </param>
        /// <param name="getter"> 维护的值的getter，在设置初始值以及卸载阶段调用一次以同步文件 </param>
        /// <param name="setter"> 维护的值的setter，用户若需要在代码中改值，最好使用GetValue(key)，但直接修改自己的值也会在最后保存文件时同步保存，只是不能同步UI </param>
        /// <param name="group"> 值所在的组，若不需要分组则留空 </param>
        /// <param name="action"> 若需注册UI且需要额外的回调函数，则填入，若不需要则留空，此处禁止调用ConfigManager的SetVal </param>
        /// <param name="asm"> 注册的程序集，留空则为调用者本身 </param>
        /// <returns> 返回配置项的Key，可以通过GetValue/SetValue函数查询系统内的值 </returns>
        public static string RegisterConfig<T>(UIConfigAttribute<T> uiAttr, string displayName, Func<T> getter,
                                               Action<T> setter, string group = ConfigAttribute.DefaultGroup,
                                               Action<T>? action = null, Assembly? asm = null)
            => RegisterConfigImpl(asm ?? Assembly.GetCallingAssembly(), displayName, getter(), getter, setter, uiAttr, group, action);

        /// <summary>
        /// 通过枚举值注册一个下拉列表，将自动从枚举值生成下拉列表。
        /// </summary>
        /// <typeparam name="TEnum"> 用于配置的枚举类型 </typeparam>
        /// <param name="uiAttr"> 需要注册UI的Attribute，相关文本将自动调用本地化文件 </param>
        /// <param name="displayName"> 显示文本，用于保存以及显示在UI系统里 </param>
        /// <param name="getter"> 维护的值的getter，在设置初始值以及卸载阶段调用一次以同步文件 </param>
        /// <param name="setter"> 维护的值的setter，用户若需要在代码中改值，最好使用GetValue(key)，但直接修改自己的值也会在最后保存文件时同步保存，只是不能同步UI </param>
        /// <param name="group"> 值所在的组，若不需要分组则留空 </param>
        /// <param name="action"> 若需注册UI且需要额外的回调函数，则填入，若不需要则留空，此处禁止调用ConfigManager的SetVal </param>
        /// <param name="asm"> 注册的程序集，留空则为调用者本身 </param>
        /// <returns> 返回配置项的Key，可以通过GetValue/SetValue函数查询系统内的值 </returns>
        public static string RegisterConfig<TEnum>(UIDropdownAttribute uiAttr, string displayName, Func<TEnum> getter,
                                                   Action<TEnum> setter, string group = ConfigAttribute.DefaultGroup,
                                                   Action<TEnum>? action = null, Assembly? asm = null)
            where TEnum : Enum
            => RegisterConfigImpl(asm ?? Assembly.GetCallingAssembly(), displayName, getter(), getter, setter, uiAttr, group, action);

        /// <summary>
        /// 直接通过非空值注册一个配置项，由此MOD自行维护该值的生命周期，可通过 GetValue/SetValue 查询修改。
        /// </summary>
        /// <typeparam name="T"> 注册的配置项类型 </typeparam>
        /// <param name="uiAttr"> 需要注册UI的Attribute，相关文本将自动调用本地化文件 </param>
        /// <param name="displayName"> 显示文本，用于保存以及显示在UI系统里 </param>
        /// <param name="defaultValue"> 默认值 </param>
        /// <param name="group"> 值所在的组，若不需要分组则留空 </param>
        /// <param name="action"> 若需注册UI且需要额外的回调函数，则填入，若不需要则留空，此处禁止调用ConfigManager的SetVal </param>
        /// <param name="asm"> 注册的程序集，留空则为调用者本身 </param>
        /// <returns> 返回配置项的Key，可以通过GetValue/SetValue函数查询系统内的值 </returns>
        public static string RegisterConfig<T>(UIConfigAttribute<T> uiAttr, string displayName, T defaultValue,
                                               string group = ConfigAttribute.DefaultGroup, Action<T>? action = null,
                                               Assembly? asm = null)
            => RegisterConfigImpl(asm ?? Assembly.GetCallingAssembly(), displayName, defaultValue, group, uiAttr, action);

        /// <summary>
        /// 用一个非空枚举值生成下拉列表注册一个配置项，由此MOD自行维护该值的生命周期，可通过 GetValue/SetValue 查询修改。
        /// </summary>
        /// <typeparam name="TEnum"> 用于配置的枚举类型 </typeparam>
        /// <param name="uiAttr"> 需要注册UI的Attribute，相关文本将自动调用本地化文件 </param>
        /// <param name="displayName"> 显示文本，用于保存以及显示在UI系统里 </param>
        /// <param name="defaultValue"> 默认枚举值 </param>
        /// <param name="group"> 值所在的组，若不需要分组则留空 </param>
        /// <param name="action"> 若需注册UI且需要额外的回调函数，则填入，若不需要则留空，此处禁止调用ConfigManager的SetVal </param>
        /// <param name="asm"> 注册的程序集，留空则为调用者本身 </param>
        /// <returns> 返回配置项的Key，可以通过GetValue/SetValue函数查询系统内的值 </returns>
        public static string RegisterConfig<TEnum>(UIDropdownAttribute uiAttr, string displayName, TEnum defaultValue,
                                                   string group = ConfigAttribute.DefaultGroup,
                                                   Action<TEnum>? action = null, Assembly? asm = null)
                    where TEnum : Enum
                    => RegisterConfigImpl(asm ?? Assembly.GetCallingAssembly(), displayName, defaultValue, group, uiAttr, action);

        /// <summary>
        /// 通过形如 `() => ClassName.StaticName / () => InstanceName.FieldName` 的表达式注册一个配置项，字段/属性/静态/实例均可。
        /// </summary>
        /// <remarks> 可为实例对象，但应当自行维护生命周期，保证自注册至MOD卸载期间不被销毁。 </remarks>
        /// <typeparam name="T"> 注册的配置项类型 </typeparam>
        /// <param name="uiAttr"> 需要注册UI的Attribute，相关文本将自动调用本地化文件 </param>
        /// <param name="displayName"> 显示文本，用于保存以及显示在UI系统里 </param>
        /// <param name="expr"> 形如 `() => ClassName.StaticName / () => InstanceName.FieldName` 的表达式 </param>
        /// <param name="group"> 值所在的组，若不需要分组则留空 </param>
        /// <param name="action"> 若需注册UI且需要额外的回调函数，则填入，若不需要则留空，此处禁止调用ConfigManager的SetVal </param>
        /// <param name="asm"> 注册的程序集，留空则为调用者本身 </param>
        /// <returns> 返回配置项的Key，可以通过GetValue/SetValue函数查询系统内的值 </returns>
        /// <exception cref="ArgumentException"> 传递的表达式不合法 </exception>
        public static string RegisterConfig<T>(UIConfigAttribute<T> uiAttr, string displayName, Expression<Func<T>> expr,
                                               string group = ConfigAttribute.DefaultGroup, Action<T>? action = null,
                                               Assembly? asm = null)
        {
            try
            {
                var (getter, setter) = ExprHelper.GetOrCreateAccessors(expr);
                return RegisterConfig(uiAttr, displayName, getter, setter, group, action, asm);
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"{ModRegistry.GetTag(asm)}: 注册条目 {displayName} 时出现问题", ex);
            }
        }

        /// <summary>
        /// 通过形如 `() => ClassName.StaticName / () => InstanceName.FieldName` 的表达式注册一个下拉列表，字段/属性/静态/实例均可。
        /// </summary>
        /// <typeparam name="TEnum"> 用于配置的枚举类型 </typeparam>
        /// <param name="uiAttr"> 需要注册UI的Attribute，相关文本将自动调用本地化文件 </param>
        /// <param name="displayName"> 显示文本，用于保存以及显示在UI系统里 </param>
        /// <param name="expr"> 形如 `() => ClassName.StaticName / () => InstanceName.FieldName` 的表达式 </param>
        /// <param name="group"> 值所在的组，若不需要分组则留空 </param>
        /// <param name="action"> 若需注册UI且需要额外的回调函数，则填入，若不需要则留空，此处禁止调用ConfigManager的SetVal </param>
        /// <param name="asm"> 注册的程序集，留空则为调用者本身 </param>
        /// <returns> 返回配置项的Key，可以通过GetValue/SetValue函数查询系统内的值 </returns>
        public static string RegisterConfig<TEnum>(UIDropdownAttribute uiAttr, string displayName,
                                                     Expression<Func<TEnum>> expr,
                                                     string group = ConfigAttribute.DefaultGroup,
                                                     Action<TEnum>? action = null, Assembly? asm = null)
            where TEnum : Enum
        {
            try
            {
                var (getter, setter) = ExprHelper.GetOrCreateAccessors(expr);
                return RegisterConfig(uiAttr, displayName, getter, setter, group, action, asm);
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"{ModRegistry.GetTag(asm)}: 注册条目 {displayName} 时出现问题", ex);
            }
        }
    }
}