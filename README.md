# JmcModLib

# 0. 前言

这是一个我个人做MOD时候用到的基础库，把他抽出来作为前置项目依赖方便个人维护，本来很早就动工了，结果越写越大越写越大。

[Release](https://github.com/JMC2002/JmcModLib/releases)

## 主要亮点
- 文档详尽、XML注释齐全，IDE友好
- 报错信息详细，方便排查问题，同时可以为使用日志库的库自动生成控制输出的配置项
- 测试齐全，大量测试覆盖边界情况（比如反射库就有将近两千行测试）
- 配置相关学习成本极低，只需要在静态字段/属性上添加几个特性就可以自动生成可持久化的配置项和UI，同时完美处理UI库的依赖顺序
- 反射工具库代码量低、性能好，API基本贴合标准库习惯
- 只需把本地化csv文档放在自己的文件夹下即可自动加载本地化，0文件操作代码
- 可扩展性强，比如你可以只需要重载一下接口就可以自定义存储后端，项目本身也按模块划分，内部可维护性强

# 1. 使用方法
开发上，在订阅此MOD后在项目`.csproj`添加引入
```xml
    <Reference Include="JmcModLib">
      <HintPath>$(SteamWorkShop)3613297900\JmcModLib.dll</HintPath>
    </Reference>
```
不过更推荐直接使用[ModTemplate](https://github.com/JMC2002/ModTemplate)模板`csproj`内容，不仅包含了创意工坊的地点，还包含了一些比较实用的功能，比如自动根据项目文件夹下的`Core/VersionInfo.cs`获取版本号，写入到程序集、每次生成自动将构建的dll复制到项目文件夹下的MOD同名文件夹中，并将这个文件夹的内容与游戏本地MOD文件夹的内容同步，在生成完成后，还会自动跳出弹窗询问是否打开鸭科夫。
此外，这个模板项目还可以自动处理与JmcModLib以及其他前置的依赖顺序，避免手动调整加载顺序的麻烦，并在需要的时候弹窗提示。

本项目有详尽的XML文档注释，引入依赖后在IDE中会有相应的提示。

## 用法速览
### 注册
在`OnAfterSetup`后一行`ModRegistry.Register(info, name, version)`即可完成注册
### 使用
在静态字段/属性上添加`[Config("配置项名称", group: "分组名称（可选）")]`即可生成一个配置项并持久化存储

在静态方法上添加`[UIButton("描述")]` 即可生成一个按钮UI，点击时会调用这个方法

在标记了`[Config]`的字段/属性上添加`[UIAttribute]`即可生成相应的UI元素（按钮、下拉框、滑动条、开关等）

调用`L10n.Get("key")`即可获取当前系统语言的本地化文本（注册UI时会自动使用本地化文本）

使用`ModLogger.Trace/Debug/Info/Warn/Error/Fatal`即可打印不同等级的日志，并可以自动生成调节打印格式与打印等级的用户设置与UI

使用`MethodAccessor/MemberAccessor.Get+Ivoke/GetValue/SetValue`等API即可方便地进行反射调用，支持泛型/out/ref参数/默认参数/索引器/Attribute，同时自动缓存并提供对应的强类型版本

MOD卸载不需要写任何卸载逻辑，系统会自动完成卸载，并在重连后也不会有问题（包括Setting UI）

# 2. 主要功能

## 2.1 配置管理器
- 自动扫描使用`[Config]`标记的静态字段/属性，生成配置项并持久化存储（默认路径是存档路径下的/JmcModLibConfig/，在订阅了相关UI MOD后可以直接在游戏中点击按钮复制路径）
- 在这些字段中，对于标记了`[UIAttribute]`的字段，如果检测到有相应的UI Mod，会自动为其生成UI界面，并与配置项关联；此MOD将会自动处理相关UI Mod的顺序
	>示例：比如你想为一个浮点数配置项生成滑动条UI，可以这样写：
	```cs
    [UIFloatSlider(1, 100)]
    [Config("测试浮点数", group: "分组2")]	// 分组是可选的
    public static float v = 3.14f;
	```
	这样就可以直接生成一个初始值为3.14的范围在1到100之间的滑动条UI，并且这个值会被持久化存储

	或者直接在你的静态方法上标注`[UIButton]`，就可以直接生成一个按钮UI，测试的时候非常方便
- 除此之外，还提供了直接注册配置的API，可以动态创建配置项和按钮，不过对于实例对象需要你自己维护生命周期，你也可以直接从一个值注册一个配置项并传入回调，让MOD托管生命周期。
- 目前暂时只适配了`ModSetting`，后面可以考虑增加适配其他，

## 2.2 日志工具库
- 提供了一个简单的日志系统，可以方便地为每个打印指定等级，并调整打印等级与TAG格式，以免出现某些MOD那样开发时的调试信息在运行时疯狂刷屏、全删掉又不方便用户报BUG时排查
- 提供了日志配置UI的预制件，默认可以生成一个简单的日志配置UI，可以让用户在游戏中调整日志等级与格式（比如选择是否需要输出时间、方法名之类的）
- 保留以后更换打印后端的扩展能力  

## 2.3 反射工具库
- 提供了一些反射库API，极大简化了反射调用的代码量
- 同时提供了更好的性能，自动生成缓存，所有调用者都可以共享缓存，同时对于强类型会自动生成委托，经测试百万次双参数方法调用通用接口（93ms）较原始反射调用（581.37ms）耗时减少83.99%、强类型接口（2.13ms)耗时减少99.6%
- 测试齐全，覆盖各种边界情况；功能齐全，标准库反射能做的都能做

更详细的API说明可以查看[注册器文档](/Docs/ModRegistry.md) / [Config文档](/Docs/ConfigManager.md) / [本地化文档](/Docs/L10n.md) / [日志库文档](/Docs/ModLogger.md) / [反射库文档](/Docs/Reflection.md)或者查看[XML注释](/JmcModLib/JmcModLib.xml)

# 3. 环境依赖与兼容性
- 本MOD仅依赖`Newtonsoft.Json`，版本为13.0.4，模组已内置
- 本MOD不直接强依赖于任何UI Mod，但是建议配合[ModSetting](https://steamcommunity.com/sharedfiles/filedetails/?id=3595729494) 使用以获得配置项UI支持
- 本MOD不修改游戏任何代码逻辑，理论上不与任何MOD冲突
- 为获得更好的使用体验，建议将本MOD的排序调到最高
- 通过观察，`谁偷了我的帧数`MOD加载的情况下会严重影响强类型委托外的反射调用的性能测试，百万次调用测试耗时达到1s（不过这个数量级应该不会在实际使用中出现）
- 加载顺序上，如果直接引用dll不做任何处理，需要保证本MOD在子MOD顺序之上，不过建议使用[ModTemplate](https://github.com/JMC2002/ModTemplate)的相关方法无序依赖；本MOD维护的其他Setting UI（暂时只有ModSetting）不需要处理加载顺序

# 4. 其他
- 可以把文档喂给AI让他帮你用这个库，参考：[prompt](https://github.com/JMC2002/Duckov-AnalyzeCsClass?tab=readme-ov-file#%E5%96%82%E7%BB%99ai)

- API约定：
	- 所有API中涉及Assembly的参数均可选，默认使用`Assembly.GetCallingAssembly()`返回调用方的程序集

- AI辅助声明：
	- 开发过程中使用GPT辅助开发
	- 生成（除README外的）文档使用GPT生成大体框架，然后手动润色补充
	- 测试代码与覆盖范围主体部分由GPT生成
	- MOD内各个字词的翻译文本由GPT生成
	- MOD图标由GPT生成

- 许可证：
	- 本MOD采用LGPL-2.1许可证

如果有人用了这个MOD，可以加入群（[点击入群(617674584)](http://qm.qq.com/cgi-bin/qm/qr?_wv=1027&k=Kii1sz9bmNmgEgnmsasbKn6h3etgSoQR&authKey=Hni0nbFlbd%2BfDZ1GoklCdtHw4r79SuEsHvz9Pi4qs020w1f8D2DHD8EEnNN1OXo6&noverify=0&group_code=617674584)
）交流反馈

# 5. FAQ
- Q：为什么叫这个名字？
- A：因为不想起名字，直接叫ModLib又担心冲突，用我的ID拼上去肯定就不会冲突了

# 6. 最后
感谢阅读到这里，如果你觉得这个MOD对你有帮助，欢迎给个Star鼓励一下，也欢迎提出任何建议和意见，我会尽量改进和完善这个MOD，谢谢！

测试项目：
[TestMod](https://github.com/JMC2002/Duckov-TemplateMod)

模板项目：
[ModTemplate](https://github.com/JMC2002/ModTemplate)