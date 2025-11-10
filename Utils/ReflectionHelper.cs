using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace JmcModLib.Utils
{
    public static class ReflectionHelper
    {
        // 缓存区（线程安全）
        private static readonly ConcurrentDictionary<(Type, string), FieldInfo?> _fieldCache = new();
        private static readonly ConcurrentDictionary<(Type, string), MethodInfo?> _methodCache = new();

        // ================== 字段操作 ==================

        /// <summary>
        /// 获取私有字段值（自动缓存 FieldInfo）
        /// </summary>
        public static T GetFieldValue<T>(object obj, string fieldName)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj), $"GetFieldValue<{typeof(T).Name}> 失败：obj 为 null");
            }

            var type = obj.GetType();
            var field = _fieldCache.GetOrAdd((type, fieldName),
                key => key.Item1.GetField(key.Item2, BindingFlags.Instance | BindingFlags.NonPublic));

            if (field == null)
            {
                throw new MissingFieldException($"在 {type.Name} 中找不到字段 {fieldName}");
            }

            var value = field.GetValue(obj);
            if (value is T t)
            {
                return t;
            }
            else
            {
                throw new InvalidCastException($"字段 {fieldName} 的类型与预期的 {typeof(T).Name} 不匹配");
            }
        }

        /// <summary>
        /// 设置私有字段值（自动缓存 FieldInfo）
        /// </summary>
        public static void SetFieldValue<T>(object obj, string fieldName, T value)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj), $"SetFieldValue 失败：obj 为 null");
            }

            var type = obj.GetType();
            var field = _fieldCache.GetOrAdd((type, fieldName),
                key => key.Item1.GetField(key.Item2, BindingFlags.Instance | BindingFlags.NonPublic));

            if (field == null)
            {
                throw new MissingFieldException($"在 {type.Name} 中找不到字段 {fieldName}");
            }

            field.SetValue(obj, value);
        }

        // ================== 方法操作 ==================

        /// <summary>
        /// 调用私有实例方法（可返回值，自动缓存 MethodInfo）
        /// </summary>
        public static T CallMethod<T>(object obj, string methodName, params object[] args)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj), $"CallMethod<{typeof(T).Name}> 失败：obj 为 null");
            }

            var type = obj.GetType();
            var method = _methodCache.GetOrAdd((type, methodName),
                key => key.Item1.GetMethod(key.Item2, BindingFlags.Instance | BindingFlags.NonPublic));

            if (method == null)
            {
                throw new MissingFieldException($"在 {type.Name} 中找不到方法 {methodName}");
            }

            try
            {
                var result = method.Invoke(obj, args);
                if (result is T t)
                {
                    return t;
                }
                else
                {
                    throw new InvalidCastException($"方法 {methodName} 的返回值类型与预期的 {typeof(T).Name} 不匹配");
                }
            }
            catch (TargetInvocationException ex)
            {
                throw new InvalidOperationException($"调用方法 {methodName} 时发生错误", ex.InnerException);
            }
        }

        public static void CallVoidMethod(object obj, string methodName, params object[] args)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj), $"CallVoidMethod 失败：obj 为 null");
            }

            var type = obj.GetType();
            var method = _methodCache.GetOrAdd((type, methodName),
                key => key.Item1.GetMethod(key.Item2, BindingFlags.Instance | BindingFlags.NonPublic));

            if (method == null)
            {
                throw new MissingFieldException($"在 {type.Name} 中找不到方法 {methodName}");
            }

            try
            {
                method.Invoke(obj, args); // 直接调用，无需返回值
            }
            catch (TargetInvocationException ex)
            {
                throw new InvalidOperationException($"调用方法 {methodName} 时发生错误", ex.InnerException);
            }
        }


        /// <summary>
        /// 调用静态私有方法（可返回值，自动缓存 MethodInfo）
        /// </summary>
        public static T CallStaticMethod<T>(Type type, string methodName, params object[] args)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type), $"CallStaticMethod<{typeof(T).Name}>失败：type 为 null");
            }

            var method = _methodCache.GetOrAdd((type, methodName),
                key => key.Item1.GetMethod(key.Item2, BindingFlags.Static | BindingFlags.NonPublic));

            if (method == null)
            {
                throw new MissingMethodException($"在 {type.Name} 中找不到静态方法 {methodName}");
            }

            try
            {
                var result = method.Invoke(null, args);
                if (result is T t)
                {
                    return t;
                }
                else
                {
                    throw new InvalidCastException($"方法 {methodName} 的返回值类型与预期的 {typeof(T).Name} 不匹配");
                }
            }
            catch (TargetInvocationException ex)
            {
                throw new InvalidOperationException($"调用静态方法 {methodName} 时发生错误", ex.InnerException);
            }
        }

        public static bool CallStaticMethodWithOut<TOut>(
                                Type type, string methodName,
                                object?[] args,
                                out TOut? outValue)
        {
            outValue = default;

            if (type == null)
            {
                throw new ArgumentNullException(nameof(type), $"CallStaticMethodWithOut<{typeof(TOut).Name}> 失败：type 为 null");
            }

            var method = _methodCache.GetOrAdd((type, methodName),
                key => key.Item1.GetMethod(key.Item2, BindingFlags.Static | BindingFlags.NonPublic));

            if (method == null)
            {
                throw new MissingMethodException($"在 {type.Name} 中找不到静态方法 {methodName}");
            }

            try
            {
                var result = method.Invoke(null, args);

                // out 参数会被写回 args 数组中
                if (args.Length > 1 && args[1] is TOut t)
                    outValue = t;

                if (result is bool b)
                {
                    return b;
                }
                else
                {
                    throw new InvalidCastException($"方法 {methodName} 的返回值类型与预期的 bool 不匹配");
                }
            }
            catch (TargetInvocationException ex)
            {
                throw new InvalidOperationException($"调用静态方法 {methodName} 时发生错误", ex.InnerException);
            }
        }

    }


}
