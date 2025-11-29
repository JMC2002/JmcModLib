# JmcModLib ConfigManager 使用与集成指南

本文档介绍 `ConfigManager` 的职责、API、生命周期、典型用法与注意事项，帮助 MOD 在 JmcModLib 中高效注册、持久化与展示配置项。

命名空间：`JmcModLib.Config`（ConfigAttribute在这里）、`JmcModLib.Config.UI`（对应 UI 相关 Attribute）

## 1. 角色与职责
- 统一管理配置项注册、缓存与持久化（按 Assembly 分隔）。
- 自动在 `ModRegistry` 完成注册时扫描 `[Config]` 成员并生成 UI/存储条目。
- 提供直接注册按钮、配置项的便捷 API（支持值、getter/setter、表达式、枚举下拉）。
- 支持每个 Assembly 自定义存储实现（默认 `NewtonsoftConfigStorage`）。
- `Attribute`主要分为两类，`ConfigAttribute`标记这是一个需要维护的“数据”，负责提供数据的元信息供持久化；`UIAttribute`则用于表示这需要生成UI，负责提供UI的元信息供UI生成器使用。在`UIAttribute`中又分为`UIButton`和`UIConfig`，前者表示这是一个按钮，不涉及数据维护，后者则是一类需要维护数据的UI控件，比如滑动条、输入框、下拉框等，需要配合`ConfigAttribute`才能正常使用。
- 若你注册了本地化文本，UI的相关文本将会自动使用本地化文本（见 `L10n` 文档）。

## 2. 生命周期与事件
- 初始化：`ConfigManager.Init()`（由库内部调用）
  - 订阅 `ModRegistry.OnRegistered` 自动扫描注册；订阅 `OnUnRegistered` 自动卸载。
- 卸载：`ConfigManager.Dispose()`
  - 取消订阅事件，清理所有缓存并保存必要数据。
- 事件：
  - `OnRegistered(Assembly)`：某 Assembly 完成配置扫描与注册后触发。
  - `OnValueChanged(ConfigEntry entry, object? value)`：配置项值变更时触发。

## 3. 存储后端
- 默认实现：`NewtonsoftConfigStorage`，路径在 `Application.persistentDataPath/Saves/JmcModLibConfig`（即存档路径下）。
- 自定义存储可通过实现`IConfigStorage`后用以下方式注册：
```csharp
ConfigManager.SetStorage(new MyStorageImpl(), asm);
```
- 运行时按 Assembly 选择存储，通过 `GetStorage(asm)` 获取。

## 4. 自动扫描注册（Attribute 优先，推荐）
在 `ModRegistry.Done(asm)` 后，ConfigManager 会优先按 Attribute 自动注册，无需手动代码：
- 扫描 Assembly 所有类型：
  - 方法标注 `UIButtonAttribute` → 自动注册按钮到配置 UI；
  - 字段/属性标注 `ConfigAttribute` → 自动创建配置项；
  - 若同一成员同时标注 `UIConfigAttribute<T>`（如 `UIFloatSliderAttribute`、`UIIntSliderAttribute`、`UIToggleAttribute`、`UIInputAttribute`、`UIDropdownAttribute`），则自动生成对应 UI。
- 成功后触发 `ConfigManager.OnRegistered(asm)`。

更推荐将“静态成员”标注 Attribute：生命周期简单、无需维护实例引用，自动生成 UI 与持久化。

示例：
```csharp
    public class TestConfig
    {
        [UIIntSlider(1, 10)]
        [Config("测试整数")]
        public static int Number = 7;

        [UIToggle]
        [Config("开关", onChanged: "Test")]
        public static bool Enabled = true;  // 带回调
        public static void Test(bool newV)
        {
            ModLogger.Info($"Enabled 改变为 {newV}");
        }

        [Config("用户名")]
        public static string User = "Alice";    // 无 UI，仅持久化
    }
```

## 5. 直接注册 API（补充途径）
支持以多种形式注册配置项与按钮：

## 5.1 Attribute 速查与示例（推荐优先使用）

### ConfigAttribute 
#### 构造函数
```csharp
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ConfigAttribute(string displayName, string? onChanged = null,
                                    string group = ConfigAttribute.DefaultGroup) : Attribute
```
#### 参数说明
- `displayName`：配置项显示名称，用于 UI 的显示以及用作持久化`json`中的键值。
- `onChanged`：配置项值变更后的回调方法名（需为一个接受配置项类型的无返回值的静态方法，且需要与配置项处于同一个类），留空则没有。
- `group`：配置项分组名称，留空为默认。

#### 使用示例
```csharp
public static class GameplayConfig
{
    // 最简单：只持久化 + 自动生成默认 UI(若再加 UIAttribute)
    [Config("移动速度", group:"角色")]
    [UIFloatSlider(0f, 30f, decimalPlaces:1)]
    private static float MoveSpeed = 6.5f;

    // bool + 开关
    [Config("是否启用冲刺", group:"角色")]
    [UIToggle]
    private static bool EnableSprint = true;

    // 枚举 + 下拉框（自动列出枚举值）
    public enum QualityPreset { Low, Medium, High }
    [Config("画质预设", group:"图形")]
    [UIDropdown]
    private static QualityPreset Quality = QualityPreset.High;

    // 字符串 + 输入框限制
    [Config("玩家名", group:"用户")]
    [UIInput(characterLimit:16)]
    private static string PlayerName = "Hero";
}
```

### 使用 OnChanged 回调
```csharp
public static class DifficultyConfig
{
    [Config("难度等级", onChanged:nameof(OnDifficultyChanged), group:"游戏")]
    [UIDropdown]
    private static DifficultyLevel Level = DifficultyLevel.Normal;

    // 回调签名要求：static + 单参数(类型需与字段一致) + void/任意返回值(非 void 会被忽略)
    private static void OnDifficultyChanged(DifficultyLevel newLevel)
    {
        ModLogger.Info($"切换难度为 {newLevel}");
    }
}
```

### UIButtonAttribute 示例
```csharp
public static class DebugTools
{
    [UIButton("重置存档", "重置", group:"调试")]
    private static void ResetSave() { /* ... */ }

    [UIButton("导出当前配置", "导出", group:"调试")]
    private static void DumpConfig() { /* ... */ }
}
```

### Int / Float 滑动条
```csharp
public static class AudioSettings
{
    [UIFloatSlider(0f, 1f, decimalPlaces:2)]
    [Config("音乐音量", group:"音频")]
    private static float MusicVolume = 0.75f;

    [UIIntSlider(1, 32)]
    [Config("最大连接数", group:"网络")]
    private static int MaxConnections = 8;
}
```

### KeyCode 绑定
```csharp
public static class InputConfig
{
    [UIKeyBind]
    [Config("快速打开菜单", group:"快捷键")]
    private static UnityEngine.KeyCode OpenMenu = UnityEngine.KeyCode.F1;
}
```

### 枚举下拉排除部分选项
```csharp
public enum RoleKind { None, Warrior, Mage, Archer }
public static class RoleConfig
{
    [Config("角色类型", group:"角色")]
    [UIDropdown(exclude: new[]{ nameof(RoleKind.None) })]
    private static RoleKind SelectedRole = RoleKind.Warrior;
}
```

此外，还可以通过继承相关基类实现自定义 UI 逻辑，对于原始类型不需要转换的，可继承 `UIConfigAttribute<T>`，对于需要转换的，可继承 `UIConverterAttribute<T>`，然后重载相关函数即可，注册时，`UIManager`会调用`BuildUI`构建，示例（不保证能跑，仅供参考）：
### Converter（自定义逻辑类型 → UI 类型）
```csharp
// 假设内部用 int 表示百分比 (0~100)，希望 UI 用 float 0~1 表示
public sealed class PercentSliderAttribute : UIConverterAttribute<float>
{
    public override float ToUI(object logicalValue) => (int)logicalValue / 100f;
    public override object FromUI(float uiValue, Type logicalType) => (int)(uiValue * 100);
    public override void BuildUI(ConfigEntry<float> entry) => ModSettingBuilder.FloatSliderBuild(entry, new UIFloatSliderAttribute(0f, 1f, 2));
}

public static class InternalConfig
{
    [Config("掉落倍率", group:"平衡")]
    [PercentSlider]
    private static int DropRatePercent = 65; // UI 显示 0.65
}
```

### Attribute 使用注意事项
- 只能在静态成员上使用
- `ConfigAttribute.OnChanged` 方法若签名不符合（非 static / 参数不匹配），会被警告或报错并跳过回调，如需注册实例对象/绑定实例函数，则需要手动调用API（见下文）。
- UI 与逻辑类型不匹配时，`IsValid` 验证失败会输出错误日志但不生成 UI。
- 下拉枚举大小写解析支持 `Enum.Parse(logicalType, value, true)`。


### 5.1 注册按钮
```csharp
string key = ConfigManager.RegisterButton(
    description: "执行操作",
    action: () => Do(),
    buttonText: "执行",
    group: "功能",
    asm: myAsm);
```

### 5.2 仅持久化（不生成 UI）
```csharp
string key = ConfigManager.RegisterConfig(
    displayName: "速度",
    getter: () => Speed,
    setter: v => Speed = v,
    group: "参数",
    asm: myAsm);
```

### 5.3 getter/setter + UI
```csharp
var ui = new UISliderAttribute(min:0, max:10);
string key = ConfigManager.RegisterConfig(ui, "速度", () => Speed, v => Speed = v,
                                          group:"参数", action: v => OnSpeedChanged(v), asm: myAsm);
```

### 5.4 枚举下拉（getter/setter）
```csharp
ConfigManager.RegisterConfig(new UIDropdownAttribute(),
                             "最低打印等级",
                             () => { return ModLogger.GetLogLevel(asm); },
                             lvl => { ModLogger.SetLogLevel(lvl, asm); },
                             DefaultGroup,
                             asm: asm);
```

### 5.5 直接用默认值（托管生命周期）
```csharp
var ui = new UISliderAttribute(0, 100);
string key = ConfigManager.RegisterConfig(ui, "音量", 50, group:"音频", action: v => OnVolume(v), asm: myAsm);
```

### 5.6 枚举默认值（托管生命周期）
```csharp
ConfigManager.RegisterConfig(new UIDropdownAttribute(),
                             "最低打印等级",
                             ModLogger.GetLogLevel(asm),
                             DefaultGroup,
                             lvl => { ModLogger.SetLogLevel(lvl, asm); },
                             asm);
```

### 5.7 表达式注册（字段/属性/静态/实例）
```csharp
string key = ConfigManager.RegisterConfig(ui, "生命值",
    expr: () => Player.Instance.HP,
    group:"角色", action: v => Log(v), asm: myAsm);
```
- 内部将自动解析表达式萃取出强类型 getter/setter，类型安全无反射开销。

## 6. 值变更与持久化
- 通过 UI 改值或调用 `ConfigManager.SetValue(key, value, asm)` 会：
  - 更新配置项当前值；
  - 立即写入存储后端（`Save(displayName, group, value)`），但会进入缓存，等到此MOD或JmcModLib卸载时才会真实写入本地；
  - 触发 `OnValueChanged`。
- 反注册时：
  - 遍历所有条目 `SyncFromData()` 写回文件；
  - 调用存储后端的 `Flush(asm)` 刷盘。
- 错误处理：
  - 当读取存储失败或读取的值不合法时，使用默认值并写入存储，并输出到`Warn`日志。

## 7. 查询辅助（ConfigManager下）
- `TryGetGroupForKey(key, asm)`：获取某 key 所在组名。
- `GetGroups(asm)` / `GetKeys(group, asm)`：枚举所有组与 key。
- `GetValue(key, asm)` / `SetValue(key, asm)`：获取当前值。
- `GetKey(displayName, group, asm)`：获取某显示名+组名对应的 key。

## 8. 注意事项
- Assembly 隔离：每个 MOD 的配置相互独立缓存与存储。
- 线程安全：内部使用 `ConcurrentDictionary` 管理条目；自定义存储需保证线程安全。
- 表达式注册失败会抛出 `ArgumentException`，请确保表达式形如 `() => Obj.Member`。
- 每个变量会通过`group`与`displayName`构造作为唯一标识的`key`，因此请确保同一组内不存在相同的`displayName`，同时这个值可以通过`ConfigManager.GetKey`获取。
- 若变量的生命周期由你持有（即除使用值注册与自动扫描外的所有注册方式），请确保变量在 MOD 生命周期内有效，避免访问已被 GC 的对象。
- 若变量由你直接可见（即除使用值注册外的所有注册方式），在配置项更新时，系统将自动调用传入或反射得到的`setter`修改你本地的值，不需要你在`OnChange`中主动维护本地的值。
- 相应地，如果你需要修改这些变量，建议调用`ConfigManager.SetValue(key, value, asm)`来修改，这样可以确保配置项的值与UI显示的一致性，同时触发相应的回调。如果你确实需要运行时本地直接修改，系统最后也会在卸载时尝试同步你本地的数据作持久化。
- 系统将自动为每个子MOD添加一个复制配置文件夹路径到剪贴板的按钮，方便用户快速定位配置文件夹。
- 系统将自动为每个子MOD添加一个重置按钮，方便用户快速重置配置。
- 在`ConfigRegistry`注册完毕（即非阻塞或链式注册调用`Done`后），系统将自动扫描当前MOD的所有配置项并注册UI，然后自动生成相关按键以及分组，因此如果这之后添加了带有分组的项，不一定能进入分组，建议在注册完毕前完成所有配置项的注册。
- `ConfigManager`的注册函数与`ModRegistry`的注册函数执行逻辑完全相同，唯一的区别是后者需要`out`参数接受返回的`key`。

## 9. 调试辅助
- 为每个MOD提供按钮 `复制配置文件夹路径到剪贴板`，用于快速定位文件目录。
- 日志输出包含 Assembly Tag（`[Name vVersion]`），便于区分来源。
