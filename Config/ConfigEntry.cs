using System;
using System.Reflection;
using JmcModLib.Reflection;
using JmcModLib.Utils;
namespace JmcModLib.Config
{
    public sealed class ConfigEntry
    {
        public Type DeclaringType { get; }
        public MemberAccessor Accessor { get; }
        public ConfigAttribute Attribute { get; }

        // 对于 instance 字段，需要一个实例
        private readonly object? _instance;

        public ConfigEntry(Type declaringType, MemberAccessor accessor, ConfigAttribute attr)
        {
            DeclaringType = declaringType;
            Accessor = accessor;
            Attribute = attr;

            // static 字段/属性：instance = null
            // instance 字段：创建一个实例
            if (!Accessor.IsStatic)
            {
                _instance = Activator.CreateInstance(declaringType);
            }
            else
            {
                _instance = null;
            }
        }

        public object? GetValue() => Accessor.GetValue(_instance);

        public void SetValue(object? value)
        {
            Accessor.SetValue(_instance, value);

            // 如果声明了 OnChanged 方法，就调它
            if (!string.IsNullOrEmpty(Attribute.OnChanged))
            {
                var m = MethodAccessor.Get(
                    DeclaringType,
                    Attribute.OnChanged,
                    null
                );
                ModLogger.Debug("调用OnChanged");
                m.Invoke(_instance, value);
            }
            else
            {
                ModLogger.Debug("未注册OnChanged");
            }
        }
    }
}
