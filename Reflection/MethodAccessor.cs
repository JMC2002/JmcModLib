using JmcModLib.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace JmcModLib.Reflection
{
    /// <summary>
    /// 用于反射方法
    /// </summary>
    public sealed class MethodAccessor : ReflectionAccessorBase<MethodInfo, MethodAccessor>
    {
        /// <summary>
        /// 是否为静态
        /// </summary>
        public override bool IsStatic => Member.IsStatic;

        // 允许为 null（当这是一个泛型定义尚未闭包时）
        private readonly Func<object?, object?[], object?>? _invoker;

        private MethodAccessor(MethodInfo method, bool createInvoker = true)
            : base(method)
        {
            // 仅在 method 已经是 concrete（非 open generic）并且 caller 希望创建 invoker 时创建
            if (createInvoker && !method.IsGenericMethodDefinition)
                _invoker = CreateInvoker(method);
            else
                _invoker = null; // 延迟创建
        }

        /// <summary>
        /// 从 MethodInfo 获取 MethodAccessor 并缓存
        /// </summary>
        public static MethodAccessor Get(MethodInfo method)
        {
            // 缓存 key 应当是 method
            // 当 method.IsGenericMethodDefinition 为 true，构造器不会立即生成 invoker
            bool canInvoke =
                !method.IsGenericMethodDefinition;               // 延迟生成泛型方法
            return GetOrCreate(method, m => new MethodAccessor(m, createInvoker: canInvoke));
        }

        // ============================================================
        //   获取类型的所有方法（含私有 / 实例 / 静态）
        // ============================================================

        /// <summary>
        /// 获取某类型的所有方法（可选择包含继承方法）
        /// </summary>
        public static IEnumerable<MethodAccessor> GetAll(Type type, BindingFlags flags = DefaultFlags)
        {
            return type.GetMethods(flags)
                       .Select(Get);
        }

        /// <summary>
        /// 泛型版本
        /// </summary>
        public static IEnumerable<MethodAccessor> GetAll<T>(BindingFlags flags = DefaultFlags)
            => GetAll(typeof(T), flags);

        /// <summary>
        /// 获取类型下方法的 MethodAccessor（可匹配参数类型）
        /// </summary>
        /// <param name="type"> 类类型 </param>
        /// <param name="methodName"> 方法名 </param>
        /// <param name="parameterTypes"> 方法的参数列表类型，泛型位将跳过，不填则默认找第一个（在有多个重载的情况下） </param>
        /// <returns> 返回一个MethodAccessor，若是泛型方法，需要进一步Make，否则可以直接invoke </returns>
        /// <exception cref="MissingMethodException"></exception>
        public static MethodAccessor Get(Type type, string methodName, Type[]? parameterTypes = null)
        {
            var methods = GetAll(type)
                         .Select(ma => ma.Member)
                         .Where(m => m.Name == methodName);

            if (parameterTypes != null)
            {
                methods = methods.Where(m =>
                {
                    var ps = m.GetParameters();
                    if (parameterTypes.Length > ps.Length) return false;

                    for (int i = 0; i < ps.Length; i++)
                    {
                        var mp = ps[i].ParameterType;

                        // 如果方法参数是泛型占位符（T1、T2…），则接受任意类型匹配
                        if (mp.IsGenericParameter)
                            continue;

                        if (mp != parameterTypes[i])
                            return false;
                    }

                    // 多出的参数必须是 optional
                    for (int i = parameterTypes.Length; i < ps.Length; i++)
                    {
                        if (!ps[i].IsOptional)
                            return false;
                    }

                    return true;
                });
            }

            var method = methods.FirstOrDefault()
                ?? throw new MissingMethodException($"在 {type.FullName} 找不到方法 {methodName}");

            return Get(method);
        }

        /// <summary>
        /// 构造泛型方法实例
        /// </summary>
        public MethodAccessor MakeGeneric(params Type[] genericTypes)
        {
            if (!Member.IsGenericMethodDefinition)
                throw new InvalidOperationException("该方法不是泛型方法定义");

            var constructed = Member.MakeGenericMethod(genericTypes);
            return Get(constructed);
        }

        /// <summary>
        /// 调用方法（实例/静态）
        /// </summary>
        /// <param name="instance"> 实例对象，静态则留空 </param>
        /// <param name="args"> 调用的参数列表 </param>
        /// <returns> 返回方法的返回值 </returns>
        /// <exception cref="ArgumentNullException"> 实例方法需要实例对象 </exception>
        /// <exception cref="InvalidOperationException"> 泛型方法需要先MakeGeneric(...)  </exception>
        public object? Invoke(object? instance, params object?[] args)
        {
            if (!IsStatic && instance == null)
                throw new ArgumentNullException(nameof(instance), $"调用实例方法 {Name} 需要实例对象");

            if (_invoker == null)
                throw new InvalidOperationException($"方法 {Name} 是泛型方法定义，需要先调用 MakeGeneric(...) 生成具体方法再调用 Invoke。");

            // -------------------------
            // 补齐默认参数
            // -------------------------
            var ps = Member.GetParameters();

            // 如果用户传入的参数不足，则自动补齐默认值
            if (args.Length < ps.Length)
            {
                object?[] newArgs = new object?[ps.Length];

                // 复制用户传入的部分
                for (int i = 0; i < args.Length; i++)
                    newArgs[i] = args[i];

                // 补齐默认参数
                for (int i = args.Length; i < ps.Length; i++)
                {
                    var p = ps[i];
                    if (!p.IsOptional)
                        throw new TargetParameterCountException(
                            $"方法 {Name} 的参数 {p.Name} 没有默认值，但用户未提供");

                    // C# 默认参数的值在编译器层面写死到了 metadata 的 DefaultValue 里
                    newArgs[i] = p.DefaultValue;
                }

                args = newArgs;
            }
            else if (args.Length > ps.Length)
            {
                throw new TargetParameterCountException(
                    $"调用方法 {Name} 的参数过多：期望 {ps.Length}，实际 {args.Length}");
            }

            return _invoker(instance, args);
        }

        /// <summary>
        /// 创建方法调用委托
        /// </summary>
        private static Func<object?, object?[], object?> CreateInvoker(MethodInfo method)
        {
            var owner = method.DeclaringType;

            // 如果 owner 不可用，则退回到模块级别
            if (owner == null ||
                owner.IsInterface ||
                owner.IsArray ||
                owner.IsByRef ||
                owner.IsPointer ||
                owner.ContainsGenericParameters)
            {
                owner = null;
                ModLogger.Trace($"方法{method.Name}宿主不可用，回退到模块级别");
            }

            var parameters = method.GetParameters();
            var dm = owner != null ?
                new DynamicMethod($"invoke_{method.DeclaringType!.Name}_{method.Name}",
                                  typeof(object),
                                  [typeof(object), typeof(object?[])],
                                  method.DeclaringType, true) :
                new DynamicMethod($"invoke_{method.DeclaringType!.Name}_{method.Name}",
                                  typeof(object),
                                  [typeof(object), typeof(object?[])],
                                  method.Module, true);

            var il = dm.GetILGenerator();

            // 为 ref/out 参数分配局部变量
            LocalBuilder[] locals = new LocalBuilder[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].ParameterType.IsByRef)
                    locals[i] = il.DeclareLocal(parameters[i].ParameterType.GetElementType()!);
            }

            // 加载实例
            if (!method.IsStatic)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Castclass, method.DeclaringType);
            }

            // 加载参数
            for (int i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];
                var paramType = param.ParameterType;
                bool isByRef = paramType.IsByRef;
                Type elementType = isByRef ? paramType.GetElementType()! : paramType;

                if (isByRef)
                {
                    if (param.IsOut)
                    {
                        // ------ OUT 参数：创建默认值 ------
                        il.Emit(OpCodes.Ldloca_S, locals[i]);
                        il.Emit(OpCodes.Initobj, elementType);
                        il.Emit(OpCodes.Ldloca_S, locals[i]);
                    }
                    else
                    {
                        // ------ REF 参数：从 args 读取 ------
                        il.Emit(OpCodes.Ldarg_1);
                        il.Emit(OpCodes.Ldc_I4, i);
                        il.Emit(OpCodes.Ldelem_Ref);

                        EmitUnboxWithEnumSupport(il, elementType);

                        il.Emit(OpCodes.Stloc, locals[i]);
                        il.Emit(OpCodes.Ldloca_S, locals[i]);
                    }
                }
                else
                {
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Ldc_I4, i);
                    il.Emit(OpCodes.Ldelem_Ref);

                    EmitUnboxWithEnumSupport(il, elementType);
                }
            }

            // 调用方法
            il.EmitCall(method.IsVirtual && !method.IsFinal && !method.IsStatic ? OpCodes.Callvirt : OpCodes.Call, method, null);

            // 返回值处理
            if (method.ReturnType == typeof(void))
                il.Emit(OpCodes.Ldnull);
            else if (method.ReturnType.IsValueType)
                il.Emit(OpCodes.Box, method.ReturnType);

            // 写回 ref/out 参数
            for (int i = 0; i < parameters.Length; i++)
            {
                if (!parameters[i].ParameterType.IsByRef) continue;

                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldloc, locals[i]);
                if (locals[i].LocalType.IsValueType)
                    il.Emit(OpCodes.Box, locals[i].LocalType);
                il.Emit(OpCodes.Stelem_Ref);
            }

            il.Emit(OpCodes.Ret);
            return (Func<object?, object?[], object?>)dm.CreateDelegate(typeof(Func<object?, object?[], object?>));
        }

        /// <summary>
        /// 对普通值类型执行 Unbox_Any
        /// 对 enum 正确执行底层类型转换 + enum 转换
        /// 对引用类型执行 Castclass
        /// </summary>
        private static void EmitUnboxWithEnumSupport(ILGenerator il, Type type)
        {
            if (type.IsEnum)
            {
                Type underlying = Enum.GetUnderlyingType(type);

                // 反射传来的 object 先按 underlying unbox
                il.Emit(OpCodes.Unbox_Any, underlying);

                // underlying → enum
                // 大部分情况 underlying = Int32
                // IL 不允许直接 conv 到 enum 类型
                // 所以先 box → unbox enum
                il.Emit(OpCodes.Box, underlying);
                il.Emit(OpCodes.Unbox_Any, type);
                return;
            }

            if (type.IsValueType)
            {
                il.Emit(OpCodes.Unbox_Any, type);
            }
            else
            {
                il.Emit(OpCodes.Castclass, type);
            }
        }
    }
}