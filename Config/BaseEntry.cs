using JmcModLib.Reflection;
using System;
using System.Reflection;

namespace JmcModLib.Config
{
    /// <summary>
    /// 所有配置条目的基类
    /// </summary>
    public abstract class BaseEntry(Assembly asm, string group, Type declaringType)
    {
        internal Assembly assembly { get; } = asm;
        internal Type DeclaringType { get; } = declaringType;
        internal abstract string Key { get; }
        internal string Group { get; } = group;

        /// <summary>
        /// 通过 DeclaringType 和 Name 生成唯一 Key（当前asm下唯一）
        /// </summary>
        /// <param name="declaringType">变量所在的类的类型</param>
        /// <param name="Name">变量的名称</param>
        /// <returns>返回一个形如{declaringType.FullName}.{Name}的唯一Key</returns>
        public static string GetKey(Type declaringType, string Name) =>
            $"{declaringType.FullName}.{Name}";
    }

    /// <summary>
    /// 配置条目的派生基类，包含一个访问器
    /// </summary>
    public abstract class BaseEntry<TAccessor>(
                                Assembly asm, 
                                string group, 
                                Type declaringType, 
                                TAccessor accessor) 
        : BaseEntry(asm, group, declaringType)
        where TAccessor : ReflectionAccessorBase
    {
        internal override string Key => GetKey(DeclaringType, Accessor.Name);

        internal TAccessor Accessor = accessor;
    }
}