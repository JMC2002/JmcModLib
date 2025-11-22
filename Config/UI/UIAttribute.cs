using JmcModLib.Config.UI.ModSetting;
using System;
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
    /// 
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
    }

    /// <summary>
    /// ui 配置属性基类。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public abstract class UIConfigAttribute : UIBaseAttribute
    {
        internal abstract bool IsValid(ConfigEntry entry);

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
        internal Type RequiredType => typeof(T);
        internal override bool IsValid(ConfigEntry entry)
            => entry.Accessor.MemberType == RequiredType;
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
            if (entry.Accessor.MemberType != RequiredType) return false;
            var v = (T)ConfigManager.GetValue(entry)!;
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

        internal override void BuildUI(ConfigEntry entry)
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
        internal override void BuildUI(ConfigEntry entry)
        {
            ModSettingBuilder.IntSliderBuild(entry, this);
        }
    }

    /// <summary>
    /// 开关属性
    /// </summary>
    public sealed class UIToggleAttribute : UIConfigAttribute<bool>
    {
        internal override void BuildUI(ConfigEntry entry)
        {
            ModSettingBuilder.ToggleBuild(entry);
        }
    }

    /// <summary>
    /// 添加一个下拉框属性，仅支持枚举类型
    /// </summary>
    public sealed class UIDropdownAttribute : UIConfigAttribute
    {
        internal override bool IsValid(ConfigEntry entry)
            => entry.Accessor.MemberType.IsEnum;

        internal override void BuildUI(ConfigEntry entry)
        {
            ModSettingBuilder.DropdownBuild(entry);
        }
    }

    /// <summary>
    /// 绑定按键属性
    /// </summary>
    public sealed class UIKeyBindAttribute : UIConfigAttribute<KeyCode>
    {
        internal override void BuildUI(ConfigEntry entry)
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

        internal override void BuildUI(ConfigEntry entry)
        {
            ModSettingBuilder.InputBuild(entry, this);
        }
    }
}