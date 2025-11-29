# JmcModLib ModLogger 使用与集成指南

本文档介绍 `ModLogger` 的职责、配置、API 与集成方式，帮助 MOD 在 JmcModLib 中实现一致的日志输出、分级过滤与 UI 配置。

命名空间：`JmcModLib.Utils`

## 1. 角色与职责
- 提供基于 Assembly 的日志记录（支持等级与格式标签过滤）。
- 统一输出格式，包含Mod名、版本号与调用源信息（文件/行/成员）。
- 提供 UI 集成（最低打印等级、格式标签开关），可在注册时使用`LogConfigUIFlags`选择生成配置界面。

## 2. 等级与格式
- 日志等级：`Trace` < `Debug` < `Info` < `Warn` < `Error` < `Fatal`
- 最小等级控制：每个 Assembly 独立设置最小输出等级。
- 格式标签：通过 `LogFormatFlags` 控制输出内容（时间、等级、源、堆栈等）。

## 3. 注册与配置
```csharp
// 在链式构建器中注册当前 Assembly 的日志设置
builder.RegistLogger(level: LogLevel.Info,
                     tagFlags: LogFormatFlags.Default,
                     uIFlags: LogConfigUIFlags.Default);
```

## 4. 基本用法
```csharp
ModLogger.Trace("消息");
ModLogger.Debug("调试信息");
ModLogger.Info("提示");
ModLogger.Warn("警告", ex);
ModLogger.Error("错误", ex);
ModLogger.Fatal(ex, "致命错误");

// 指定 Assembly（如在跨库调用时保证归属）
ModLogger.Info("消息", asm: myAsm);
```
- `Fatal` 在Debug模式下构建会抛出异常，Release下构建只会打印信息，防止用户运行时崩溃。

## 5. 输出格式与示例
统一格式示例：
```
[JmcModLib v1.0.6] [04:44:38] [INFO] BuildTestButtons.cs -> TestFatal (L25): 开始查找Debug模式
```
- 包含 Mod 标签、时间、等级、调用文件、成员与行号、消息文本。

## 6. UI 集成
`ModLogger` 内置 UI 预制件，由 `BuildLoggerUI` 生成。通过在注册时传入 `LogConfigUIFlags` 进行开关：

可用标志：
- `LogConfigUIFlags.LogLevel`：最低打印等级下拉（默认开启）。
- `LogConfigUIFlags.FormatFlags`：格式标签开关集合（默认开启）。
- `LogConfigUIFlags.TestButtons`：测试按钮（Trace/Debug/Info/Warn/Error/Fatal）（默认关闭，需手动开启或使用 All）。
- `LogConfigUIFlags.Default`：等同于 `LogLevel | FormatFlags`。
- `LogConfigUIFlags.All`：包含全部（含测试按钮）。

示例：
```csharp
// 仅开启默认（等级 + 格式开关）
builder.RegistLogger(LogLevel.Info, LogFormatFlags.Default, LogConfigUIFlags.Default);

// 开启全部（含测试按钮）
builder.RegistLogger(LogLevel.Info, LogFormatFlags.Default, LogConfigUIFlags.All);

// 仅开启测试按钮（调试用途）
builder.RegistLogger(LogLevel.Info, LogFormatFlags.Default, LogConfigUIFlags.TestButtons);
```

## 7. 最佳实践
- 良好的日志等级维护是开发时的重要工具，既可以帮助开发者调试，又可以避免用户端运行时信息过多，等级过滤既可以节省性能，又可以保证在必要的时候能够看到必要的信息
- 纯显示调用路径使用`Trace`，调试信息使用`Debug`，用户提示使用`Info`，预料中的可恢复错误使用`Warn`，预料中的不可恢复错误使用`Error`，预料外的错误（如传参不对）使用`Fatal`
- Error及以上的等级会调用打印堆栈，因此不要大量调用

## 8. 调试与故障排查
- 通过 `LogConfigUIFlags` 的测试按钮检查当前最小等级与输出格式设置是否生效。
- 如日志被吞，检查 `ModLogger.GetLogLevel` 与 UI 中的最低等级设置。
- 若 UI 不显示，确认 `ModRegistry.Done(asm)` 是否已触发。
