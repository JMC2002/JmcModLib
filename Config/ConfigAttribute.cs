using JmcModLib.Utils;
using System;
using System.Reflection;

namespace JmcModLib.Config
{
    /// <summary>
    /// 标记这个变量/字段是一个配置项
    /// </summary>
    /// <remarks>
    /// 标记这个变量/字段是一个配置项
    /// </remarks>
    /// <param name="displayName">显示名（用于放在UI上以及作为json中的key）</param>
    /// <param name="onChanged">配置变更时的额外回调方法名称，需要和字段/变量在同一个类中，接受参数为新赋的值，将会在实际修改值前调用（注：不需要写变更变量的操作）</param>
    /// <param name="group"> 配置所在的分组，默认为 DefaultGroup</param>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class ConfigAttribute(string displayName, string? onChanged = null,
                                        string group = ConfigAttribute.DefaultGroup) : Attribute
    {
        /// <summary>
        /// 显示名（用于放在UI上以及作为json中的key）
        /// </summary>
        public string DisplayName { get; } = displayName;

        /// <summary>
        /// 配置变更时的额外回调方法名称，需要和字段/变量在同一个类中，接受参数为新赋的值，将会在实际修改值前调用（注：不需要写变更变量的操作）
        /// </summary>
        public string? OnChanged { get; } = onChanged;

        /// <summary>
        /// 配置所在的分组，默认为 DefaultGroup
        /// </summary>
        public string Group { get; } = group;

        /// <summary>
        /// 默认分组保留字，值为 "DefaultGroup"
        /// </summary>
        public const string DefaultGroup = "DefaultGroup";

        /// <summary>
        /// 检查 MethodInfo 是否满足 回调 的要求（静态、void、单参）
        /// </summary>
        /// <param name="method">要检查的方法</param>
        /// <param name="type"></param>
        /// <param name="level">返回日志等级，若验证成功返回空，若返回值不匹配返回WARN</param>
        /// <param name="errorMessage">返回错误描述，验证成功返回空</param>
        /// <returns>如果警告等级在WARN以下返回 true，否则返回 false</returns>
        public static bool IsValidMethod(MethodInfo method, Type type, out LogLevel? level, out string? errorMessage)
        {
            level = null;
            errorMessage = null;

            // 必须是静态
            if (!method.IsStatic)
            {
                level = LogLevel.Error;
                errorMessage = "方法必须是静态方法";
                return false;
            }

            // 必须只有一个参数
            var parameters = method.GetParameters();
            if (parameters.Length != 1)
            {
                level = LogLevel.Error;
                errorMessage = $"方法只能有 1 个参数，实际有 {parameters.Length} 个参数";
                return false;
            }

            var p = parameters[0];

            // 参数类型必须匹配指定类型
            if (p.ParameterType != type)
            {
                level = LogLevel.Error;
                errorMessage =
                    $"方法的唯一参数类型必须是 {type.FullName}，实际是 {p.ParameterType.FullName}";
                return false;
            }

            // ② 返回值必须为 void
            if (method.ReturnType != typeof(void))
            {
                level = LogLevel.Warn;
                errorMessage =
                    $"方法返回类型必须是 void，实际为 {method.ReturnType.Name}，返回值将被丢弃";
            }

            return true;
        }

    }
}