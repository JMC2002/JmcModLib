using Duckov.Modding.UI;
using JmcModLib.Config.Entry;
using JmcModLib.Config.UI;
using JmcModLib.Config.UI.ModSetting;
using JmcModLib.Core;
using JmcModLib.Reflection;
using JmcModLib.Utils;
using System;
using System.Reflection;
using Unity.Burst.Intrinsics;
using Unity.VisualScripting.FullSerializer;
using static UnityEngine.Rendering.DebugUI;

namespace JmcModLib.Config
{
    /// <summary>
    /// 承载配置信息的类。
    /// </summary>
    public abstract class ConfigEntry(Assembly asm, string group, string displayName) 
        : BaseEntry(asm, group, displayName), IConfigAccessor
    {
        /// <summary>
        /// 数据的原类型，比如传入enum -> string中的enum，若不会额外转换，直接返回TargetType。
        /// </summary>
        public abstract Type LogicalType { get; }
        /// <summary>
        /// 存储的实际类型，比如将enum存为string，则返回string
        /// </summary>
        public abstract Type UIType { get; }
        public abstract object? GetValue();
        public abstract void SetValue(object? value);
        public abstract bool Reset();

        public abstract event Action<object?>? OnChanged;

        internal abstract void Sync();
    }


    public sealed class ConfigEntry<T> : ConfigEntry, IConfigAccessor<T>
    {
        /// <summary>
        /// 数据的原类型，比如传入enum -> string中的enum，若不会额外转换，直接返回TargetType。
        /// </summary>
        public override Type LogicalType { get; }
        /// <summary>
        /// 存储的实际类型，比如将enum存为string，则返回string
        /// </summary>
        public override Type UIType => typeof(T);
        /// <summary>
        /// 字段/属性最初的默认值，用于 Reset。
        /// </summary>
        internal T DefaultValue { get; }
        private readonly Func<T>? getter;
        private readonly Action<T>? setter;

        public ConfigEntry(Assembly asm,
                           string displayName,
                           string group,
                           T defaultValue, 
                           Func<T> getter, 
                           Action<T> setter, 
                           Action<T>? action,
                           Type logicType)
            : base(asm, group, displayName)
        {
            LogicalType = logicType;
            DefaultValue = defaultValue;
            this.getter = getter;
            this.setter = setter;
            OnChangedTyped += action;
        }

        public ConfigEntry(Assembly asm,
                           MemberAccessor member,
                           MethodAccessor? method,
                           ConfigAttribute attr,
                           Type logicalType)
            : base(asm, attr.Group, attr.DisplayName)
        {
            if (!member.IsStatic)
                throw new ArgumentException(
                    $"构造{member.Name}出错: 不允许使用MemberAccessor/MethodAccessor构造非静态Config");

            if (!member.CanRead || !member.CanWrite)
                throw new ArgumentException(
                    $"构造{member.Name}出错: ConfigEntry 需要可读写的成员");

            LogicalType = logicalType;

            if (method != null)
            {
                if (!ConfigAttribute.IsValidMethod(method.Member, UIType, out var lvl, out var error))
                    throw new ArgumentException($"构造{member.Name}出错: {error}");
                else
                {
                    ModLogger.Log(lvl, error);
                    if (method.TypedDelegate is Action<T> change)
                        OnChangedTyped += change;
                    else
                        OnChangedTyped += method.InvokeStaticVoid;
                }
            }

            if (member.TypedGetter is Func<T> func)
                getter = func;
            else
                getter = () => (T)member.GetValue(null)!;

            DefaultValue = getter();

            if (member.TypedSetter is Action<T> action)
                setter = action;
            else
                setter = value => member.SetValue(value);

            if (method != null)
                if (method.TypedDelegate is Action<T> change)
                    OnChangedTyped += change;
                else
                    OnChangedTyped += method.InvokeStaticVoid;
        }

        public override bool Reset()
        {
            var now = GetTypedValue();
            if (Equals(now, DefaultValue))
            {
                ModLogger.Trace($"{Key} 的旧值为{now}, 与默认值{DefaultValue}相等，跳过重置");
                return false;
            }
            ModLogger.Debug($"将{Key} 的旧值{now}重置为默认值{DefaultValue}");
            SetTypedValue(DefaultValue);
            return true;
        }

        /// <summary>
        /// 同步某一个配置项
        /// </summary>
        internal override void Sync()
        {
            var storage = ConfigManager.GetStorage(Assembly);
            // 获取已保存的值
            if (storage.TryLoad(DisplayName, Group, UIType, out var loaded, Assembly))
            {
                try
                {
                    setter!((T)loaded!);
                }
                catch(Exception ex)
                {
                    ModLogger.Warn(
                        $"{ModRegistry.GetTag(Assembly)}: 从文件中读取到了 {Key} 的值为 {loaded}，但是写入失败，尝试使用默认值覆盖", ex);
                    storage.Save(DisplayName, Group, getter!(), Assembly);
                }
            }
            else
            {
                ModLogger.Debug($"文件中不存在条目{Key}, 新建条目");
                // 如果没有保存的值，则保存当前值
                storage.Save(DisplayName, Group, getter!(), Assembly);
            }
        }

        private void Save(T val)
        {
            var storage = ConfigManager.GetStorage(Assembly);
            storage.Save(DisplayName, Group, val, Assembly);
        }

        // 强类型 API（避免与基类抽象方法同名冲突）
        public T GetTypedValue() => getter!();
        public void SetTypedValue(T value)
        {
            ModLogger.Debug($"将旧值设置为{value}");
            setter!(value);
            Save(value);
            OnChangedTyped?.Invoke(value);
            _onChanged?.Invoke(value);
            ModSettingLinker.SyncValue(this,  value);
        }

        // 显式实现泛型接口的方法
        T IConfigAccessor<T>.GetValue() => GetTypedValue();
        void IConfigAccessor<T>.SetValue(T value) => SetTypedValue(value);

        // 实现抽象基类的方法
        public override object? GetValue() => GetTypedValue();
        public override void SetValue(object? value) => SetTypedValue((T)value!);

        public event Action<T>? OnChangedTyped;
        private event Action<object?>? _onChanged;
        public override event Action<object?>? OnChanged
        {
            add => _onChanged += value;
            remove => _onChanged -= value;
        }
    }
}