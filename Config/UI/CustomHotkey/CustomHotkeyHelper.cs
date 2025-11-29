using Duckov.Modding;
using JmcModLib.Reflection;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// * 改为你的命名空间
namespace JmcModLib.Config.UI.CustomHotkey
{
    internal static class CustomHotkeyHelper
    {
        internal static bool IsInited => customHotkey is not null;
        internal const ulong publishedFileId = 3594709838; // CustomHotkey 模组的 Steam 发布文件 ID
        internal const string ModName = "CustomHotkey"; // CustomHotkey 模组的显示名

        // * 改为你的模组名
        // private const string ModName = "YourModName";

        private static Duckov.Modding.ModBehaviour? customHotkey;

        private static MethodInfo? addNewHotkeyMethod;
        private static MethodInfo? removeHotkeyMethod;
        private static MethodInfo? getHotkeyMethod;
        private static EventInfo? onCustomHotkeyChangedEvent;
        private static MemberAccessor? CustomHotkeyDict;

        internal static string GetKey(string modName, string saveName)
        {
            return modName + "_" + saveName;
        }

        internal static void SetKey(string modName, string saveName, KeyCode keyCode)
        {
            if (CustomHotkeyDict == null || customHotkey == null)
                return;
            var key = GetKey(modName, saveName);
            var dict = CustomHotkeyDict.GetValue<Duckov.Modding.ModBehaviour, Dictionary<string, KeyCode>>(customHotkey);
            if (dict == null || !dict.ContainsKey(key))
                return;

            var chv = dict[key];

            // ② HotkeyMono
            var hotkeyMonoAcc = MemberAccessor.Get(chv.GetType(), "HotkeyMono");
            var hotkeyMono = hotkeyMonoAcc.GetValue(chv);
            if (hotkeyMono == null || hotkeyMono.Equals(null))
                return;

            // ③ RefreshInputIndicator(KeyCode)
            var refresh = MethodAccessor.Get(
                hotkeyMono.GetType(),
                "RefreshInputIndicator",
                [typeof(KeyCode)]
            );

            refresh.Invoke(hotkeyMono, keyCode);
        }

        /// <summary>
        /// 尝试初始化
        /// </summary>
        /// <remarks>该方法需要首先调用，用来缓存一些反射用的变量</remarks>
        public static bool TryInit()
        {
            if (customHotkey != null)
                return true;
            (bool isFind, ModInfo modInfo) = TryGetCustomHotkeyModInfo();
            if (!isFind)
            {
                // ModLogger.Debug($"未找到CustomHotkey模组信息");
                return false;
            }
            if (!ModManager.IsModActive(modInfo, out customHotkey))
            {
                // ModLogger.Debug($"CustomHotkey模组未激活");
                return false;
            }

            Type customHotkeyType = customHotkey.GetType();
            addNewHotkeyMethod = customHotkeyType.GetMethod("AddNewHotkey", BindingFlags.Public | BindingFlags.Instance);
            removeHotkeyMethod = customHotkeyType.GetMethod("RemoveHotkey", BindingFlags.Public | BindingFlags.Instance);
            getHotkeyMethod = customHotkeyType.GetMethod("GetHotkey", BindingFlags.Public | BindingFlags.Instance);
            onCustomHotkeyChangedEvent = customHotkeyType.GetEvent("OnCustomHotkeyChanged", BindingFlags.Public | BindingFlags.Static);
            CustomHotkeyDict = MemberAccessor.Get(customHotkeyType, "customHotkeyDict");
            return true;
        }

        /// <summary>
        /// 添加新的自定义热键
        /// </summary>
        /// <param name="saveName">保存的热键名</param>
        /// <param name="defaultHotkey">默认热键值</param>
        /// <param name="showName">显示的热键名</param>
        public static void AddNewHotkey(string ModName, string saveName, KeyCode defaultHotkey, string showName)
        {
            if (customHotkey == null)
            {
                //Debug.Log($"{ModName}：未找到CustomHotkey模组实例");
                return;
            }
            addNewHotkeyMethod?.Invoke(customHotkey, [ModName, saveName, defaultHotkey, showName]);
        }

        /// <summary>
        /// 移除自定义热键
        /// </summary>
        /// <param name="saveName">保存的热键名</param>
        public static void RemoveHotkey(string ModName, string saveName)
        {
            if (customHotkey == null)
            {
                //Debug.Log($"{ModName}：未找到CustomHotkey模组实例");
                return;
            }
            removeHotkeyMethod?.Invoke(customHotkey, [ModName, saveName]);
        }

        /// <summary>
        /// 获取自定义按键值
        /// </summary>
        /// <param name="saveName">保存的热键名</param>
        public static KeyCode GetHotkey(string ModName, string saveName)
        {
            if (customHotkey == null)
            {
                //Debug.Log($"{ModName}：未找到CustomHotkey模组实例");
                return KeyCode.None;
            }

            object? result = getHotkeyMethod?.Invoke(customHotkey, [ModName, saveName]);
            if (result == null)
                return KeyCode.None;
            return Enum.TryParse(result.ToString(), out KeyCode keyCode) ? keyCode : KeyCode.None;
        }

        /// <summary>
        /// 尝试添加当热键修改时的回调
        /// </summary>
        /// <remarks>多次调用并不会重复添加回调</remarks>
        public static void TryAddEvent2OnCustomHotkeyChangedEvent(string ModName, Action callback)
        {
            if (onCustomHotkeyChangedEvent == null)
                return;
            onCustomHotkeyChangedEvent.RemoveEventHandler(null, callback);
            onCustomHotkeyChangedEvent.AddEventHandler(null, callback);
        }

        /// <summary>
        /// 移除当热键修改时的回调
        /// </summary>
        public static void RemoveEvent2OnCustomHotkeyChangedEvent(string ModName, Action callback)
        {
            onCustomHotkeyChangedEvent?.RemoveEventHandler(null, callback);
        }

        private static (bool isFind, ModInfo modInfo) TryGetCustomHotkeyModInfo()
        {
            List<ModInfo>? modInfos = ModManager.modInfos;
            if (modInfos == null || modInfos.Count == 0)
                return (false, default);
            foreach (ModInfo modInfo in modInfos)
            {
                if (modInfo.publishedFileId == publishedFileId)
                    return (true, modInfo);
            }
            return (false, default);
        }
    }
}
