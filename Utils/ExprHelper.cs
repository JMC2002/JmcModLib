using JmcModLib.Utils;
using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Unity.Burst.Intrinsics;
using UnityEngine;
using static ExprHelper;
using static UnityEngine.Rendering.DebugUI;

// 实例成员缓存：Assembly -> target -> (MemberInfo -> Accessors)
using InsCache = System.Collections.Concurrent.ConcurrentDictionary<System.Reflection.Assembly, System.Runtime.CompilerServices.ConditionalWeakTable<object, System.Collections.Concurrent.ConcurrentDictionary<System.Reflection.MemberInfo, ExprHelper.MemberAccessors>>>;
using InsDict = System.Runtime.CompilerServices.ConditionalWeakTable<object, System.Collections.Concurrent.ConcurrentDictionary<System.Reflection.MemberInfo, ExprHelper.MemberAccessors>>;
using MemDict = System.Collections.Concurrent.ConcurrentDictionary<System.Reflection.MemberInfo, ExprHelper.MemberAccessors>;
using StaCache = System.Collections.Concurrent.ConcurrentDictionary<System.Reflection.Assembly, System.Collections.Concurrent.ConcurrentDictionary<System.Reflection.MemberInfo, ExprHelper.MemberAccessors>>;

public static class ExprHelper
{
    // 每个 Assembly 一份配置
    private class AssemblyConfig
    {
        public bool EnableCache { get; set; } = true;
        public MemberAccessMode Mode { get; set; } = MemberAccessMode.Default;
    }

    private static readonly ConcurrentDictionary<Assembly, AssemblyConfig> _configs = new();

    private static bool GetEnableCache(Assembly? asm = null)
    {
        return _configs.GetOrAdd(asm ?? Assembly.GetCallingAssembly(), _ => new AssemblyConfig()).EnableCache;
    }

    private static void SetEnableCache(bool value, Assembly? asm = null)
    {
        asm ??= Assembly.GetCallingAssembly();
        var cfg = _configs.GetOrAdd(asm, _ => new AssemblyConfig());

        if (cfg.EnableCache != value)
        {
            cfg.EnableCache = value;
            ClearAssemblyCache(asm);
            ModLogger.Debug($"[{asm.GetName().Name}] 缓存已{(value ? "开启" : "关闭")}");
        }
    }

    public static bool EnableCache
    {
        get => GetEnableCache(Assembly.GetCallingAssembly());
        set => SetEnableCache(value, Assembly.GetCallingAssembly());
    }

    private static MemberAccessMode GetAccessMode(Assembly? asm = null)
    {
        return _configs.GetOrAdd(asm ?? Assembly.GetCallingAssembly(), _ => new AssemblyConfig()).Mode;
    }

    private static void SetAccessMode(MemberAccessMode mode, Assembly? asm = null)
    {
        asm ??= Assembly.GetCallingAssembly();
        var cfg = _configs.GetOrAdd(asm, _ => new AssemblyConfig());

        if (cfg.Mode != mode)
        {
            cfg.Mode = mode;
            string modeText = mode switch
            {
                MemberAccessMode.Reflection => "反射",
                MemberAccessMode.ExpressionTree => "表达式树",
                MemberAccessMode.Emit => "Emit",
                _ => "未知"
            };
            ModLogger.Info($"[{asm.GetName().Name}] MemberAccessor 切换为 {modeText} 模式");
            ClearAssemblyCache(asm);
        }
    }

    public static MemberAccessMode AccessMode
    {
        get => GetAccessMode(Assembly.GetCallingAssembly());
        set => SetAccessMode(value, Assembly.GetCallingAssembly());
    }


    public enum MemberAccessMode
    {
        Reflection,
        ExpressionTree,
        Emit,
        Default = Emit,
    }

    static void Expect<T>(T? _) { }
    static void Check()
    {
        // 检查类型别名的嵌套关系是否正确，不正确将报错，静态检查，不运行
        Expect<ConditionalWeakTable<object, MemDict>>(default(InsDict));
        Expect<ConcurrentDictionary<Assembly, InsDict>>(default(InsCache));
        Expect<ConcurrentDictionary<Assembly, MemDict>>(default(StaCache));
    }

    private static readonly InsCache _insCache = new();
    private static readonly StaCache _staCache = new();

    public record MemberAccessors(Delegate Getter, Delegate Setter);

    public static (Func<T> getter, Action<T> setter) GetOrCreateAccessors<T>
        (Expression<Func<T>> expr, Assembly? assembly = null)
        => GetOrCreateAccessors(expr, out _,assembly ?? Assembly.GetCallingAssembly());

    /// <summary>
    /// 获取或创建 getter/setter
    /// </summary>
    public static (Func<T> getter, Action<T> setter) GetOrCreateAccessors<T>
        (Expression<Func<T>> expr, out bool cacheHit, Assembly? assembly = null)
    {
        if (expr.Body is not MemberExpression memberExpr)
            throw new ArgumentException("表达式必须是字段或属性，例如 () => Config.ShowFPS", nameof(expr));

        // ModLogger.Debug($"当前是否开启缓存: {EnableCache}");

        var member = memberExpr.Member;
        var targetExpr = memberExpr.Expression;
        var asm = assembly ?? Assembly.GetCallingAssembly();
        // object target = StaticKey;

        bool isStatic = member switch
        {
            FieldInfo f => f.IsStatic,
            PropertyInfo p => (p.GetGetMethod(true) ?? p.GetSetMethod(true))?.IsStatic ?? false,
            _ => throw new ArgumentException($"成员 {member.Name} 不是字段或属性")
        };

        object? target = null;
        if (!isStatic)
        {
            var targetGetter = Expression.Lambda<Func<object>>(Expression.Convert(targetExpr, typeof(object))).Compile();
            target = targetGetter() ?? throw new InvalidOperationException("实例对象不能为空");
        }


        if (!GetEnableCache(asm))
        {
            // 缓存关闭：直接创建新的访问器
            var accessors = CreateAccessors<T>(member, target, asm);
            cacheHit = false;
            return ((Func<T>)accessors.Getter, (Action<T>)accessors.Setter);
        }
        else
        {
            var memDict = target == null ? _staCache.GetOrAdd(asm, _ => new())
                                         : _insCache.GetOrAdd(asm, _ => new()).GetOrCreateValue(target);

            bool created = false;
            var accessors = memDict.GetOrAdd(member, _ =>
            {
                created = true;
                return CreateAccessors<T>(member, target, asm);
            });

            cacheHit = !created;
            return ((Func<T>)accessors.Getter, (Action<T>)accessors.Setter);
        }
    }

    private static MemberAccessors CreateAccessors<T>(MemberInfo member, object? target, Assembly assembly)
    {
        // ModLogger.Debug($"为{assembly.GetName().Name}选择模式，值为{GetAccessMode(assembly)}");
        // 根据 AccessMode 选择后端
        return GetAccessMode(assembly) switch
        {
            MemberAccessMode.Emit => CreateAccessorsByEmit<T>(member, target),
            MemberAccessMode.Reflection => CreateAccessorsByReflection<T>(member, target),
            MemberAccessMode.ExpressionTree => CreateAccessorsByExpressionTree<T>(member, target),
            _ => throw new InvalidOperationException("Unknown AccessMode")
        };
    }

    /// <summary>
    /// 创建 getter/setter
    /// </summary>
    private static MemberAccessors CreateAccessorsByExpressionTree<T>(MemberInfo member, object? target)
    {
        // ModLogger.Debug("CreateAccessorsByExpressionTree");
        var valueParam = Expression.Parameter(typeof(T), "value");

        switch (member)
        {
            case FieldInfo fi:
                {
                    var instanceExpr = target != null ? Expression.Constant(target) : null;
                    var getterExpr = Expression.Field(instanceExpr, fi);
                    var setterExpr = Expression.Assign(Expression.Field(instanceExpr, fi), valueParam);

                    var getter = Expression.Lambda<Func<T>>(Expression.Convert(getterExpr, typeof(T))).Compile();
                    var setter = Expression.Lambda<Action<T>>(setterExpr, valueParam).Compile();

                    return new MemberAccessors(getter, setter);
                }

            case PropertyInfo pi:
                {
                    var instanceExpr = target != null ? Expression.Constant(target) : null;

                    var getter = pi.CanRead && pi.GetMethod != null
                        ? Expression.Lambda<Func<T>>(Expression.Convert(Expression.Property(instanceExpr, pi), typeof(T))).Compile()
                        : new Func<T>(() => throw new InvalidOperationException($"属性 {pi.Name} 没有 getter"));

                    var setter = pi.CanWrite && pi.SetMethod != null
                        ? Expression.Lambda<Action<T>>(Expression.Assign(Expression.Property(instanceExpr, pi), valueParam), valueParam).Compile()
                        : new Action<T>(_ => throw new InvalidOperationException($"属性 {pi.Name} 没有 setter"));

                    return new MemberAccessors(getter, setter);
                }

            default:
                throw new ArgumentException($"成员 {member.Name} 不是字段或属性");
        }
    }

    /// <summary>
    /// 使用 DynamicMethod + IL Emit 创建 getter/setter
    /// </summary>
    private static MemberAccessors CreateAccessorsByEmit<T>(MemberInfo member, object? target)
    {
        // ModLogger.Debug("CreateAccessorsByEmit");
        switch (member)
        {
            case FieldInfo fi:
                {
                    Func<T> getter;
                    Action<T> setter;

                    if (fi.IsStatic)
                    {
                        // --- 静态字段 ---
                        var getterMethod = new DynamicMethod(
                            $"get_{fi.Name}_{Guid.NewGuid():N}",
                            typeof(T),
                            Type.EmptyTypes,
                            typeof(object).Module,
                            true);
                        var il = getterMethod.GetILGenerator();
                        il.Emit(OpCodes.Ldsfld, fi);
                        if (fi.FieldType != typeof(T))
                            il.Emit(OpCodes.Castclass, typeof(T));
                        il.Emit(OpCodes.Ret);
                        getter = (Func<T>)getterMethod.CreateDelegate(typeof(Func<T>));

                        var setterMethod = new DynamicMethod(
                            $"set_{fi.Name}_{Guid.NewGuid():N}",
                            typeof(void),
                            new[] { typeof(T) },
                            typeof(object).Module,
                            true);
                        var il2 = setterMethod.GetILGenerator();
                        il2.Emit(OpCodes.Ldarg_0);
                        if (fi.FieldType.IsValueType && typeof(T) != fi.FieldType)
                            il2.Emit(OpCodes.Unbox_Any, fi.FieldType);
                        else if (typeof(T) != fi.FieldType)
                            il2.Emit(OpCodes.Castclass, fi.FieldType);
                        il2.Emit(OpCodes.Stsfld, fi);
                        il2.Emit(OpCodes.Ret);
                        setter = (Action<T>)setterMethod.CreateDelegate(typeof(Action<T>));
                    }
                    else
                    {
                        if (target == null)
                            throw new ArgumentNullException(nameof(target), $"实例字段 {fi.Name} 的 target 不能为 null");

                        // --- 实例字段 ---
                        // getter: (object obj) => (T)((YourType)obj).Field
                        var getterMethod = new DynamicMethod(
                            $"get_{fi.Name}_{Guid.NewGuid():N}",
                            typeof(T),
                            new[] { typeof(object) },
                            typeof(object).Module,
                            true);
                        var il = getterMethod.GetILGenerator();
                        il.Emit(OpCodes.Ldarg_0);
                        if (fi.DeclaringType!.IsValueType)
                            il.Emit(OpCodes.Unbox, fi.DeclaringType);
                        else
                            il.Emit(OpCodes.Castclass, fi.DeclaringType);
                        il.Emit(OpCodes.Ldfld, fi);
                        if (fi.FieldType != typeof(T))
                            il.Emit(OpCodes.Castclass, typeof(T));
                        il.Emit(OpCodes.Ret);
                        var getterRaw = (Func<object, T>)getterMethod.CreateDelegate(typeof(Func<object, T>));
                        getter = () => getterRaw(target);

                        // setter: (object obj, T value) => ((YourType)obj).Field = value
                        var setterMethod = new DynamicMethod(
                            $"set_{fi.Name}_{Guid.NewGuid():N}",
                            typeof(void),
                            new[] { typeof(object), typeof(T) },
                            typeof(object).Module,
                            true);
                        var il2 = setterMethod.GetILGenerator();
                        il2.Emit(OpCodes.Ldarg_0);
                        if (fi.DeclaringType!.IsValueType)
                            il2.Emit(OpCodes.Unbox, fi.DeclaringType);
                        else
                            il2.Emit(OpCodes.Castclass, fi.DeclaringType);
                        il2.Emit(OpCodes.Ldarg_1);
                        if (fi.FieldType.IsValueType && typeof(T) != fi.FieldType)
                            il2.Emit(OpCodes.Unbox_Any, fi.FieldType);
                        else if (typeof(T) != fi.FieldType)
                            il2.Emit(OpCodes.Castclass, fi.FieldType);
                        il2.Emit(OpCodes.Stfld, fi);
                        il2.Emit(OpCodes.Ret);
                        var setterRaw = (Action<object, T>)setterMethod.CreateDelegate(typeof(Action<object, T>));
                        setter = v => setterRaw(target, v);
                    }

                    return new MemberAccessors(getter, setter);
                }

            case PropertyInfo pi:
                {
                    if (!pi.CanRead && !pi.CanWrite)
                        throw new ArgumentException($"属性 {pi.Name} 没有 getter/setter");

                    if (!pi.GetMethod!.IsStatic && target == null)
                        throw new ArgumentNullException(nameof(target), $"实例属性 {pi.Name} 的 target 不能为 null");

                    // --- Getter ---
                    Func<T> getter = pi.CanRead
                        ? (pi.GetMethod!.IsStatic
                            ? (Func<T>)Delegate.CreateDelegate(typeof(Func<T>), pi.GetMethod!)
                            : (Func<T>)Delegate.CreateDelegate(typeof(Func<T>), target!, pi.GetMethod!))
                        : (() => throw new InvalidOperationException($"属性 {pi.Name} 没有 getter"));

                    // --- Setter ---
                    Action<T> setter = pi.CanWrite
                        ? (pi.SetMethod!.IsStatic
                            ? (Action<T>)Delegate.CreateDelegate(typeof(Action<T>), pi.SetMethod!)
                            : (Action<T>)Delegate.CreateDelegate(typeof(Action<T>), target!, pi.SetMethod!))
                        : (_ => throw new InvalidOperationException($"属性 {pi.Name} 没有 setter"));

                    return new MemberAccessors(getter, setter);
                }


            default:
                throw new ArgumentException($"成员 {member.Name} 不是字段或属性");
        }
    }

    private static MemberAccessors CreateAccessorsByReflection<T>(MemberInfo member, object? target)
    {
        // ModLogger.Debug("CreateAccessorsByReflection");
        switch (member)
        {
            case FieldInfo fi:
                {
                    Func<T> getter = () => (T)fi.GetValue(target)!;
                    Action<T> setter = v => fi.SetValue(target, v);
                    return new MemberAccessors(getter, setter);
                }

            case PropertyInfo pi:
                {
                    Func<T> getter = pi.CanRead && pi.GetMethod != null
                        ? () => (T)pi.GetValue(target)!
                        : () => throw new InvalidOperationException($"属性 {pi.Name} 没有 getter");

                    Action<T> setter = pi.CanWrite && pi.SetMethod != null
                        ? v => pi.SetValue(target, v)
                        : _ => throw new InvalidOperationException($"属性 {pi.Name} 没有 setter");

                    return new MemberAccessors(getter, setter);
                }

            default:
                throw new ArgumentException($"成员 {member.Name} 不是字段或属性");
        }
    }

    /// <summary>
    /// 清理某个 Assembly 的缓存
    /// </summary>
    public static void ClearAssemblyCache(Assembly? assembly = null)
    {
        assembly ??= Assembly.GetCallingAssembly();
        _insCache.TryRemove(assembly, out _);
        _staCache.TryRemove(assembly, out _);
        ModLogger.Info($"[{assembly.GetName().Name}] 缓存已清空");
    }

    /// <summary>
    /// 清理所有缓存
    /// </summary>
    private static void ClearAll()
    {
        _insCache.Clear();
        _staCache.Clear();
        ModLogger.Info($"已清空所有缓存");
    }
}
