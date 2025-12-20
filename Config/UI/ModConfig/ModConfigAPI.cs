using JmcModLib.Reflection;
using JmcModLib.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace JmcModLib.Config.UI.ModConfig
{
    internal static class ModConfigAPI
    {
        public const string MOD_NAME = "ModConfig"; // ModConfig 的 Mod Name
        private const string MOD_BEHAVIOUR_TYPE = "ModConfig.ModBehaviour";
        private const string OPTIONS_MANAGER_TYPE = "ModConfig.OptionsManager_Mod";

        internal static bool IsInit { get; private set; }

        // 方法访问器缓存
        private static MethodAccessor? _addDropdownList;
        private static MethodAccessor? _addInputWithSlider;
        private static MethodAccessor? _addBoolDropdownList;
        private static MethodAccessor? _addOnOptionsChanged;
        private static MethodAccessor? _removeOnOptionsChanged;

        // 存储访问器缓存 (用于同步值)
        private static MethodAccessor? _managerSave;
        private static MethodAccessor? _managerLoad;

        /// <summary>
        /// 初始化反射访问器
        /// </summary>
        public static bool Init()
        {
            if (IsInit) return true;

            try
            {
                // 1. 查找 ModConfig 程序集
                var modConfigAsm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == MOD_NAME || a.FullName.Contains(MOD_NAME));

                if (modConfigAsm == null)
                {
                    ModLogger.Trace("未找到 ModConfig 程序集");
                    return false;
                }

                // 2. 获取类型
                var behaviourType = modConfigAsm.GetType(MOD_BEHAVIOUR_TYPE);
                var managerType = modConfigAsm.GetType(OPTIONS_MANAGER_TYPE);

                if (behaviourType == null || managerType == null)
                {
                    ModLogger.Error("找到 ModConfig 程序集但未找到核心类型");
                    return false;
                }

                // 3. 构建访问器
                _addDropdownList = MethodAccessor.Get(behaviourType, "AddDropdownList");
                _addInputWithSlider = MethodAccessor.Get(behaviourType, "AddInputWithSlider");
                _addBoolDropdownList = MethodAccessor.Get(behaviourType, "AddBoolDropdownList");
                _addOnOptionsChanged = MethodAccessor.Get(behaviourType, "AddOnOptionsChangedDelegate");
                _removeOnOptionsChanged = MethodAccessor.Get(behaviourType, "RemoveOnOptionsChangedDelegate");

                _managerSave = MethodAccessor.Get(managerType, "Save");
                _managerLoad = MethodAccessor.Get(managerType, "Load");

                IsInit = true;
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error("ModConfigAPI 初始化反射失败", ex);
                return false;
            }
        }

        // ---------- UI 构建 API ----------

        public static void AddDropdownList(string modName, string key, string desc, SortedDictionary<string, object> options, Type type, object defVal)
        {
            if (!IsInit) return;
            // ModConfig 的 key 需要带前缀吗？看源码 AddDropdownList 内部会做 key = $"{modName}_{key}"
            // 所以这里只需要传原始 key
            _addDropdownList?.Invoke(null, modName, key, desc, options, type, defVal);
        }

        public static void AddBoolDropdownList(string modName, string key, string desc, bool defVal)
        {
            if (!IsInit) return;
            _addBoolDropdownList?.Invoke(null, modName, key, desc, defVal);
        }

        public static void AddInputWithSlider(string modName, string key, string desc, Type type, object defVal, Vector2? range)
        {
            if (!IsInit) return;
            // 处理可空参数
            object?[] args = [modName, key, desc, type, defVal, range];
            // 此时必须走通用 Invoke 因为 range 是 Nullable<Vector2> 且位于最后，可能涉及重载匹配或参数补齐
            _addInputWithSlider?.Invoke(null, args);
        }

        // ---------- 事件监听 API ----------

        public static void AddOnOptionsChangedDelegate(Action<string> action)
        {
            if (!IsInit) return;
            _addOnOptionsChanged?.InvokeStaticVoid(action);
        }

        public static void RemoveOnOptionsChangedDelegate(Action<string> action)
        {
            if (!IsInit) return;
            _removeOnOptionsChanged?.InvokeStaticVoid(action);
        }

        // ---------- 数据同步 API (核心) ----------

        /// <summary>
        /// 从 ModConfig 的存储中加载值
        /// </summary>
        public static T Load<T>(string fullKey, T defaultValue)
        {
            if (!IsInit || _managerLoad == null) return defaultValue;
            try
            {
                // Load<T>(string key, T defaultValue)
                var genericLoad = _managerLoad.MakeGeneric(typeof(T));
                return (T)genericLoad.Invoke(null, [fullKey, defaultValue])!;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"ModConfig Load<{typeof(T).Name}> 失败: {fullKey}", ex);
                return defaultValue;
            }
        }

        /// <summary>
        /// 向 ModConfig 的存储中写入值 (实现 Jmc -> ModConfig 的同步)
        /// </summary>
        /// <remarks>
        /// 注意：ModConfig 内部的 key 是 "ModName_Key"。
        /// 如果你传入的是原始 Key，需要自己拼接。但这里的 Save 对应 OptionsManager_Mod.Save(key, val)，
        /// 该方法直接存 ES3，不会自动加前缀。所以调用此方法时必须传入 "ModName_Key"。
        /// </remarks>
        public static void Save<T>(string fullKey, T value)
        {
            if (!IsInit || _managerSave == null) return;
            try
            {
                // Save<T>(string key, T obj)
                var genericSave = _managerSave.MakeGeneric(typeof(T));
                genericSave.Invoke(null, [fullKey, value]);
                ModLogger.Trace($"已同步值到 ModConfig: {fullKey} = {value}");
            }
            catch (Exception ex)
            {
                ModLogger.Error($"ModConfig Save<{typeof(T).Name}> 失败: {fullKey}", ex);
            }
        }
    }
}