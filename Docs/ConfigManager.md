# JmcModLib ConfigManager 使用与集成指南

本文档介绍 `ConfigManager` 的职责、API、生命周期、典型用法与注意事项，帮助 MOD 在 JmcModLib 中高效注册、持久化与展示配置项。

## 1. 角色与职责
- 统一管理配置项注册、缓存与持久化（按 Assembly 分隔）。
- 自动在 `ModRegistry` 完成注册时扫描 `[Config]` 成员并生成 UI/存储条目。
- 提供直接注册按钮、配置项的便捷 API（支持值、getter/setter、表达式、枚举下拉）。
- 支持每个 Assembly 自定义存储实现（默认 `NewtonsoftConfigStorage`）。

## 2. 生命周期与事件
- 初始化：`ConfigManager.Init()`（由库内部调用）
  - 订阅 `ModRegistry.OnRegistered` 自动扫描注册；订阅 `OnUnRegistered` 自动卸载。
- 卸载：`ConfigManager.Dispose()`
  - 取消订阅事件，清理所有缓存并保存必要数据。
- 事件：
  - `OnRegistered(Assembly)`：某 Assembly 完成配置扫描与注册后触发。
  - `OnValueChanged(ConfigEntry entry, object? value)`：配置项值变更时触发。

## 3. 存储后端
- 默认实现：`NewtonsoftConfigStorage`，路径在 `Application.persistentDataPath/Saves/JmcModLibConfig`。
- 自定义存储：
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
public static class Gameplay
{
    [Config("移动速度", group:"角色")]
    [UIFloatSlider(0f, 20f, decimalPlaces:1)]
    private static float MoveSpeed = 6.5f;

    [Config("是否启用冲刺", group:"角色")]
    [UIToggle]
    private static bool EnableSprint = true;

    [UIButton("重置进度", buttonText:"重置", group:"调试")]
    private static void ResetProgress() { /* ... */ }
}
```

## 5. 直接注册 API（补充途径）
支持以多种形式注册配置项与按钮：

## 5.1 Attribute 速查与示例（推荐优先使用）

### ConfigAttribute 基本用法
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
    [Config("音乐音量", group:"音频")]
    [UIFloatSlider(0f, 1f, decimalPlaces:2)]
    private static float MusicVolume = 0.75f;

    [Config("最大连接数", group:"网络")]
    [UIIntSlider(1, 32)]
    private static int MaxConnections = 8;
}
```

### KeyCode 绑定
```csharp
public static class InputConfig
{
    [Config("快速打开菜单", group:"快捷键")]
    [UIKeyBind]
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

### Converter（自定义逻辑类型 → UI 类型）
```csharp
// 假设内部用 int 表示百分比 (0~100)，希望 UI 用 float 0~1 表示
public sealed class PercentSliderAttribute : UIConverterAttribute<float>
{
    public override float ToUI(object logicalValue) => (int)logicalValue / 100f;
    public override object FromUI(float uiValue, Type logicalType) => (int)(uiValue * 100);
    internal override void BuildUI(ConfigEntry<float> entry) => ModSettingBuilder.FloatSliderBuild(entry, new UIFloatSliderAttribute(0f, 1f, 2));
}

public static class InternalConfig
{
    [Config("掉落倍率", group:"平衡")]
    [PercentSlider]
    private static int DropRatePercent = 65; // UI 显示 0.65
}
```

### 与表达式注册混用（实例对象场景）
当需要对“实例成员”使用 UI，而实例生命周期可控时，可改用表达式注册：
```csharp
// 实例对象
public class Player
{
    public float HP { get; set; } = 100f;
}
static Player _player = new();

// 注册阶段：
ConfigManager.RegisterConfig(new UIFloatSliderAttribute(0f, 500f), "玩家血量", () => _player.HP, v => _player.HP = v);
```

### Attribute 使用注意事项
- 推荐优先在静态成员上使用：避免实例生命周期带来的非对齐与 GC 问题。
- `ConfigAttribute.OnChanged` 方法若签名不符合（非 static / 参数不匹配），会被警告或报错并跳过回调。
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
var ui = new UIDropdownAttribute(nameof(MyEnum));
string key = ConfigManager.RegisterConfig(ui, "模式", () => Mode, v => Mode = v,
                                          group:"参数", action: v => OnModeChanged(v), asm: myAsm);
```

### 5.5 直接用默认值（托管生命周期）
```csharp
var ui = new UISliderAttribute(0, 100);
string key = ConfigManager.RegisterConfig(ui, "音量", 50, group:"音频", action: v => OnVolume(v), asm: myAsm);
```

### 5.6 枚举默认值（托管生命周期）
```csharp
var ui = new UIDropdownAttribute(nameof(MyEnum));
string key = ConfigManager.RegisterConfig(ui, "默认模式", MyEnum.Fast,
                                          group:"参数", action: v => OnMode(v), asm: myAsm);
```

### 5.7 表达式注册（字段/属性/静态/实例）
```csharp
string key = ConfigManager.RegisterConfig(ui, "生命值",
    expr: () => Player.Instance.HP,
    group:"角色", action: v => Log(v), asm: myAsm);
```
- 内部解析表达式为强类型 getter/setter，类型安全无反射开销。

## 6. 值变更与持久化
- 通过 UI 改值或调用 `ConfigManager.SetValue(key, value, asm)` 会：
  - 更新配置项当前值；
  - 立即写入存储后端（`Save(displayName, group, value)`）；
  - 触发 `OnValueChanged`。
- 反注册时：
  - 遍历所有条目 `SyncFromData()` 写回文件；
  - 调用存储后端的 `Flush(asm)` 刷盘。

## 7. 查询辅助
- `TryGetGroupForKey(key, asm)`：获取某 key 所在组名。
- `GetGroups(asm)` / `GetKeys(group, asm)`：枚举所有组与 key。
- `GetValue(key, asm)`：获取当前值。

## 8. 注意事项
- Assembly 隔离：每个 MOD 的配置相互独立缓存与存储。
- 线程安全：内部使用 `ConcurrentDictionary` 管理条目；自定义存储需保证线程安全。
- 表达式注册失败会抛出 `ArgumentException`，请确保表达式形如 `() => Obj.Member`。

## 9. 调试辅助
- 提供按钮 `复制配置文件夹路径到剪贴板`，用于快速定位文件目录。
- 日志输出包含 Assembly Tag（`[Name vVersion]`），便于区分来源。
