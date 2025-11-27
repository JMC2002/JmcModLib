using JmcModLib.Core;
using SodaCraft.Localizations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace JmcModLib.Utils
{
    using TableType = Dictionary<Assembly, Dictionary<string, string>>;

    /// <summary>
    /// 多语言本地化系统（按程序集管理）
    /// </summary>
    public static class L10n
    {
        private static readonly TableType _localizedTables = [];
        private static readonly TableType _fallbackTables = [];
        private static readonly Dictionary<Assembly, string> _basePaths = [];

        /// <summary>
        /// 当前语言
        /// </summary>
        public static SystemLanguage CurrentLanguage { get; private set; }

        /// <summary>
        /// 语言变更后的事件，不建议订阅<c>LocalizationManager.CurrentLanguage</c>，不然可能本地化不及时生效
        /// </summary>
        public static event Action<SystemLanguage>? LanguageChanged;

        internal static void Init()
        {
            CurrentLanguage = LocalizationManager.CurrentLanguage;
            LocalizationManager.OnSetLanguage += OnLanguageChanged;
            ModRegistry.OnUnRegistered += TryUnRegister;
        }

        /// <summary>
        /// 当 JmcModLib 或宿主 MOD 卸载时调用
        /// </summary>
        internal static void Dispose()
        {
            LocalizationManager.OnSetLanguage -= OnLanguageChanged;
            ModRegistry.OnUnRegistered -= TryUnRegister;
            _basePaths.Clear();
        }

        /// <summary>
        /// 判断某程序集是否已经被注册过语言文件。
        /// </summary>
        public static bool IsRegistered(Assembly assembly)
        {
            return _basePaths.ContainsKey(assembly);
        }

        /// <summary>
        /// 注册当前程序集的本地化文件夹路径（例如 "Mods/MyMod/Lang"）。
        /// 若找不到指定的备用语言对应的文件，会将指定文件夹的第一个 `.csv` 文件作为备用语言文件。
        /// </summary>
        /// <param name="langFolderRelative">存放本地化csv的相对路径，默认为“Lang”</param>
        /// <param name="fallbackLang">指定某语言文件不存在时的备份语言，默认为英语</param>
        /// <param name="assembly">程序集，默认为调用者</param>
        internal static void Register(string langFolderRelative = "Lang"
                                  , SystemLanguage fallbackLang = SystemLanguage.English
                                  , Assembly? assembly = null)
        {
            assembly ??= Assembly.GetCallingAssembly();

            if (IsRegistered(assembly))
            {
                ModLogger.Warn($"尝试重复注册本地化程序集：{assembly.FullName}");
                return;
            }

            var Tag = ModRegistry.GetTag(assembly);
            if (Tag is null)
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
            _localizedTables[assembly] = LoadForAssembly(assembly, CurrentLanguage);

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

            ModLogger.Info($"成功为注册{ModRegistry.GetTag(assembly)}注册本地化");
        }

        /// <summary>
        /// 反注册当前程序集的本地化数据。
        /// </summary>
        /// <param name="assembly">程序集，默认为调用者</param>
        private static void UnRegister(Assembly? assembly = null)
        {
            assembly ??= Assembly.GetCallingAssembly();

            if (!IsRegistered(assembly))
            {
                ModLogger.Warn($"尝试反注册未注册的本地化程序集：{assembly.FullName}");
                return;
            }

            ModLogger.Debug($"反注册 {assembly.GetName().Name} 的本地化数据");

            _basePaths.Remove(assembly);
            _localizedTables.Remove(assembly);
            _fallbackTables.Remove(assembly);
        }

        private static void TryUnRegister(Assembly assembly)
        {
            if (IsRegistered(assembly))
            {
                UnRegister(assembly);
            }
        }

        /// <summary>
        /// 翻译当前程序集的键值
        /// </summary>
        public static string Get(string key, Assembly? assembly = null)
        {
            assembly ??= Assembly.GetCallingAssembly();
            if (!Exist(assembly))
                return key;     // 若未注册，直接返回

            ModLogger.Debug($"开始寻找{key}");
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
            var tag = ModRegistry.GetTag(assembly);
            ModLogger.Warn($"{tag}: 未找到 key = \"{key}\" 对应的本地化文本，返回 key 本身。");

            return key; // fallback to key
        }

        /// <summary>
        /// 当游戏语言切换时自动更新
        /// </summary>
        private static void OnLanguageChanged(SystemLanguage newLang)
        {
            ModLogger.Debug($"检测到语言变更：{CurrentLanguage} → {newLang}");
            CurrentLanguage = newLang;
            foreach (var asm in _basePaths.Keys)
                _localizedTables[asm] = LoadForAssembly(asm, newLang);
            LanguageChanged?.Invoke(newLang);
        }

        /// <summary>
        /// 从路径加载
        /// </summary>
        private static Dictionary<string, string> LoadForPath(string path)
        {
            if (!File.Exists(path))
            {
                ModLogger.Warn($"未找到语言文件: {path}");
                return [];
            }

            try
            {
                ModLogger.Debug($"已加载语言文件：{path}");
                return LoadCSV(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"加载语言文件失败: {path}", ex);
                return [];
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
        public static string GetLanguageFileName(SystemLanguage lang)
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

        /// <summary>
        /// 返回程序集是否注册了本地化文本
        /// </summary>
        /// <param name="asm"></param>
        /// <returns></returns>
        public static bool Exist(Assembly? asm = null)
        {
            asm ??= Assembly.GetCallingAssembly();
            return _basePaths.ContainsKey(asm);
        }

        /// <summary>
        /// 判断程序集在当前语言是否注册了某个Key
        /// </summary>
        public static bool ExistKey(string key, Assembly? asm = null)
        {
            asm ??= Assembly.GetCallingAssembly();
            return _basePaths.ContainsKey(asm) && _localizedTables[asm].ContainsKey(key);
        }
    }
}