using JmcModLib.Core;
using JmcModLib.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine; // JsonUtility
using Newtonsoft.Json;

namespace JmcModLib.Config
{
    public sealed class NewtonsoftConfigStorage : IConfigStorage
    {
        private readonly string _rootFolder;
        private readonly object _globalLock = new();
        private readonly Dictionary<Assembly, object> _fileLocks = new();

        // 用作内存缓存，避免频繁读写文件
        // cache: asm -> group -> key -> json-string
        private readonly ConcurrentDictionary<Assembly,
            Dictionary<string, Dictionary<string, object?>>> _cache = new();

        // 记录哪些 asm 的缓存是脏的，需要写回文件
        private readonly ConcurrentDictionary<Assembly, bool> _dirty = new();


        public NewtonsoftConfigStorage(string rootFolder)
        {
            _rootFolder = rootFolder;
            if (!Directory.Exists(_rootFolder))
                Directory.CreateDirectory(_rootFolder);
        }

        private object GetFileLock(Assembly asm)
        {
            lock (_fileLocks)
            {
                if (!_fileLocks.TryGetValue(asm, out var o))
                {
                    o = new object();
                    _fileLocks[asm] = o;
                }
                return o;
            }
        }

        private string NormalizeGroup(string? g)
            => string.IsNullOrWhiteSpace(g) ? IConfigStorage.DefaultGroup : g!;


        private string GetFilePath(Assembly asm)
        {
            var modName = ModRegistry.GetModInfo(asm)?.Name;
            if (string.IsNullOrWhiteSpace(modName))
                modName = asm.GetName().Name ?? "UnknownMod";
            return Path.Combine(_rootFolder, modName + ".json");
        }

        // ------------------ file read/write ------------------
        public class FileWrapper
        {
            public Dictionary<string, SerializableGroup> groups = new();
        }

        public class SerializableGroup
        {
            public Dictionary<string, object?> items = new();
        }

        private Dictionary<string, Dictionary<string, object?>> ReadFileRaw(Assembly asm)
        {
            var file = GetFilePath(asm);
            if (!File.Exists(file))
                return new();

            lock (GetFileLock(asm))
            {
                ModLogger.Debug($"{ModRegistry.GetTag(asm)} 读取配置文件 {file}");
                var raw = File.ReadAllText(file);
                if (string.IsNullOrWhiteSpace(raw))
                    return new();

                var wrapper = JsonConvert.DeserializeObject<FileWrapper>(raw);
                var dict = new Dictionary<string, Dictionary<string, object?>>();

                foreach (var g in wrapper.groups)
                {
                    var inner = new Dictionary<string, object?>(g.Value.items);
                    dict[g.Key] = inner;
                }

                return dict;
            }
        }

        private void WriteFileRaw(Assembly asm, Dictionary<string, Dictionary<string, object?>> data)
        {
            var file = GetFilePath(asm);

            var groups = new Dictionary<string, SerializableGroup>();
            ModLogger.Trace($"共有{data.Count}组");
            foreach (var g in data)
            {
                var sg = new SerializableGroup();
                foreach (var kv in g.Value)
                    sg.items[kv.Key] = kv.Value; // value 是 object?，Newtonsoft 会自己序列化

                groups[g.Key] = sg;
            }

            var wrapper = new FileWrapper { groups = groups };
            var json = JsonConvert.SerializeObject(wrapper, Formatting.Indented);

            lock (GetFileLock(asm))
            {

                File.WriteAllText(file, json);
                ModLogger.Debug($"{ModRegistry.GetTag(asm)} 写入配置文件 {file}");
            }
        }

        private Dictionary<string, Dictionary<string, object?>> GetOrLoadCache(Assembly asm)
        {
            return _cache.GetOrAdd(asm, key => ReadFileRaw(asm));
        }

        // Helper: 用来包装任意类型的值以便序列化
        [Serializable]
        private class ValueWrapper<T>
        {
            public T value = default!;
        }

        private object? SerializeValue(object? value)
        {
            return value; // 直接返回
        }

        private static object? DeserializeValue(object? raw, Type targetType)
        {
            if (raw == null)
            {
                // null + 值类型（int/float/bool/struct） → 返回默认值
                if (targetType.IsValueType)
                {
                    // 可空值类型 (int?)
                    if (Nullable.GetUnderlyingType(targetType) != null)
                        return null;

                    return Activator.CreateInstance(targetType); // int=0, float=0, bool=false
                }

                // 引用类型
                return null;
            }

            // 如果 raw 已经是目标类型，直接返回
            if (targetType.IsInstanceOfType(raw))
                return raw;

            // 枚举处理
            if (targetType.IsEnum)
            {
                return Enum.Parse(targetType, raw.ToString()!);
            }

            // 可空<T> 处理
            var underlying = Nullable.GetUnderlyingType(targetType);
            if (underlying != null)
            {
                return Convert.ChangeType(raw, underlying);
            }

            // 普通值类型转换
            return Convert.ChangeType(raw, targetType);
        }


        // -------- IConfigStorage impl --------

        public bool Exists(Assembly? asm)
        {
            asm ??= Assembly.GetCallingAssembly();
            return File.Exists(GetFilePath(asm));
        }

        public bool TryLoad(ConfigEntry entry, out object? value, Assembly? asm)
        {
            asm ??= Assembly.GetCallingAssembly();

            var group = entry.Group;
            group = NormalizeGroup(group);
            var key = entry.Attribute.DisplayName;
            Type type = entry.Accessor.MemberType;

            var cache = GetOrLoadCache(asm);

            // 如果 group 不存在或 DisplayName 不存在，直接返回 false
            if (!cache.TryGetValue(group, out var inner) || !inner.TryGetValue(key, out var jsonStr))
            {
                value = null;
                return false;
            }

            try
            {
                value = DeserializeValue(jsonStr, type);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"{ModRegistry.GetTag(asm)} 尝试加载配置项'{key}'时反序列化出现异常", ex);
                value = null;
                return false;
            }
        }

        public void Save(ConfigEntry entry, object? value, Assembly? asm)
        {
            asm ??= Assembly.GetCallingAssembly();

            var group = entry.Group;
            group = NormalizeGroup(group);

            var cache = GetOrLoadCache(asm);

            if (!cache.TryGetValue(group, out var inner))
            {
                inner = new Dictionary<string, object?>();
                cache[group] = inner;
            }

            var key = entry.Attribute.DisplayName;
            inner[key] = SerializeValue(value);

            _dirty[asm] = true;    // 标记为需要写回
        }


        public void Flush(Assembly? asm)
        {
            asm ??= Assembly.GetCallingAssembly();

            if (!_dirty.TryGetValue(asm, out var dirty) || !dirty)
                return; // 未修改，无需写回

            if (!_cache.TryGetValue(asm, out var data))
                return;
            ModLogger.Trace("进入 Flush 阶段");
            WriteFileRaw(asm, data);
            _dirty[asm] = false;
        }
    }
}
