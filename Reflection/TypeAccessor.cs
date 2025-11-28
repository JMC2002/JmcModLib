using JmcModLib.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace JmcModLib.Reflection
{
    /// <summary>
    /// 类型访问器 - 提供对 Type 本身及其成员的统一访问
    /// </summary>
    public class TypeAccessor : ReflectionAccessorBase<Type, TypeAccessor>
    {
        // =============================================
        //   核心属性
        // =============================================

        /// <summary>
        /// 被访问的类型（等同于 MemberInfo）
        /// </summary>
        public Type Type => MemberInfo;

        // IsStatic 对于 Type 的判断
        public TypeAccessor(Type type) : base(type)
        {
            IsStatic = type.IsAbstract && type.IsSealed; // 静态类的特征
        }

        // =============================================
        //   静态获取方法
        // =============================================

        /// <summary>
        /// 获取或创建 TypeAccessor（会缓存）
        /// </summary>
        public static TypeAccessor Get(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            return GetOrCreate(type, t => new TypeAccessor(t));
        }

        /// <summary>
        /// 泛型版本（会缓存）
        /// </summary>
        public static TypeAccessor Get<T>() => Get(typeof(T));

        public static IEnumerable<TypeAccessor> GetAll(Assembly asm)
        {
            foreach (var t in asm.GetTypes())
            {
                if (ReflectionAccessorBase.IsSaveOwner(t))
                    yield return new TypeAccessor(t);
            }
        }

        // =============================================
        //   实例创建
        // =============================================

        /// <summary>
        /// 创建实例（调用无参构造函数）
        /// </summary>
        public object? CreateInstance()
        {
            try
            {
                return Activator.CreateInstance(Type);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"创建实例失败: {Type.FullName}", ex);
                return null;
            }
        }

        /// <summary>
        /// 创建实例（带参数）
        /// </summary>
        public object? CreateInstance(params object?[] args)
        {
            try
            {
                return Activator.CreateInstance(Type, args);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"创建实例失败: {Type.FullName}", ex);
                return null;
            }
        }

        /// <summary>
        /// 泛型创建实例
        /// </summary>
        public T? CreateInstance<T>() where T : class
        {
            return CreateInstance() as T;
        }
    }
}