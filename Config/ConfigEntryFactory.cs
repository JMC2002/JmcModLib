using JmcModLib.Config.Entry;
using JmcModLib.Config.UI;
using JmcModLib.Reflection;
using JmcModLib.Utils;
using System;
using System.Reflection;
using Unity.VisualScripting;

namespace JmcModLib.Config
{
    static class ConfigEntryFactory
    {
        private static readonly MethodAccessor CreateTypedMethod =
            MethodAccessor.Get(typeof(ConfigEntryFactory), nameof(CreateTypedGeneric));
        private static readonly MethodAccessor CreateTypedWithConvertMethod =
            MethodAccessor.Get(typeof(ConfigEntryFactory), nameof(CreateTypedWithConvert));

        private static ConfigEntry<T> CreateTypedGeneric<T>(
            Assembly asm,
            MemberAccessor acc,
            MethodAccessor? method,
            ConfigAttribute attr,
            Type logicType)
        {
            return new ConfigEntry<T>(asm, acc, method, attr, logicType);
        }

        private static ConfigEntry<TUI> CreateTypedWithConvert<TUI, TLogical>(
            Assembly asm,
            MemberAccessor member,
            MethodAccessor? method,
            ConfigAttribute attr,
            UINeedCovertAttribute uiAttr)
        {
            if (!member.IsStatic)
                throw new ArgumentException(
                    $"构造{member.Name}出错: 不允许使用MemberAccessor/MethodAccessor构造非静态Config");

            if (!member.CanRead || !member.CanWrite)
                throw new ArgumentException(
                    $"构造{member.Name}出错: ConfigEntry 需要可读写的成员");

            Action<TLogical>? change = null;
            if (method != null)
            {
                if (!ConfigAttribute.IsValidMethod(method.Member, typeof(TLogical), out var lvl, out var error))
                    throw new ArgumentException($"构造{member.Name}出错: {error}");
                else
                {
                    ModLogger.Log(lvl, error);
                    if (method.TypedDelegate is Action<TLogical> t)
                        change = t;
                    else
                        change = v => method.InvokeStaticVoid(v);
                }
            }

            Type logicalType = typeof(TLogical);
            if (uiAttr is UIConverterAttribute<TUI> covAttr)
            {
                void setter(TUI v)
                {
                    TLogical s;
                    try
                    {
                        s = (TLogical)covAttr.FromUI(v, logicalType);
                    }
                    catch (Exception ex)
                    {
                        throw new ArgumentException(
                            $"设置配置项 {attr.DisplayName} 时由转换出错: {ex.Message}", ex);
                    }
                    try
                    {
                        member.SetValue(s);
                    }
                    catch (Exception ex)
                    {
                        throw new ArgumentException(
                            $"设置配置项 {attr.DisplayName} 时出错: {ex.Message}", ex);
                    }
                }

                TUI getter()
                {
                    TLogical s = member.GetValue<TLogical>()!;
                    return covAttr.ToUI(s);
                }

                void action (TUI v)
                {
                    TLogical s = (TLogical)covAttr.FromUI(v, logicalType);
                    change.Invoke(s);
                }

                return new ConfigEntry<TUI>(asm, 
                                            attr.DisplayName, 
                                            attr.Group, 
                                            getter(), 
                                            getter, 
                                            setter,
                                            change == null ? null : action, 
                                            logicalType);
            }
            else
                throw new ArgumentException("UINeedCovertAttribute 类型不正确");
        }

        public static ConfigEntry Create(
            Assembly asm,
            MemberAccessor acc,
            MethodAccessor? method,
            ConfigAttribute attr,
            UIConfigAttribute? uiAttr)
        {
            if (uiAttr != null && uiAttr is UINeedCovertAttribute covAttr)
            {
                var memberType = acc.MemberType;
                var closed = CreateTypedWithConvertMethod.MakeGeneric(covAttr.UIType, memberType);
                return (ConfigEntry)closed.Invoke(null, asm, acc, method, attr, covAttr)!;
            }
            else
            {
                var memberType = acc.MemberType;
                var closed = CreateTypedMethod.MakeGeneric(memberType);
                return (ConfigEntry)closed.Invoke(null, asm, acc, method, attr, memberType)!;
            }
        }
    }

}
