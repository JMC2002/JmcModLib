using JmcModLib.Reflection;
using JmcModLib.Utils;
using System;
using System.Reflection;
using Unity.VisualScripting;
namespace JmcModLib.Config
{
    public sealed class ConfigEntry
    {
        public string Key => GetKey(DeclaringType, Accessor.Name);
        public string Group { get; }
        public Type DeclaringType { get; }
        public MemberAccessor Accessor { get; }
        public ConfigAttribute Attribute { get; }

        /// <summary>
        /// 字段/属性最初的默认值，用于 Reset。
        /// </summary>
        public object? DefaultValue { get; }

        /// <summary>
        /// 通过 DeclaringType 和 Name 生成唯一 Key（当前asm下唯一）
        /// </summary>
        /// <param name="declaringType">变量所在的类的类型</param>
        /// <param name="Name">变量的名称</param>
        /// <returns>返回一个形如{declaringType.FullName}.{Name}的唯一Key</returns>
        public static string GetKey(Type declaringType, string Name) =>
            $"{declaringType.FullName}.{Name}";

        public ConfigEntry(Type declaringType, MemberAccessor accessor, ConfigAttribute attr, object? defaultValue)
        {
            DeclaringType = declaringType;
            Accessor = accessor;
            Attribute = attr;
            Group = NormalizeGroup(attr?.Group);
            DefaultValue = defaultValue;
        }

        private static string NormalizeGroup(string? g) =>
            string.IsNullOrWhiteSpace(g) ? "__default" : g!;

        //public object? GetValue() => Accessor.GetValue(_instance);

        //public void SetValue(object? value)
        //{
        //    Accessor.SetValue(_instance, value);

        //    // 如果声明了 OnChanged 方法，就调它
        //    if (!string.IsNullOrEmpty(Attribute.OnChanged))
        //    {
        //        var m = MethodAccessor.GetValue(
        //            DeclaringType,
        //            Attribute.OnChanged,
        //            null
        //        );
        //        ModLogger.Debug("调用OnChanged");
        //        m.Invoke(_instance, value);
        //    }
        //    else
        //    {
        //        ModLogger.Debug("未注册OnChanged");
        //    }
        //}
    }
}
