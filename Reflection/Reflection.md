# JmcModLib.Reflection API 文档（MemberAccessor / MethodAccessor）

该文档总结了你的两个反射加速器的 API：MemberAccessor 与 MethodAccessor。
它们用于：
1. 提供高性能、缓存式的反射访问。
2. 提供可缓存的 Set/Get/Invoke 委托，提高反射调用性能。

# ==============================
# == MemberAccessor API 概述 ===
# ==============================
```
class MemberAccessor
{
    # 基本属性
    string Name;              // 成员名称
    Type MemberType;          // 成员的数据类型（字段类型/属性类型）
    bool IsStatic;            // 成员是否静态

    # 工厂函数
    static MemberAccessor Get(MemberInfo member);   // 获取单个 MemberAccessor
    static MemberAccessor? Get(Type type, string name);  // 按名称查找
    static IEnumerable<MemberAccessor> GetAll(Type type, MemberFlags flags = Default);
      // 返回某类型的所有字段+属性（由 flags 决定 Public/NonPublic/Static/Instance，默认为以上四个全选）
      // 返回的是 IEnumerable，零开销延迟序列，不会强制数组化
      public static IEnumerable<MemberAccessor> GetAll<T>(BindingFlags flags = DefaultFlags) // 泛型版本
    # 访问器组合
    object? GetValue(object? instance);             // 若为静态则需要传null
    void SetValue(object? instance, object? value); // 若为静态则需要传null

    # 诊断
    static int CacheCount;
}
```

===========================
= 用法示例（无语法高亮）=
===========================
```csharp
var acc = MemberAccessor.Get(typeof(Config), "ShowFPS");
acc.GetValue(configInstance);
acc.SetValue(configInstance, true);
var accField = MemberAccessor.Get(typeof(DemoClass), "StaticField");
accField.SetValue(null, 200);
var f = accField.GetValue(null);
foreach (var m in MemberAccessor.GetAll(type))
{
    
}
```

=============================
= MethodAccessor API 概述 =
=============================

```
class MethodAccessor
{
    # 基本属性
    string Name;            // 方法名
    MethodInfo Method;      // 原始 MethodInfo
    public Type DeclaringType => Method.DeclaringType!;
    public bool IsStatic;

    # 工厂函数
    static MethodAccessor Get(MethodInfo method);          // 获取单个方法
    // 根据类类型与方法名构造访问器，param代表形参类型，泛型参数可占位跳过，参数传null将默认返回第一个重载
    static MethodAccessor Get(Type type, string methodName, Type[]? parameterTypes = null); 
    
    // 构造一个泛型函数，泛型函数在Get后需要MakeGeneric传入模板参数方可invoke
    MethodAccessor MakeGeneric(params Type[] genericTypes)
    static IEnumerable<MethodAccessor> GetAll(Type type, MethodFlags flags = Default);
      // 获取类型内所有方法（由 flags 决定 Public/NonPublic/Static/Instance，默认全选）
    
    static IEnumerable<MethodAccessor> GetAll<T>(BindingFlags flags = DefaultFlags) // 泛型版本
    # 调用器
    Func<object?, object?[]?, object?> Invoker;
        // 用于高性能调用的方法委托
        // 若是 static，instance 可以为 null

    // instance代表实例对象，静态则填null
    object? Invoke(object? instance, params object?[] args)
    # 诊断
    static int CacheCount;
}
```
===========================
= 用法示例（无语法高亮）=
===========================

var method = MethodAccessor.Get(typeof(Player).GetMethod("Jump"));
method.Invoke(playerInstance, null);

foreach (var m in MethodAccessor.GetAll(typeof(Player)))
{
    Console.WriteLine(m.Name);
}

===========================
= Flags 说明（可选）=
===========================

MemberFlags / MethodFlags 支持以下组合：
- Instance
- Static
- Public
- NonPublic
- DeclaredOnly
- Default（Instance + Static + Public + NonPublic）

```cs
class AttributeAccessor
{
// 获取指定成员上的某种 attribute
 AttributeAccessor Get(MemberInfo member, Type attrType)
}
```