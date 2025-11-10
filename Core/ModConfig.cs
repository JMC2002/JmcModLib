using JmcModLib.Utils;
using System;
using System.IO;
using UnityEngine;

namespace JmcModLib.Core
{
    public static class ModConfig
    {
        // === 私有存储字段 ===
        private static bool _enableDebugLogs = false;
        public static bool EnableDebugLogs
        {
            get => _enableDebugLogs;
            set => Set(ref _enableDebugLogs, value);
        }

        // === 事件：配置变化通知 ===
        public static event Action? OnConfigChanged;

        // === 文件路径 ===
        private static readonly string ConfigDir =
            Path.Combine(Application.persistentDataPath, "Saves");

        private static readonly string ConfigName = $"{VersionInfo.Name}Config.json";
        private static readonly string ConfigPath = Path.Combine(ConfigDir, ConfigName);

        // === 统一更新接口 ===
        private static void Set<T>(ref T field, T newValue, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        {
            if (!Equals(field, newValue))
            {
                var oldValue = field;
                field = newValue;
                Save();
                OnConfigChanged?.Invoke();
                ModLogger.Info($"配置更新: {name} {oldValue} → {newValue}");
            }
        }

        // === 加载配置 ===
        public static void Load()
        {
            try
            {
                if (!Directory.Exists(ConfigDir))
                {
                    Directory.CreateDirectory(ConfigDir);
                    ModLogger.Warn($"配置目录不存在，已创建：{ConfigDir}");
                }

                if (!File.Exists(ConfigPath))
                {
                    ModLogger.Warn("配置文件不存在，创建默认配置...");
                    Save();
                    return;
                }

                string json = File.ReadAllText(ConfigPath);
                var cfg = JsonUtility.FromJson<TempConfig>(json);

                if (cfg == null)
                {
                    ModLogger.Error("配置文件内容不合法，已恢复默认配置。");
                    File.Delete(ConfigPath);
                    Save();
                    return;
                }

                _enableDebugLogs = cfg.EnableDebugLogs;

                ModLogger.Info("配置加载完成。");
                OnConfigChanged?.Invoke();
            }
            catch (Exception ex)
            {
                ModLogger.Error("加载配置时发生异常，恢复默认配置。", ex);
                Save();
            }
        }

        // === 保存配置 ===
        public static void Save()
        {
            try
            {
                var cfg = new TempConfig
                {
                    EnableDebugLogs = _enableDebugLogs
                };

                string json = JsonUtility.ToJson(cfg, true);
                File.WriteAllText(ConfigPath, json);

                ModLogger.Info($"配置已保存: {ConfigPath}");
            }
            catch (Exception ex)
            {
                ModLogger.Error("保存配置时发生异常。", ex);
            }
        }

        public static void InvokeConfigChanged()
        {
            OnConfigChanged?.Invoke();
        }

        // === 内部序列化类 ===
        [Serializable]
        private class TempConfig
        {
            public bool EnableDebugLogs;
        }
    }
}
