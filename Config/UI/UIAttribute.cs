using JmcModLib.Config.UI.ModSetting;
using JmcModLib.Utils;
using System;
using System.Reflection;
using UnityEngine;

namespace JmcModLib.Config.UI
{
    /// <summary>
    /// 整体标签基类
    /// </summary>
    public abstract class UIBaseAttribute : Attribute
    {
        internal abstract void BuildUI(BaseEntry entry);
    }

    /// <summary>
    /// 一个按钮属性
    /// </summary>
    /// <remarks>
    /// 只能绑定在静态无参无返回值构造上
    /// </remarks>
    /// <param name="description"></param>
    /// <param name="buttonText"></param>
    /// <param name="group"></param>
    public sealed class UIButtonAttribute(
                             string description,
                             string buttonText = "按钮",
                             string group = ConfigAttribute.DefaultGroup) : UIBaseAttribute
    {
        internal string Description { get; } = description;
        internal string ButtonText { get; } = buttonText;
        internal string Group { get; } = group;

        internal override void BuildUI(BaseEntry bEntry)
        {
            if (bEntry is not ButtonEntry entry)
                throw new ArgumentException("UIButtonAttribute 只适用于 ButtonEntry.");
            ModSettingBuilder.ButtonBuild(entry, this);
        }

        /// <summary>
        /// 检查 MethodInfo 是否满足 UIButtonAttribute 的要求（静态、void、无参）
        /// </summary>
        /// <param name="method">要检查的方法</param>
        /// <param name="level"></param>
        /// <returns>如果方法合法返回 true，否则返回 false</returns>
        /// <param name="errorMessage">如果验证失败，返回错误描述</param>
        public static bool IsValidMethod(MethodInfo method, out LogLevel? level, out string? errorMessage)
        {
            level = null;
            errorMessage = null;

            // 检查是否为静态方法
            if (!method.IsStatic)
            {
                level = LogLevel.Error;
                errorMessage = $"方法必须是静态方法";
                return false;
            }

            // 检查参数个数是否为 0
            var parameters = method.GetParameters();
            if (parameters.Length != 0)
            {
                level = LogLevel.Error;
                errorMessage = $"方法不能有参数，实际有 {parameters.Length} 个参数";
                return false;
            }

            // 检查返回类型是否为 void
            if (method.ReturnType != typeof(void))
            {
                level = LogLevel.Warn;
                errorMessage = $"方法返回类型实际为 {method.ReturnType.Name}，返回值将被丢弃";
            }

            return true;
        }
    }

    /// <summary>
    /// 需要维护数据的 ui 配置属性基类。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public abstract class UIConfigAttribute : UIBaseAttribute
    {
        internal abstract Type UIType { get; }
        internal virtual bool IsValid(ConfigEntry entry)
            => entry.UIType == UIType;
        internal override void BuildUI(BaseEntry bEntry)
        {
            if (bEntry is not ConfigEntry entry)
                throw new ArgumentException("UIConfigAttribute 只适用于 ConfigEntry.");
            BuildUI(entry);
        }

        internal abstract void BuildUI(ConfigEntry entry);
    }

    /// <summary>
    /// 用于标记具有某个类型约束的基类属性
    /// </summary>
    public abstract class UIConfigAttribute<T> : UIConfigAttribute
    {
        internal override Type UIType => typeof(T);
        internal override void BuildUI(ConfigEntry bEntry)
        {
            if (bEntry is not ConfigEntry<T> entry)
                throw new ArgumentException("UIConfigAttribute 类型绑定错误.");
            BuildUI(entry);
        }
        internal abstract void BuildUI(ConfigEntry<T> entry);
    }

    /// <summary>
    /// 具有某个类型的滑动条
    /// </summary>
    public abstract class UISliderAttribute<T>(
                            T min,
                            T max,
                            int characterLimit = 5) : UIConfigAttribute<T>
        where T : IComparable<T>
    {
        internal T Min { get; } = min;
        internal T Max { get; } = max;

        internal int CharacterLimit { get; } = characterLimit;
        internal override bool IsValid(ConfigEntry entry)
        {
            if (entry.UIType != UIType)
                return false;
            var v = (T)entry.GetValue()!;
            return v.CompareTo(Min) >= 0 && v.CompareTo(Max) <= 0;
        }
    }

    /// <summary>
    /// float 滑动条属性
    /// </summary>
    /// <remarks>
    /// float 滑动条属性
    /// </remarks>
    /// <param name="min">滑动下限</param>
    /// <param name="max">滑动上限</param>
    /// <param name="decimalPlaces">小数位数</param>
    /// <param name="characterLimit">输入字符限制</param>
    public sealed class UIFloatSliderAttribute(
                            float min, 
                            float max, 
                            int decimalPlaces = 1, 
                            int characterLimit = 5) : UISliderAttribute<float>(min, max, characterLimit)
    {

        internal int DecimalPlaces { get; } = decimalPlaces;

        internal override void BuildUI(ConfigEntry<float> entry)
        {
            ModSettingBuilder.FloatSliderBuild(entry, this);
        }
    }

    /// <summary>
    /// Int 滑动条属性
    /// </summary>
    /// <remarks>
    /// Int 滑动条属性
    /// </remarks>
    /// <param name="min">滑动下限</param>
    /// <param name="max">滑动上限</param>
    /// <param name="characterLimit">输入字符限制</param>
    public sealed class UIIntSliderAttribute(
                            int min, 
                            int max, 
                            int characterLimit = 5) : UISliderAttribute<int>(min, max, characterLimit)
    {
        internal override void BuildUI(ConfigEntry<int> entry)
        {
            ModSettingBuilder.IntSliderBuild(entry, this);
        }
    }

    /// <summary>
    /// 开关属性
    /// </summary>
    public sealed class UIToggleAttribute : UIConfigAttribute<bool>
    {
        internal override void BuildUI(ConfigEntry<bool> entry)
        {
            ModSettingBuilder.ToggleBuild(entry);
        }
    }
    /// <summary>
    /// 绑定按键属性
    /// </summary>
    public sealed class UIKeyBindAttribute : UIConfigAttribute<KeyCode>
    {
        internal override void BuildUI(ConfigEntry<KeyCode> entry)
        {
            ModSettingBuilder.KeyBindBuild(entry);
        }
    }

    /// <summary>
    /// 输入框属性
    /// </summary>
    /// <remarks>
    /// 初始化一个输入框属性
    /// </remarks>
    /// <param name="characterLimit">输入字符限制</param>
    public sealed class UIInputAttribute(int characterLimit = 5) : UIConfigAttribute<string>
    {
        internal int CharacterLimit { get; } = characterLimit;

        internal override void BuildUI(ConfigEntry<string> entry)
        {
            ModSettingBuilder.InputBuild(entry, this);
        }
    }


    public abstract class UINeedCovertAttribute : UIConfigAttribute
    {
    }

    public abstract class UIConverterAttribute<T> : UINeedCovertAttribute
    {
        internal override Type UIType => typeof(T);

        /// <summary>
        /// 将原始数据转换为UI层/存储层需要的数据
        /// </summary>
        public abstract T ToUI(object logicalValue);
        /// <summary>
        /// 从UI层/存储层转回原始数据
        /// </summary>
        public abstract object FromUI(T uiValue, Type logicalType);

        internal override void BuildUI(ConfigEntry bEntry)
        {
            if (bEntry is not ConfigEntry<T> entry)
                throw new ArgumentException("UIConverterAttribute 类型绑定错误.");
            BuildUI(entry);
        }
        internal abstract void BuildUI(ConfigEntry<T> entry);
    }

    /// <summary>
    /// 添加一个下拉框属性，仅支持枚举类型
    /// </summary>
    /// <param name="exclude">要排除的枚举选项，字符串表示，不检查是否存在此枚举项</param>
    public sealed class UIDropdownAttribute(string[]? exclude = null) : UIConverterAttribute<string>
    {
        internal string[]? Exclude { get; } = exclude;
        internal override bool IsValid(ConfigEntry entry)
            => entry.UIType == UIType && entry.LogicalType.IsEnum;

        public override string ToUI(object logicalValue)
                    => logicalValue?.ToString()!;
        /// <summary>
        /// 从UI层/存储层转回原始数据
        /// </summary>
        public override object FromUI(string uiValue, Type logicalType)
                    => uiValue != null ? Enum.Parse(logicalType, uiValue, true) : null!;
        internal override void BuildUI(ConfigEntry<string> entry)
        {
            if (entry.LogicalType.IsEnum)
            {
                ModSettingBuilder.DropdownBuild(entry, this);
            }
        }

        internal void BuildUITyped<TEnum>(ConfigEntry<TEnum> entry)
            where TEnum : Enum
        {
            ModSettingBuilder.DropdownBuild(entry);
        }
    }
}