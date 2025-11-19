using System;
using System.Reflection;

namespace JmcModLib.Config
{
    public interface IConfigStorage
    {
        void Save(string key, string group, object? value, Assembly? asm);
        bool TryLoad(string key, string group, Type type, out object? value, Assembly? asm);
        bool Exists(Assembly? asm);

        void Flush(Assembly? asm);
    }
}
