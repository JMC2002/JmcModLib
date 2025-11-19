using System;
using System.Reflection;

namespace JmcModLib.Config
{
    public interface IConfigStorage
    {
        public const string NullValue = "__null__";
        public const string DefaultGroup = "__default";

        // void Save(string key, object? value, Assembly? asm);
        void Save(ConfigEntry entry, object? value, Assembly? asm);
        // bool TryLoad(string key, Type type, out object? value, Assembly? asm);
        bool TryLoad(ConfigEntry entry, out object? value, Assembly? asm);
        bool Exists(Assembly? asm);

        void Flush(Assembly? asm);
    }
}
