using JmcModLib.Reflection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace JmcModLib.Utils
{
    /// <summary>
    /// MOD依赖检测结果
    /// </summary>
    public class ModDependencyResult
    {
        public bool IsLoaded { get; set; }
        public bool VersionMatch { get; set; }
        public bool AllMethodsAvailable { get; set; }
        public Version? ActualVersion { get; set; }
        public List<string> MissingMethods { get; set; } = new();
        public Type? ModType { get; set; }

        public bool IsFullyAvailable => IsLoaded && VersionMatch && AllMethodsAvailable;

        public string GetSummary()
        {
            if (!IsLoaded) return "MOD未加载";
            if (!VersionMatch) return $"版本不匹配 (实际: {ActualVersion})";
            if (!AllMethodsAvailable) return $"缺失方法: {string.Join(", ", MissingMethods)}";
            return "完全可用";
        }
    }

    /// <summary>
    /// 方法签名定义
    /// </summary>
    public class MethodSignature
    {
        public string Name { get; set; }
        public Type[]? ParameterTypes { get; set; }
        public bool IsStatic { get; set; }

        public MethodSignature(string name, Type[]? parameterTypes = null, bool isStatic = true)
        {
            Name = name;
            ParameterTypes = parameterTypes;
            IsStatic = isStatic;
        }
    }

    /// <summary>
    /// MOD依赖检测器 - 通用模块
    /// </summary>
    public class ModDependencyChecker
    {
        private readonly string modName;
        private readonly string typeName;
        private readonly Version? requiredVersion;
        private readonly List<MethodSignature> requiredMethods;
        private readonly Dictionary<string, MethodAccessor> methodCache = new();

        private Type? cachedType;
        private ModDependencyResult? cachedResult;

        public ModDependencyChecker(string modName, string typeName, Version? requiredVersion = null)
        {
            this.modName = modName;
            this.typeName = typeName;
            this.requiredVersion = requiredVersion;
            this.requiredMethods = new List<MethodSignature>();
        }

        /// <summary>
        /// 添加必需的方法签名
        /// </summary>
        public ModDependencyChecker RequireMethod(string methodName, Type[]? parameterTypes = null, bool isStatic = true)
        {
            requiredMethods.Add(new MethodSignature(methodName, parameterTypes, isStatic));
            return this;
        }

        /// <summary>
        /// 批量添加必需的方法
        /// </summary>
        public ModDependencyChecker RequireMethods(params MethodSignature[] methods)
        {
            requiredMethods.AddRange(methods);
            return this;
        }

        /// <summary>
        /// 执行完整检测
        /// </summary>
        public ModDependencyResult Check()
        {
            if (cachedResult != null) return cachedResult;

            var result = new ModDependencyResult();

            // 1. 检测类型是否存在
            cachedType = FindTypeInAssemblies(typeName);
            result.ModType = cachedType;
            result.IsLoaded = cachedType != null;

            if (!result.IsLoaded)
            {
                cachedResult = result;
                return result;
            }

            // 2. 检测版本
            if (requiredVersion != null)
            {
                result.ActualVersion = GetModVersion(cachedType);
                result.VersionMatch = result.ActualVersion != null &&
                                     result.ActualVersion >= requiredVersion;
            }
            else
            {
                result.VersionMatch = true; // 未指定版本要求则视为匹配
            }

            // 3. 检测方法
            result.AllMethodsAvailable = CheckMethods(cachedType, result.MissingMethods);

            cachedResult = result;
            return result;
        }

        /// <summary>
        /// 快速检查是否完全可用
        /// </summary>
        public bool IsAvailable()
        {
            return Check().IsFullyAvailable;
        }

        /// <summary>
        /// 获取已缓存的方法访问器（需先调用Check）
        /// </summary>
        public MethodAccessor? GetMethod(string methodName)
        {
            if (cachedType == null)
            {
                ModLogger.Warn($"请先调用Check()检测MOD: {modName}");
                return null;
            }

            return methodCache.TryGetValue(methodName, out var accessor) ? accessor : null;
        }

        /// <summary>
        /// 尝试调用方法（自动处理异常）
        /// </summary>
        public bool TryInvoke(string methodName, object? instance, out object? result, params object?[] args)
        {
            result = null;
            var accessor = GetMethod(methodName);
            if (accessor == null) return false;

            try
            {
                result = accessor.Invoke(instance, args);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"调用方法失败 {modName}.{methodName}", ex);
                return false;
            }
        }

        /// <summary>
        /// 尝试调用void方法
        /// </summary>
        public bool TryInvokeVoid(string methodName, object? instance, params object?[] args)
        {
            var accessor = GetMethod(methodName);
            if (accessor == null) return false;

            try
            {
                accessor.Invoke(instance, args);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"调用方法失败 {modName}.{methodName}", ex);
                return false;
            }
        }

        /// <summary>
        /// 重置缓存（用于MOD热重载等场景）
        /// </summary>
        public void ResetCache()
        {
            cachedResult = null;
            cachedType = null;
            methodCache.Clear();
        }

        // === 私有方法 ===

        private Type? FindTypeInAssemblies(string typeName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (Assembly assembly in assemblies)
            {
                if (assembly.FullName.Contains(modName))
                {
                    ModLogger.Debug($"找到{modName}相关程序集: {assembly.FullName}");
                }

                Type? type = assembly.GetType(typeName);
                if (type != null) return type;
            }

            ModLogger.Warn($"找不到类型: {typeName}");
            return null;
        }

        private Version? GetModVersion(Type type)
        {
            // 尝试多种常见的版本字段名
            string[] versionFieldNames = { "VERSION", "Version", "version", "MOD_VERSION" };

            foreach (var fieldName in versionFieldNames)
            {
                var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
                if (field != null)
                {
                    var value = field.GetValue(null);

                    // 支持 Version 类型
                    if (value is Version version)
                        return version;

                    // 支持 float 类型（如你的示例）
                    if (value is float floatVersion)
                        return ParseFloatVersion(floatVersion);

                    // 支持 string 类型
                    if (value is string strVersion && Version.TryParse(strVersion, out var parsedVersion))
                        return parsedVersion;
                }
            }

            return null;
        }

        private Version ParseFloatVersion(float floatVersion)
        {
            // 0.4f -> 0.4.0
            int major = (int)floatVersion;
            int minor = (int)((floatVersion - major) * 10);
            return new Version(major, minor, 0);
        }

        private bool CheckMethods(Type type, List<string> missingMethods)
        {
            bool allAvailable = true;

            foreach (var methodSig in requiredMethods)
            {
                try
                {
                    var accessor = MethodAccessor.Get(type, methodSig.Name, methodSig.ParameterTypes);
                    methodCache[methodSig.Name] = accessor;
                }
                catch (MissingMethodException)
                {
                    allAvailable = false;
                    string signature = $"{methodSig.Name}({FormatParameters(methodSig.ParameterTypes)})";
                    missingMethods.Add(signature);
                    ModLogger.Warn($"方法不存在: {signature}");
                }
            }

            return allAvailable;
        }

        private string FormatParameters(Type[]? paramTypes)
        {
            if (paramTypes == null || paramTypes.Length == 0) return "";
            return string.Join(", ", paramTypes.Select(t => t.Name));
        }
    }

    /// <summary>
    /// 使用示例与扩展方法
    /// </summary>
    public static class ModDependencyExtensions
    {
        /// <summary>
        /// 创建ModSetting的依赖检测器（预配置版本）
        /// </summary>
        public static ModDependencyChecker CreateModSettingChecker(Version requiredVersion)
        {
            return new ModDependencyChecker("ModSetting", "ModSetting.ModBehaviour", requiredVersion)
                .RequireMethod("AddDropDownList", new[] {
                    typeof(object), typeof(string), typeof(string),
                    typeof(List<string>), typeof(string), typeof(Action<string>)
                })
                .RequireMethod("AddSlider", new[] {
                    typeof(object), typeof(string), typeof(string),
                    typeof(float), typeof(Vector2), typeof(Action<float>), typeof(int), typeof(int)
                })
                .RequireMethod("AddToggle", new[] {
                    typeof(object), typeof(string), typeof(string),
                    typeof(bool), typeof(Action<bool>)
                })
                .RequireMethod("GetValue")
                .RequireMethod("SetValue");
        }

        /// <summary>
        /// 创建通用检测器的快捷方法
        /// </summary>
        public static ModDependencyChecker ForMod(string modName, string typeName, string? versionString = null)
        {
            Version? version = null;
            if (versionString != null && Version.TryParse(versionString, out var v))
                version = v;

            return new ModDependencyChecker(modName, typeName, version);
        }
    }
}

// ============ 使用示例 ============
/*
using JmcModLib.Integration;

// 示例1: 检测ModSetting
var modSettingChecker = ModDependencyExtensions.CreateModSettingChecker(new Version(0, 4, 0));
var result = modSettingChecker.Check();

if (result.IsFullyAvailable)
{
    Debug.Log("ModSetting完全可用！");
    
    // 调用方法
    modSettingChecker.TryInvoke("AddToggle", null, out _, 
        modInfo, "myKey", "描述", true, (Action<bool>)OnToggleChanged);
}
else
{
    Debug.LogWarning($"ModSetting不可用: {result.GetSummary()}");
}

// 示例2: 自定义MOD检测
var customChecker = new ModDependencyChecker("MyAwesomeMod", "MyAwesomeMod.Core", new Version(1, 2, 0))
    .RequireMethod("Initialize", null, true)
    .RequireMethod("ProcessData", new[] { typeof(string), typeof(int) }, true)
    .RequireMethod("GetStatus", null, true);

if (customChecker.IsAvailable())
{
    // 使用强类型调用
    var initMethod = customChecker.GetMethod("Initialize");
    initMethod?.Invoke(null);
    
    // 或使用安全调用
    customChecker.TryInvokeVoid("ProcessData", null, "test", 42);
    
    customChecker.TryInvoke("GetStatus", null, out var status);
    Debug.Log($"状态: {status}");
}

// 示例3: 链式快速创建
var checker = ModDependencyExtensions
    .ForMod("SomeMod", "SomeMod.Manager", "2.1.0")
    .RequireMethod("DoSomething")
    .RequireMethod("DoOther", new[] { typeof(int) });

if (checker.IsAvailable())
{
    // 使用
}
*/