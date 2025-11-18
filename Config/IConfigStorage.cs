using System;

namespace JmcModLib.Config
{
    public interface IConfigStorage
    {
        void Save(string key, object? value);
        object? Load(string key, Type type);
        bool Exists(string key);
    }
}
