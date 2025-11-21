using JmcModLib.Reflection;
using System;
using System.Reflection;

namespace JmcModLib.Config
{
    /// <summary>
    /// Button 类型的配置项条目
    /// </summary>
    /// <param name="asm"></param>
    /// <param name="declaringType"></param>
    /// <param name="accessor"></param>
    /// <param name="group"></param>
    public sealed class ButtonEntry(
                            Assembly asm, 
                            Type declaringType, 
                            MethodAccessor accessor, 
                            string group)
        : BaseEntry<MethodAccessor>(asm, group, declaringType, accessor)
    { }
}