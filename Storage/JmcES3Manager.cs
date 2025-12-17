using UnityEngine;
using System;
using System.IO;
using System.Reflection;
using System.Collections.Concurrent;
using JmcModLib.Core;
using JmcModLib.Utils;

namespace JmcModLib.Storage
{
    /// <summary>
    /// 基于 Easy Save 3 (ES3) 的通用数据存储管理器。
    /// 专用于保存 Mod 的状态、存档数据或复杂对象（区别于 Config 的 JSON 配置）。
    /// 数据默认存储在 Application.persistentDataPath/Saves/JmcModLib/Storage 下。
    /// </summary>
    public static class JmcES3Manager
    {
        // 定义默认根目录 (按照你的要求)
        private static readonly string _defaultRootFolder = Path.Combine(Application.persistentDataPath, "Saves/JmcModLib/Storage");

        // 缓存 ES3Settings，避免重复创建对象造成 GC 压力
        private static readonly ConcurrentDictionary<Assembly, ES3Settings> _settingsCache = new();

        /// <summary>
        /// 静态构造函数，确保目录存在
        /// </summary>
        static JmcES3Manager()
        {
            try
            {
                if (!Directory.Exists(_defaultRootFolder))
                {
                    Directory.CreateDirectory(_defaultRootFolder);
                    // 只有在第一次创建时打印日志，避免刷屏
                    ModLogger.Debug($"[JmcES3] 创建文件夹: {_defaultRootFolder}");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"[JmcES3] 创建文件夹 {_defaultRootFolder} 失败", ex);
            }
        }

        /// <summary>
        /// 获取指定 Assembly 对应的 ES3Settings。
        /// 文件名格式为: ModName.es3
        /// </summary>
        private static ES3Settings GetSettings(Assembly asm)
        {
            return _settingsCache.GetOrAdd(asm, (a) =>
            {
                // 尝试从 JmcModLib 的注册表中获取 Mod 名称，如果没有则使用程序集名称
                var modName = ModRegistry.GetModInfo(a)?.Name;
                if (string.IsNullOrWhiteSpace(modName))
                    modName = a.GetName().Name ?? "UnknownMod";

                // 拼接完整路径
                string fileName = $"{modName}.ES3";
                string fullPath = Path.Combine(_defaultRootFolder, fileName);

                return new ES3Settings
                {
                    location = ES3.Location.File,
                    path = fullPath
                    // 如果需要加密，可以在这里统一添加:
                    // encryptionType = ES3.EncryptionType.AES,
                    // password = "JmcModLib_Secret"
                };
            });
        }

        // ================= 公开 API =================

        /// <summary>
        /// 保存数据
        /// </summary>
        /// <typeparam name="T">数据类型 (支持 Unity 原生类型)</typeparam>
        /// <param name="key">键</param>
        /// <param name="value">值</param>
        /// <param name="asm">调用者的程序集 (自动获取，无需传递)</param>
        public static void Save<T>(string key, T value, Assembly? asm = null)
        {
            asm ??= Assembly.GetCallingAssembly();
            var settings = GetSettings(asm);

            try
            {
                ES3.Save(key, value, settings);
                // ES3 默认即时写入，如果需要高性能频繁写入，可考虑手动控制缓存
                ModLogger.Trace($"{ModRegistry.GetTag(asm)} 保存 '{key}'");
            }
            catch (Exception ex)
            {
                ModLogger.Error($"{ModRegistry.GetTag(asm)} [JmcES3] 保存 ('{key}') 时错误: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 读取数据
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="key">键</param>
        /// <param name="defaultValue">默认值 (当文件或Key不存在时返回)</param>
        /// <param name="asm">调用者的程序集 (自动获取，无需传递)</param>
        /// <returns>读取到的值或默认值</returns>
        public static T Load<T>(string key, T defaultValue, Assembly? asm = null)
        {
            asm ??= Assembly.GetCallingAssembly();
            var settings = GetSettings(asm);

            try
            {
                // ES3.Load 内部会自动处理文件不存在或Key不存在的情况，返回 defaultValue
                return ES3.Load(key, defaultValue, settings);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"{ModRegistry.GetTag(asm)} [JmcES3] 加载错误 ('{key}'): {ex.Message}", ex);
                return defaultValue;
            }
        }

        /// <summary>
        /// 检查是否存在某个 Key
        /// </summary>
        public static bool HasKey(string key, Assembly? asm = null)
        {
            asm ??= Assembly.GetCallingAssembly();
            var settings = GetSettings(asm);
            return ES3.KeyExists(key, settings);
        }

        /// <summary>
        /// 删除某个 Key
        /// </summary>
        public static void DeleteKey(string key, Assembly? asm = null)
        {
            asm ??= Assembly.GetCallingAssembly();
            var settings = GetSettings(asm);

            if (ES3.KeyExists(key, settings))
            {
                ES3.DeleteKey(key, settings);
                ModLogger.Trace($"{ModRegistry.GetTag(asm)} [JmcES3] Deleted Key '{key}'");
            }
        }

        /// <summary>
        /// 删除该 Mod 的整个存档文件
        /// </summary>
        public static void DeleteFile(Assembly? asm = null)
        {
            asm ??= Assembly.GetCallingAssembly();
            var settings = GetSettings(asm);

            if (ES3.FileExists(settings))
            {
                ES3.DeleteFile(settings);
                ModLogger.Debug($"{ModRegistry.GetTag(asm)} [JmcES3] Deleted File '{settings.path}'");
            }
        }

        /// <summary>
        /// 获取当前 Mod 的存档文件完整路径 (用于调试或手动操作)
        /// </summary>
        public static string GetSaveFilePath(Assembly? asm = null)
        {
            asm ??= Assembly.GetCallingAssembly();
            return GetSettings(asm).path;
        }
    }
}