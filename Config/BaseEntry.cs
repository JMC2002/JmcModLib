using JmcModLib.Reflection;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace JmcModLib.Config
{
    public abstract class BaseEntry
    {
        internal Assembly assembly { get; }
        internal Type DeclaringType { get; }
        internal abstract string Key { get; }
        internal string Group { get; }

        /// <summary>
        /// 通过 DeclaringType 和 Name 生成唯一 Key（当前asm下唯一）
        /// </summary>
        /// <param name="declaringType">变量所在的类的类型</param>
        /// <param name="Name">变量的名称</param>
        /// <returns>返回一个形如{declaringType.FullName}.{Name}的唯一Key</returns>
        public static string GetKey(Type declaringType, string Name) =>
            $"{declaringType.FullName}.{Name}";


        protected BaseEntry(Assembly asm, string group, Type declaringType)
        {
            assembly = asm;
            Group = group;
            DeclaringType = declaringType;
        }
    }

    public abstract class BaseEntry<TAccessor> : BaseEntry
        where TAccessor : ReflectionAccessorBase
    {
        internal override string Key => GetKey(DeclaringType, Accessor.Name);
            
        internal TAccessor Accessor;
        protected BaseEntry(Assembly asm, string group, Type declaringType, TAccessor accessor)
            : base(asm, group, declaringType)
        {
            Accessor = accessor;
        }
    }

}
