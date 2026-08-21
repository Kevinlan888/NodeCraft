# NodeCraft 内置节点插件化设计

## 1. 目标

把当前编译在 `NodeCraft.Flow` 中并由静态初始化器注册的 18 个内置节点迁移到一个随宿主发布的独立插件 `NodeCraft.BuiltIn`。

迁移后，“内置”只表示插件随 NodeCraft 一起构建和部署。它的发现、校验、注册和失败报告全部走现有插件链路：

```text
plugin.json
    -> PluginLoader.LoadAll
    -> IFlowPlugin.Register
    -> PluginRegistrationContext 暂存
    -> FlowNodeRegistry.RegisterPlugin 原子提交
    -> 调色板、节点工厂和执行会话
```

节点业务实现和业务 UI 不再位于 `NodeCraft.Flow`。需要内容区的节点使用独立 XAML，方便直接修改 XAML 源文件后重新构建插件；不提供运行时热更新。

## 2. 已确认的约束

- 18 个节点先合并在一个 `NodeCraft.BuiltIn` 插件中，不按调色板分类拆成多个插件。
- 所有节点定义、模型、执行器和节点专用辅助类从 `NodeCraft.Flow` 迁出。
- 删除 `BuiltInNodeRegistration` 和 `DefaultFlowNodeContentFactory`。
- 没有 `ContentFactory` 的节点内容区为空，仅显示公共节点外壳、标题和端口。
- 只有需要编辑配置或展示运行结果的 6 个节点提供独立 XAML UI。
- UI 遵循当前 Vision 插件模式：XAML 作为 `EmbeddedResource` 随程序集构建，由编辑器构造函数使用 `XamlReader.Parse` 加载。
- 控件树、布局、资源引用和样式写在 XAML 中；code-behind 不用 C# 手工搭建控件树。
- 修改 XAML 后重新构建插件生效，不支持运行时修改、热重载或外部 XAML 覆盖。
- 不兼容旧流程 XML；不保留旧 TypeKey、旧模型程序集名、类型转发或迁移别名。

## 3. 方案选择

### 3.1 采用：完整插件化

新增真正的插件程序集，宿主只通过 `plugin.json` 和 `IFlowPlugin` 发现节点。`NodeCraft.Flow` 只保留流程引擎、注册表、插件契约和通用 UI 外壳。

这使内置节点与 Vision、Communication 和 Algorithm 节点处于同一个依赖方向：插件依赖核心，核心不依赖具体节点。

### 3.2 不采用：核心实现加适配插件

不把节点代码留在 `NodeCraft.Flow` 后再增加一个只负责注册的插件。该方案虽然改动较少，但核心仍依赖具体模型和业务 UI，不能消除当前静态注册和类型判断。

### 3.3 不采用：宿主直接调用插件入口

不让 `NodeCraft` 项目直接调用 `BuiltInPlugin.Register`。宿主可以在构建阶段确保插件随应用发布，但运行时必须经过普通插件扫描，避免形成第二套注册路径。

## 4. 项目与包结构

新增项目：

```text
NodeCraft.BuiltIn/
├── NodeCraft.BuiltIn.csproj
├── plugin.json
├── Properties/
│   └── AssemblyInfo.cs
├── Plugin/
│   └── BuiltInPlugin.cs
├── Registrations/
│   ├── ValueNodeRegistrations.cs
│   ├── MathNodeRegistrations.cs
│   ├── LogicNodeRegistrations.cs
│   └── PreviewNodeRegistrations.cs
├── Nodes/
│   ├── *NodeModel.cs
│   ├── *Executor.cs
│   ├── BuiltInPortIds.cs
│   ├── BooleanNodePorts.cs
│   └── NodeValueConverter.cs
├── Views/
│   ├── StringValueEditor.xaml
│   ├── StringValueEditor.xaml.cs
│   ├── IntegerValueEditor.xaml
│   ├── IntegerValueEditor.xaml.cs
│   ├── FloatValueEditor.xaml
│   ├── FloatValueEditor.xaml.cs
│   ├── BooleanValueEditor.xaml
│   ├── BooleanValueEditor.xaml.cs
│   ├── AppendTextEditor.xaml
│   ├── AppendTextEditor.xaml.cs
│   ├── TextPreviewView.xaml
│   └── TextPreviewView.xaml.cs
└── Build/
    └── BuiltInPackaging.targets
```

项目使用 `net8.0-windows`、`UseWPF=true`、C# 9、nullable disabled，并以 `Private=false` 引用 `NodeCraft.Flow`。每份编辑器 XAML 从默认 `Page` 项中移除并显式作为 `EmbeddedResource` 包含，保持与 Vision 插件相同的资源加载方式。

稳定插件身份：

- 插件 ID：`nodecraft.builtin`
- 显示名称：`Built-in Nodes`
- 插件版本：`1.0.0`
- 入口程序集：`NodeCraft.BuiltIn.dll`
- 入口类型：`NodeCraft.BuiltIn.Plugin.BuiltInPlugin`
- API 版本：`1.0`
- 私有依赖目录：`lib`

## 5. 节点身份与分类

迁移后使用带插件命名空间的新 TypeKey：

| 分类 | 节点 | 新 TypeKey | 独立 XAML |
| --- | --- | --- | --- |
| Preview | String Value | `nodecraft.builtin.string-value` | 是 |
| Preview | Append Text | `nodecraft.builtin.append-text` | 是 |
| Preview | Text Preview | `nodecraft.builtin.text-preview` | 是 |
| Preview | JSON Serialize | `nodecraft.builtin.json-serialize` | 否 |
| Value | Integer Value | `nodecraft.builtin.integer-value` | 是 |
| Value | Float Value | `nodecraft.builtin.float-value` | 是 |
| Value | Boolean Value | `nodecraft.builtin.boolean-value` | 是 |
| Math | Add | `nodecraft.builtin.add-number` | 否 |
| Math | Multiply | `nodecraft.builtin.multiply-number` | 否 |
| Math | Subtract | `nodecraft.builtin.subtract-number` | 否 |
| Math | Divide | `nodecraft.builtin.divide-number` | 否 |
| Logic | Greater Than | `nodecraft.builtin.greater-than` | 否 |
| Logic | Less Than | `nodecraft.builtin.less-than` | 否 |
| Logic | Equal | `nodecraft.builtin.equal` | 否 |
| Logic | Boolean And | `nodecraft.builtin.boolean-and` | 否 |
| Logic | Boolean Or | `nodecraft.builtin.boolean-or` | 否 |
| Logic | Boolean Not | `nodecraft.builtin.boolean-not` | 否 |
| Logic | If | `nodecraft.builtin.if` | 否 |

旧 `node.*` TypeKey 不再注册。保存的新流程 XML 使用上表 TypeKey 和 `NodeCraft.BuiltIn` 中的新模型类型名。

## 6. 注册设计

`BuiltInPlugin.Register` 从四个分类注册工厂取得完整的 `FlowNodeRegistration`，并逐项调用 `context.Nodes.Register`。分类文件只是组织代码；每个节点仍拥有独立、完整的注册对象。

每个注册对象一次性声明：

- `FlowNodeDefinition`：TypeKey、显示名称、分类、输入输出端口和端口类型。
- `ExecutorFactory`：每次返回新的执行器实例。
- `NodeModelType` 和 `NodeFactory`：调色板创建及 XML 反序列化所需模型信息。
- `PaletteDisplayName`、`PaletteDescription` 和图标信息。
- 可选 `ContentFactory`。
- 可选 `ExecutionResultHandler` 和内容刷新策略。

注册不再分为 `Register` 和 `ConfigureEditors` 两阶段。插件完成暂存后，宿主使用现有 `FlowNodeRegistry.RegisterPlugin` 先验证整个批次，再原子提交 18 个节点。任意重复 TypeKey、缺失模型工厂或其他注册错误都会使整个插件注册失败，不留下部分节点。

注册表继续自动注入定义 slot 0 的 `flowIn` 控制输入；插件节点定义不得手工重复添加它。模型运行时端口和定义端口继续按稳定 Port ID 对齐，不能把运行时列表索引当成定义 slot。

`BuiltInNodePorts` 改为插件内部的 `BuiltInPortIds`。`FlowPorts.FlowIn` 继续属于核心；仅由 If 节点使用的 condition/true/false ID 迁入内置插件。示例插件和 CLI 模板各自声明自己的端口 ID，不再依赖内置插件类型。

## 7. XAML UI 设计

### 7.1 通用创建契约

六个编辑器各自公开与 `FlowNodeRegistration.ContentFactory` 匹配的静态工厂：

```csharp
internal static FrameworkElement CreateContent(FlowCanvas canvas, NodeModel node)
```

工厂校验具体模型类型并返回新的编辑器实例。编辑器构造函数按 Vision 模式完成以下工作：

1. 从当前插件程序集取得对应 XAML 的 manifest resource stream。
2. 使用 `XamlReader.Parse` 创建 XAML 根 `UserControl`。
3. 把根内容转移到编辑器实例的 `Content`。
4. 取得需要交互的命名控件。
5. 从模型初始化控件并注册事件。
6. 编辑成功时写回模型并调用 `FlowCanvas.NotifyGraphChanged`。

每次工厂调用必须返回新控件，避免 WPF 元素被多个节点父级复用。模型类型不匹配、资源缺失、XAML 根类型错误或命名控件缺失时抛出包含编辑器和控件名称的 `InvalidOperationException`。

### 7.2 六个编辑器

- `StringValueEditor`：字符串文本框，写回 `ValueText`。
- `IntegerValueEditor`：整数文本框；只有按 invariant culture 成功解析的值写回 `IntegerValue`。
- `FloatValueEditor`：浮点文本框；只有按 invariant culture 成功解析的值写回 `FloatValue`。
- `BooleanValueEditor`：复选框，写回 `BooleanValue`。
- `AppendTextEditor`：后缀文本框，写回 `SuffixText`。
- `TextPreviewView`：只读展示 `LastPreviewText`；没有结果时显示占位文本。

所有可视结构、标签、边距、布局、换行和主题资源引用位于 XAML。颜色使用宿主 `DynamicResource` 主题键，不硬编码 hex。code-behind 只保留资源加载、类型转换、模型交互、事件处理和变更通知。

Text Preview 的注册继续使用 `ExecutionResultHandler`，按输出 Port ID 解析 slot 并把当前执行结果写入 `LastPreviewText`。保持默认刷新策略，使结果写入后由宿主重建该节点的 XAML 内容。

### 7.3 无 UI 节点

其余 12 个节点不设置 `ContentFactory`。它们不再显示原默认内容工厂提供的公式说明、连接摘要、交换输入按钮或通用 `Output node` 文本，只显示公共节点外壳、标题和端口。

## 8. 核心清理与插件自描述

`NodeCraft.Flow` 执行以下清理：

- 删除 `Flow/Nodes` 下的 18 个具体节点及其辅助类。
- 删除 `BuiltInNodeRegistration`。
- 删除 `DefaultFlowNodeContentFactory`。
- `NodeExecutorFactory` 只创建空的全局 `FlowNodeRegistry`，不再触发静态节点注册。
- `FlowNodeRegistry` 删除默认内容工厂缓存及所有具体节点类型判断。
- `FlowNodeRegistry.BuildNodeContent` 只调用注册项的 `ContentFactory`；节点、画布、注册项或工厂缺失时返回 `null`。
- 删除核心注释和 using 中对 AddNumber、TextPreview 等具体节点的引用。

为移除核心中的插件 TypeKey 和类别图标硬编码，`FlowNodeRegistration` 新增两个可选属性：

```csharp
public string PaletteIconKind { get; set; }
public string PaletteCategoryIconKind { get; set; }
```

创建调色板时，节点图标优先使用 `PaletteIconKind`，否则使用该分类图标；分类图标使用该分类首个注册项的非空 `PaletteCategoryIconKind`，否则使用通用 `ShapeOutline`。同一插件对同一分类必须提供一致的分类图标，内置插件测试对此做显式断言。

Vision 和 Communication 注册项补充自己的图标元数据，以保持现有视觉效果。未提供新属性的第三方插件仍能注册，并得到通用图标。

## 9. 构建、部署与加载

`NodeCraft.BuiltIn` 加入 `NodeCraft.sln` 和测试项目引用。宿主构建通过明确的项目依赖先构建插件，再将最小插件包部署到：

```text
NodeCraft/bin/<Configuration>/net8.0-windows/Plugins/NodeCraft.BuiltIn/
├── plugin.json
└── NodeCraft.BuiltIn.dll
```

普通宿主构建自动执行该 staging；无需用户手工复制内置插件。staging 只清理上述精确插件目录，不能删除整个 `Plugins` 目录，避免覆盖用户安装的其他插件。

包中不得包含 `NodeCraft.Flow.dll`、`CommonControls.WPF.dll`、Microsoft logging 程序集或 WPF 框架程序集。内置插件没有私有运行时依赖，`lib` 可以省略或保持为空。

运行时不直接引用 `BuiltInPlugin`。`App.OnStartup` 解析 `PluginLoader` 时创建空注册表，随后扫描插件目录；内置插件和其他插件按现有目录排序、清单校验、隔离加载和错误报告规则处理。在 `FlowPage` 构造调色板之前，成功加载的 18 个注册项已进入全局注册表。

## 10. XML 与序列化

不新增旧 XML 迁移层。以下输入允许失败：

- `ExecutorType="node.add-number"` 等旧 TypeKey。
- `ModelType` 指向 `NodeCraft.Flow.Nodes.*` 的旧程序集限定类型名。
- 依赖旧默认节点注册已经存在的流程文件。

通用 `GraphModelXmlSerializer` 本身继续保留。新流程保存 `nodecraft.builtin.*` TypeKey 和 `NodeCraft.BuiltIn.Nodes.*` 模型类型。反序列化发生前宿主必须已经加载内置插件，这与当前插件节点的启动顺序一致。

删除或重写仅用于验证旧内置模型名回退的测试；保留通用插件节点 XML 往返和按 TypeKey 创建模型的测试。

## 11. 错误处理

- 插件包缺失：应用继续启动，调色板没有内置节点。
- manifest、入口类型、Metadata 或注册批次错误：整个插件加载失败，复用现有启动汇总通知和完整文件日志。
- XAML 资源缺失或格式错误：创建对应节点内容时抛出明确异常；自动化测试在发布前实例化全部六个工厂以提前发现。
- 编辑器取得错误模型类型：抛出包含期望模型类型的 `InvalidOperationException`。
- 数字编辑器输入暂时无效：不覆盖模型中的最后一个有效值，也不报告图已改变；下一次有效输入再写回并通知画布。
- 无 UI 节点：`BuildNodeContent` 正常返回 `null`，不视为错误。

## 12. 测试策略

### 12.1 核心注册表测试

- 新注册表不含任何隐式内置节点。
- `BuildNodeContent` 在没有注册项、没有工厂或参数为空时返回 `null`。
- 有 `ContentFactory` 时每次调用委托并返回其新实例。
- 调色板优先使用注册项图标元数据，并在缺失时使用通用图标。
- 插件批次验证和原子提交行为保持不变。

### 12.2 内置插件注册测试

- `plugin.json` 与 `BuiltInPlugin.Metadata` 的 ID、版本和入口类型一致。
- 插件准确暂存 18 个注册项，TypeKey 唯一且全部使用 `nodecraft.builtin.` 前缀。
- 分类、端口顺序、数据类型、必需性和执行器类型与现有节点行为一致。
- 每个 `NodeFactory` 和 `ExecutorFactory` 连续调用都返回不同的非空实例。
- 只有约定的 6 个注册项提供 `ContentFactory`。
- 同一调色板分类的分类图标一致，每个节点具有明确图标。

### 12.3 XAML UI 测试

在 WPF STA 测试线程中调用六个真实 `ContentFactory`：

- 每个工厂返回正确的编辑器类型和非复用实例。
- 每份嵌入 XAML 均可解析，所有必需命名控件存在。
- 字符串、布尔、整数、浮点和后缀编辑能更新对应模型并发出图变更通知。
- 非法数字文本不覆盖有效模型值。
- Text Preview 能展示 `ExecutionResultHandler` 写入的最新结果。
- XAML 使用 `DynamicResource` 主题键，不包含硬编码颜色。

### 12.4 加载与打包测试

- 使用临时插件包通过真实 `PluginLoader` 加载 `NodeCraft.BuiltIn`，确认 18 个节点原子进入注册表。
- 宿主构建输出包含 `Plugins/NodeCraft.BuiltIn/plugin.json` 和插件 DLL。
- 插件包不包含任何宿主共享程序集，也不删除或覆盖相邻插件目录。
- 从加载后的注册表创建调色板、创建一个 UI 节点和一个无 UI 节点。

### 12.5 行为与回归测试

现有数学、逻辑、值、文本预览、JSON 序列化、图执行和保存/加载测试迁移为通过 `BuiltInPlugin.Register` 注册节点。依赖全局注册表的画布和 XML 测试，在测试启动阶段通过同一插件批次路径注册一次；其余测试优先使用隔离的本地注册表。

示例插件、CLI 模板及其测试改用自身端口常量。Vision、Communication 和 Algorithm 的插件加载、执行及 UI 测试继续通过。

同步更新 `CLAUDE.md` 的架构说明和 `docs/node-plugin-development-guide.md` 的示例代码，移除对 `NodeCraft.Flow.Nodes` 与 `BuiltInNodePorts` 的公共 API 假设。历史设计和实施记录保持不改。

最终验证命令：

```powershell
dotnet build NodeCraft.sln
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows
dotnet run --project NodeCraft.Cli.Tests/NodeCraft.Cli.Tests.csproj
git diff --check
```

## 13. 验收标准

1. `NodeCraft.Flow` 不包含具体内置节点、内置节点注册器或默认业务 UI 工厂。
2. 普通宿主构建自动产生可扫描的 `NodeCraft.BuiltIn` 插件包。
3. 宿主只通过现有 `PluginLoader` 注册 18 个内置节点，没有直接注册旁路。
4. 调色板仍显示 Preview、Value、Math 和 Logic 分类中的 18 个节点。
5. 六个指定节点的内容完全由独立 XAML 定义，修改 XAML 并重建插件即可改变 UI。
6. 其余 12 个节点内容区为空，不再出现 C# 手写的默认业务 UI。
7. 新建、执行、保存和重新加载使用新 TypeKey 的流程正常工作。
8. 旧内置节点 XML 不保证加载，也不存在兼容代码。
9. 插件缺失或加载失败时应用仍可启动，并通过现有通知和日志暴露错误。
10. 解决方案构建、NodeCraft 测试跑棒、CLI 测试跑棒和差异检查全部通过。

## 14. 不在本次范围内

- 把内置节点继续拆成多个插件。
- 兼容或自动迁移旧流程 XML。
- XAML 热重载、运行时外部 XAML 覆盖或无需编译的主题编辑。
- 修改 Vision 插件现有的 XAML 加载机制。
- 重新设计节点计算语义、端口数据类型或执行器生命周期。
- 新增插件安装、启停、卸载或市场 UI。
