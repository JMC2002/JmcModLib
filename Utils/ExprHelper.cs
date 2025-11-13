using JmcModLib.Utils;
using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using UnityEngine;
using static ExprHelper;
// 实例成员缓存：Assembly -> target -> (MemberInfo -> Accessors)
using InsCache = System.Collections.Concurrent.ConcurrentDictionary<System.Reflection.Assembly, System.Runtime.CompilerServices.ConditionalWeakTable<object, System.Collections.Concurrent.ConcurrentDictionary<System.Reflection.MemberInfo, ExprHelper.MemberAccessors>>>;
using InsDict = System.Runtime.CompilerServices.ConditionalWeakTable<object, System.Collections.Concurrent.ConcurrentDictionary<System.Reflection.MemberInfo, ExprHelper.MemberAccessors>>;
using MemDict = System.Collections.Concurrent.ConcurrentDictionary<System.Reflection.MemberInfo, ExprHelper.MemberAccessors>;
using StaCache = System.Collections.Concurrent.ConcurrentDictionary<System.Reflection.Assembly, System.Collections.Concurrent.ConcurrentDictionary<System.Reflection.MemberInfo, ExprHelper.MemberAccessors>>;

public static class ExprHelper
{
    private static bool _enableCache = true;
    /// <summary>
    /// 是否启用缓存。默认开启。
    /// 关闭后每次都会重新编译 Expression，不使用任何缓存。
    /// </summary>
    public static bool EnableCache
    {
        get { return _enableCache; }
        set
        {
            if (_enableCache != value)
            {
                _enableCache = value;
                ClearAll();
                ModLogger.Debug($"缓存已经{(value ? "开启" : "关闭")}");
            }
        }
    }


    public enum MemberAccessMode
    {
        Reflection,
        ExpressionTree,
        Emit,
        Default = Emit,
    }
    private static MemberAccessMode _mode = MemberAccessMode.Default;

    public static MemberAccessMode AccessMode
    {
        get => _mode;
        set
        {
            if (_mode != value)
            {
                _mode = value;

                string modeText = _mode switch
                {
                    MemberAccessMode.Reflection => "反射",
                    MemberAccessMode.ExpressionTree => "表达式树",
                    MemberAccessMode.Emit => "Emit",
                    _ => "未知"
                };

                ModLogger.Info($"MemberAccessor 切换为 {modeText} 模式");
                ClearAll(); // 清空缓存，防止跨模式混用
            }
        }
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
        => GetOrCreateAccessors(expr, out _,assembly);

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


        if (!EnableCache)
        {
            // 缓存关闭：直接创建新的访问器
            var accessors = CreateAccessors<T>(member, target);
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
                return CreateAccessors<T>(member, target);
            });

            cacheHit = !created;
            return ((Func<T>)accessors.Getter, (Action<T>)accessors.Setter);
        }
    }

    private static MemberAccessors CreateAccessors<T>(MemberInfo member, object? target)
    {
        // 根据 AccessMode 选择后端
        return AccessMode switch
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

                    var getter = pi.CanRead && pi.GetMethod != null
                        ? (Func<T>)(pi.GetMethod.IsStatic
                            ? Delegate.CreateDelegate(typeof(Func<T>), pi.GetMethod)
                            : new Func<T>(() => (T)pi.GetValue(target)!))
                        : new Func<T>(() => throw new InvalidOperationException($"属性 {pi.Name} 没有 getter"));

                    var setter = pi.CanWrite && pi.SetMethod != null
                        ? (Action<T>)(pi.SetMethod.IsStatic
                            ? Delegate.CreateDelegate(typeof(Action<T>), pi.SetMethod)
                            : new Action<T>(v => pi.SetValue(target, v)))
                        : new Action<T>(_ => throw new InvalidOperationException($"属性 {pi.Name} 没有 setter"));

                    return new MemberAccessors(getter, setter);
                }

            default:
                throw new ArgumentException($"成员 {member.Name} 不是字段或属性");
        }
    }

    private static MemberAccessors CreateAccessorsByReflection<T>(MemberInfo member, object? target)
    {
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
    public static void ClearAssemblyCache(Assembly? assembly)
    {
        _insCache.TryRemove(assembly ?? Assembly.GetCallingAssembly(), out _);
        _staCache.TryRemove(assembly ?? Assembly.GetCallingAssembly(), out _);
    }

    /// <summary>
    /// 清理所有缓存
    /// </summary>
    public static void ClearAll()
    {
        _insCache.Clear();
        _staCache.Clear();
    }
}
