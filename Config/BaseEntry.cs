using JmcModLib.Reflection;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace JmcModLib.Config
{
    public abstract class BaseEntry<TAccessor>
        where TAccessor : ReflectionAccessorBase
    {
        internal Assembly assembly { get; }
        internal Type DeclaringType { get; }
        internal virtual string Key => GetKey(DeclaringType, Accessor.Name);
        internal string Group { get; }
            
        internal TAccessor Accessor;

        /// <summary>
        /// 通过 DeclaringType 和 Name 生成唯一 Key（当前asm下唯一）
        /// </summary>
        /// <param name="declaringType">变量所在的类的类型</param>
        /// <param name="Name">变量的名称</param>
        /// <returns>返回一个形如{declaringType.FullName}.{Name}的唯一Key</returns>
        public static string GetKey(Type declaringType, string Name) =>
            $"{declaringType.FullName}.{Name}";


        protected BaseEntry(Assembly asm, string group, Type declaringType, TAccessor accessor)
        {
            assembly = asm;
            Group = group;
            DeclaringType = declaringType;
            Accessor = accessor;
        }
    }

}
