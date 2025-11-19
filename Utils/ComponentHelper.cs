using System;
using UnityEngine;

namespace JmcModLib.Utils
{
    /// <summary>
    /// 一些用来往GameObject里加Component的方法
    /// </summary>
    public static class ComponentHelper
    {
        /// <summary>
        /// 确保组件被添加并初始化
        /// <example>
        /// 示例：
        /// <code>
        /// var ret = Utils.ComponentHelper.AddComponentIfNeeded&lt;UI.ModEntryDragHandler>(__instance.gameObject, handler => handler.Setup(__instance), "ModEntryDragHandler 已添加并初始化");
        /// if (!ret)
        /// {
        ///     ModLogger.Debug("ModEntryDragHandler已经被添加过了");
        /// }
        /// </code>
        /// </example>
        /// </summary>
        /// <typeparam name="T">组件类型</typeparam>
        /// <param name="instance">目标 GameObject</param>
        /// <param name="initializeMethod">初始化方法，接受该组件的实例作为参数</param>
        /// <param name="info">可选的参数，成功添加的日志信息，如果有传递，会打印到Debug日志中</param>
        /// <returns>如果组件已存在返回 false，成功添加返回 true</returns>
        /// 
        public static bool AddComponentIfNeeded<T>(GameObject instance, Action<T> initializeMethod, string? info = null) where T : Component
        {
            if (instance.GetComponent<T>() != null)
            {
                return false;
            }

            initializeMethod(instance.AddComponent<T>());
            if (info != null)
            {
                ModLogger.Debug(info);
            }
            return true;
        }

        /// <summary>
        /// 若GameObject中已存在component，删除后再添加，否则直接添加
        /// </summary>
        /// <typeparam name="T">组件类型</typeparam>
        /// <param name="instance">目标 GameObject</param>
        /// <param name="initializeMethod">初始化方法，接受该组件的实例作为参数</param>
        /// <param name="info">可选的参数，成功添加的日志信息，如果有传递，会打印到Debug日志中</param>
        /// <returns>如果是新增的，返回true，否则返回false</returns>
        public static bool AddComponentAlways<T>(GameObject instance, Action<T> initializeMethod, string? info = null) where T : Component
        {
            bool flg = true;
            if (instance.GetComponent<T>() != null)
            {
                UnityEngine.Object.Destroy(instance.GetComponent<T>());
                flg = false;
            }

            initializeMethod(instance.AddComponent<T>());
            if (info != null)
            {
                ModLogger.Debug(info);
            }
            return flg;
        }

        /// <summary>
        /// 如果GameObject中已存在component组件，执行onComponentFound，否则添加
        /// </summary>
        /// <typeparam name="T">组件类型</typeparam>
        /// <param name="instance">目标 GameObject</param>
        /// <param name="initializeMethod">初始化方法，接受该组件的实例作为参数</param>
        /// <param name="onComponentFound">待执行的函数</param>
        /// <param name="info">可选的参数，成功添加的日志信息，如果有传递，会打印到Debug日志中</param>
        /// <returns></returns>
        public static bool AddComponentOr<T>(GameObject instance, Action<T> initializeMethod, Action<T> onComponentFound, string? info = null) where T : Component
        {
            var component = instance.GetComponent<T>();
            if (component != null)
            {
                onComponentFound(component);
                return false;
            }

            initializeMethod(instance.AddComponent<T>());
            if (info != null)
            {
                ModLogger.Debug(info);
            }
            return true;
        }
    }
}
