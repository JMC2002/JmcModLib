using JmcModLib.Config.UI;
using JmcModLib.Reflection;
using System;
using System.Reflection;

namespace JmcModLib.Config.Entry
{
    internal static class ConfigEntryFactory
    {
        private static readonly MethodAccessor CreateTypedMethod =
            MethodAccessor.Get(typeof(ConfigEntryFactory), nameof(CreateTypedGeneric));
        private static readonly MethodAccessor CreateTypedWithConvertMethod =
            MethodAccessor.Get(typeof(ConfigEntryFactory), nameof(CreateTypedWithConvert));
        private static readonly MethodAccessor CreateTypedWithConvertActionMethod =
            MethodAccessor.Get(typeof(ConfigEntryFactory), nameof(CreateTypedWithConvertAction));

        private static ConfigEntry<T> CreateTypedGeneric<T>(
            Assembly asm,
            MemberAccessor acc,
            MethodAccessor? method,
            ConfigAttribute attr,
            Type logicType,
            UIConfigAttribute<T>? uiAttr)
        {
            return new ConfigEntry<T>(asm, acc, method, attr, logicType, uiAttr);
        }

        private static ConfigEntry<TUI> CreateTypedWithConvertAction<TUI, TLogical>(
            Assembly asm,
            string displayName,
            string group,
            TLogical defaultOri,
            Func<TLogical> getterOri,
            Action<TLogical> setterOri,
            Action<TLogical>? change,
            UINeedCovertAttribute uiAttr)
        {
            Type logicalType = typeof(TLogical);
            if (uiAttr is UIConverterAttribute<TUI> covAttr)
            {
                TUI defaultValue;
                try
                {
                    defaultValue = covAttr.ToUI(defaultOri!);

                }
                catch (Exception ex)
                {
                    throw new ArgumentException(
                        $"创建配置项 {displayName} 时转换默认值出错: {ex.Message}", ex);
                }
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
                            $"设置配置项 {displayName} 时转换出错: {ex.Message}", ex);
                    }
                    try
                    {
                        setterOri(s);
                    }
                    catch (Exception ex)
                    {
                        throw new ArgumentException(
                            $"设置配置项 {displayName} 时出错: {ex.Message}", ex);
                    }
                }

                TUI getter()
                {
                    TLogical s = getterOri()!;
                    return covAttr.ToUI(s);
                }

                void action(TUI v)
                {
                    TLogical s = (TLogical)covAttr.FromUI(v, logicalType);
                    change.Invoke(s);
                }

                return new ConfigEntry<TUI>(asm, displayName, group, defaultValue, getter, setter,
                                            change == null ? null : action, logicalType, covAttr);
            }
            else
                throw new ArgumentException("UINeedCovertAttribute 类型不正确");
        }

        private static ConfigEntry<TUI> CreateTypedWithConvert<TUI, TLogical>(
            Assembly asm,
            MemberAccessor member,
            MethodAccessor? method,
            ConfigAttribute attr,
            UINeedCovertAttribute uiAttr)
        {
            var (getter, setter, change) = ConfigEntry<TLogical>.TraitAccessors(member, method);
            return CreateTypedWithConvertAction<TUI, TLogical>(asm, attr.DisplayName, attr.Group, getter(), getter, setter, change, uiAttr);
        }

        public static ConfigEntry Create(
            Assembly asm,
            MemberAccessor acc,
            MethodAccessor? method,
            ConfigAttribute attr,
            UIConfigAttribute? uiAttr)
        {
            if (uiAttr is not null and UINeedCovertAttribute covAttr)
            {
                var memberType = acc.MemberType;
                var closed = CreateTypedWithConvertMethod.MakeGeneric(covAttr.UIType, memberType);
                return (ConfigEntry)closed.Invoke(null, asm, acc, method, attr, covAttr)!;
            }
            else
            {
                var memberType = acc.MemberType;
                var closed = CreateTypedMethod.MakeGeneric(memberType);
                return (ConfigEntry)closed.Invoke(null, asm, acc, method, attr, memberType, uiAttr)!;
            }
        }

        public static ConfigEntry Create<T>(Assembly asm,
                           string displayName,
                           string group,
                           T defaultValue,
                           Func<T> getter,
                           Action<T> setter,
                           Action<T>? action,
                           Type logicType,
                           UIConfigAttribute? uiAttr)
        {
            if (uiAttr is not null and UINeedCovertAttribute covAttr)
            {
                var closed = CreateTypedWithConvertActionMethod.MakeGeneric(covAttr.UIType, typeof(T));
                return (ConfigEntry)closed.Invoke(null, asm, displayName, group, defaultValue, getter, setter, action, uiAttr)!;
            }
            else
            {
                return new ConfigEntry<T>(asm, displayName, group, defaultValue, getter, setter, action, logicType, uiAttr);
            }
        }
    }

}
