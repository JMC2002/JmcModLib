using JmcModLib.Core;
using JmcModLib.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine; // JsonUtility

namespace JmcModLib.Config
{
    public sealed class UnityJsonConfigStorage : IConfigStorage
    {
        private readonly string _rootFolder;
        private readonly object _globalLock = new();
        private readonly Dictionary<Assembly, object> _fileLocks = new();

        // 用作内存缓存，避免频繁读写文件
        // cache: asm -> group -> key -> json-string
        private readonly ConcurrentDictionary<Assembly,
            Dictionary<string, Dictionary<string, string>>> _cache = new();

        // 记录哪些 asm 的缓存是脏的，需要写回文件
        private readonly ConcurrentDictionary<Assembly, bool> _dirty = new();


        public UnityJsonConfigStorage(string rootFolder)
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
        [Serializable]
        private class FileWrapper
        {
            // group -> ( key -> json-string )
            public SerializableGroup[] groups = Array.Empty<SerializableGroup>();
        }

        [Serializable]
        private class SerializableGroup
        {
            public string name = "";
            public SerializableKV[] items = Array.Empty<SerializableKV>();
        }

        [Serializable]
        private class SerializableKV
        {
            public string key = "";
            public string json = ""; // 序列化后的字符串
        }

        private Dictionary<string, Dictionary<string, string>> ReadFileRaw(Assembly asm)
        {
            var file = GetFilePath(asm);
            if (!File.Exists(file))
                return new Dictionary<string, Dictionary<string, string>>();

            lock (GetFileLock(asm))
            {
                ModLogger.Debug($"{ModRegistry.GetTag(asm)} 读取配置文件 {file}");
                var raw = File.ReadAllText(file);
                if (string.IsNullOrWhiteSpace(raw))
                    return new Dictionary<string, Dictionary<string, string>>();

                var wrapper = JsonUtility.FromJson<FileWrapper>(raw);
                var dict = new Dictionary<string, Dictionary<string, string>>();

                if (wrapper?.groups != null)
                {
                    foreach (var g in wrapper.groups)
                    {
                        var inner = new Dictionary<string, string>();
                        if (g.items != null)
                        {
                            foreach (var kv in g.items)
                                inner[kv.key] = kv.json;
                        }
                        dict[g.name] = inner;
                    }
                }

                return dict;
            }
        }

        private void WriteFileRaw(Assembly asm, Dictionary<string, Dictionary<string, string>> data)
        {
            var file = GetFilePath(asm);

            var wrapper = new FileWrapper();
            var groups = new List<SerializableGroup>();
            ModLogger.Trace($"共有{data.Count}组");
            foreach (var kv in data)
            {
                var sg = new SerializableGroup { name = kv.Key };
                var items = new List<SerializableKV>();
                ModLogger.Trace($"组 {kv.Key} 有 {kv.Value.Count} 个值");
                foreach (var item in kv.Value)
                    items.Add(new SerializableKV { key = item.Key, json = item.Value });

                sg.items = items.ToArray();
                groups.Add(sg);
            }

            wrapper.groups = groups.ToArray();
            ModLogger.Trace($"wrapper.groups.count = {wrapper.groups.Length}");
            ModLogger.Trace($"wrapper.groups[0].items.Length = {wrapper.groups[0].items.Length}");
            var json = JsonUtility.ToJson(wrapper, true);
            ModLogger.Trace(json);

            lock (GetFileLock(asm))
            {

                File.WriteAllText(file, json);
                ModLogger.Debug($"{ModRegistry.GetTag(asm)} 写入配置文件 {file}");
            }
        }

        private Dictionary<string, Dictionary<string, string>> GetOrLoadCache(Assembly asm)
        {
            return _cache.GetOrAdd(asm, key => ReadFileRaw(asm));
        }

        private Dictionary<string, Dictionary<string, string>> ReadFile(Assembly asm)
        {
            var path = GetFilePath(asm);
            if (!File.Exists(path))
                return new Dictionary<string, Dictionary<string, string>>();

            lock (GetFileLock(asm))
            {
                var raw = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(raw)) return new Dictionary<string, Dictionary<string, string>>();

                var wrapper = JsonUtility.FromJson<FileWrapper>(raw);
                var dict = new Dictionary<string, Dictionary<string, string>>();
                if (wrapper?.groups != null)
                {
                    foreach (var g in wrapper.groups)
                    {
                        var inner = new Dictionary<string, string>();
                        if (g.items != null)
                        {
                            foreach (var kv in g.items)
                                inner[kv.key] = kv.json;
                        }
                        dict[g.name] = inner;
                    }
                }
                return dict;
            }
        }

        private void WriteFile(Assembly asm, Dictionary<string, Dictionary<string, string>> data)
        {
            var path = GetFilePath(asm);
            var wrapper = new FileWrapper();
            var groups = new List<SerializableGroup>();
            foreach (var kv in data)
            {
                var sg = new SerializableGroup { name = kv.Key };
                var items = new List<SerializableKV>();
                foreach (var item in kv.Value)
                {
                    items.Add(new SerializableKV { key = item.Key, json = item.Value });
                }
                sg.items = items.ToArray();
                groups.Add(sg);
            }
            wrapper.groups = groups.ToArray();

            var json = JsonUtility.ToJson(wrapper, true);

            lock (GetFileLock(asm))
            {
                File.WriteAllText(path, json);
            }
        }

        // Helper: 用来包装任意类型的值以便 JsonUtility 序列化
        [Serializable]
        private class ValueWrapper<T>
        {
            public T value = default!;
        }

        private string SerializeValue(object? value)
        {
            if (value == null) return IConfigStorage.NullValue; // sentinel

            Type t = value.GetType();
            var wrapperType = typeof(ValueWrapper<>).MakeGenericType(t);
            var wrapper = Activator.CreateInstance(wrapperType)!;
            var f = wrapperType.GetField("value")!;
            f.SetValue(wrapper, value);
            string json = JsonUtility.ToJson(wrapper);
            return json;
        }

        private object? DeserializeValue(string json, Type targetType)
        {
            if (json == IConfigStorage.NullValue) return null;
            var wrapperType = typeof(ValueWrapper<>).MakeGenericType(targetType);
            var wrapper = JsonUtility.FromJson(json, wrapperType);
            var f = wrapperType.GetField("value")!;
            return f.GetValue(wrapper);
        }

        // -------- IConfigStorage impl --------

        public bool Exists(Assembly? asm)
        {
            asm ??= Assembly.GetCallingAssembly();
            return File.Exists(GetFilePath(asm));
        }

        public bool TryLoad(string key, Type type, out object? value, Assembly? asm)
        {
            asm ??= Assembly.GetCallingAssembly();

            var group = ConfigManager.TryGetGroupForKey(key, asm);
            if (group == null)
            {
                value = null;
                return false;
            }
            group = NormalizeGroup(group);

            var cache = GetOrLoadCache(asm);

            // 如果 group 不存在或 key 不存在，直接返回 false
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
                ModLogger.Error($"{ModRegistry.GetTag(asm)} 尝试加载配置项{key}时反序列化出现异常", ex);
                value = null;
                return false;
            }
        }

        public void Save(string key, object? value, Assembly? asm)
        {
            asm ??= Assembly.GetCallingAssembly();

            var group = ConfigManager.TryGetGroupForKey(key, asm) 
                ?? throw new InvalidOperationException($"{ModRegistry.GetTag(asm)} Key '{key}' 未注册.");

            group = NormalizeGroup(group);

            var cache = GetOrLoadCache(asm);

            if (!cache.TryGetValue(group, out var inner))
            {
                inner = new Dictionary<string, string>();
                cache[group] = inner;
            }

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
