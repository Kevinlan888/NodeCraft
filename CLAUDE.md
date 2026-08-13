# CLAUDE.md

本文件为 Claude Code（claude.ai/code）在本仓库工作时提供指引。

## 构建、运行与测试

```bash
# 构建解决方案
dotnet build NodeCraft.sln

# 运行宿主应用
dotnet run --project NodeCraft/NodeCraft.csproj

# 运行控制台测试跑棒（WPF 目标，需在 Windows 上执行）
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows
# 期望输出 ALL PASS

# 运行 CLI 脚手架工具自测
dotnet run --project NodeCraft.Cli.Tests/NodeCraft.Cli.Tests.csproj
```

NuGet 源见 `nuget.config`：本地 `Packages/` 优先，回退到 nuget.org。`CommonControls.WPF` 以 `PackageReference` 形式引用（版本 `1.0.0`）。

## 架构总览

```
NodeCraft.sln
├── NodeCraft/              # WPF 宿主应用（WinExe, net8.0-windows）
│   ├── App.xaml            # 合并 CommonControlTheme + FluentDesign.Defaults.xaml + Flow.xaml
│   ├── App.xaml.cs         # OnStartup 加载插件（PluginLoader.LoadAll）后构建 MainWindow
│   ├── MainWindow.xaml     # FramelessWindowEx + 菜单（文件/流程/视图·深色主题）
│   ├── Pages/FlowPage.xaml # 流程编辑器页面
│   └── Plugins/            # 清单读取、隔离加载上下文、NLog 引导、启动通知
│
├── NodeCraft.Flow/         # 引擎 + 插件 API（net8.0-windows, RootNamespace=NodeCraft）
│   ├── Flow/               # FlowCanvas、GraphModel、GraphExecutor、FlowNodeRegistry、FlowTypeValidator
│   ├── Localization/       # 流程本地化资源
│   ├── Plugins/            # IFlowPlugin、IPluginContext、IPluginNodeRegistrar、PluginMetadata
│   └── Themes/Flow.xaml    # 流程编辑器资源
│
├── NodeCraft.PluginSample/ # 示例插件（多节点 + 私有依赖 PrivateDependency）
├── NodeCraft.Cli/          # nodecraft-cli 脚手架工具（net8.0, dotnet tool）
├── NodeCraft.Cli.Tests/    # CLI 自测（自运行 exe）
├── NodeCraft.Tests/        # 控制台测试跑棒（自运行 exe, net8.0-windows）
└── Packages/               # 本地 NuGet 源（CommonControls.WPF.1.0.0.nupkg）
```

## 插件系统关键约定

- **加载时机**：`App.OnStartup` 在 `MainWindow`/`FlowPage` 构造前扫描插件，保证画布节点就绪。扫描 `Path.Combine(AppContext.BaseDirectory, "Plugins")` 下**直接子目录**中含 `plugin.json` 的包，按 `StringComparer.OrdinalIgnoreCase` 排序。
- **包布局**：`<app root>\Plugins\<PackageFolder>\` 下含 `plugin.json`、`<EntryAssembly>.dll`、`lib\`（私有依赖）。清单字段：`id`（无空白稳定 ID）、`entryAssembly`、`entryType`、`apiVersion`（host/plugin 契约主版本门控，`1.0`）、`privateLibraryPath`（默认 `lib`）。
- **注册与身份**：`IFlowPlugin.Metadata.Id` 必须等于清单 `id` 且必须带 `Version`；`Register` 通过 `context.Nodes.Register(...)` 暂存节点注册，宿主在 `Register` 返回后原子调用 `FlowNodeRegistry.RegisterPlugin`。同一扫描内重复插件 ID 被拒绝（前者保留）；重复节点 `TypeKey` 被拒绝（已加载节点不被替换）。
- **TypeKey 稳定**：`.flow.xml` 身份基于 `TypeKey` 而非程序集限定类型名，必须保持稳定并带命名空间前缀（如 `company.sample.nodes.value`）。
- **共享程序集**：`NodeCraft.Flow`、`CommonControls.WPF`、WPF 框架程序集、`Microsoft.Extensions.Logging` 保持默认加载上下文统一类型身份（见 `PluginLoader.CreateSharedAssemblyNames`）。私有依赖在每插件可回收 `AssemblyLoadContext` 中从包根目录与 `lib` 解析。**不要**把 `NodeCraft.Flow.dll` 或 `CommonControls.WPF.dll` 拷入插件包。
- **自定义节点 UI**：通过 `FlowNodeRegistration.ContentFactory` 提供，每个节点实例返回新的 `FrameworkElement`；插件 UI 运行在宿主进程，遵守 WPF UI 线程规则，使用宿主 `DynamicResource` 主题键而非硬编码颜色。
- **slot 语义**：slot = `FlowNodeDefinition` 端口索引（非运行时列表索引）；`flowIn` 恒为输入 slot 0（单入连接，`AllowMultipleConnections=false`）。端口解析用定义端口（`ResolveInputPortId`/定义端口），**不要**用 `InputParameters[i]` 位置索引——运行时端口顺序可能与定义不同（如 `AddNumber` 运行时 `[inputA,inputB,flowIn]` vs 定义 `[flowIn,inputA,inputB]`）。`Connector.Slot`/`IsInput` 携带 slot 信息。
- **连线类型校验**：`FlowTypeValidator.ValidateNodeInput(receivedType, inputType, strict=false)`。相等类型、`*`、`MATCH_TYPE` 通过；逗号分隔联合类型非严格模式取交集、严格模式取子集。`FlowDataType` 键：`string`/`number`/`boolean`/`object`/`control`/`*`/`MATCH_TYPE`；`object` 保留旧通配兼容。`control` 类型支持 If/Else 条件分支，分支由 `flowIn` Active 门控（未被选中的分支下游执行 `Skipped`）。
- **错误与日志**：启动失败经 `PluginStartupNotification` 汇总一次（不含堆栈与异常体）；完整失败经 NLog 写入 `%LocalAppData%\NodeCraft\Logs\nodecraft-${shortdate}.log`，含插件 ID、加载阶段、完整异常（含内部异常）。插件为受信进程内代码，无沙箱/签名校验/热加载/卸载 UX/市场安装流程。

## 主题规则

- 颜色一律使用 `DynamicResource`，**不硬编码 hex**。主题键（`color*`）来自 CommonControls.WPF 包（Light/Dark 两套）。
- 宿主主题在 `App.xaml` 中装配：`CommonControlTheme Theme="Light"` + `pack://application:,,,/CommonControls.WPF;component/Themes/FluentDesign.Defaults.xaml` + `/NodeCraft.Flow;component/Themes/Flow.xaml`。
- 切换主题：`MainWindow`「视图 → 深色主题」菜单修改 `CommonControlTheme.Theme`（Light/Dark）。
- 依赖控件库资源时经 `pack://application:,,,/CommonControls.WPF;component/...` 访问。

## 依赖

- `CommonControls.WPF` 通过 NuGet `PackageReference` 引用（版本 `1.0.0`），本地包位于 `Packages/`。更新方式：在 CommonControls.WPF 仓库 `dotnet pack`，将新 nupkg 覆盖到 `Packages/` 并同步 `nuget.config`。
- 宿主使用 `Microsoft.Extensions.*` + NLog 6；`NodeCraft.Flow` 仅依赖 `Microsoft.Extensions.Logging.Abstractions` 与 `CommonControls.WPF`。
- 更新本地包后需在 Windows 上重新 `dotnet build NodeCraft.sln` 并跑 `NodeCraft.Tests` 确认 ALL PASS。

## 代码规范

- **C# 语言版本 9.0**，无隐式 using，需显式 `using` 语句。
- **库与宿主项目（NodeCraft、NodeCraft.Flow、NodeCraft.Cli）关闭 nullable**（`<Nullable>disable</Nullable>`）；测试项目启用。
- 自定义控件遵循 WPF 标准模式（DefaultStyleKey 元数据 + Generic.xaml 模板），仅在有明确业务需求时覆盖 `Style`/`Background`/`BorderBrush`/`ControlTemplate`。
