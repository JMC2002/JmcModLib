using JmcModLib.Reflection;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace JmcModLib.Core.AttributeRouter
{
    /// <summary>
    /// Handler 的统一接口。
    /// - Handle：当 Attribute 被发现时调用。
    /// - Unregister：可选，若实现则在 Assembly Unscan 时被调用，参数为 asm 与被处理过的 accessor 列表。
    /// </summary>
    public interface IAttributeHandler
    {
        void Handle(Assembly asm, ReflectionAccessorBase accessor, Attribute attribute);

        // 可选撤销/反注册动作
        Action<Assembly, IReadOnlyList<ReflectionAccessorBase>>? Unregister { get; }
    }
}
