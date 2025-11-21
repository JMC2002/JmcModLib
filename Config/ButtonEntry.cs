using JmcModLib.Reflection;
using System;
using System.Reflection;

namespace JmcModLib.Config
{
    public sealed class ButtonEntry : BaseEntry<MethodAccessor>
    {
        internal ButtonEntry(Assembly asm, Type declaringType, MethodAccessor accessor, string group)
                    : base(asm, group, declaringType, accessor) { }
    }
}