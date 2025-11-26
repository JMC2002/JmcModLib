# JmcModLib MOD 工作流程指南

本文档概述一个使用 JmcModLib 的 MOD 从初始化到卸载的完整工作流程，包含注册、配置、日志、本地化与 UI 的协作关系与调用顺序。

## 1. 生命周期阶段概览
1. 载入阶段（游戏启动/插件加载）
2. 注册阶段（完成 `ModRegistry` 注册）
3. 自动扫描与配置阶段（ConfigManager/L10n 根据注册事件执行工作）
4. 运行阶段（UI 交互、配置持久化、日志输出）
5. 卸载阶段（反注册、保存、清理资源）

---

## 2. 载入阶段
- 游戏或插件框架加载 MOD 程序集。
- JmcModLib 核心模块初始化：
  - `ModRegistry.Init()`：初始化并订阅卸载事件；
  - `ConfigManager.Init()`：初始化并订阅 `OnRegistered/OnUnRegistered`；
  - `L10n.Init()`：准备本地化环境。

---

## 3. 注册阶段
MOD 在适合的时机（如 OnAfterSetup）调用以下其中一种注册方式：

### 3.1 自动完成注册（简单）
```csharp
ModRegistry.Register(VersionInfo.ModInfo, name:"MyMod", version:"1.0.0");
```
- 成功注册后内部立即调用 `Done(asm)`，触发 `OnRegistered(asm)` 事件。

### 3.2 延迟完成注册（链式）
```csharp
var builder = ModRegistry.Register(true, VersionInfo.ModInfo, "MyMod", "1.0.0");
if (builder != null)
{
    builder
        .RegistL10n("Lang")
        // Logger UI 预制件：默认仅构建等级与格式开关；如需测试按钮，使用 All 或显式 TestButtons
        .RegistLogger(LogLevel.Info, LogFormatFlags.Default, LogConfigUIFlags.Default)
        .Done();
}
```
- 通过 `RegistryBuilder` 可在注册完成前指定本地化目录、日志设置、以及自定义配置项与 UI。

---

## 4. 自动扫描与配置阶段（以 Attribute 为主）
在 `Done(asm)` 触发的 `OnRegistered(asm)` 事件中：
- `ConfigManager.RegisterAllInAssembly(asm)`：
  - 扫描所有类型：
    - 方法标注 `UIButtonAttribute` → 注册到配置 UI（适合静态无参方法）；
    - 字段/属性标注 `ConfigAttribute`（更推荐静态成员）→ 创建配置项；
    - 若同时标注 `UIConfigAttribute<T>`（如 `UIFloatSlider`、`UIIntSlider`、`UIToggle`、`UIInput`、`UIDropdown`）→ 自动生成 UI；
  - 完成后广播 `ConfigManager.OnRegistered(asm)`（可被 UI 系统或其他模块订阅）。
- `L10n`：按注册的路径与备选语言加载 `.csv` 本地化文件，并提供文本解析。

更推荐在静态成员上标注 Attribute：生命周期简单、无需实例管理，自动生成 UI 与持久化。

示例：
```csharp
public static class Settings
{
    [Config("移动速度", group:"角色")]
    [UIFloatSlider(0, 20, decimalPlaces:1)]
    private static float MoveSpeed = 6.5f;

    [Config("画质", group:"图形")]
    [UIDropdown]
    private static QualityLevel Quality = QualityLevel.High;

    [UIButton("重置进度", "重置", group:"调试")]
    private static void ResetProgress() {}
}
```

更多 Attribute 用法（速查）：
```csharp
// 开关
[Config("是否启用冲刺", group:"角色")]
[UIToggle]
private static bool EnableSprint = true;

// Int 滑动条
[Config("最大连接数", group:"网络")]
[UIIntSlider(1, 64)]
private static int MaxConnections = 8;

// 输入框
[Config("玩家名", group:"用户")]
[UIInput(characterLimit:16)]
private static string PlayerName = "Hero";

// Key 绑定
[Config("打开菜单", group:"快捷键")]
[UIKeyBind]
private static UnityEngine.KeyCode OpenMenu = UnityEngine.KeyCode.F1;

// 枚举下拉（排除 None）
public enum RoleKind { None, Warrior, Mage, Archer }
[Config("角色类型", group:"角色")]
[UIDropdown(exclude: new[]{ nameof(RoleKind.None) })]
private static RoleKind Role = RoleKind.Warrior;

// OnChanged 回调
[Config("难度等级", onChanged: nameof(OnDifficultyChanged), group:"游戏")]
[UIDropdown]
private static DifficultyLevel Difficulty = DifficultyLevel.Normal;
private static void OnDifficultyChanged(DifficultyLevel lvl) { ModLogger.Info($"切换难度 {lvl}"); }
```

---

## 5. 运行阶段
- UI 展示配置项与按钮；用户交互触发更新：
  - `ConfigManager.SetValue(key, value, asm)`：更新值并即时存储；触发 `OnValueChanged(entry, value)`。
- 日志输出：
  - `ModLogger` 按每个 Assembly 的最小等级与格式标签输出；
  - Logger 的 UI 由 `LogConfigUIFlags` 控制是否构建最低打印等级、格式开关与测试按钮。
- 其他模块可以订阅事件进行联动：
  - 例如配置变更回调、按钮触发逻辑等。

---

## 6. 卸载阶段
- 游戏或框架通知卸载：`ModManager.OnModWillBeDeactivated` 触发。
- `ModRegistry.TryUnRegistered` 映射到 Assembly，执行：
  - `ModRegistry.UnRegister(asm)`：广播 `OnUnRegistered(asm)`；
  - `ConfigManager.Unregister(asm)`：
    - `SaveAllInAssembly(asm)`：遍历所有条目，确保最新数据写入存储；
    - 清理缓存、UI、存储后端映射；
  - 清理 `_mods` 和 `_pathToAssembly` 记录。

---

## 7. 推荐调用顺序（摘要）
1. 游戏初始化 → JmcModLib Init（内部）
2. MOD 在 OnAfterSetup 调用 `ModRegistry.Register(...)`
3. 若需自定义：使用 `RegistryBuilder` 链式注册 L10n/Logger/Config/UI → `Done()`
4. `ConfigManager` 自动扫描并注册配置项与按钮，L10n 加载本地化
5. 运行时：
   - 配置变更 → 实时保存；
   - 日志输出按等级与标签；
6. 卸载：`ModRegistry.UnRegister` → `ConfigManager.Unregister` → 保存并清理

---

## 8. 故障排查提示
- 注册时机：必须在 `OnAfterSetup` 以后，确保 `ModInfo.displayName` 可用。
- 日志未显示：检查最小等级与 UI 设置，或调用 `ModRegistry.SetLogLevel(level, asm)`。
- UI 不出现：确认 `Done(asm)` 是否已触发；检查 `ConfigUIManager.Init()` 是否正常。
- 表达式注册失败：确保形如 `() => Obj.Member`，由 `ExprHelper` 解析为强类型 getter/setter。

---

## 9. 参考文档
- `Docs/ModRegistry.md`
- `Docs/ConfigManager.md`
- `Docs/ModLogger.md`
- 反射库：`Docs/Reflection.md` / `Reflection/Accessors-Usage.md`

---

## 10. 主要依赖关系（解决方案级）
- Unity 引擎运行时（日志输出使用 `UnityEngine.Debug`；UI 依赖 Unity 环境）
- Duckov.Modding（Mod 生命周期与卸载事件来源：`ModManager.OnModWillBeDeactivated`）
- Newtonsoft.Json（默认配置存储 `NewtonsoftConfigStorage` 使用）
- .NET Standard 2.1 BCL（反射、表达式树、并发集合等）
- JmcModLib 内部模块：
  - Core：`ModRegistry`、`RegistryBuilder`、`VersionInfo`
  - Config：`ConfigManager`、`ConfigEntry`、`ConfigEntryFactory`、`IConfigStorage`、`ConfigUIManager`、各类 `UIAttribute`
  - Utils：`ModLogger`、`BuildLoggerUI`（`LogConfigUIFlags`、`BuildLogLevelSettings`、`BuildFormatFlags`、`BuildTestButtons`）
  - Reflection：`MemberAccessor`、`MethodAccessor`、`ReflectionAccessorBase`、`FastMemberAccessor`
