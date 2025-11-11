using JmcModLib.Utils;
using SodaCraft.Localizations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace JmcModLib.Core
{
    using TableType = Dictionary<Assembly, Dictionary<string, string>>;
    /// <summary>
    /// 多语言本地化系统（按程序集管理）
    /// </summary>
    public static class Localization
    {
        private static readonly TableType _localizedTables = new();
        private static readonly TableType _fallbackTables = new();
        private static readonly Dictionary<Assembly, string> _basePaths = new();

        private static SystemLanguage _currentLanguage;

        static Localization()
        {
            _currentLanguage = LocalizationManager.CurrentLanguage;
            LocalizationManager.OnSetLanguage += OnLanguageChanged;
        }

        /// <summary>
        /// 当 JmcModLib 或宿主 MOD 卸载时调用
        /// </summary>
        public static void Dispose()
        {
            LocalizationManager.OnSetLanguage -= OnLanguageChanged;
            _basePaths.Clear();
        }

        /// <summary>
        /// 注册当前程序集的本地化文件夹路径（例如 "Mods/MyMod/Lang"）。
        /// 若找不到指定的备用语言对应的文件，会将指定文件夹的第一个 `.csv` 文件作为备用语言文件。       
        /// </summary>
        /// <param name="langFolderRelative">存放本地化csv的相对路径，默认为“Lang”</param>
        /// <param name="fallbackLang">指定某语言文件不存在时的备份语言，默认为英语</param>
        /// <param name="assembly">程序集，默认为调用者</param>
        public static void Register(string langFolderRelative = "Lang"
                                  , SystemLanguage fallbackLang = SystemLanguage.English
                                  , Assembly? assembly = null)
        {
            assembly ??= Assembly.GetCallingAssembly();

            var Tag = Core.ModRegistry.GetTag(assembly);
            if (Tag == null)
                ModLogger.Warn("程序集未注册");
            else
                ModLogger.Debug($"为{Tag}注册本地化模块");

                // 获取 DLL 所在路径
                string? asmPath = assembly.Location;
            if (string.IsNullOrEmpty(asmPath))
            {
                ModLogger.Warn("无法确定程序集路径，可能为动态加载程序集。");
                return;
            }

            string modDir = Path.GetDirectoryName(asmPath)!;

            // 拼接最终语言目录路径
            string langPath = Path.Combine(modDir, langFolderRelative);

            if (!Directory.Exists(langPath))
            {
                ModLogger.Warn($"未找到语言文件夹: {langPath}");
                return;
            }

            _basePaths[assembly] = langPath;
            _localizedTables[assembly] = LoadForAssembly(assembly, _currentLanguage);

            // 首先尝试从fallbackLang获取文件
            _fallbackTables[assembly] = LoadForAssembly(assembly, fallbackLang);

            // 如果 fallbackLang 也没有文件，则自动取第一个 .csv
            if (_fallbackTables[assembly].Count <= 0)
            {
                var csvFiles = Directory.GetFiles(langPath, "*.csv", SearchOption.TopDirectoryOnly);
                if (csvFiles.Length > 0)
                {
                    string firstCsvPath = csvFiles[0];
                    try
                    {
                        _fallbackTables[assembly] = LoadForPath(firstCsvPath);
                        ModLogger.Warn($"未找到 fallback 语言 {fallbackLang}，使用 {Path.GetFileName(firstCsvPath)} 作为备用语言。");
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Error($"加载备用语言文件失败: {firstCsvPath}", ex);
                    }
                }
                else
                {
                    ModLogger.Warn($"未在 {langPath} 中找到任何 .csv 文件。");
                }
            }
        }


        /// <summary>
        /// 翻译当前程序集的键值
        /// </summary>
        public static string Tr(string key, Assembly? assembly = null)
        {
            ModLogger.Debug($"开始寻找{key}");
            assembly ??= Assembly.GetCallingAssembly();
            if (_localizedTables.TryGetValue(assembly, out var dict) &&
                dict.TryGetValue(key, out var value))
            {
                ModLogger.Debug($"成功找到{value}");
                return value;
            }

            if (_fallbackTables.TryGetValue(assembly, out var fallback) 
             && fallback.TryGetValue(key, out var fallbackValue))
            {
                ModLogger.Debug($"在fallback中成功找到{fallbackValue}");
                return fallbackValue;
            }

            // 打印警告，仅在都没找到时提示一次
            var tag = ModRegistry.GetTag(assembly) ?? "[UnknownMod]";
            ModLogger.Warn($"{tag}: 未找到 key = \"{key}\" 对应的本地化文本，返回 key 本身。");

            return key; // fallback to key
        }

        /// <summary>
        /// 跨Mod翻译
        /// </summary>
        public static string TrFrom(string key, Assembly modAssembly)
        {
            if (_localizedTables.TryGetValue(modAssembly, out var dict) &&
                dict.TryGetValue(key, out var value))
            {
                return value;
            }
            return key;
        }

        /// <summary>
        /// 卸载当前程序集的本地化
        /// </summary>
        public static void Unregister(Assembly? assembly = null)
        {
            assembly ??= Assembly.GetCallingAssembly();
            _localizedTables.Remove(assembly);
            _basePaths.Remove(assembly);
        }

        /// <summary>
        /// 当游戏语言切换时自动更新
        /// </summary>
        private static void OnLanguageChanged(SystemLanguage newLang)
        {
            ModLogger.Debug($"检测到语言变更：{_currentLanguage} → {newLang}");
            _currentLanguage = newLang;
            foreach (var asm in _basePaths.Keys)
                _localizedTables[asm] = LoadForAssembly(asm, newLang);
        }

        /// <summary>
        /// 从路径加载
        /// </summary>
        private static Dictionary<string, string> LoadForPath(string path)
        {
            if (!File.Exists(path))
            {
                ModLogger.Warn($"未找到语言文件: {path}");
                return new Dictionary<string, string>();
            }

            try
            {
                ModLogger.Debug($"已加载语言文件：{path}");
                return LoadCSV(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"加载语言文件失败: {path}", ex);
                return new Dictionary<string, string>();
            }
        }

        /// <summary>
        /// 加载指定程序集和语言
        /// </summary>
        private static Dictionary<string, string> LoadForAssembly(Assembly asm, SystemLanguage lang)
        {
            string basePath = _basePaths[asm];
            string fileName = GetLanguageFileName(lang);
            string path = Path.Combine(basePath, fileName);

            return LoadForPath(path);
        }


        /// <summary>
        /// 根据 SystemLanguage 返回语言文件名
        /// </summary>
        private static string GetLanguageFileName(SystemLanguage lang)
        {
            return $"{lang}.csv";
        }

        /// <summary>
        /// 使用游戏自带的 CSV 工具加载
        /// </summary>
        private static Dictionary<string, string> LoadCSV(string csvContent)
        {
            var result = new Dictionary<string, string>();
            var table = CSVUtilities.ReadCSV(csvContent);
            foreach (var row in table)
            {
                if (row.Count >= 2)
                {
                    string key = row[0].Trim();
                    string value = row[1].Trim();
                    if (!result.ContainsKey(key))
                        result[key] = value;
                }
            }
            return result;
        }
    }
}
