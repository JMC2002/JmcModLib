using Duckov.Modding;
using JmcModLib.Core;
using JmcModLib.Core.AttributeRouter;
using JmcModLib.Reflection;
using JmcModLib.Utils;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace JmcModLib.Dependency
{
    /// <summary>
    /// 负责解析并注册 [ModLink] 标记的方法，让 ModLinker 正确调度。
    /// </summary>
    public sealed class ModLinkAttributeHandler : IAttributeHandler
    {
        private static readonly Type[] _allowedParamTypes =
        [
            typeof(ModInfo),
            typeof(Duckov.Modding.ModBehaviour)
        ];

        // 判断方法是否符合 ModLink 的要求
        private static bool IsValidLinkMethod(MethodInfo m)
        {
            if (!m.IsStatic)
            {
                ModLogger.Error($"[ModLink] 方法 {m.Name} 必须是静态函数");
                return false;
            }

            var ps = m.GetParameters();

            if (ps.Length < 0 || ps.Length > _allowedParamTypes.Length)
            {
                ModLogger.Error($"[ModLink] 方法 {m.Name} 参数数量不符合要求，当前为 {ps.Length}，允许范围为 0 到 {_allowedParamTypes.Length}");
                return false;
            }

            // 逐个检查参数必须按顺序匹配 _allowedParamTypes
            for (int i = 0; i < ps.Length; i++)
            {
                if (ps[i].ParameterType != _allowedParamTypes[i])
                {
                    ModLogger.Error($"[ModLink] 方法 {m.Name} 参数类型不符合要求，第 {i + 1} 个参数应为 {_allowedParamTypes[i].Name}，但实际为 {ps[i].ParameterType.Name}");
                    return false;
                }
            }

            // 检查返回值
            if (m.ReturnType != typeof(void))
            {
                ModLogger.Debug($"[ModLink] 方法 {m.Name} 有返回值 {m.ReturnType.Name})，但将被忽略。");
            }

            return true;
        }


        public void Handle(Assembly asm, ReflectionAccessorBase accessor, Attribute attribute)
        {
            if (attribute is not ModLinkAttribute link)
                return;

            if (accessor is not MethodAccessor method)
            {
                ModLogger.Error("[ModLink] 只能标记在方法上");
                return;
            }

            if (!IsValidLinkMethod(method.MemberInfo))
            {
                // 错误信息已在 IsValidLinkMethod 中输出
                return;
            }

            var mi = method.MemberInfo;


            Action call;

            if (!mi.IsStatic)
            {
                ModLogger.Error($"方法 {mi.Name} 必须是静态函数");
                return;
            }
            else
            {
                call = () => mi.Invoke(null, null);
            }

            bool flg = ModLinker.TryRegister(link.Name, method, link.Event, asm, true);
            if (flg)
            {
                ModLogger.Debug($"{ModRegistry.GetTag(asm)} 注册方法 {mi.Name} 监听 {link.Name} 的 {link.Event}");
            }
            else
            {
                ModLogger.Warn($"{ModRegistry.GetTag(asm)} 注册方法 {mi.Name} 监听 {link.Name} 的 {link.Event} 失败");
            }

        }

        /// <summary>
        /// 当整个 Assembly 注销时，Router 会调用这里执行清理
        /// </summary>
        public Action<Assembly, IReadOnlyList<ReflectionAccessorBase>>? Unregister =>
            (asm, list) =>
            {
                // 注销整个 Assembly 的所有 ModLink
                ModLinker.UnregisterAssembly(asm);
            };
    }
}
