using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace JmcModLib.Reflection
{
    /// <summary>
    /// 用于反射方法
    /// </summary>
    public sealed class MethodAccessor : ReflectionAccessorBase<MethodInfo, MethodAccessor>
    {
        // ==============================
        //   快速查找缓存
        // ==============================

        // (Type → 该类型的方法按名称分组索引) 仅构建一次，避免每次调用遍历全部方法集合
        private static readonly ConcurrentDictionary<Type, Dictionary<string, List<MethodInfo>>> _typeMethodGroups = new();

        // (Type, Name) → 第一个方法（用于 parameterTypes == null 的快速路径，不创建 ParamSignature）
        private static readonly ConcurrentDictionary<(Type, string), MethodAccessor> _nameFirstAccessor = new();

        // (Type → (Name → (ParamSignature → MethodAccessor))) 二级索引，按需填充，不预生成全部前缀
        private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, ConcurrentDictionary<ParamSignature, MethodAccessor>>> _signatureIndex = new();

        /// <summary>
        /// 参数签名（用于缓存键）
        /// 说明：
        ///   1. null / 未提供参数列表用 Length = -1 表示（即 default ParamSignature）
        ///   2. 泛型参数占位符统一使用 RuntimeTypeHandle = default 记录，使不同的 TUI / T1 在同一方法定义上产生相同签名
        /// </summary>
        private readonly struct ParamSignature : IEquatable<ParamSignature>
        {
            public readonly int Length;
            private readonly int _hash;

            public ParamSignature(Type[]? types)
            {
                if (types == null)
                {
                    Length = -1; // 未指定参数类型
                    _hash = 0;
                    return;
                }

                Length = types.Length;
                unchecked
                {
                    int h = 17;
                    for (int i = 0; i < types.Length; i++)
                    {
                        // 泛型参数占位符统一 => 0
                        var t = types[i];
                        int v = t.IsGenericParameter ? 0 : t.TypeHandle.GetHashCode();
                        h = (h * 31) + v;
                    }
                    _hash = h;
                }
            }

            public bool Equals(ParamSignature other) => Length == other.Length && _hash == other._hash;
            public override bool Equals(object? obj) => obj is ParamSignature ps && Equals(ps);
            public override int GetHashCode() => HashCode.Combine(Length, _hash);
            public static ParamSignature From(Type[]? types) => new(types);
        }
        /// <summary>
        /// 是否为静态
        /// </summary>
        public override bool IsStatic => MemberInfo.IsStatic;

        // 允许为 null（当这是一个泛型定义尚未闭包时）
        private readonly Func<object?, object?[], object?>? _invoker;

        // 无ref/out/默认参数的快速路径，避免object[]装箱
        private readonly Func<object?, object?>? _fastInvoker0;
        private readonly Func<object?, object?, object?>? _fastInvoker1;
        private readonly Func<object?, object?, object?, object?>? _fastInvoker2;
        private readonly Func<object?, object?, object?, object?, object?>? _fastInvoker3;

        // ThreadStatic buffer for default parameter completion to avoid per-call allocation
        [ThreadStatic] private static object?[]? _defaultArgBuffer;

        // 强类型委托 (Func<...>/Action<...>) ，ref/out或者泛型方法不可用
        /// <summary>
        /// 若可用，返回强类型委托 (Func/Action)。实例方法第一个参数是声明类型实例；静态方法不含实例参数。
        /// 不支持 ref/out/泛型定义/包含可变参数的方法。
        /// </summary>
        public Delegate? TypedDelegate { get; }

        private MethodAccessor(MethodInfo method, bool createInvoker = true)
            : base(method)
        {
            // 仅在 method 已经是 concrete（非 open generic）并且 caller 希望创建 invoker 时创建
            if (createInvoker && !method.IsGenericMethodDefinition)
                _invoker = CreateInvoker(method);
            else
                _invoker = null; // 延迟创建

            // 构建 fastInvoker（仅限非泛型定义、无 ref/out、参数数<=3、无可选参数缺省逻辑）
            if (_invoker != null)
            {
                var ps = method.GetParameters();
                bool suitable = ps.Length <= 3 && ps.All(p => !p.ParameterType.IsByRef) && ps.All(p => !p.IsOptional);
                if (suitable)
                {
                    switch (ps.Length)
                    {
                        case 0: _fastInvoker0 = CreateFastInvoker0(method); break;
                        case 1: _fastInvoker1 = CreateFastInvoker1(method, ps[0]); break;
                        case 2: _fastInvoker2 = CreateFastInvoker2(method, ps[0], ps[1]); break;
                        case 3: _fastInvoker3 = CreateFastInvoker3(method, ps[0], ps[1], ps[2]); break;
                    }
                }

                // 构建强类型委托（支持更多参数，只要无 ref/out/可选参数）
                bool typedOk = !method.IsGenericMethodDefinition && ps.All(p => !p.ParameterType.IsByRef) && ps.All(p => !p.IsOptional);
                if (typedOk)
                {
                    try { TypedDelegate = CreateTypedDelegate(method); } catch { /* 忽略生成失败 */ }
                }
            }
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

        /// <summary>
        /// 获取强类型委托（若不存在抛异常）
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public Delegate GetTypedDelegate() => TypedDelegate ?? throw new InvalidOperationException($"方法 {Name} 没有可用的强类型委托");

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

        // ==============================
        // Strong typed delegate builder (Expression)
        // ==============================
        private static Delegate CreateTypedDelegate(MethodInfo method)
        {
            var ps = method.GetParameters();
            var paramExprs = new List<ParameterExpression>();
            ParameterExpression? instanceParam = null;
            if (!method.IsStatic)
            {
                instanceParam = System.Linq.Expressions.Expression.Parameter(method.DeclaringType!, "instance");
                paramExprs.Add(instanceParam);
            }
            foreach (var p in ps)
            {
                paramExprs.Add(System.Linq.Expressions.Expression.Parameter(p.ParameterType, p.Name ?? "p"));
            }

            System.Linq.Expressions.Expression call = method.IsStatic
                ? System.Linq.Expressions.Expression.Call(method, paramExprs.Skip(method.IsStatic ? 0 : 1))
                : System.Linq.Expressions.Expression.Call(instanceParam!, method, paramExprs.Skip(1));

            bool isVoid = method.ReturnType == typeof(void);
            Type delegateType = isVoid
                ? GetActionType(paramExprs.Select(e => e.Type).ToArray())
                : GetFuncType(paramExprs.Select(e => e.Type).Concat(new[] { method.ReturnType }).ToArray());

            var lambda = System.Linq.Expressions.Expression.Lambda(delegateType, call, paramExprs);
            return lambda.Compile();
        }

        private static Type GetActionType(Type[] types)
        {
            return types.Length switch
            {
                0 => typeof(Action),
                1 => typeof(Action<>).MakeGenericType(types),
                2 => typeof(Action<,>).MakeGenericType(types),
                3 => typeof(Action<,,>).MakeGenericType(types),
                4 => typeof(Action<,,,>).MakeGenericType(types),
                5 => typeof(Action<,,,,>).MakeGenericType(types),
                6 => typeof(Action<,,,,,>).MakeGenericType(types),
                7 => typeof(Action<,,,,,,>).MakeGenericType(types),
                8 => typeof(Action<,,,,,,,>).MakeGenericType(types),
                _ => throw new NotSupportedException("参数过多，无法生成 Action 委托")
            };
        }

        private static Type GetFuncType(Type[] types)
        {
            return types.Length switch
            {
                1 => typeof(Func<>).MakeGenericType(types),
                2 => typeof(Func<,>).MakeGenericType(types),
                3 => typeof(Func<,,>).MakeGenericType(types),
                4 => typeof(Func<,,,>).MakeGenericType(types),
                5 => typeof(Func<,,,,>).MakeGenericType(types),
                6 => typeof(Func<,,,,,>).MakeGenericType(types),
                7 => typeof(Func<,,,,,,>).MakeGenericType(types),
                8 => typeof(Func<,,,,,,,>).MakeGenericType(types),
                _ => throw new NotSupportedException("参数过多，无法生成 Func 委托")
            };
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
            // 1. parameterTypes == null 快速路径（只按名称）
            if (parameterTypes == null)
            {
                // 直接命中名称缓存
                if (_nameFirstAccessor.TryGetValue((type, methodName), out var firstAcc))
                    return firstAcc;
            }

            var sig = ParamSignature.From(parameterTypes);

            // 2. 签名索引命中（已构建）
            if (parameterTypes != null &&
                _signatureIndex.TryGetValue(type, out var nameMap) &&
                nameMap.TryGetValue(methodName, out var sigMap) &&
                sigMap.TryGetValue(sig, out var acc))
            {
                return acc;
            }

            // 3. 获取 / 构建类型方法按名称分组
            var groups = _typeMethodGroups.GetOrAdd(type, t =>
            {
                var dict = new Dictionary<string, List<MethodInfo>>(StringComparer.Ordinal);
                foreach (var m in t.GetMethods(DefaultFlags))
                {
                    if (!dict.TryGetValue(m.Name, out var list))
                        dict[m.Name] = list = new List<MethodInfo>(4);
                    list.Add(m);
                }
                return dict;
            });

            // 3. 找到名称组
            if (!groups.TryGetValue(methodName, out var overloads))
                throw new MissingMethodException($"在 {type.FullName} 找不到方法 {methodName}");

            // 4. 遍历重载进行匹配（通常数量较少）
            MethodInfo? matched = null;
            if (parameterTypes == null)
            {
                // 未指定参数，直接取第一个重载
                matched = overloads[0];
            }
            else
            {
                // 遍历重载进行匹配（通常数量较少）
                foreach (var m in overloads)
                {
                    var ps = m.GetParameters();
                    if (parameterTypes.Length > ps.Length) continue;

                    bool ok = true;
                    for (int i = 0; i < parameterTypes.Length; i++)
                    {
                        var mp = ps[i].ParameterType;
                        if (mp.IsGenericParameter) continue; // 泛型参数占位接受任意
                        if (mp != parameterTypes[i]) { ok = false; break; }
                    }
                    if (!ok) continue;

                    // 剩余参数必须是 optional
                    for (int i = parameterTypes.Length; i < ps.Length; i++)
                    {
                        if (!ps[i].IsOptional) { ok = false; break; }
                    }
                    if (!ok) continue;

                    matched = m;
                    break;
                }
            }

            if (matched == null)
                throw new MissingMethodException($"在 {type.FullName} 找不到方法 {methodName} 指定的参数签名");

            var accessor = Get(matched);

            // 5. 写入快速名称缓存（只在名称路径）
            if (parameterTypes == null)
            {
                _nameFirstAccessor.TryAdd((type, methodName), accessor);
            }
            else
            {
                // 写入签名索引
                var nameDict = _signatureIndex.GetOrAdd(type, _ => new ConcurrentDictionary<string, ConcurrentDictionary<ParamSignature, MethodAccessor>>(StringComparer.Ordinal));
                var sigDict = nameDict.GetOrAdd(methodName, _ => new ConcurrentDictionary<ParamSignature, MethodAccessor>());
                sigDict.TryAdd(sig, accessor);
            }

            return accessor;
        }

        /// <summary>
        /// 构造泛型方法实例
        /// </summary>
        public MethodAccessor MakeGeneric(params Type[] genericTypes)
        {
            if (!MemberInfo.IsGenericMethodDefinition)
                throw new InvalidOperationException("该方法不是泛型方法定义");

            var constructed = MemberInfo.MakeGenericMethod(genericTypes);
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
            var ps = MemberInfo.GetParameters();

            // 如果用户传入的参数不足，则自动补齐默认值
            if (args.Length < ps.Length)
            {
                // 复用 ThreadStatic buffer
                var buffer = _defaultArgBuffer;
                if (buffer == null || buffer.Length != ps.Length)
                {
                    buffer = new object?[ps.Length];
                    _defaultArgBuffer = buffer;
                }

                // 复制已传参数
                for (int i = 0; i < args.Length; i++) buffer[i] = args[i];

                // 补齐默认参数
                for (int i = args.Length; i < ps.Length; i++)
                {
                    var p = ps[i];
                    if (!p.IsOptional)
                        throw new TargetParameterCountException($"方法 {Name} 的参数 {p.Name} 没有默认值，但用户未提供");
                    buffer[i] = p.DefaultValue;
                }
                args = buffer;
            }
            else if (args.Length > ps.Length)
            {
                throw new TargetParameterCountException(
                    $"调用方法 {Name} 的参数过多：期望 {ps.Length}，实际 {args.Length}");
            }

            return _invoker(instance, args);
        }

        // ==============================
        // 快速调用以重载形式集成（0–3 个参数）
        // 当可用时会路由到预编译的快速委托，否则回退到使用 params object?[] 的路径。

        /// <summary>
        /// 无参特化
        /// </summary>
        public object? Invoke(object? instance)
        {
            if (_fastInvoker0 != null)
            {
                if (!IsStatic && instance == null)
                    throw new ArgumentNullException(nameof(instance), $"调用实例方法 {Name} 需要实例对象");
                return _fastInvoker0(instance);
            }
            return Invoke(instance, []);
        }

        /// <summary>
        /// 单参特化
        /// </summary>
        public object? Invoke(object? instance, object? a0)
        {
            if (_fastInvoker1 != null)
            {
                if (!IsStatic && instance == null)
                    throw new ArgumentNullException(nameof(instance), $"调用实例方法 {Name} 需要实例对象");
                return _fastInvoker1(instance, a0);
            }
            return Invoke(instance, [a0]);
        }

        /// <summary>
        /// 二参特化
        /// </summary>
        public object? Invoke(object? instance, object? a0, object? a1)
        {
            if (_fastInvoker2 != null)
            {
                if (!IsStatic && instance == null)
                    throw new ArgumentNullException(nameof(instance), $"调用实例方法 {Name} 需要实例对象");
                return _fastInvoker2(instance, a0, a1);
            }
            return Invoke(instance, [a0, a1]);
        }

        /// <summary>
        /// 三参特化
        /// </summary>
        public object? Invoke(object? instance, object? a0, object? a1, object? a2)
        {
            if (_fastInvoker3 != null)
            {
                if (!IsStatic && instance == null)
                    throw new ArgumentNullException(nameof(instance), $"调用实例方法 {Name} 需要实例对象");
                return _fastInvoker3(instance, a0, a1, a2);
            }
            return Invoke(instance, [a0, a1, a2]);
        }

        // =============================================
        // 泛型语法糖 (强类型委托优先)
        // =============================================
        /// <summary>
        /// 无参特化强类型版本
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TResult Invoke<TTarget, TResult>(TTarget instance)
        {
            if (!IsStatic && instance == null)
                throw new ArgumentNullException(nameof(instance));
            var td = TypedDelegate;
            if (td is Func<TTarget, TResult> f)
                return f(instance);
            // Fast path 0-param instance method
            if (_fastInvoker0 != null)
            {
                var r = _fastInvoker0(instance);
                return r is null ? default! : (TResult)r;
            }
            var ret = Invoke(instance);
            return ret is null ? default! : (TResult)ret;
        }

        /// <summary>
        /// 单参特化强类型版本
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TResult Invoke<TTarget, T1, TResult>(TTarget instance, T1 a1)
        {
            if (!IsStatic && instance == null)
                throw new ArgumentNullException(nameof(instance));
            var td = TypedDelegate;
            if (td is Func<TTarget, T1, TResult> f)
                return f(instance, a1);
            if (_fastInvoker1 != null)
            {
                var r = _fastInvoker1(instance, a1);
                return r is null ? default! : (TResult)r;
            }
            var ret = Invoke(instance, a1);
            return ret is null ? default! : (TResult)ret;
        }

        /// <summary>
        /// 二参特化强类型版本
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TResult Invoke<TTarget, T1, T2, TResult>(TTarget instance, T1 a1, T2 a2)
        {
            if (!IsStatic && instance == null)
                throw new ArgumentNullException(nameof(instance));
            var td = TypedDelegate;
            if (td is Func<TTarget, T1, T2, TResult> f)
                return f(instance, a1, a2);
            if (_fastInvoker2 != null)
            {
                var r = _fastInvoker2(instance, a1, a2);
                return r is null ? default! : (TResult)r;
            }
            var ret = Invoke(instance, a1, a2);
            return ret is null ? default! : (TResult)ret;
        }

        /// <summary>
        /// 三参特化强类型版本
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TResult Invoke<TTarget, T1, T2, T3, TResult>(TTarget instance, T1 a1, T2 a2, T3 a3)
        {
            if (!IsStatic && instance == null)
                throw new ArgumentNullException(nameof(instance));
            var td = TypedDelegate;
            if (td is Func<TTarget, T1, T2, T3, TResult> f)
                return f(instance, a1, a2, a3);
            if (_fastInvoker3 != null)
            {
                var r = _fastInvoker3(instance, a1, a2, a3);
                return r is null ? default! : (TResult)r;
            }
            var ret = Invoke(instance, a1, a2, a3);
            return ret is null ? default! : (TResult)ret;
        }

        /// <summary>
        /// 无返回值特化的强类型版本
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void InvokeVoid<TTarget>(TTarget instance)
        {
            if (!IsStatic && instance == null)
                throw new ArgumentNullException(nameof(instance));
            var td = TypedDelegate;
            if (td is Action<TTarget> a)
            {
                a(instance);
                return;
            }
            if (_fastInvoker0 != null)
            {
                _fastInvoker0(instance);
                return;
            }
            Invoke(instance);
        }

        /// <summary>
        /// 无返回值特化的强类型版本
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void InvokeVoid<TTarget, T1>(TTarget instance, T1 a1)
        {
            if (!IsStatic && instance == null)
                throw new ArgumentNullException(nameof(instance));
            var td = TypedDelegate;
            if (td is Action<TTarget, T1> a)
            {
                a(instance, a1);
                return;
            }
            if (_fastInvoker1 != null)
            {
                _fastInvoker1(instance, a1);
                return;
            }
            Invoke(instance, a1);
        }

        /// <summary>
        /// 无返回值特化的强类型版本
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void InvokeVoid<TTarget, T1, T2>(TTarget instance, T1 a1, T2 a2)
        {
            if (!IsStatic && instance == null)
                throw new ArgumentNullException(nameof(instance));
            var td = TypedDelegate;
            if (td is Action<TTarget, T1, T2> a)
            {
                a(instance, a1, a2);
                return;
            }
            if (_fastInvoker2 != null)
            {
                _fastInvoker2(instance, a1, a2);
                return;
            }
            Invoke(instance, a1, a2);
        }

        /// <summary>
        /// 无返回值特化的强类型版本
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void InvokeVoid<TTarget, T1, T2, T3>(TTarget instance, T1 a1, T2 a2, T3 a3)
        {
            if (!IsStatic && instance == null)
                throw new ArgumentNullException(nameof(instance));
            var td = TypedDelegate;
            if (td is Action<TTarget, T1, T2, T3> a)
            {
                a(instance, a1, a2, a3);
                return;
            }
            if (_fastInvoker3 != null)
            {
                _fastInvoker3(instance, a1, a2, a3);
                return;
            }
            Invoke(instance, a1, a2, a3);
        }

        /// <summary>
        /// 静态特化的强类型版本
        /// </summary>
        public TResult InvokeStatic<TResult>()
        {
            if (!IsStatic)
                throw new InvalidOperationException($"方法 {Name} 不是静态方法，不能使用 Invoke<TResult>()");
            if (TypedDelegate is Func<TResult> f)
                return f();
            return (TResult?)Invoke(null)!;
        }

        /// <summary>
        /// 静态特化的强类型版本
        /// </summary>
        public TResult InvokeStatic<T1, TResult>(T1 a1)
        {
            if (!IsStatic)
                throw new InvalidOperationException($"方法 {Name} 不是静态方法，不能使用 Invoke<T1,TResult>(...)");
            if (TypedDelegate is Func<T1, TResult> f)
                return f(a1);
            return (TResult?)Invoke(null, a1)!;
        }

        /// <summary>
        /// 静态特化的强类型版本
        /// </summary>
        public TResult InvokeStatic<T1, T2, TResult>(T1 a1, T2 a2)
        {
            if (!IsStatic)
                throw new InvalidOperationException($"方法 {Name} 不是静态方法，不能使用 Invoke<T1,T2,TResult>(...)");
            if (TypedDelegate is Func<T1, T2, TResult> f)
                return f(a1, a2);
            return (TResult?)Invoke(null, a1, a2)!;
        }

        /// <summary>
        /// 静态特化的强类型版本
        /// </summary>
        public TResult InvokeStatic<T1, T2, T3, TResult>(T1 a1, T2 a2, T3 a3)
        {
            if (!IsStatic)
                throw new InvalidOperationException($"方法 {Name} 不是静态方法，不能使用 Invoke<T1,T2,T3,TResult>(...)");
            if (TypedDelegate is Func<T1, T2, T3, TResult> f)
                return f(a1, a2, a3);
            return (TResult?)Invoke(null, a1, a2, a3)!;
        }

        /// <summary>
        /// 静态无返回值特化的强类型版本
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void InvokeStaticVoid()
        {
            if (!IsStatic)
                throw new InvalidOperationException($"方法 {Name} 不是静态方法，不能使用 InvokeStaticVoid()");
            var td = TypedDelegate;
            if (td is Action a)
            {
                a();
                return;
            }
            if (_fastInvoker0 != null)
            {
                _fastInvoker0(null);
                return;
            }
            Invoke(null);
        }

        /// <summary>
        /// 静态无返回值特化的强类型版本
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void InvokeStaticVoid<T1>(T1 a1)
        {
            if (!IsStatic)
                throw new InvalidOperationException($"方法 {Name} 不是静态方法，不能使用 InvokeStaticVoid<T1>(...)");
            var td = TypedDelegate;
            if (td is Action<T1> a)
            {
                a(a1);
                return;
            }
            if (_fastInvoker1 != null)
            {
                _fastInvoker1(null, a1);
                return;
            }
            Invoke(null, a1);
        }

        /// <summary>
        /// 静态无返回值特化的强类型版本
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void InvokeStaticVoid<T1, T2>(T1 a1, T2 a2)
        {
            if (!IsStatic)
                throw new InvalidOperationException($"方法 {Name} 不是静态方法，不能使用 InvokeStaticVoid<T1,T2>(...)");
            var td = TypedDelegate;
            if (td is Action<T1, T2> a)
            {
                a(a1, a2);
                return;
            }
            if (_fastInvoker2 != null)
            {
                _fastInvoker2(null, a1, a2);
                return;
            }
            Invoke(null, a1, a2);
        }

        /// <summary>
        /// 静态无返回值特化的强类型版本
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void InvokeStaticVoid<T1, T2, T3>(T1 a1, T2 a2, T3 a3)
        {
            if (!IsStatic)
                throw new InvalidOperationException($"方法 {Name} 不是静态方法，不能使用 InvokeStaticVoid<T1,T2,T3>(...)");
            var td = TypedDelegate;
            if (td is Action<T1, T2, T3> a)
            {
                a(a1, a2, a3);
                return;
            }
            if (_fastInvoker3 != null)
            {
                _fastInvoker3(null, a1, a2, a3);
                return;
            }
            Invoke(null, a1, a2, a3);
        }

        // ==============================
        // 创建快速 Invoker
        // ==============================
        private static Func<object?, object?> CreateFastInvoker0(MethodInfo method)
        {
            var dm = new DynamicMethod($"fast_invoke0_{method.DeclaringType!.Name}_{method.Name}", typeof(object), new[] { typeof(object) }, method.Module, true);
            var il = dm.GetILGenerator();

            if (!method.IsStatic)
            {
                il.Emit(OpCodes.Ldarg_0);
                if (method.DeclaringType!.IsValueType)
                    il.Emit(OpCodes.Unbox, method.DeclaringType!);
                else
                    il.Emit(OpCodes.Castclass, method.DeclaringType!);
            }

            if (method.DeclaringType!.IsValueType && !method.IsStatic)
            {
                il.Emit(OpCodes.Constrained, method.DeclaringType!);
                il.Emit(OpCodes.Callvirt, method);
            }
            else
            {
                il.EmitCall(method.IsVirtual && !method.IsFinal && !method.IsStatic ? OpCodes.Callvirt : OpCodes.Call, method, null);
            }

            if (method.ReturnType == typeof(void))
                il.Emit(OpCodes.Ldnull);
            else if (method.ReturnType.IsValueType)
                il.Emit(OpCodes.Box, method.ReturnType);

            il.Emit(OpCodes.Ret);
            return (Func<object?, object?>)dm.CreateDelegate(typeof(Func<object?, object?>));
        }

        private static Func<object?, object?, object?> CreateFastInvoker1(MethodInfo method, ParameterInfo p0)
        {
            var dm = new DynamicMethod($"fast_invoke1_{method.DeclaringType!.Name}_{method.Name}", typeof(object), new[] { typeof(object), typeof(object) }, method.Module, true);
            var il = dm.GetILGenerator();

            if (!method.IsStatic)
            {
                il.Emit(OpCodes.Ldarg_0);
                if (method.DeclaringType!.IsValueType)
                    il.Emit(OpCodes.Unbox, method.DeclaringType!);
                else
                    il.Emit(OpCodes.Castclass, method.DeclaringType!);
            }

            il.Emit(OpCodes.Ldarg_1);
            EmitParamCast(il, p0.ParameterType);

            EmitDirectCall(il, method);

            if (method.ReturnType == typeof(void)) il.Emit(OpCodes.Ldnull); else if (method.ReturnType.IsValueType) il.Emit(OpCodes.Box, method.ReturnType);
            il.Emit(OpCodes.Ret);
            return (Func<object?, object?, object?>)dm.CreateDelegate(typeof(Func<object?, object?, object?>));
        }

        private static Func<object?, object?, object?, object?> CreateFastInvoker2(MethodInfo method, ParameterInfo p0, ParameterInfo p1)
        {
            var dm = new DynamicMethod($"fast_invoke2_{method.DeclaringType!.Name}_{method.Name}", typeof(object), new[] { typeof(object), typeof(object), typeof(object) }, method.Module, true);
            var il = dm.GetILGenerator();
            if (!method.IsStatic)
            {
                il.Emit(OpCodes.Ldarg_0);
                if (method.DeclaringType!.IsValueType)
                    il.Emit(OpCodes.Unbox, method.DeclaringType!);
                else
                    il.Emit(OpCodes.Castclass, method.DeclaringType!);
            }
            il.Emit(OpCodes.Ldarg_1); EmitParamCast(il, p0.ParameterType);
            il.Emit(OpCodes.Ldarg_2); EmitParamCast(il, p1.ParameterType);
            EmitDirectCall(il, method);
            if (method.ReturnType == typeof(void)) il.Emit(OpCodes.Ldnull); else if (method.ReturnType.IsValueType) il.Emit(OpCodes.Box, method.ReturnType);
            il.Emit(OpCodes.Ret);
            return (Func<object?, object?, object?, object?>)dm.CreateDelegate(typeof(Func<object?, object?, object?, object?>));
        }

        private static Func<object?, object?, object?, object?, object?> CreateFastInvoker3(MethodInfo method, ParameterInfo p0, ParameterInfo p1, ParameterInfo p2)
        {
            var dm = new DynamicMethod($"fast_invoke3_{method.DeclaringType!.Name}_{method.Name}", typeof(object), new[] { typeof(object), typeof(object), typeof(object), typeof(object) }, method.Module, true);
            var il = dm.GetILGenerator();
            if (!method.IsStatic)
            {
                il.Emit(OpCodes.Ldarg_0);
                if (method.DeclaringType!.IsValueType)
                    il.Emit(OpCodes.Unbox, method.DeclaringType!);
                else
                    il.Emit(OpCodes.Castclass, method.DeclaringType!);
            }
            il.Emit(OpCodes.Ldarg_1); EmitParamCast(il, p0.ParameterType);
            il.Emit(OpCodes.Ldarg_2); EmitParamCast(il, p1.ParameterType);
            il.Emit(OpCodes.Ldarg_3); EmitParamCast(il, p2.ParameterType);
            EmitDirectCall(il, method);
            if (method.ReturnType == typeof(void)) il.Emit(OpCodes.Ldnull); else if (method.ReturnType.IsValueType) il.Emit(OpCodes.Box, method.ReturnType);
            il.Emit(OpCodes.Ret);
            return (Func<object?, object?, object?, object?, object?>)dm.CreateDelegate(typeof(Func<object?, object?, object?, object?, object?>));
        }

        private static void EmitParamCast(ILGenerator il, Type type)
        {
            if (type.IsEnum)
            {
                var underlying = Enum.GetUnderlyingType(type);
                il.Emit(OpCodes.Unbox_Any, underlying);
                il.Emit(OpCodes.Box, underlying);
                il.Emit(OpCodes.Unbox_Any, type);
            }
            else if (type.IsValueType)
            {
                il.Emit(OpCodes.Unbox_Any, type);
            }
            else
            {
                il.Emit(OpCodes.Castclass, type);
            }
        }

        private static void EmitDirectCall(ILGenerator il, MethodInfo method)
        {
            if (method.DeclaringType!.IsValueType && !method.IsStatic)
            {
                il.Emit(OpCodes.Constrained, method.DeclaringType!);
                il.Emit(OpCodes.Callvirt, method);
            }
            else
            {
                il.EmitCall(method.IsVirtual && !method.IsFinal && !method.IsStatic ? OpCodes.Callvirt : OpCodes.Call, method, null);
            }
        }

        /// <summary>
        /// 创建方法调用委托
        /// </summary>
        private static Func<object?, object?[], object?> CreateInvoker(MethodInfo method)
        {
            var parameters = method.GetParameters();
            var dm = IsSaveOwner(method.DeclaringType) ?
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

            bool isValueType = method.DeclaringType!.IsValueType;
            // 加载实例
            if (!method.IsStatic)
            {
                il.Emit(OpCodes.Ldarg_0);
                if (isValueType)
                {
                    // struct：必须 unbox 得到地址
                    il.Emit(OpCodes.Unbox, method.DeclaringType!);
                }
                else
                {
                    // class：正常 castclass
                    il.Emit(OpCodes.Castclass, method.DeclaringType!);
                }
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
            if (isValueType && !method.IsStatic)
            {
                // struct 实例方法必须用 constrained
                il.Emit(OpCodes.Constrained, method.DeclaringType!);
                il.Emit(OpCodes.Callvirt, method);
            }
            else
            {
                il.EmitCall(method.IsVirtual && !method.IsFinal && !method.IsStatic ? OpCodes.Callvirt : OpCodes.Call, method, null);
            }


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