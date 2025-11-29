using JmcModLib.Config;
using JmcModLib.Config.UI;
using JmcModLib.Utils;
using System;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;

namespace JmcModLib.Core
{
    /// <summary>
    /// 用于支持链式调用注册的构建器，以注册需要手动特殊指定的模块，仅可通过 ModRegistry.Register(bool deferredCompletion, ...) 获取。
    /// </summary>
    /// <remarks>
    /// 结束后调用 Done() 完成注册，否则不会触发注册完成事件。
    /// </remarks>
    public sealed class RegistryBuilder
    {
        private readonly Assembly _assembly;
        internal RegistryBuilder(Assembly assembly)
        {
            if (assembly == null)
                throw new ArgumentNullException(nameof(assembly));
            if (!ModRegistry.IsRegistered(assembly))
                throw ModRegistry.CreateNotRegisteredException(assembly);
            this._assembly = assembly;
        }

        internal bool _registedL10n = false;
        internal bool _registedLogger = false;

        /// <summary>
        /// 注册当前程序集的本地化文件夹路径（例如 "Mods/MyMod/Lang"）。
        /// 若找不到指定的备用语言对应的文件，会将指定文件夹的第一个 `.csv` 文件作为备用语言文件。
        /// </summary>
        /// <param name="langFolderRelative">存放本地化csv的相对路径，默认为“Lang”</param>
        /// <param name="fallbackLang">指定某语言文件不存在时的备份语言，默认为英语</param>
        /// <returns> 返回当前的RegsistryBuilder实例以支持链式调用 </returns>
        public RegistryBuilder RegisterL10n(string langFolderRelative = "Lang",
                                            SystemLanguage fallbackLang = SystemLanguage.English)
        {
            if (_registedL10n)
            {
                ModLogger.Warn($"已为{ModRegistry.GetTag(_assembly)}注册本地化，不能重复注册");
                return this;
            }
            _registedL10n = true;
            L10n.Register(langFolderRelative, fallbackLang, _assembly);
            return this;
        }

        /// <summary>
        /// 注册当前日志库的日志打印设置。
        /// </summary>
        /// <param name="level"> 默认的最低打印等级 </param>
        /// <param name="tagFlags"> 默认显示的标签 </param>
        /// <param name="uIFlags"> 默认的自动添加UI，默认添加日志最低打印等级列表与各个标签的开关并集成到一个组里 </param>
        /// <returns> 返回当前的RegsistryBuilder实例以支持链式调用 </returns>
        public RegistryBuilder RegisterLogger(LogLevel level = ModLogger.DefaultLogLevel,
                                              LogFormatFlags tagFlags = LogFormatFlags.Default,
                                              LogConfigUIFlags uIFlags = LogConfigUIFlags.Default)
        {
            if (_registedLogger)
            {
                ModLogger.Warn($"已为{ModRegistry.GetTag(_assembly)}注册日志库，不能重复注册");
                return this;
            }

            _registedLogger = true;

            ModLogger.RegisterAssembly(_assembly, level, tagFlags, uIFlags);
            return this;
        }

        #region 注册Config相关的快捷方法
        /// <summary>
        /// 注册一个按钮，相关文本将自动调用本地化文件
        /// </summary>
        /// <param name="key">返回按钮的Key</param>
        /// <param name="description"> 按钮的描述文本 </param>
        /// <param name="action"> 按钮的行为 </param>
        /// <param name="buttonText"> 按钮上的文本 </param>
        /// <param name="group"> 按钮所在的组，留空为默认 </param>
        /// <param name="l10nAsm"> 本地化指定的程序集，留空则与主程序集相同 </param>
        public RegistryBuilder RegisterButton(out string key,
                                              string description, Action action, string buttonText = "按钮",
                                              string group = ConfigAttribute.DefaultGroup,
                                              Assembly? l10nAsm = null)
        {
            key = ConfigManager.RegisterButton(description, action, buttonText, group, _assembly, l10nAsm ?? Assembly.GetCallingAssembly());
            return this;
        }

        /// <summary>
        /// 仅注册配置项，不绑定任何UI，仅用于持久化
        /// </summary>
        /// <remarks> 可为实例对象，但应当自行维护生命周期，保证自注册至MOD卸载期间不被销毁。 </remarks>
        /// <typeparam name="T"> 注册的值的类型 </typeparam>
        /// <param name="key"> 返回配置项的Key，可以通过GetValue/SetValue函数查询系统内的值 </param>
        /// <param name="displayName"> 用作在持久化文本中的条目名称 </param>
        /// <param name="getter"> 维护的值的getter，在设置初始值以及卸载阶段调用一次以同步文件 </param>
        /// <param name="setter"> 维护的值的setter，用户若需要在代码中改值，最好使用GetValue(key)，但直接修改自己的值也会在最后保存文件时同步保存，只是不能同步UI </param>
        /// <param name="group"> 值所在的组，若不需要分组则留空 </param>
        /// <param name="l10nAsm"> 本地化指定的程序集，留空则与主程序集相同 </param>
        public RegistryBuilder RegisterConfig<T>(out string key, string displayName, Func<T> getter, Action<T> setter,
                                               string group = ConfigAttribute.DefaultGroup,
                                               Assembly? l10nAsm = null)
        {
            key = ConfigManager.RegisterConfig(displayName, getter, setter, group, _assembly);
            return this;
        }

        /// <summary>
        /// 通过getter/setter 注册一个配置项。
        /// </summary>
        /// <remarks> 可为实例对象，但应当自行维护生命周期，保证自注册至MOD卸载期间不被销毁。 </remarks>
        /// <typeparam name="T"> 注册的值的类型 </typeparam>
        /// <param name="key"> 返回配置项的Key，可以通过GetValue/SetValue函数查询系统内的值 </param>
        /// <param name="uiAttr"> 需要注册UI的Attribute，相关文本将自动调用本地化文件 </param>
        /// <param name="displayName"> 显示文本，用于保存以及显示在UI系统里 </param>
        /// <param name="getter"> 维护的值的getter，在设置初始值以及卸载阶段调用一次以同步文件 </param>
        /// <param name="setter"> 维护的值的setter，用户若需要在代码中改值，最好使用GetValue(key)，但直接修改自己的值也会在最后保存文件时同步保存，只是不能同步UI </param>
        /// <param name="group"> 值所在的组，若不需要分组则留空 </param>
        /// <param name="action"> 若需注册UI且需要额外的回调函数，则填入，若不需要则留空，此处禁止调用ConfigManager的SetVal </param>
        /// <param name="l10nAsm"> 本地化指定的程序集，留空则与主程序集相同 </param>
        public RegistryBuilder RegisterConfig<T>(out string key, UIConfigAttribute<T> uiAttr, string displayName, Func<T> getter,
                                               Action<T> setter, string group = ConfigAttribute.DefaultGroup,
                                               Action<T>? action = null, Assembly? l10nAsm = null)
        {
            key = ConfigManager.RegisterConfig(uiAttr, displayName, getter, setter, group, action, _assembly, l10nAsm ?? Assembly.GetCallingAssembly());
            return this;
        }

        /// <summary>
        /// 通过枚举值注册一个下拉列表，将自动从枚举值生成下拉列表。
        /// </summary>
        /// <typeparam name="TEnum"> 用于配置的枚举类型 </typeparam>
        /// <param name="key"> 返回配置项的Key，可以通过GetValue/SetValue函数查询系统内的值 </param>
        /// <param name="uiAttr"> 需要注册UI的Attribute，相关文本将自动调用本地化文件 </param>
        /// <param name="displayName"> 显示文本，用于保存以及显示在UI系统里 </param>
        /// <param name="getter"> 维护的值的getter，在设置初始值以及卸载阶段调用一次以同步文件 </param>
        /// <param name="setter"> 维护的值的setter，用户若需要在代码中改值，最好使用GetValue(key)，但直接修改自己的值也会在最后保存文件时同步保存，只是不能同步UI </param>
        /// <param name="group"> 值所在的组，若不需要分组则留空 </param>
        /// <param name="action"> 若需注册UI且需要额外的回调函数，则填入，若不需要则留空，此处禁止调用ConfigManager的SetVal </param>
        /// <param name="l10nAsm"> 本地化指定的程序集，留空则与主程序集相同 </param>
        public RegistryBuilder RegisterConfig<TEnum>(out string key, UIDropdownAttribute uiAttr, string displayName,
                                                     Func<TEnum> getter, Action<TEnum> setter,
                                                     string group = ConfigAttribute.DefaultGroup,
                                                     Action<TEnum>? action = null, Assembly? l10nAsm = null)
            where TEnum : Enum
        {
            key = ConfigManager.RegisterConfig(uiAttr, displayName, getter, setter, group, action, _assembly, l10nAsm ?? Assembly.GetCallingAssembly());
            return this;
        }

        /// <summary>
        /// 直接通过非空值注册一个配置项，由此MOD自行维护该值的生命周期，可通过 GetValue/SetValue 查询修改。
        /// </summary>
        /// <typeparam name="T"> 注册的配置项类型 </typeparam>
        /// <param name="key"> 返回配置项的Key，可以通过GetValue/SetValue函数查询系统内的值 </param>
        /// <param name="uiAttr"> 需要注册UI的Attribute，相关文本将自动调用本地化文件 </param>
        /// <param name="displayName"> 显示文本，用于保存以及显示在UI系统里 </param>
        /// <param name="defaultValue"> 默认值 </param>
        /// <param name="group"> 值所在的组，若不需要分组则留空 </param>
        /// <param name="action"> 若需注册UI且需要额外的回调函数，则填入，若不需要则留空，此处禁止调用ConfigManager的SetVal </param>
        /// <param name="l10nAsm"> 本地化指定的程序集，留空则与主程序集相同 </param>
        public RegistryBuilder RegisterConfig<T>(out string key, UIConfigAttribute<T> uiAttr, string displayName,
                                                 T defaultValue, string group = ConfigAttribute.DefaultGroup,
                                                 Action<T>? action = null, Assembly? l10nAsm = null)
        {
            key = ConfigManager.RegisterConfig(uiAttr, displayName, defaultValue, group, action, _assembly, l10nAsm ?? Assembly.GetCallingAssembly());
            return this;
        }

        /// <summary>
        /// 用一个非空枚举值生成下拉列表注册一个配置项，由此MOD自行维护该值的生命周期，可通过 GetValue/SetValue 查询修改。
        /// </summary>
        /// <typeparam name="TEnum"> 用于配置的枚举类型 </typeparam>
        /// <param name="key"> 返回配置项的Key，可以通过GetValue/SetValue函数查询系统内的值 </param>
        /// <param name="uiAttr"> 需要注册UI的Attribute，相关文本将自动调用本地化文件 </param>
        /// <param name="displayName"> 显示文本，用于保存以及显示在UI系统里 </param>
        /// <param name="defaultValue"> 默认枚举值 </param>
        /// <param name="group"> 值所在的组，若不需要分组则留空 </param>
        /// <param name="action"> 若需注册UI且需要额外的回调函数，则填入，若不需要则留空，此处禁止调用ConfigManager的SetVal </param>
        /// <param name="l10nAsm"> 本地化指定的程序集，留空则与主程序集相同 </param>
        public RegistryBuilder RegisterConfig<TEnum>(out string key, UIDropdownAttribute uiAttr, string displayName,
                                                     TEnum defaultValue, string group = ConfigAttribute.DefaultGroup,
                                                     Action<TEnum>? action = null, Assembly? l10nAsm = null)
                    where TEnum : Enum
        {
            key = ConfigManager.RegisterConfig(uiAttr, displayName, defaultValue, group, action, _assembly, l10nAsm ?? Assembly.GetCallingAssembly());
            return this;
        }

        /// <summary>
        /// 通过形如 `() => ClassName.StaticName / () => InstanceName.FieldName` 的表达式注册一个配置项，字段/属性/静态/实例均可。
        /// </summary>
        /// <remarks> 可为实例对象，但应当自行维护生命周期，保证自注册至MOD卸载期间不被销毁。 </remarks>
        /// <typeparam name="T"> 注册的配置项类型 </typeparam>
        /// <param name="key"> 返回配置项的Key，可以通过GetValue/SetValue函数查询系统内的值 </param>
        /// <param name="uiAttr"> 需要注册UI的Attribute，相关文本将自动调用本地化文件 </param>
        /// <param name="displayName"> 显示文本，用于保存以及显示在UI系统里 </param>
        /// <param name="expr"> 形如 `() => ClassName.StaticName / () => InstanceName.FieldName` 的表达式 </param>
        /// <param name="group"> 值所在的组，若不需要分组则留空 </param>
        /// <param name="action"> 若需注册UI且需要额外的回调函数，则填入，若不需要则留空，此处禁止调用ConfigManager的SetVal </param>
        /// <param name="l10nAsm"> 本地化指定的程序集，留空则与主程序集相同 </param>
        /// <exception cref="ArgumentException"> 传递的表达式不合法 </exception>
        public RegistryBuilder RegisterConfig<T>(out string key, UIConfigAttribute<T> uiAttr, string displayName,
                                                 Expression<Func<T>> expr, string group = ConfigAttribute.DefaultGroup,
                                                 Action<T>? action = null, Assembly? l10nAsm = null)
        {
            key = ConfigManager.RegisterConfig(uiAttr, displayName, expr, group, action, _assembly, l10nAsm ?? Assembly.GetCallingAssembly());
            return this;
        }

        /// <summary>
        /// 通过形如 `() => ClassName.StaticName / () => InstanceName.FieldName` 的表达式注册一个下拉列表，字段/属性/静态/实例均可。
        /// </summary>
        /// <typeparam name="TEnum"> 用于配置的枚举类型 </typeparam>
        /// <param name="key"> 返回配置项的Key，可以通过GetValue/SetValue函数查询系统内的值 </param>
        /// <param name="uiAttr"> 需要注册UI的Attribute，相关文本将自动调用本地化文件 </param>
        /// <param name="displayName"> 显示文本，用于保存以及显示在UI系统里 </param>
        /// <param name="expr"> 形如 `() => ClassName.StaticName / () => InstanceName.FieldName` 的表达式 </param>
        /// <param name="group"> 值所在的组，若不需要分组则留空 </param>
        /// <param name="action"> 若需注册UI且需要额外的回调函数，则填入，若不需要则留空，此处禁止调用ConfigManager的SetVal </param>
        /// <param name="l10nAsm"> 本地化指定的程序集，留空则与主程序集相同 </param>
        public RegistryBuilder RegisterConfig<TEnum>(out string key, UIDropdownAttribute uiAttr, string displayName,
                                                     Expression<Func<TEnum>> expr,
                                                     string group = ConfigAttribute.DefaultGroup,
                                                     Action<TEnum>? action = null, Assembly? l10nAsm = null)
               where TEnum : Enum
        {
            key = ConfigManager.RegisterConfig(uiAttr, displayName, expr, group, action, _assembly, l10nAsm ?? Assembly.GetCallingAssembly());
            return this;
        }

        #endregion

        private bool _done = false;
        /// <summary>
        /// 结束注册过程，触发注册完成事件（开始自动扫描配置、按默认值初始化未手动初始化的模块），返回void。
        /// </summary>
        public void Done()
        {
            if (_done)
            {
                ModLogger.Warn($"已为{ModRegistry.GetTag(_assembly)}调用Done，不能重复调用");
                return;
            }
            ModRegistry.Done(_assembly);
            _done = true;
        }
    }
}