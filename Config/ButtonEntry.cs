using JmcModLib.Reflection;
using System;
using System.Reflection;

namespace JmcModLib.Config
{
    public sealed class ButtonEntry(
                            Assembly asm, 
                            Type declaringType, 
                            MethodAccessor accessor, 
                            string group)
        : BaseEntry<MethodAccessor>(asm, group, declaringType, accessor)
    { }
}