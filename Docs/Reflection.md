# JmcModLib 反射访问器使用指南

本文档介绍 `MemberAccessor` 与 `MethodAccessor` 的用法、最佳实践与注意事项（不包含泛型版本）。

## 1. 总览
- `MemberAccessor`：统一访问字段与属性（含静态/实例、私有、索引器）。
- `MethodAccessor`：调用方法（含静态/实例、重载、默认参数、泛型方法实例化）。
- 强类型委托：
  - `MemberAccessor.TypedGetter` / `MemberAccessor.TypedSetter`
  - `MethodAccessor.TypedDelegate`
  当可用时优先使用强类型委托以获得更好性能。

所有访问器均缓存构建结果，多次获取同一成员/方法会命中缓存，降低查找成本。

---

## 2. MemberAccessor 使用

### 2.1 获取访问器
```csharp
var accField = MemberAccessor.Get(typeof(MyType), "Field");
var accProp  = MemberAccessor.Get(typeof(MyType), "Prop");
```

- 支持私有成员，默认 `BindingFlags` 为：`Public | NonPublic | Instance | Static | FlattenHierarchy`。
- 获取失败抛出 `MissingMemberException`。

### 2.2 读写字段/属性
```csharp
// 实例成员
accField.SetValue(obj, 123);
var v1 = accField.GetValue(obj);

// 静态成员
accProp.SetValue(null, "hello");
var v2 = accProp.GetValue(null);
```
- 非静态成员 `target` 不能为空，否则抛出 `ArgumentNullException`。
- 只读/常量成员写入会抛出 `InvalidOperationException`。

### 2.3 索引器
```csharp
var idx1 = MemberAccessor.GetIndexer(typeof(MyList), typeof(int));
var value = idx1.GetValue(list, 1);
idx1.SetValue(list, "X", 1);

var idx2 = MemberAccessor.GetIndexer(typeof(MyList), typeof(int), typeof(int));
var value2 = idx2.GetValue(list, 3, 5);
```
- 使用 `GetIndexer(type, params Type[] parameterTypes)` 获取特定索引器重载。
- 调用时索引参数数量必须匹配，否则抛出 `ArgumentException`。

### 2.4 强类型委托与语法糖（推荐）
当成员不是索引器时，会自动生成强类型委托以避免装箱与运行时转换：

- 实例字段/属性
  - Getter: `Func<TTarget, TValue>`
  - Setter: `Action<TTarget, TValue>`
- 静态字段/属性
  - Getter: `Func<TValue>`
  - Setter: `Action<TValue>`

示例：
```csharp
var acc = MemberAccessor.Get(typeof(MyType), "Field");
var getter = (Func<MyType, int>)acc.TypedGetter!;
var setter = (Action<MyType, int>)acc.TypedSetter!;

int v = getter(obj);
setter(obj, 42);
```
> 只读/常量成员仅提供 Getter；索引器不提供强类型委托。

### 2.5 值类型注意事项
- 对装箱的 struct 调用 `SetValue` 修改的是“装箱副本”。
- 若要更新原 struct，请显式拆箱到局部变量后再赋回。

---

## 3. MethodAccessor 使用

### 3.1 获取访问器
```csharp
// 指定参数类型（推荐）
var add = MethodAccessor.Get(typeof(MyType), "Add", new[]{ typeof(int), typeof(int) });

// 仅按名称（存在多个重载时取第一个）
var getName = MethodAccessor.Get(typeof(MyType), "GetName");
```
- 指定参数类型时，允许尾随的形参是“可选参数”，调用时可缺省。
- 获取失败抛出 `MissingMethodException`。

### 3.2 调用
```csharp
// 快速重载（0~3 个实参，无 ref/out/可选参数补齐）
add.Invoke(obj, 1, 2);
getName.Invoke(obj);

// 通用路径（params object?[]）
add.Invoke(obj, new object?[]{ 1, 2 });
```
- 实例方法 `instance` 不能为空，否则抛出 `ArgumentNullException`。
- 传参不足（且参数无默认值）或过多时抛出 `TargetParameterCountException`。
- 当形参存在默认值时，通用路径会自动补齐缺省值。

### 3.3 强类型委托与语法糖（推荐）
满足以下条件将自动生成强类型委托：非泛型定义、无 `ref/out`、形参无可选参数。
- 实例方法：委托原型形如 `Func<TTarget, T1, T2, TResult>` 或 `Action<TTarget, ...>`
- 静态方法：委托原型形如 `Func<T1, T2, TResult>` 或 `Action<...>`

#### 有返回值方法
```csharp
// 强类型委托
var acc = MethodAccessor.Get(typeof(MyType), "Add", new[]{ typeof(int), typeof(int) });
var typed = (Func<MyType, int, int, int>)acc.TypedDelegate!;
int sum = typed(obj, 1, 2);

// 语法糖（实例方法）
int sum2 = acc.Invoke<MyType,int,int,int>(obj, 1, 2);

// 静态方法强类型委托
var sacc = MethodAccessor.Get(typeof(MyType), "StaticAdd", null);
var styped = (Func<int, int, int>)sacc.TypedDelegate!;
int ssum = styped(1, 2);

// 语法糖（静态方法）
int ssum2 = sacc.InvokeStatic<int,int,int>(1, 2);
```

#### void 返回值方法
对于 `void` 返回类型，生成 `Action` 委托，并提供专用的 `InvokeVoid` / `InvokeStaticVoid` 语法糖：
```csharp
// 实例 void 方法
var inc = MethodAccessor.Get(typeof(MyType), "Increment");
var incAction = (Action<MyType>)inc.TypedDelegate!;
incAction(obj);  // 或
inc.InvokeVoid<MyType>(obj);

// 静态 void 方法
var staticInc = MethodAccessor.Get(typeof(MyType), "StaticInc");
var staticIncAction = (Action)staticInc.TypedDelegate!;
staticIncAction();  // 或
staticInc.InvokeStaticVoid();

// 带参数的 void 方法
var addAssign = MethodAccessor.Get(typeof(MyType), "AddAssign", new[]{ typeof(int), typeof(int) });
var aaAction = (Action<int, int>)addAssign.TypedDelegate!;
aaAction(5, 10);  // 或
addAssign.InvokeStaticVoid<int, int>(5, 10);
```
> `InvokeVoid` 用于实例方法，`InvokeStaticVoid` 用于静态方法，支持 0~3 参数的快速路径。

### 3.4 ref/out 参数
- 需走通用路径：`Invoke(instance, object?[] args)`，调用后从 `args` 读取写回值。
```csharp
var acc = MethodAccessor.Get(typeof(MyType), "RefOutMethod", new[]{ typeof(int), typeof(int).MakeByRefType() });
var args = new object?[]{ 5, null };
acc.Invoke(obj, args);
int outVal = (int)args[1]!;
```

### 3.5 泛型方法
- 对于开放泛型定义，先 `MakeGeneric(...)` 再调用：
```csharp
var echo = MethodAccessor.Get(typeof(MyType), "Echo", new[]{ typeof(object) });
var echoString = echo.MakeGeneric(typeof(string));
var r = echoString.Invoke(obj, "hello");
```
- 未闭合的泛型方法直接 `Invoke` 会抛出 `InvalidOperationException`。

---

## 4. Attribute 访问
对任意访问器均可读取自定义特性：
```csharp
var m = MemberAccessor.Get(typeof(MyType), "Field");
var attr = m.GetAttribute<MyAttr>();
bool has = m.HasAttribute<MyAttr>();
var all = m.GetAllAttributes();

var method = MethodAccessor.Get(typeof(MyType), "DoWork");
var mattr = method.GetAttribute<MyAttr>();
```

---

## 5. 批量获取与缓存
- 批量扫描：
```csharp
var members = MemberAccessor.GetAll(typeof(MyType));
var methods = MethodAccessor.GetAll(typeof(MyType));
```
- 缓存统计：
```csharp
int mc = MemberAccessor.CacheCount;
int fc = MethodAccessor.CacheCount;
```
> 同一成员/方法多次获取返回同一访问器实例（引用相等）。

---

## 6. 常见异常
- `MissingMemberException` / `MissingMethodException`：找不到成员/方法。
- `ArgumentNullException`：实例成员/方法传入了 `null` 实例。
- `TargetParameterCountException`：参数数量不匹配（不足/过多）。
- `InvalidOperationException`：
  - 写入 `readonly/const` 字段。
  - 未闭合的泛型方法调用。
  - 索引器误用（未传索引参数）。

---

## 7. 性能建议
- **优先使用强类型委托**：`MemberAccessor.TypedGetter/TypedSetter`、`MethodAccessor.TypedDelegate`（零装箱、编译期类型安全）。
- **其次使用语法糖**：`Invoke<...>()`、`InvokeVoid<...>()`、`InvokeStatic<...>()`、`InvokeStaticVoid<...>()`。
  - 这些方法优先走强类型委托路径，次优走 0~3 参数快速路径（动态方法，无 `object[]` 分配）。
  - 若无可用委托则回退到通用 `Invoke(instance, object?[])` 路径。
- **仅在必要时使用通用路径**：`Invoke(instance, object?[])`
  - 需要处理 `ref/out` 参数时必须使用。
  - 需要动态补齐默认参数时使用。
  - 参数数量 > 3 时无法走快速路径。
- 复用已获取的访问器实例（内部已缓存，多次获取返回同一实例）。

### 性能对标
基于 1,000,000 次迭代的性能测试（详见 `Template/Test/ReflectionTestSuite.cs`）：
- **字段访问**：强类型 `Typed` 与直接访问仅差 **1~2%**。
- **方法调用（2 参数）**：经测试百万次双参数方法调用通用接口（93ms）较原始反射调用（581.37ms）耗时减少83.99%、强类型接口（2.13ms)耗时减少99.6%
- **缓存查找**：十万次`Get`查找中，平均每次查找 < **0.1 μs**（命中缓存）。

---

## 8. 限制与兼容性
- `MemberAccessor` 跳过不受支持的成员类型（如 `ref struct`/`Span<T>`/指针类型等）。
- 索引器不提供强类型委托。
- 值类型（struct）通过装箱对象调用 `SetValue` 修改的是“装箱副本”。

---

## 9. void 方法专用语法糖

为了优雅地处理 `void` 返回值的方法，提供了专用的 `InvokeVoid` 与 `InvokeStaticVoid` 语法糖，避免将 `void` 误解为 `TResult` 导致的类型错误。

### 实例 void 方法
```csharp
var inc = MethodAccessor.Get(typeof(MyType), "Increment");

// 无参
inc.InvokeVoid<MyType>(obj);

// 1 参数
var add = MethodAccessor.Get(typeof(MyType), "Add", new[]{ typeof(int) });
add.InvokeVoid<MyType, int>(obj, 5);

// 2 参数
var addTwo = MethodAccessor.Get(typeof(MyType), "AddTwo", new[]{ typeof(int), typeof(int) });
addTwo.InvokeVoid<MyType, int, int>(obj, 3, 7);

// 3 参数
var addThree = MethodAccessor.Get(typeof(MyType), "AddThree", new[]{ typeof(int), typeof(int), typeof(int) });
addThree.InvokeVoid<MyType, int, int, int>(obj, 1, 2, 3);
```

### 静态 void 方法
```csharp
var staticInc = MethodAccessor.Get(typeof(MyType), "StaticInc");

// 无参
staticInc.InvokeStaticVoid();

// 1 参数
var staticAdd = MethodAccessor.Get(typeof(MyType), "StaticAdd", new[]{ typeof(int) });
staticAdd.InvokeStaticVoid<int>(5);

// 2 参数
var staticAddTwo = MethodAccessor.Get(typeof(MyType), "StaticAddTwo", new[]{ typeof(int), typeof(int) });
staticAddTwo.InvokeStaticVoid<int, int>(3, 7);

// 3 参数
var staticAddThree = MethodAccessor.Get(typeof(MyType), "StaticAddThree", new[]{ typeof(int), typeof(int), typeof(int) });
staticAddThree.InvokeStaticVoid<int, int, int>(1, 2, 3);
```

> **与强类型委托等效**：这些方法实际上都会优先使用生成的 `Action<...>` 强类型委托，性能与直接调用委托相当。

---

## 10. 代码示例（汇总）
```csharp
// 字段
var f = MemberAccessor.Get(typeof(MyType), "Field");
f.SetValue(obj, 10);
var fv = f.GetValue(obj);

// 字段（强类型）
var fget = (Func<MyType,int>)f.TypedGetter!;
var fset = (Action<MyType,int>)f.TypedSetter!;
fset(obj, 10);
int fv2 = fget(obj);

// 属性
var p = MemberAccessor.Get(typeof(MyType), "Prop");
p.SetValue(obj, "abc");
var pv = p.GetValue(obj);

// 属性（强类型）
var pget = (Func<MyType,string>)p.TypedGetter!;
var pset = (Action<MyType,string>)p.TypedSetter!;
pset(obj, "abc");
string pv2 = pget(obj);

// 方法（有返回值）
var add = MethodAccessor.Get(typeof(MyType), "Add", new[]{ typeof(int), typeof(int) });
var r1 = add.Invoke(obj, 1, 2);

// 方法（有返回值，强类型）
var addTyped = (Func<MyType,int,int,int>)add.TypedDelegate!;
int r2 = addTyped(obj, 1, 2);

// 方法（有返回值，语法糖）
int r3Sugar = add.Invoke<MyType,int,int,int>(obj, 1, 2);

// 方法（void，实例）
var inc = MethodAccessor.Get(typeof(MyType), "Increment");
inc.InvokeVoid<MyType>(obj);
// 或使用强类型委托
var incAction = (Action<MyType>)inc.TypedDelegate!;
incAction(obj);

// 方法（void，静态，无参）
var staticInc = MethodAccessor.Get(typeof(MyType), "StaticInc");
staticInc.InvokeStaticVoid();
// 或使用强类型委托
var staticIncAction = (Action)staticInc.TypedDelegate!;
staticIncAction();

// 方法（void，静态，2 参数）
var addAssign = MethodAccessor.Get(typeof(MyType), "AddAssign", new[]{ typeof(int), typeof(int) });
addAssign.InvokeStaticVoid<int, int>(5, 10);
// 或使用强类型委托
var aaAction = (Action<int, int>)addAssign.TypedDelegate!;
aaAction(5, 10);

// 索引器
var idx = MemberAccessor.GetIndexer(typeof(MyList), typeof(int));
var iv = idx.GetValue(list, 1);
idx.SetValue(list, "x", 1);

// 泛型方法
var echo = MethodAccessor.Get(typeof(MyType), "Echo", new[]{ typeof(object) });
var echoStr = echo.MakeGeneric(typeof(string));
var r3 = echoStr.Invoke(obj, "hello");
```

---

如需更多示例，请参考项目 `Template/Test/ReflectionTestSuite.cs` 中的测试与性能对比用例。
