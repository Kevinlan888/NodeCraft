# NodeCraft 节点开发指南设计说明

## 1. 目标

创建一份中文 Markdown 开发指南 `docs/node-plugin-development-guide.md`，帮助以下两类读者独立完成一个新的 NodeCraft 插件和节点：

- 没有阅读过 NodeCraft 内核的新成员；
- 需要根据仓库约定生成代码、测试和插件包的 AI 编码助手。

指南必须以当前仓库的实际 API 和可运行示例为准，读者应能够从空项目或 `NodeCraft.PluginSample` 出发，完成一个能被宿主加载、显示在节点面板、参与图执行、保存到 `.flow.xml` 并通过测试的节点。

## 2. 文档形态

最终交付物为一份主指南：

`docs/node-plugin-development-guide.md`

主指南采用“先完成一个最小节点，再按需查阅专题”的结构。文档内使用 Mermaid 表达插件加载和图执行数据流；代码示例使用当前项目的 C# 9、.NET 8 WPF、显式 `using` 和现有命名空间约定。

设计说明本身不新增运行时代码、不改变插件 API，也不引入新的模板项目或 CLI 命令。

## 3. 参考实现和权威边界

指南使用以下代码作为交叉验证来源：

- `NodeCraft.PluginSample`：最小插件、多节点注册、私有依赖和自定义编辑器；
- `NodeCraft.Vision`：Session 生命周期、Iteration 数据源、外部资源和执行结果处理；
- `NodeCraft.Communication`：动态输入、持久化配置、连接资源和可配置失败策略；
- `NodeCraft.Flow`：插件契约、节点定义、端口、图适配、图执行、序列化和动态端口实现；
- `NodeCraft.Tests`：当前仓库的自运行测试跑棒、WPF STA 测试和插件集成测试。

当示例代码和概念性描述发生冲突时，以当前 `NodeCraft.Flow` 公共 API 和仓库测试行为为准；指南会明确标出只适用于示例插件的实现细节，避免读者把业务逻辑误当成框架要求。

## 4. 指南章节设计

### 4.1 快速开始

提供一个最短可执行路径：

1. 确认 Windows、.NET SDK、WPF 和本地包环境；
2. 创建插件项目并引用 `NodeCraft.Flow`；
3. 创建 `plugin.json`；
4. 实现一个字符串值 NodeModel 和 Executor；
5. 在 `IFlowPlugin.Register` 中注册节点；
6. 构建并放入宿主的 `Plugins` 目录；
7. 启动 NodeCraft，验证节点面板、执行和保存；
8. 运行 `NodeCraft.Tests` 测试跑棒。

这一节只介绍完成第一个节点所需的最小概念，后续章节再解释每个 API 的完整语义。

### 4.2 架构总览

解释以下对象的职责和边界：

- `plugin.json`：宿主发现插件时使用的包描述；
- `IFlowPlugin`：插件入口和节点注册入口；
- `PluginMetadata`：插件稳定 ID、显示名和版本；
- `FlowNodeRegistration`：节点定义、执行器工厂、模型工厂、调色板和 UI 绑定；
- `FlowNodeDefinition`：运行时端口和类型契约；
- `NodeModel`：画布状态、用户配置和持久化状态；
- `WorkflowNode`：图适配后的运行时节点和输入字典；
- `IFlowNodeExecutor`：一次 iteration 的执行逻辑；
- `GraphExecutionSession`：Session 启动、Iteration 调度和反向清理。

包含两张 Mermaid 图：

- 插件发现、加载、注册到节点面板的流程；
- 画布模型经 `GraphModelWorkflowAdapter` 转换后进入 `GraphExecutor` 的执行流程。

### 4.3 插件项目和包布局

提供可复制的 `.csproj`、`plugin.json` 和目录模板，并说明：

- `TargetFramework`、`UseWPF`、`LangVersion` 和 nullable 的当前约定；
- `entryAssembly`、`entryType`、`apiVersion`、`privateLibraryPath` 的含义；
- 插件输出目录必须包含的文件；
- `lib` 私有依赖的放置规则；
- 为什么不能把 `NodeCraft.Flow.dll`、`CommonControls.WPF.dll` 或 WPF 框架程序集复制到插件包；
- 插件 ID、入口类型和 `PluginMetadata.Id` 必须保持一致；
- 宿主扫描插件目录的边界、同 ID 和重复 TypeKey 的行为。

### 4.4 最小节点完整教程

使用一个“字符串值节点”贯穿示例，给出完整文件：

- `Nodes/HelloValueNodeModel.cs`；
- `Nodes/HelloValueExecutor.cs`；
- `Plugin/HelloPlugin.cs`；
- `plugin.json`；
- 最小测试。

示例会显式展示：

- 稳定的 TypeKey 命名；
- `NodeModel` 构造函数中的 `ExecutorType`、名称和端口列表；
- `IWorkflowNodeValueProvider.WriteWorkflowInputs`；
- `FlowNodeDefinition` 的输入/输出端口；
- Executor 对输入的读取、取消检查和输出字典；
- `FlowNodeRegistration` 的 `NodeModelType`、`NodeFactory`、调色板信息和 Executor 工厂。

### 4.5 端口、数据类型和 slot

完整解释固定端口的定义方式：

- `FlowPortDefinition.Id`；
- `DisplayName`；
- `IOType`；
- `DataType`；
- `PreferredDirection`；
- `IsRequired`；
- `DefaultValue`；
- `Availability`；
- `IsControlPort`。

解释当前类型系统中的 `string`、`number`、`boolean`、`object`、`control`、`*` 和 `MATCH_TYPE`，以及类型校验的基本行为。

特别加入一个“slot 规则”警告框：slot 是有效 `FlowNodeDefinition` 的端口索引，不是 `NodeModel.InputParameters` 的列表位置。动态端口、`flowIn` 和运行时端口排序不能通过列表下标猜测。

### 4.6 NodeModel、WorkflowNode 和持久化

用数据流示例区分三个阶段：

```text
NodeModel 属性
    ↓ WriteWorkflowInputs
WorkflowNode.Inputs
    ↓ GraphExecutor / Session
IFlowNodeExecutor
```

说明：

- 哪些值应该作为 NodeModel 公共属性；
- 哪些值应该写入 Workflow 输入字典；
- 现有 XML 序列化如何保存自定义公共属性；
- 如何给新属性设置默认值；
- 如何保证旧图加载时有合理行为；
- 为什么 Executor 不应该依赖 UI 控件或直接持有 NodeModel；
- 如何测试模型属性、Workflow 输入和 XML 往返的一致性。

### 4.7 Executor 开发模式

按复杂度介绍四种模式：

1. 无状态 iteration Executor；
2. 带静态配置的 Executor；
3. 实现 `IFlowNodeSessionLifecycle` 的资源型节点；
4. 同时实现 `IFlowIterationSource` 的连续数据源节点。

每种模式都包含生命周期时序、适用场景、最小代码和测试策略。重点覆盖：

- `StartSessionAsync` 只初始化一次的资源；
- `PrepareIterationAsync` 获取下一帧或下一项数据；
- `ExecuteAsync` 只处理当前 iteration；
- `StopSessionAsync` 的幂等清理；
- 启动失败、执行失败、取消和停止竞争条件；
- `CancellationToken` 不能被吞掉；
- 连接、文件句柄、相机、线程和 Task 的所有权。

### 4.8 自定义 WPF 编辑器

参考 Sample 和 Communication 编辑器，说明：

- `ContentFactory` 的签名和返回值；
- XAML 作为 EmbeddedResource 的项目配置；
- `XamlReader.Parse` 和 root content 拆分；
- 控件初始化期间的 `_initializing` 守卫；
- 文本框、复选框和数值输入如何写回 NodeModel；
- 何时调用 `FlowCanvas.NotifyGraphChanged`；
- 不合法输入如何保留旧值；
- `DynamicResource` 主题资源和 UI 线程要求；
- 每个 NodeModel 实例必须获得独立的 UI 内容。

附一个包含字符串、整数、布尔值和路径配置的可复制编辑器模板，并明确动态端口控制仍由通用 Flow UI 负责。

### 4.9 动态输入端口

以 Communication TCP Client 为示例，解释：

- `FlowDynamicInputTemplate` 的字段；
- `PortIdPrefix`、`DisplayNamePrefix`、`MinCount`、`InitialCount`、`MaxCount`；
- `FlowPortAvailability.Iteration` 和 `IsRequired`；
- `MaterializeNodePorts` 和 `ResolveDefinition`；
- 添加、删除、重编号和链接重建；
- 动态端口的稳定 ID 和顺序；
- Executor 必须遍历有效 definition 中的动态端口，而不是遍历输入字典；
- 动态输入值与静态配置的区别。

列出动态节点的测试矩阵：初始数量、无限添加、删除中间端口、链接往返、缺失值、错误 slot、类型不兼容和执行顺序。

### 4.10 错误、日志和失败策略

建立错误分类表：

| 错误类别 | 默认处理 | 是否可以由节点策略忽略 |
| --- | --- | --- |
| 配置缺失或非法 | 启动/校验失败 | 通常不能 |
| 必需输入缺失 | iteration 失败 | 通常不能 |
| 输入编码/转换失败 | 节点失败，记录上下文 | 需按节点语义明确 |
| 外部资源连接失败 | Session 启动失败并清理 | 通常不能 |
| 发送/写入失败 | 由节点策略决定 | 可以，但必须记录 |
| 取消 | 传播取消 | 不能吞掉 |

结合 `stopOnSendFailure` 说明 try/catch 的覆盖范围，防止开发者误以为所有异常都会服从同一个失败策略。

同时介绍：

- `ILogger` 的结构化日志；
- 日志中应包含节点 ID、端口 ID 和业务上下文；
- 不要静默吞异常；
- 如何保留主异常并让宿主负责清理异常。

### 4.11 执行结果、预览和临时状态

说明 `ExecutionResultHandler`、`RefreshContentAfterExecution` 与 NodeModel 临时状态的关系，包含：

- 如何把输出显示到自定义预览控件；
- 如何区分持久化配置和运行时预览状态；
- 如何处理 null、空输出和执行失败后的 UI 状态；
- 何时需要刷新内容，何时不需要重新创建内容。

### 4.12 私有依赖和插件加载

用 Sample 插件的私有 formatter 说明：

- 私有依赖项目如何构建；
- `lib` 目录如何由 MSBuild staging target 生成；
- 共享程序集由宿主默认上下文提供；
- 私有程序集由插件可回收加载上下文解析；
- 常见的类型身份冲突、缺失 DLL 和包路径越界问题；
- 如何使用 PluginLoader 集成测试验证最终包，而不是只验证主项目编译。

### 4.13 测试指南

以当前自运行 `NodeCraft.Tests` 模式提供测试模板，覆盖：

- 插件 manifest 和项目结构；
- 插件 metadata 和注册结果；
- NodeModel 默认值及 Workflow 输入投影；
- 端口和类型契约；
- Executor 的成功、失败、取消和输出；
- Session 启动、重复停止、启动失败清理和资源释放；
- 动态端口添加/删除、顺序和链接；
- XML 保存/加载往返；
- WPF 编辑器 STA 行为；
- PluginLoader 最终包加载；
- loopback 网络或其他本地外部资源测试。

明确验证命令：

```powershell
dotnet build NodeCraft.sln --no-restore
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore
git diff --check
```

同时说明如何区分产品代码失败、测试环境失败和权限/网络边界失败。

### 4.14 常见错误和排错路径

按症状组织排错表，例如：

- 插件没有出现在面板；
- 插件加载失败但项目能编译；
- 节点出现但旧图无法加载；
- 端口显示顺序不对；
- 链接类型校验失败；
- Session 资源泄漏；
- 取消后 Task 仍在运行；
- 配置保存了但 Executor 没读到；
- `stopOnSendFailure` 对某类异常不生效；
- 自定义编辑器初始化时误触发 GraphChanged；
- 测试通过但宿主加载失败。

每个排错项包含“观察点 → 相关文件 → 最小验证 → 常见根因”。

### 4.15 AI 专用开发协议

增加可直接交给 AI 的流程：

1. 先读取 `CLAUDE.md`、`NodeCraft.PluginSample` 和相关 Flow API；
2. 先确定节点是无状态、Session、Iteration 还是动态端口类型；
3. 写出 TypeKey、端口契约、配置字段和生命周期表；
4. 先补最小失败测试；
5. 按 Model → Definition/Registration → Executor → Editor → Integration 顺序实现；
6. 在每个组件边界检查数据是否正确传递；
7. 执行构建、测试、包加载和 diff 检查；
8. 报告文件、行号、测试结果和已知环境问题。

提供一份 AI 开发检查清单，要求 AI 明确回答：

- TypeKey 是否稳定且命名空间化；
- manifest ID 和 Metadata ID 是否一致；
- 端口 slot 是否基于 definition；
- 配置是否同时覆盖 UI、Workflow、XML 和 Executor；
- 取消和清理是否覆盖所有路径；
- 动态端口是否有顺序和链接测试；
- 自定义编辑器是否使用主题资源；
- 最终插件包是否能由 PluginLoader 加载。

## 5. 非目标

本次指南不包含：

- 修改 NodeCraft 核心 API；
- 新增插件脚手架 CLI；
- 自动生成完整插件项目；
- 讲解通用 WPF 或 C# 基础；
- 为所有 Vision SDK 设备提供硬件接入教程；
- 设计新的图文件格式；
- 解决现有测试跑棒中的环境权限问题。

## 6. 完成标准

最终指南完成时应满足：

- 新人可以依照快速开始创建并加载一个最小插件；
- AI 可以根据模板实现一个固定端口节点、配置型节点和动态输入节点；
- 每个关键公共 API 都能追溯到仓库中的实现或测试；
- 文档明确区分模型、Workflow、Definition、Session 和 Iteration；
- 文档包含可复制代码，而不是只有概念描述；
- 文档包含失败路径、取消、资源清理和测试要求；
- 文档内所有章节内容均已明确，没有未完成标记或含糊表述；
- 文档中的命令、路径和项目名称与当前仓库一致。
