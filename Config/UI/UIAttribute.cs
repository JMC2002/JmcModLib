using System;
using UnityEngine;

namespace JmcModLib.Config.UI
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public abstract class UIConfigAttribute : Attribute
    {
        internal virtual Type RequiredType => typeof(object);
        internal virtual bool IsValid(ConfigEntry entry) => entry.Accessor.MemberType == RequiredType;
        internal abstract void BuildUI(ConfigEntry entry);
    }

    public sealed class UISliderAttribute : UIConfigAttribute
    {
        internal override Type RequiredType => typeof(float);

        internal float Min { get; }
        internal float Max { get; }
        internal int DecimalPlaces { get; }
        internal int CharacterLimit { get; }

        internal override bool IsValid(ConfigEntry entry)
        {
            if (entry.Accessor.MemberType != RequiredType) return false;
            var v = (float)ConfigManager.GetValue(entry)!;
            return v >= Min && v <= Max;
        }
            

        public UISliderAttribute(float min, float max, int decimalPlaces = 1, int characterLimit = 5)
        {
            Min = min;
            Max = max;
            DecimalPlaces = decimalPlaces;
            CharacterLimit = characterLimit;
        }
        internal override void BuildUI(ConfigEntry entry)
        {
            ModSetting.ModSettingBuilder.FloatSliderBuild(entry, this);
        }
    }

    public sealed class UIIntSliderAttribute : UIConfigAttribute
    {
        internal override Type RequiredType => typeof(int);

        internal int Min { get; }
        internal int Max { get; }
        internal int CharacterLimit { get; }
        internal override bool IsValid(ConfigEntry entry)
        {
            if (entry.Accessor.MemberType != RequiredType) return false;
            var v = (int)ConfigManager.GetValue(entry)!;
            return v >= Min && v <= Max;
        }

        public UIIntSliderAttribute(int min, int max, int characterLimit = 5)
        {
            Min = min;
            Max = max;
            CharacterLimit = characterLimit;
        }

        internal override void BuildUI(ConfigEntry entry)
        {
            ModSetting.ModSettingBuilder.IntSliderBuild(entry, this);
        }
    }


    public sealed class UIToggleAttribute : UIConfigAttribute
    {
        internal override Type RequiredType => typeof(bool);

        internal override void BuildUI(ConfigEntry entry)
        {
            ModSetting.ModSettingBuilder.ToggleBuild(entry);
        }
    }

    public sealed class UIDropdownAttribute : UIConfigAttribute
    {
        internal override bool IsValid(ConfigEntry entry)
            => entry.Accessor.MemberType.IsEnum;
        internal override void BuildUI(ConfigEntry entry)
        {
            ModSetting.ModSettingBuilder.DropdownBuild(entry);
        }
    }

    public sealed class UIKeyBindAttribute : UIConfigAttribute
    {
        internal override Type RequiredType => typeof(KeyCode);

        internal override void BuildUI(ConfigEntry entry)
        {
            ModSetting.ModSettingBuilder.DropdownBuild(entry);
        }
    }

    public sealed class UIInputAttribute : UIConfigAttribute
    {
        internal override Type RequiredType => typeof(string);

        internal int CharacterLimit { get; }

        public UIInputAttribute(int characterLimit = 5)
        {
            CharacterLimit = characterLimit;
        }

        internal override void BuildUI(ConfigEntry entry)
        {
            ModSetting.ModSettingBuilder.InputBuild(entry, this);
        }
    }

    //public sealed class UIInputAttribute : UIConfigAttribute
    //{
    //    public int CharacterLimit { get; }
    //    public UIInputAttribute(int characterLimit = 40)
    //    {
    //        CharacterLimit = characterLimit;
    //    }
    //}

    //public sealed class UIButtonAttribute : UIConfigAttribute
    //{
    //    public string ButtonText { get; }
    //    public UIButtonAttribute(string buttonText = "按钮")
    //    {
    //        ButtonText = buttonText;
    //    }
    //}
}
