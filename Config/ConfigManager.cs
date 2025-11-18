using JmcModLib.Reflection;
using JmcModLib.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace JmcModLib.Config
{
    public static class ConfigManager
    {
        // 按 Assembly 隔离
        private static readonly ConcurrentDictionary<Assembly, List<ConfigEntry>> _entries = new();
        private static readonly ConcurrentDictionary<Assembly, IConfigStorage> _storages = new();

        // 默认存储后端（支持子 MOD 覆盖）
        private static readonly IConfigStorage _defaultStorage =
            new UnityJsonConfigStorage("Configs");

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

        /// <summary>
        /// 自动扫描当前 Assembly 内标记了 [Config] 的字段/属性
        /// </summary>
        /// <param name="asm">默认留空表示扫描当前 Assembly</param>
        public static void RegisterAllInAssembly(Assembly? asm = null)
        {
            asm ??= Assembly.GetCallingAssembly();
            if (_entries.ContainsKey(asm))
                return; // 已注册

            var list = new List<ConfigEntry>();

            foreach (var type in asm.GetTypes())
            {
                foreach (var acc in MemberAccessor.GetAll(type))
                {
                    if (!acc.HasAttribute<ConfigAttribute>())
                        continue;

                    var attr = acc.GetAttribute<ConfigAttribute>()!;
                    var entry = new ConfigEntry(type, acc, attr);

                    list.Add(entry);
                }
            }

            _entries[asm] = list;
        }

        private static ConfigEntry? GetEntry(string DisplayName, Assembly asm)
        {
            if (!_entries.ContainsKey(asm))
            {
                ModLogger.Warn("没有找到配置项！");
                return null;
            }

            return _entries[asm].First(e => e.Attribute.DisplayName == DisplayName);
        }

        /// <summary>
        /// 获取DisplayName代表的配置的值，如果DisplayName不存在，会输出一条Warn
        /// </summary>
        /// <param name="DisplayName">注册时填的Attribute的DisplayName</param>
        /// <param name="asm">指定程序集，留空则为调用者</param>
        /// <returns>如果DisplayName不存在，会输出一条Warn并返回空，否则返回对应值</returns>
        public static object? GetValue(string DisplayName, Assembly? asm = null)
        {
            return GetEntry(DisplayName, asm ?? Assembly.GetCallingAssembly())?.GetValue();
        }

        /// <summary>
        /// 为DisplayName代表的配置的值设置新的值，如果DisplayName不存在，会输出一条Warn
        /// </summary>
        /// <param name="DisplayName">注册时填的Attribute的DisplayName</param>
        /// <param name="newVal">新的值</param>
        /// <param name="asm">指定程序集，留空则为调用者</param>
        /// <returns>如果DisplayName不存在，会输出一条Warn并返回空，否则返回对应值</returns>
        public static void SetValue(string DisplayName, object? newVal, Assembly? asm = null)
        {
            GetEntry(DisplayName, asm ?? Assembly.GetCallingAssembly())?.SetValue(newVal);
        }

        // -------------------------------------------------------
        // 持久化：Load / Save
        // -------------------------------------------------------
        public static void LoadAllInAssembly(Assembly asm)
        {
            if (!_entries.TryGetValue(asm, out var list))
                return;

            var storage = GetStorage(asm);

            foreach (var e in list)
            {
                string key = BuildKey(asm, e);

                if (storage.Exists(key))
                {
                    var val = storage.Load(key, e.Accessor.MemberType);
                    if (val != null)
                        e.SetValue(val);
                }
            }
        }

        public static void SaveAllInAssembly(Assembly asm)
        {
            if (!_entries.TryGetValue(asm, out var list))
                return;

            var storage = GetStorage(asm);

            foreach (var e in list)
            {
                object? val = e.GetValue();
                storage.Save(BuildKey(asm, e), val);
            }
        }

        // 通用 Key：Assembly.FullName + DeclaringType + MemberName
        private static string BuildKey(Assembly asm, ConfigEntry e) =>
            $"{asm.GetName().Name}.{e.DeclaringType.FullName}.{e.Accessor.Name}";
    }
}
