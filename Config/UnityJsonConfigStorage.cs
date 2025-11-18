using System;
using System.IO;
using UnityEngine;

namespace JmcModLib.Config
{
    public sealed class UnityJsonConfigStorage : IConfigStorage
    {
        private readonly string _folder;

        public UnityJsonConfigStorage(string folder)
        {
            _folder = folder;
            if (!Directory.Exists(_folder))
                Directory.CreateDirectory(_folder);
        }

        private string GetPath(string key)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                key = key.Replace(c, '_');
            return Path.Combine(_folder, key + ".json");
        }

        public bool Exists(string key) => File.Exists(GetPath(key));

        [Serializable]
        private class Wrapper<T> { public T value = default!; }

        public object? Load(string key, Type type)
        {
            string path = GetPath(key);
            if (!File.Exists(path)) return null;

            string json = File.ReadAllText(path);

            var wrapperType = typeof(Wrapper<>).MakeGenericType(type);
            object wrapper = JsonUtility.FromJson(json, wrapperType);

            return wrapperType.GetField("value").GetValue(wrapper);
        }

        public void Save(string key, object? value)
        {
            string path = GetPath(key);

            var vType = value?.GetType() ?? typeof(object);
            var wrapperType = typeof(Wrapper<>).MakeGenericType(vType);
            object wrapper = Activator.CreateInstance(wrapperType);

            wrapperType.GetField("value").SetValue(wrapper, value);

            string json = JsonUtility.ToJson(wrapper, true);
            File.WriteAllText(path, json);
        }
    }
}
