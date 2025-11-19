using System;
using System.Reflection;

namespace JmcModLib.Config
{
    public interface IConfigStorage
    {
        public const string NullValue = "__null__";
        public const string DefaultGroup = "__default";

        void Save(string key, string group, object? value, Assembly? asm);
        bool TryLoad(string key, string group, Type type, out object? value, Assembly? asm);
        bool Exists(Assembly? asm);

        void Flush(Assembly? asm);
    }
}
