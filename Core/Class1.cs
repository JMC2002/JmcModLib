using JmcModLib.Utils;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace JmcDuckovLib.Core
{
    public static class ModRegistry
    {
        private static readonly Dictionary<Assembly, ModInfo> _mods = new();

        public static void Register(string name, string version = "1.0.0", LogLevel level = LogLevel.Info, Assembly? assembly = null)
        {
            assembly ??= Assembly.GetCallingAssembly();
            _mods[assembly] = new ModInfo(name, version, level);
        }

        public static ModInfo? GetModInfo(Assembly? assembly = null)
        {
            assembly ??= Assembly.GetCallingAssembly();
            return _mods.TryGetValue(assembly, out var info) ? info : null;
        }

        public static void SetLogLevel(string name, LogLevel level)
        {
            foreach (var kvp in _mods)
            {
                if (kvp.Value.Name == name)
                {
                    _mods[kvp.Key] = kvp.Value with { Level = level };
                    return;
                }
            }
        }

        public static string? GetTag(Assembly? assembly = null)
        {
            var info = GetModInfo(assembly);
            return info is null ? null : $"[{info.Value.Name} v{info.Value.Version}]";
        }

        public record ModInfo(string Name, string Version, LogLevel Level);
    }
}
