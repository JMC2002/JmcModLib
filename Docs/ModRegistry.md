# JmcModLib ModRegistry 使用与集成指南

本文档介绍 `ModRegistry` 的职责、API、事件与最佳实践，帮助 MOD 在 JmcModLib 中完成元信息注册、链式配置与生命周期管理。

## 1. 角色与职责
- 管理 MOD 的元信息注册与反注册（按 Assembly 唯一标识）。
- 提供自动完成与延迟完成两种注册模式（支持链式构建器）。
- 广播注册完成/反注册事件，驱动其他模块（如 `ConfigManager`、`L10n`）。

## 2. 生命周期
- 初始化：`ModRegistry.Init()`（由库内部调用）
  - 初始化 `ConfigManager`、`L10n`
  - 订阅 `ModManager.OnModWillBeDeactivated` 以在卸载时反注册
- 卸载：`ModRegistry.Dispose()`
  - 取消订阅事件，清理内部缓存与索引

## 3. 注册模型
### 3.1 自动完成注册（简单）
```csharp
ModRegistry.Register(VersionInfo.ModInfo, name:"MyMod", version:"1.0.0");
```
- 成功后立即调用 `Done(asm)`，触发 `OnRegistered(asm)`。

### 3.2 延迟完成注册（链式）
```csharp
var builder = ModRegistry.Register(true, VersionInfo.ModInfo, "MyMod", "1.0.0");
if (builder != null)
{
    builder
        .RegistL10n("Lang")
        .RegistLogger(LogLevel.Debug)
        .Done();
}
```
- 当 `deferredCompletion == true` 时，返回 `RegistryBuilder`，调用方可在完成自定义模块注册后显式 `Done()`。

## 4. 事件
- `OnRegistered(Assembly)`：某 MOD 完成注册时触发。
- `OnUnRegistered(Assembly)`：某 MOD 反注册时触发。

## 5. 查询与辅助
- `IsRegistered()` / `IsRegistered(asm)`：判断是否已注册。
- `GetTag(asm)`：返回 `"[Name vVersion]"` 标签；未注册则退回 Assembly 名称+版本。
- `SetLogLevel(level, asm)`：设置该 Assembly 的日志最小等级（代理到 `ModLogger.SetMinLevel`）。

## 6. 错误与日志
- 重复注册会输出 `Warn` 并返回失败。
- 当调用 `Done(asm)` 但未注册，会调用 `ModLogger.Fatal(CreateNotRegisteredException(asm))` 记录致命错误。
- 当 `ModInfo.displayName` 为空时，会输出 `Warn`（表示时机不正确，建议在 `OnAfterSetup` 以后注册）。

## 7. 与 ConfigManager/L10n 的协作
- `ModRegistry.Init()` 会初始化 `ConfigManager` 与 `L10n`。
- `OnRegistered(asm)` 由 `ConfigManager` 订阅以自动扫描配置项并注册 UI；由 `L10n` 订阅以初始化本地化。

## 8. 最佳实践
- 在游戏生命周期的合适时机（如 `OnAfterSetup` 后）调用注册，确保 `ModInfo` 已初始化，特别提醒：`OnEnable`时不可用。
- 推荐在需要手动创建组或参数的场景使用延迟完成注册（`Register(true, ...)`）。
- 若 API 中未显式传入 `Assembly`，默认使用 `Assembly.GetCallingAssembly()`；为保证准确性，建议显式传入。

## 9. 反注册
- `UnRegister(asm)`：
  - 触发 `OnUnRegistered(asm)`，由 `ConfigManager` 等模块执行保存与清理。
  - 清理 `_mods`、`_pathToAssembly` 映射。
- 游戏卸载钩子：`TryUnRegistered(ModInfo info, ModBehaviour modBehaviour)` 会根据 `info.path` 查找对应 Assembly 并自动反注册。
