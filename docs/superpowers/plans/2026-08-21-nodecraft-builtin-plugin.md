# NodeCraft 内置节点插件化实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 `NodeCraft.Flow` 中 18 个具体内置节点迁入随宿主发布的 `NodeCraft.BuiltIn` 插件；18 个节点都通过插件注册，并各自使用可直接编辑后重新构建的独立 XAML 内容视图。

**Architecture:** `NodeCraft.Flow` 只保留流程引擎、注册表、插件契约和公共画布外壳。`NodeCraft.BuiltIn` 依赖核心并通过 `plugin.json -> PluginLoader -> IFlowPlugin.Register -> FlowNodeRegistry.RegisterPlugin` 原子注册全部节点。宿主构建只负责把最小插件包放进 `Plugins/NodeCraft.BuiltIn`，运行时没有直接注册旁路。视图采用现有 Vision 模式：XAML 从 WPF `Page` 项移除、作为程序集 `EmbeddedResource` 构建，具体视图构造函数通过 `XamlReader.Parse` 加载并转移根内容；code-behind 只负责类型校验、控件查找和交互，不用 C# 创建业务控件树。

**Tech Stack:** C# 9、.NET 8 `net8.0-windows`、WPF、MSBuild、NodeCraft.Flow 插件契约、现有控制台测试跑棒、PowerShell。

**Spec:** `docs/superpowers/specs/2026-08-21-nodecraft-builtin-plugin-design.md`

## Global Constraints

- 不注册任何旧 `node.*` TypeKey，不提供旧 `NodeCraft.Flow.Nodes.*` 模型类型转发、XML 别名或迁移代码。
- 18 个节点都必须有非空 `ContentFactory`、独立 `.xaml` 和独立 `.xaml.cs`；不能把多个节点的业务控件树重新放回共享 C# 工厂。
- 共享代码只允许加载 XAML、查找命名控件、解析数字、解析连接名称、查找 slot、交换输入和通知画布；不得 `new StackPanel`、`new TextBlock`、`new TextBox`、`new Button` 等创建业务 UI。
- 每个 `CreateContent(FlowCanvas, NodeModel)` 都校验具体模型并返回新视图实例；资源、根类型或命名控件错误必须抛出含视图/控件名的 `InvalidOperationException`。
- 编辑控件写回模型后调用 `canvas.NotifyGraphChanged(refreshNodeContents: false)`，避免每次键入都重建正在编辑的 WPF 内容；交换输入调用默认 `NotifyGraphChanged()`，让连接摘要刷新。
- 输入摘要通过发起 `BuildNodeContent` 的注册表解析定义，而不是通过 `NodeExecutorFactory.Registry` 静态全局变量。核心为此给 `FlowCanvas` 增加 public getter/internal setter 的 `NodeRegistry`，并由 `FlowNodeRegistry.BuildNodeContent` 在调用工厂前赋值。
- 注册表仍自动注入 slot 0 的 `FlowPorts.FlowIn`。插件定义不得重复声明该控制端口；连接 UI 必须过滤 `IsControlPort` 后再识别一元/二元数据输入。
- 插件包只能包含 `plugin.json` 和 `NodeCraft.BuiltIn.dll`；不得复制 `NodeCraft.Flow.dll`、`CommonControls.WPF.dll`、Microsoft logging 或 WPF 框架程序集。
- 宿主 staging 只能删除精确的 `Plugins/NodeCraft.BuiltIn` 目录，不能删除整个 `Plugins` 目录或相邻插件包。
- 所有代码步骤遵循 RED → GREEN → REFACTOR：先运行并看到预期失败，再添加最小实现，再运行通过；每个任务完成后单独提交。
- 所有源码编辑和删除使用 `apply_patch`；不要用脚本、重定向或 `dotnet sln add` 代替受控文件编辑。
- 保留用户已有和无关的工作区修改；每次提交只 stage 本任务列出的路径。

---

## File Map

### Core and existing plugins

- `NodeCraft.Flow/Flow/FlowNodeRegistry.cs` — 增加图标元数据，最终移除默认业务内容工厂。
- `NodeCraft.Flow/Flow/FlowCanvas.cs` — 暴露当前内容创建所用的 `NodeRegistry`。
- `NodeCraft.Flow/Flow/NodeExecutorFactory.cs` — 最终只创建空注册表。
- `NodeCraft.Flow/Flow/FlowPorts.cs` — 最终只保留 `FlowIn`。
- `NodeCraft.Flow/Flow/DefaultFlowNodeContentFactory.cs` — 最终删除。
- `NodeCraft.Flow/Flow/Nodes/*.cs` — 18 个节点及其辅助类最终全部删除。
- `NodeCraft.Vision/Plugin/VisionPlugin.cs`、`NodeCraft.Vision/Plugin/StereoCameraRegistration.cs` — 注册自身调色板图标。
- `NodeCraft.Communication/Plugin/CommunicationPlugin.cs` — 注册自身调色板图标。

### New built-in plugin

- `NodeCraft.BuiltIn/NodeCraft.BuiltIn.csproj`, `plugin.json`, `Properties/AssemblyInfo.cs`。
- `NodeCraft.BuiltIn/Plugin/BuiltInPlugin.cs`。
- `NodeCraft.BuiltIn/Registrations/PreviewNodeRegistrations.cs`, `ValueNodeRegistrations.cs`, `MathNodeRegistrations.cs`, `LogicNodeRegistrations.cs`。
- `NodeCraft.BuiltIn/Nodes/BuiltInPortIds.cs`, `BooleanNodePorts.cs`, `NodeValueConverter.cs`。
- `NodeCraft.BuiltIn/Nodes/{StringValue,AppendText,TextPreview,JsonSerialize,IntegerValue,FloatValue,BooleanValue,AddNumber,MultiplyNumber,SubtractNumber,DivideNumber,GreaterThan,LessThan,Equal,BooleanAnd,BooleanOr,BooleanNot,If}{NodeModel,Executor}.cs`。
- `NodeCraft.BuiltIn/Views/BuiltInXamlViewLoader.cs`, `BuiltInInputViewSupport.cs`。
- `NodeCraft.BuiltIn/Views/{StringValueEditor,AppendTextEditor,TextPreviewView,JsonSerializeView,IntegerValueEditor,FloatValueEditor,BooleanValueEditor,AddNumberView,MultiplyNumberView,SubtractNumberView,DivideNumberView,GreaterThanView,LessThanView,EqualView,BooleanAndView,BooleanOrView,BooleanNotView,IfView}.xaml` 及对应 `.xaml.cs`。
- `NodeCraft.BuiltIn/Build/BuiltInPackaging.targets`。

### Tests, consumers, and docs

- `NodeCraft.sln`, `NodeCraft/NodeCraft.csproj`, `NodeCraft.Tests/NodeCraft.Tests.csproj`, `NodeCraft.Tests/Program.cs`。
- 新增 `NodeCraft.Tests/FlowNodeRegistryPresentationTests.cs`, `BuiltInPreviewNodeTests.cs`, `BuiltInValueNodeTests.cs`, `BuiltInMathNodeTests.cs`, `BuiltInLogicNodeTests.cs`, `BuiltInPluginContractTests.cs`, `BuiltInPackagingTests.cs`, `FlowCoreSeparationTests.cs`, `BuiltInTestBootstrap.cs`。
- 更新 `NodeCraft.Tests/JsonSerializeNodeTests.cs`, `DynamicInputPortTests.cs`, `DocumentLifecycleTests.cs`, `CommunicationTests.cs`, `VisionIntegrationTests.cs`。
- 新增 `NodeCraft.PluginSample/Nodes/SamplePortIds.cs`，并更新 sample 的模型、执行器和注册入口。
- 更新 `NodeCraft.Cli/TemplateText.cs`, `NodeCraft.Cli.Tests/TemplateTests.cs`, `NodeCraft.Cli.Tests/GeneratorTests.cs`, `NodeCraft.Cli.Tests/NewCommandTests.cs`。
- 更新 `CLAUDE.md`, `docs/node-plugin-development-guide.md`。

---

### Task 1: 让注册项自描述调色板图标，并给内容工厂传递注册表上下文

**Files:** Modify `NodeCraft.Flow/Flow/FlowNodeRegistry.cs`, `NodeCraft.Flow/Flow/FlowCanvas.cs`, `NodeCraft.Vision/Plugin/VisionPlugin.cs`, `NodeCraft.Vision/Plugin/StereoCameraRegistration.cs`, `NodeCraft.Communication/Plugin/CommunicationPlugin.cs`, `NodeCraft.Tests/Program.cs`, `NodeCraft.Tests/VisionPluginTests.cs`, `NodeCraft.Tests/CommunicationTests.cs`; create `NodeCraft.Tests/FlowNodeRegistryPresentationTests.cs`.

**Interfaces:** `FlowNodeRegistration.PaletteIconKind`, `FlowNodeRegistration.PaletteCategoryIconKind`; `FlowCanvas.NodeRegistry { get; internal set; }`. Generic fallback is `ShapeOutline`.

- [ ] Step 1: Add `RunFlowNodeRegistryPresentationTests()` as the first isolated registry test group in `Program.Main`. Write a compilation-failing test that constructs two registrations in one category and sets the new icon properties. The first registration omits a category icon; the second supplies `FolderOutline`. Assert the category and the first item both fall back to `FolderOutline`, while the second item uses its explicit `StarOutline`.

  Also write a factory-context test:

  ~~~csharp
  var registry = new FlowNodeRegistry();
  var sawRegistry = false;
  var registration = CreatePresentationRegistration("test.presentation.content", "Content");
  registration.ContentFactory = (canvas, node) =>
  {
      sawRegistry = ReferenceEquals(canvas.NodeRegistry, registry);
      return new Border();
  };
  registry.RegisterPlugin("test.presentation", new[] { registration });
  var canvas = new FlowCanvas();
  var node = registration.NodeFactory();
  var content = registry.BuildNodeContent(canvas, node);
  return sawRegistry && content is Border;
  ~~~

  Run:

  ~~~powershell
  dotnet build NodeCraft.Tests/NodeCraft.Tests.csproj
  ~~~

  Expected: CS1061 failures for `PaletteIconKind`, `PaletteCategoryIconKind`, and `FlowCanvas.NodeRegistry`.

- [ ] Step 2: Add both nullable string properties to `FlowNodeRegistration` and `NodeRegistry` to `FlowCanvas`. In `BuildNodeContent`, assign `canvas.NodeRegistry = this` before either the registration-specific factory or the temporary legacy fallback is invoked. Do not remove `DefaultFlowNodeContentFactory` yet.

- [ ] Step 3: Replace `ResolveCategoryIconKind` and `ResolveNodeIconKind` with registration metadata. Precompute category icons from eligible palette registrations before creating items so a later registration can provide the category's first non-empty icon and earlier items still inherit it:

  ~~~csharp
  var categoryIcons = eligibleRegistrations
      .GroupBy(item => ResolveCategoryName(item), StringComparer.OrdinalIgnoreCase)
      .ToDictionary(
          group => group.Key,
          group => group.Select(item => item.PaletteCategoryIconKind)
              .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "ShapeOutline",
          StringComparer.OrdinalIgnoreCase);
  ~~~

  Item icon selection is `registration.PaletteIconKind` when non-empty, otherwise the precomputed category icon. Delete both hard-coded switch methods so core contains no plugin category names or TypeKeys.

- [ ] Step 4: Give every Vision registration `PaletteCategoryIconKind = "CameraOutline"`; use `CameraOutline` for camera/stereo/virtual items and `ImageOutline` for image preview. Give Communication both category and node icon `LanConnect`. Extend existing Vision/Communication assertions to verify these values survive `RegisterPlugin` and palette creation.

- [ ] Step 5: Run the full NodeCraft test runner and inspect the named new tests, then commit:

  ~~~powershell
  dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows
  git diff --check
  git add NodeCraft.Flow/Flow/FlowNodeRegistry.cs NodeCraft.Flow/Flow/FlowCanvas.cs NodeCraft.Vision/Plugin/VisionPlugin.cs NodeCraft.Vision/Plugin/StereoCameraRegistration.cs NodeCraft.Communication/Plugin/CommunicationPlugin.cs NodeCraft.Tests/Program.cs NodeCraft.Tests/FlowNodeRegistryPresentationTests.cs NodeCraft.Tests/VisionPluginTests.cs NodeCraft.Tests/CommunicationTests.cs
  git commit -m "feat: move palette presentation into node registrations"
  ~~~

---

### Task 2: 建立 `NodeCraft.BuiltIn` 并迁移 Preview 分类的四个节点和 XAML

**Files:** Create `NodeCraft.BuiltIn/NodeCraft.BuiltIn.csproj`, `plugin.json`, `Properties/AssemblyInfo.cs`, `Plugin/BuiltInPlugin.cs`, `Registrations/PreviewNodeRegistrations.cs`, `Nodes/BuiltInPortIds.cs`, `Nodes/NodeValueConverter.cs`, the eight `StringValue`/`AppendText`/`TextPreview`/`JsonSerialize` model and executor files, `Views/BuiltInXamlViewLoader.cs`, `Views/BuiltInInputViewSupport.cs`, four Preview XAML/code-behind pairs, `NodeCraft.Tests/BuiltInPreviewNodeTests.cs`; modify `NodeCraft.sln`, `NodeCraft.Tests/NodeCraft.Tests.csproj`, `NodeCraft.Tests/Program.cs`.

**Interfaces:** Plugin ID `nodecraft.builtin`; Preview keys `nodecraft.builtin.string-value`, `.append-text`, `.text-preview`, `.json-serialize`. Shared port IDs are internal to the plugin.

- [ ] Step 1: Patch the solution with project GUID `{C8F6B4D1-1F73-4D7C-A58E-9B2E6F307A41}` and Debug/Release configurations. Add a direct test project reference to `NodeCraft.BuiltIn`. Create the plugin project with `net8.0-windows`, `UseWPF=true`, nullable disabled, C# 9, x64, `AssemblyName`/`RootNamespace` `NodeCraft.BuiltIn`, and a `Private="false"` reference to `NodeCraft.Flow`. Add `InternalsVisibleTo("NodeCraft.Tests")`.

  Use this manifest:

  ~~~json
  {
    "id": "nodecraft.builtin",
    "entryAssembly": "NodeCraft.BuiltIn.dll",
    "entryType": "NodeCraft.BuiltIn.Plugin.BuiltInPlugin",
    "apiVersion": "1.0",
    "privateLibraryPath": "lib"
  }
  ~~~

- [ ] Step 2: Add `RunBuiltInPreviewNodeTestsAsync()` to `Program.Main` before global built-in registration. Write failing contract tests for project/manifest identity, plugin metadata, four new TypeKeys, exact Preview definitions and fresh model/executor factories. Add the four XAML paths to the expected resource list and assert the project text contains matching `<Page Remove=...>` and `<EmbeddedResource Include=...>` entries. Run the test project and observe missing namespace/type compilation failures.

- [ ] Step 3: Copy the four existing model/executor behaviors into `NodeCraft.BuiltIn.Nodes`, replace every old key with the new namespaced key, and replace `BuiltInNodePorts` with internal `BuiltInPortIds`. Define these constants once:

  ~~~csharp
  internal const string Input = "input";
  internal const string InputA = "inputA";
  internal const string InputB = "inputB";
  internal const string Output = "output";
  internal const string Value = "value";
  internal const string Suffix = "suffix";
  internal const string Condition = "condition";
  internal const string True = "true";
  internal const string False = "false";
  ~~~

  Keep current defaults (`ComfyUI`, ` from DemoApp`, empty preview text), port data types, JSON indentation, and cancellation behavior. Do not touch the old core copies yet.

- [ ] Step 4: Implement `BuiltInXamlViewLoader` with `GetManifestResourceStream`, `StreamReader`, and `XamlReader.Parse`. Its API must load a `UserControl`, detach `root.Content`, attach it to the concrete view, and provide `RequireElement<T>(root, viewName, elementName)`. Error messages name the missing `.xaml`, expected `UserControl`, or missing element. It must not instantiate business controls.

  Implement the four view factories in this exact shape:

  ~~~csharp
  internal static FrameworkElement CreateContent(FlowCanvas canvas, NodeModel node)
  {
      if (node is not StringValueNodeModel valueNode)
      {
          throw new InvalidOperationException(
              "StringValueEditor requires a StringValueNodeModel.");
      }

      return new StringValueEditor(canvas, valueNode);
  }
  ~~~

  `StringValueEditor.xaml` owns `ValueEditor`; `AppendTextEditor.xaml` owns `SuffixEditor`; `TextPreviewView.xaml` owns `PreviewText`; `JsonSerializeView.xaml` owns `InputValue`. Wrap each layout in themed `Border` elements using `DynamicResource`; put labels, formula `JSON`, description, margins and placeholder text in XAML. Code-behind uses initialization guards, writes model values, and calls `NotifyGraphChanged(false)` only on real user changes.

- [ ] Step 5: Implement only the unary portion of `BuiltInInputViewSupport` needed by JSON. It reads `canvas.NodeRegistry`, resolves the target registration, filters control inputs, resolves the matching `PortParameter.LinkId`/`GraphLink`, and displays `未连接`, `已连接`, or `source.Name + " · " + output.DisplayName`. Throw a clear exception if the view is attached outside a registry content route or the node does not have exactly one data input.

- [ ] Step 6: Implement `PreviewNodeRegistrations.CreateAll()` and initially have `BuiltInPlugin.Register` stage those four registrations. Each registration sets definition, model/factories, palette text, category icon `ViewDashboardOutline`, explicit item icon, and content factory in one object initializer. Use `FormatText`, `ViewDashboardOutline`, `EyeOutline`, and `ViewDashboardOutline` respectively. Capture the Text Preview definition in its `ExecutionResultHandler`, find the output slot by `BuiltInPortIds.Output`, and never query the global registry.

- [ ] Step 7: Add STA interaction tests. Through a local registry and `registry.BuildNodeContent`:

  - call each factory twice and assert correct, distinct view instances;
  - edit string/suffix controls and assert model changes plus one `GraphChanged` notification;
  - set invalid model type and assert an informative exception;
  - apply a Text Preview execution result, rebuild content, and assert current text/placeholder;
  - build a linked String → JSON graph and assert the unary connection summary;
  - read each XAML source and assert `DynamicResource`, expected named controls, no hard-coded `#` colors; read code-behind and reject `new StackPanel`, `new TextBlock`, `new TextBox`, `new Button`, and `new Border`.

- [ ] Step 8: Run tests and commit:

  ~~~powershell
  dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows
  git diff --check
  git add NodeCraft.BuiltIn NodeCraft.sln NodeCraft.Tests/NodeCraft.Tests.csproj NodeCraft.Tests/Program.cs NodeCraft.Tests/BuiltInPreviewNodeTests.cs
  git commit -m "feat: add built-in Preview plugin nodes with XAML views"
  ~~~

---

### Task 3: 迁移 Value 分类的三个节点和独立 XAML 编辑器

**Files:** Create the six `IntegerValue`/`FloatValue`/`BooleanValue` model and executor files, `Registrations/ValueNodeRegistrations.cs`, `Views/IntegerValueEditor.xaml(.cs)`, `FloatValueEditor.xaml(.cs)`, `BooleanValueEditor.xaml(.cs)`, `NodeCraft.Tests/BuiltInValueNodeTests.cs`; modify `NodeCraft.BuiltIn/NodeCraft.BuiltIn.csproj`, `Plugin/BuiltInPlugin.cs`, `NodeCraft.Tests/Program.cs`.

**Interfaces:** Keys `nodecraft.builtin.integer-value`, `.float-value`, `.boolean-value`; numeric parsing is invariant and preserves the last valid model value on invalid text.

- [ ] Step 1: Add `RunBuiltInValueNodeTests()` and write missing-type tests for the three model defaults, definitions, new keys, fresh factories, explicit icons, and XAML resources. Add STA tests that type `17`, `-2`, `3.5`, `NaN text`, and toggle the boolean editor; assert invalid numeric text neither changes the model nor emits `GraphChanged`. Run and observe compilation failure.

- [ ] Step 2: Copy the three model/executor behaviors into the plugin namespace and change their constants to the new keys. Preserve integer, double and boolean output semantics and the existing model defaults `42`, `3.14`, and `true`.

- [ ] Step 3: Create three themed XAML layouts with named controls `IntegerEditor`, `FloatEditor`, `BooleanEditor`. Put `Integer`, `Float`, `Enabled`, spacing and styles in XAML. In code-behind:

  - initialize integer text with invariant integer formatting and accept only `int.TryParse(..., NumberStyles.Integer, InvariantCulture)`;
  - initialize float text with `F3` invariant formatting and accept only finite values from `double.TryParse(..., NumberStyles.Float, InvariantCulture)`;
  - keep the checkbox content synchronized to `True`/`False`;
  - suppress initialization events and notify with `refreshNodeContents:false` only after valid user changes.

- [ ] Step 4: Add `ValueNodeRegistrations.CreateAll()` with category `Value`, category icon `FormatListNumbered`, node icons `Numeric`, `Numeric`, `ToggleSwitchOutline`, and all three content factories. Append this provider after Preview in `BuiltInPlugin.Register`.

- [ ] Step 5: Run the full test runner and commit:

  ~~~powershell
  dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows
  git diff --check
  git add NodeCraft.BuiltIn/NodeCraft.BuiltIn.csproj NodeCraft.BuiltIn/Plugin/BuiltInPlugin.cs NodeCraft.BuiltIn/Registrations/ValueNodeRegistrations.cs NodeCraft.BuiltIn/Nodes/IntegerValueExecutor.cs NodeCraft.BuiltIn/Nodes/IntegerValueNodeModel.cs NodeCraft.BuiltIn/Nodes/FloatValueExecutor.cs NodeCraft.BuiltIn/Nodes/FloatValueNodeModel.cs NodeCraft.BuiltIn/Nodes/BooleanValueExecutor.cs NodeCraft.BuiltIn/Nodes/BooleanValueNodeModel.cs NodeCraft.BuiltIn/Views/IntegerValueEditor.xaml NodeCraft.BuiltIn/Views/IntegerValueEditor.xaml.cs NodeCraft.BuiltIn/Views/FloatValueEditor.xaml NodeCraft.BuiltIn/Views/FloatValueEditor.xaml.cs NodeCraft.BuiltIn/Views/BooleanValueEditor.xaml NodeCraft.BuiltIn/Views/BooleanValueEditor.xaml.cs NodeCraft.Tests/Program.cs NodeCraft.Tests/BuiltInValueNodeTests.cs
  git commit -m "feat: add built-in Value plugin nodes with XAML editors"
  ~~~

---

### Task 4: 迁移 Math 分类并把连接摘要/交换输入行为接到 XAML 控件

**Files:** Create the eight `AddNumber`/`MultiplyNumber`/`SubtractNumber`/`DivideNumber` model and executor files, `Registrations/MathNodeRegistrations.cs`, four Math XAML/code-behind pairs, `NodeCraft.Tests/BuiltInMathNodeTests.cs`; modify `Views/BuiltInInputViewSupport.cs`, `NodeCraft.BuiltIn.csproj`, `Plugin/BuiltInPlugin.cs`, `NodeCraft.Tests/Program.cs`.

**Interfaces:** Keys `nodecraft.builtin.add-number`, `.multiply-number`, `.subtract-number`, `.divide-number`; binary UI uses non-control `inputA`/`inputB` definitions and swaps target slots plus runtime `LinkId` values.

- [ ] Step 1: Add `RunBuiltInMathNodeTestsAsync()` and write missing-type tests for all four definitions/executors and XAML resource declarations. In an STA graph with two Integer sources and an Add target, assert initial connection summaries include both source/output names, the button says `Swap A/B`, clicking it swaps both `GraphLink.TargetSlot` values and the target model's `inputA`/`inputB` `LinkId`s, and exactly one graph change is raised. Run and observe compilation failure.

- [ ] Step 2: Copy the four model/executor behaviors into the plugin namespace with new keys. Preserve conversion through `NodeValueConverter`, divide-by-zero returning `0d`, port types and cancellation.

- [ ] Step 3: Extend `BuiltInInputViewSupport` with:

  ~~~csharp
  internal static void BindBinary(
      FlowCanvas canvas,
      NodeModel node,
      TextBlock firstValue,
      TextBlock secondValue,
      Button swapButton)
  ~~~

  Resolve exactly two non-control definitions from `canvas.NodeRegistry`. Use their actual definition indices (which include injected `flowIn`) as target slots. Initialize summaries and button label (`Swap A/B`, `Move A -> B`, or `Move B -> A`); disable the button when both inputs are unconnected. On click, re-read current links, swap target slots, update runtime ports by stable Port ID, and call `canvas.NotifyGraphChanged()`.

- [ ] Step 4: Create independent XAML for `AddNumberView`, `MultiplyNumberView`, `SubtractNumberView`, and `DivideNumberView`. Each owns its formula/Chinese description, `InputAValue`, `InputBValue`, and `SwapInputsButton`; all margins, rows, labels, wrapping and theme resources stay in XAML. Each code-behind only loads the resource, requires those three elements, calls `BindBinary`, and exposes a typed fresh-instance factory.

- [ ] Step 5: Add `MathNodeRegistrations.CreateAll()` with category icon `CalculatorVariant`, node icons `Plus`, `Close`, `Minus`, `DivisionBox`, and append it after Value in `BuiltInPlugin.Register`. Assert formulas `A + B`, `A * B`, `A - B`, `A / B` and the divide-by-zero description in UI tests.

- [ ] Step 6: Run execution and UI tests, then commit:

  ~~~powershell
  dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows
  git diff --check
  git add NodeCraft.BuiltIn/NodeCraft.BuiltIn.csproj NodeCraft.BuiltIn/Plugin/BuiltInPlugin.cs NodeCraft.BuiltIn/Registrations/MathNodeRegistrations.cs NodeCraft.BuiltIn/Nodes/AddNumberExecutor.cs NodeCraft.BuiltIn/Nodes/AddNumberNodeModel.cs NodeCraft.BuiltIn/Nodes/MultiplyNumberExecutor.cs NodeCraft.BuiltIn/Nodes/MultiplyNumberNodeModel.cs NodeCraft.BuiltIn/Nodes/SubtractNumberExecutor.cs NodeCraft.BuiltIn/Nodes/SubtractNumberNodeModel.cs NodeCraft.BuiltIn/Nodes/DivideNumberExecutor.cs NodeCraft.BuiltIn/Nodes/DivideNumberNodeModel.cs NodeCraft.BuiltIn/Views/BuiltInInputViewSupport.cs NodeCraft.BuiltIn/Views/AddNumberView.xaml NodeCraft.BuiltIn/Views/AddNumberView.xaml.cs NodeCraft.BuiltIn/Views/MultiplyNumberView.xaml NodeCraft.BuiltIn/Views/MultiplyNumberView.xaml.cs NodeCraft.BuiltIn/Views/SubtractNumberView.xaml NodeCraft.BuiltIn/Views/SubtractNumberView.xaml.cs NodeCraft.BuiltIn/Views/DivideNumberView.xaml NodeCraft.BuiltIn/Views/DivideNumberView.xaml.cs NodeCraft.Tests/Program.cs NodeCraft.Tests/BuiltInMathNodeTests.cs
  git commit -m "feat: add built-in Math plugin nodes with XAML views"
  ~~~

---

### Task 5: 迁移 Logic 分类的七个节点和独立 XAML

**Files:** Create the fourteen `GreaterThan`/`LessThan`/`Equal`/`BooleanAnd`/`BooleanOr`/`BooleanNot`/`If` model and executor files, `Nodes/BooleanNodePorts.cs`, `Registrations/LogicNodeRegistrations.cs`, seven Logic XAML/code-behind pairs, `NodeCraft.Tests/BuiltInLogicNodeTests.cs`; modify `NodeCraft.BuiltIn.csproj`, `Plugin/BuiltInPlugin.cs`, `NodeCraft.Tests/Program.cs`.

**Interfaces:** Keys `nodecraft.builtin.greater-than`, `.less-than`, `.equal`, `.boolean-and`, `.boolean-or`, `.boolean-not`, `.if`; If-only IDs come from plugin-internal constants.

- [ ] Step 1: Add `RunBuiltInLogicNodeTestsAsync()` and write missing-type tests for seven new registrations, definitions, factory freshness and XAML resource declarations. Add UI assertions for all formulas; a binary swap regression; Boolean Not's unary summary; and If's localized True/False labels with non-null themed foregrounds. Run and observe compilation failure.

- [ ] Step 2: Copy all seven models/executors and `BooleanNodePorts` into `NodeCraft.BuiltIn.Nodes`. Replace keys and port constants. `IfNodeModel`/`IfExecutor` must use `BuiltInPortIds.Condition`, `.True`, `.False`; do not leave those IDs in `FlowPorts`. Preserve numeric comparison, object equality, boolean conversion and control-flow output semantics.

- [ ] Step 3: Create five independent binary XAML views (`GreaterThan`, `LessThan`, `Equal`, `BooleanAnd`, `BooleanOr`) with their own formula/description and the three shared named elements consumed by `BindBinary`. Create `BooleanNotView.xaml` with `InputValue` consumed by `BindUnary`.

  Create `IfView.xaml` with `IF`, `TrueLabel`, and `FalseLabel`. Resolve localization in XAML via:

  ~~~xml
  xmlns:l="clr-namespace:NodeCraft.Localization;assembly=NodeCraft.Flow"
  Text="{l:Loc FlowPort_true}"
  Foreground="{DynamicResource colorStatusSuccessForeground1}"
  ~~~

  and the corresponding false/danger resource. No If label or brush is constructed in code-behind.

- [ ] Step 4: Add `LogicNodeRegistrations.CreateAll()` with category and explicit per-item icon `SourceBranch`, then append it after Math. Each registration has its own content factory and the exact existing port definition/type/requiredness.

- [ ] Step 5: Execute representative workflows for `>`, `<`, equality, AND, OR, NOT and both If branches; ensure tests use the new keys and local plugin registry. Run the full runner and commit:

  ~~~powershell
  dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows
  git diff --check
  git add NodeCraft.BuiltIn/NodeCraft.BuiltIn.csproj NodeCraft.BuiltIn/Plugin/BuiltInPlugin.cs NodeCraft.BuiltIn/Registrations/LogicNodeRegistrations.cs NodeCraft.BuiltIn/Nodes/BooleanNodePorts.cs NodeCraft.BuiltIn/Nodes/GreaterThanExecutor.cs NodeCraft.BuiltIn/Nodes/GreaterThanNodeModel.cs NodeCraft.BuiltIn/Nodes/LessThanExecutor.cs NodeCraft.BuiltIn/Nodes/LessThanNodeModel.cs NodeCraft.BuiltIn/Nodes/EqualExecutor.cs NodeCraft.BuiltIn/Nodes/EqualNodeModel.cs NodeCraft.BuiltIn/Nodes/BooleanAndExecutor.cs NodeCraft.BuiltIn/Nodes/BooleanAndNodeModel.cs NodeCraft.BuiltIn/Nodes/BooleanOrExecutor.cs NodeCraft.BuiltIn/Nodes/BooleanOrNodeModel.cs NodeCraft.BuiltIn/Nodes/BooleanNotExecutor.cs NodeCraft.BuiltIn/Nodes/BooleanNotNodeModel.cs NodeCraft.BuiltIn/Nodes/IfExecutor.cs NodeCraft.BuiltIn/Nodes/IfNodeModel.cs NodeCraft.BuiltIn/Views/GreaterThanView.xaml NodeCraft.BuiltIn/Views/GreaterThanView.xaml.cs NodeCraft.BuiltIn/Views/LessThanView.xaml NodeCraft.BuiltIn/Views/LessThanView.xaml.cs NodeCraft.BuiltIn/Views/EqualView.xaml NodeCraft.BuiltIn/Views/EqualView.xaml.cs NodeCraft.BuiltIn/Views/BooleanAndView.xaml NodeCraft.BuiltIn/Views/BooleanAndView.xaml.cs NodeCraft.BuiltIn/Views/BooleanOrView.xaml NodeCraft.BuiltIn/Views/BooleanOrView.xaml.cs NodeCraft.BuiltIn/Views/BooleanNotView.xaml NodeCraft.BuiltIn/Views/BooleanNotView.xaml.cs NodeCraft.BuiltIn/Views/IfView.xaml NodeCraft.BuiltIn/Views/IfView.xaml.cs NodeCraft.Tests/Program.cs NodeCraft.Tests/BuiltInLogicNodeTests.cs
  git commit -m "feat: add built-in Logic plugin nodes with XAML views"
  ~~~

---

### Task 6: 锁定完整的 18 节点注册、执行和 XAML 契约

**Files:** Create `NodeCraft.Tests/BuiltInPluginContractTests.cs`; modify `NodeCraft.Tests/Program.cs` and any `NodeCraft.BuiltIn/Registrations/*.cs`, `Nodes/*.cs`, or `Views/*` files exposed by the contract tests.

**Interfaces:** `BuiltInPlugin.Register` stages exactly 18 registrations in Preview → Value → Math → Logic order; all keys start with `nodecraft.builtin.`; no old key is registered.

- [ ] Step 1: Add `RunBuiltInPluginContractTestsAsync()` before any global bootstrap. Write this exact ordered key assertion:

  ~~~csharp
  var expectedTypeKeys = new[]
  {
      "nodecraft.builtin.string-value",
      "nodecraft.builtin.append-text",
      "nodecraft.builtin.text-preview",
      "nodecraft.builtin.json-serialize",
      "nodecraft.builtin.integer-value",
      "nodecraft.builtin.float-value",
      "nodecraft.builtin.boolean-value",
      "nodecraft.builtin.add-number",
      "nodecraft.builtin.multiply-number",
      "nodecraft.builtin.subtract-number",
      "nodecraft.builtin.divide-number",
      "nodecraft.builtin.greater-than",
      "nodecraft.builtin.less-than",
      "nodecraft.builtin.equal",
      "nodecraft.builtin.boolean-and",
      "nodecraft.builtin.boolean-or",
      "nodecraft.builtin.boolean-not",
      "nodecraft.builtin.if",
  };
  ~~~

  Construct `BuiltInPlugin`, stage through `PluginRegistrationContext`, and assert metadata (`nodecraft.builtin`, `Built-in Nodes`, `1.0.0`), exact order/count, case-insensitive uniqueness, key prefix, non-null model/factories/content factories, and fresh model/executor instances. Assert none of these legacy keys is present: `node.string-value`, `node.add-number`, `node.if`, `node.json-serialize`.

- [ ] Step 2: Add a definition snapshot table and compare every registration before and after `RegisterPlugin`. The table records category, data-input IDs/order/types/requiredness, output IDs/order/types, display names, and icons. After registration, assert slot 0 is exactly one `flowIn` control input and the remaining data ports match the snapshot. This catches accidental duplicated control ports or positional registration drift.

- [ ] Step 3: Add an atomicity test. Clone the staged list, append a registration with a duplicate TypeKey, call `new FlowNodeRegistry().RegisterPlugin(...)`, expect `InvalidOperationException`, and assert the registry contains none of the 18 keys. Do not weaken the existing batch validation to make the test pass.

- [ ] Step 4: In an STA themed window, create every model through its registered `NodeFactory`, build content twice through the registered registry, and assert 36 non-null `FrameworkElement` instances with 18 expected view type names and no reused references. Compare `Assembly.GetManifestResourceNames()` to the exact 18 `.xaml` resource names. Assert every XAML file has a matching code-behind and every registration's `ContentFactory.Method.DeclaringType` is the corresponding view class.

- [ ] Step 5: Add a source-policy test over `NodeCraft.BuiltIn/Views/*.xaml.cs`: allow `new <ConcreteView>`, loader/helper allocations, and event args, but fail on business-control construction (`new StackPanel`, `new Grid`, `new Border`, `new TextBlock`, `new TextBox`, `new CheckBox`, `new Button`, `new RoundButton`). Assert every XAML contains `DynamicResource` and no `#RGB`, `#ARGB`, `#RRGGBB`, or `#AARRGGBB` literal.

- [ ] Step 6: Add executor behavior cases for every family: fixed string/int/double/bool output, append suffix, text preview pass-through, indented JSON, all four arithmetic operations including divide by zero, three comparisons, AND/OR/NOT, and true/false If control output. Use the local plugin registry and `GraphExecutor(workflow, registry)` so tests prove the plugin registration is sufficient.

- [ ] Step 7: Run the runner. Fix only mismatches between the approved spec and the migrated code; do not add compatibility aliases. Commit:

  ~~~powershell
  dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows
  git diff --check
  git add NodeCraft.BuiltIn NodeCraft.Tests/Program.cs NodeCraft.Tests/BuiltInPluginContractTests.cs
  git commit -m "test: lock built-in plugin node contracts"
  ~~~

---

### Task 7: 构建最小插件包，并让普通宿主构建自动 staging

**Files:** Create `NodeCraft.BuiltIn/Build/BuiltInPackaging.targets`, `NodeCraft.Tests/BuiltInPackagingTests.cs`; modify `NodeCraft.BuiltIn/NodeCraft.BuiltIn.csproj`, `NodeCraft/NodeCraft.csproj`, `NodeCraft.Tests/Program.cs` and test staging helpers in `NodeCraft.Tests/Program.cs`.

**Interfaces:** Explicit target `StageBuiltInPlugin`; override property `BuiltInPackageRoot`; host output `$(TargetDir)Plugins\NodeCraft.BuiltIn`; package files are manifest plus entry DLL only.

- [ ] Step 1: Add `RunBuiltInPackagingTestsAsync()` and write static failing assertions that the plugin project imports `Build/BuiltInPackaging.targets`, the target has `Name="StageBuiltInPlugin"` and `DependsOnTargets="Build"`, and `NodeCraft.csproj` has a `ReferenceOutputAssembly="false"` project dependency plus an `AfterTargets="Build"` host staging target. Run and observe the named assertion fail.

- [ ] Step 2: Write a process test that invokes:

  ~~~powershell
  dotnet msbuild NodeCraft.BuiltIn/NodeCraft.BuiltIn.csproj -t:StageBuiltInPlugin -p:Configuration=Release -p:BuiltInPackageRoot=<unique-temp-package>
  ~~~

  Before running, create a sibling plugin directory with a sentinel file. Expected RED: MSBuild reports missing target. The eventual GREEN assertions are exactly two root files (`plugin.json`, `NodeCraft.BuiltIn.dll`), no `lib` content, none of the forbidden shared assemblies anywhere under the package, and the sibling sentinel still present.

- [ ] Step 3: Implement `BuiltInPackaging.targets` with a default package root only when the override is empty. Validate `$(TargetPath)` and source `plugin.json`; remove only `$(BuiltInPackageRoot)`, recreate it, and copy those two files. Do not use wildcards from `$(TargetDir)`.

  The essential target shape is:

  ~~~xml
  <Target Name="StageBuiltInPlugin" DependsOnTargets="Build">
    <Error Condition="!Exists('$(TargetPath)')" Text="Built-in plugin assembly was not built." />
    <Error Condition="!Exists('$(MSBuildProjectDirectory)\plugin.json')" Text="Built-in plugin manifest is missing." />
    <RemoveDir Directories="$(BuiltInPackageRoot)" />
    <MakeDir Directories="$(BuiltInPackageRoot)" />
    <Copy SourceFiles="$(TargetPath);$(MSBuildProjectDirectory)\plugin.json"
          DestinationFolder="$(BuiltInPackageRoot)" />
  </Target>
  ~~~

- [ ] Step 4: In `NodeCraft.csproj`, add a build-order-only project reference:

  ~~~xml
  <ProjectReference Include="..\NodeCraft.BuiltIn\NodeCraft.BuiltIn.csproj"
                    ReferenceOutputAssembly="false"
                    Private="false" />
  ~~~

  Add `StageBuiltInPluginForHost` `AfterTargets="Build"`. It calls the plugin project's explicit target with the current Configuration/TargetFramework and `BuiltInPackageRoot=$(TargetDir)Plugins\NodeCraft.BuiltIn`. Do not add a C# `using`, assembly reference, or direct `BuiltInPlugin.Register` call to the host.

- [ ] Step 5: Extend package tests to build `NodeCraft/NodeCraft.csproj`, assert the ordinary host output contains the exact package, and assert the host root does not receive `NodeCraft.BuiltIn.dll` as a compile/runtime reference. Check a pre-created adjacent plugin sentinel is untouched across a rebuild.

- [ ] Step 6: Add a real-loader test. Copy the built plugin DLL and manifest to a unique temporary `Plugins/NodeCraft.BuiltIn` folder, construct a fresh `FlowNodeRegistry` and real `PluginLoader`, call `LoadAll`, and assert one successful result plus all 18 registrations. In an STA themed window create all 18 nodes and their content through that loaded registry. Drop every node/content/loader/report reference and call the existing load-context cleanup helper in `finally`.

- [ ] Step 7: Run packaging, loader, and full regression tests, then commit:

  ~~~powershell
  dotnet build NodeCraft/NodeCraft.csproj
  dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows
  git diff --check
  git add NodeCraft.BuiltIn/Build/BuiltInPackaging.targets NodeCraft.BuiltIn/NodeCraft.BuiltIn.csproj NodeCraft/NodeCraft.csproj NodeCraft.Tests/Program.cs NodeCraft.Tests/BuiltInPackagingTests.cs
  git commit -m "feat: stage built-in nodes as a host plugin"
  ~~~

---

### Task 8: 让 Sample 插件和 CLI 模板拥有自己的端口常量

**Files:** Create `NodeCraft.PluginSample/Nodes/SamplePortIds.cs`; modify `NodeCraft.PluginSample/Plugin/SamplePlugin.cs`, `Nodes/SampleValueNodeModel.cs`, `SampleValueExecutor.cs`, `SamplePreviewNodeModel.cs`, `SamplePreviewExecutor.cs`, `NodeCraft.Cli/TemplateText.cs`, `NodeCraft.Cli.Tests/TemplateTests.cs`, `GeneratorTests.cs`, `NewCommandTests.cs`, and sample-related fixtures/workflows in `NodeCraft.Tests/Program.cs`.

**Interfaces:** Sample uses internal `SamplePortIds`; generated plugin uses an internal `NodePortIds` declared in its generated node source; neither references `NodeCraft.Flow.Nodes` or `BuiltInNodePorts`.

- [ ] Step 1: Add CLI/sample source-policy tests that scan generated templates and `NodeCraft.PluginSample/**/*.cs` and reject both `using NodeCraft.Flow.Nodes;` and `BuiltInNodePorts`. Run `dotnet run --project NodeCraft.Cli.Tests/NodeCraft.Cli.Tests.csproj`; expected RED is the explicit policy failure.

- [ ] Step 2: Create:

  ~~~csharp
  namespace NodeCraft.PluginSample.Nodes
  {
      internal static class SamplePortIds
      {
          internal const string Input = "input";
          internal const string Output = "output";
          internal const string Value = "value";
      }
  }
  ~~~

  Replace every sample registration/model/executor usage with these constants and remove the old namespace import. Keep the sample's external TypeKeys and package behavior unchanged.

- [ ] Step 3: In `TemplateText.NodeModel`, emit one namespace-level internal `NodePortIds` with `Value` and `Output`. Make plugin entry, generated model and generated executor reference it. Do not add a sixth generated source file, so UI/no-UI generator file-count contracts remain stable. Update template tests to assert the local constants exist once and all old imports are absent.

- [ ] Step 4: Replace `BuiltInNodePorts` in `NodeCraft.Tests/Program.cs` sample workflows and duplicate-plugin fixtures with local test literals/constants. Preserve the test that sample execution works without shipping shared host assemblies.

- [ ] Step 5: Run both test runners and commit:

  ~~~powershell
  dotnet run --project NodeCraft.Cli.Tests/NodeCraft.Cli.Tests.csproj
  dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows
  git diff --check
  git add NodeCraft.PluginSample NodeCraft.Cli/TemplateText.cs NodeCraft.Cli.Tests/TemplateTests.cs NodeCraft.Cli.Tests/GeneratorTests.cs NodeCraft.Cli.Tests/NewCommandTests.cs NodeCraft.Tests/Program.cs
  git commit -m "refactor: make plugin port identifiers self-owned"
  ~~~

---

### Task 9: 把现有测试和新 XML 全部切到插件模型/TypeKey

**Files:** Create `NodeCraft.Tests/BuiltInTestBootstrap.cs`; modify `NodeCraft.Tests/Program.cs`, `JsonSerializeNodeTests.cs`, `DynamicInputPortTests.cs`, `DocumentLifecycleTests.cs`, `CommunicationTests.cs`, `VisionIntegrationTests.cs`, and any additional non-historical test source returned by the old-namespace/old-key search.

**Interfaces:** Tests that need the global registry register `BuiltInPlugin` once through `PluginRegistrationContext` plus `RegisterPlugin`; isolated tests keep local registries. Serialized model names use `NodeCraft.BuiltIn.Nodes.*, NodeCraft.BuiltIn`.

- [ ] Step 1: Add a bootstrap helper without calling it yet:

  ~~~csharp
  private static void RegisterBuiltInPluginForTests()
  {
      if (NodeExecutorFactory.Registry.Contains(StringValueNodeModel.FlowNodeTypeKey))
      {
          return;
      }

      var plugin = new BuiltInPlugin();
      var context = new PluginRegistrationContext(NullLogger.Instance, new Version(1, 0));
      plugin.Register(context);
      NodeExecutorFactory.Registry.RegisterPlugin(plugin.Metadata.Id, context.Registrations);
  }
  ~~~

  It must not call any category registration helper directly. Invoke it in `Main` after isolated core/plugin contract tests and before Algorithm/Vision/Flow tests that use built-in models.

- [ ] Step 2: Change test imports from `NodeCraft.Flow.Nodes` to `NodeCraft.BuiltIn.Nodes`. Replace hard-coded built-in keys with the model constants where practical and otherwise with `nodecraft.builtin.*`. Update `CreateRegistryWithBuiltInStringValue` to stage the one selected registration from a `BuiltInPlugin` context into a local registry, rather than copying a registration out of the global registry.

- [ ] Step 3: Rewrite the V4 graph XML fixture and startup/document fixtures to new model identities and TypeKeys, for example:

  ~~~xml
  ModelType="NodeCraft.BuiltIn.Nodes.IntegerValueNodeModel, NodeCraft.BuiltIn"
  ExecutorType="nodecraft.builtin.integer-value"
  ~~~

  Preserve the generic link reconciliation, save/load, startup file, duplicate ID, graph adapter and execution assertions. Delete only tests whose sole purpose was resolving the old core model assembly; do not add an old-XML migration assertion.

- [ ] Step 4: Update JSON and If workflows to new keys. Update Communication/Vision test source nodes to the plugin namespace. Ensure GraphExecutor instances that use local registries receive that registry explicitly; global-registry tests run only after the bootstrap.

- [ ] Step 5: Run a non-historical source search. At this stage, matches are allowed only inside the still-present `NodeCraft.Flow/Flow/Nodes` and `DefaultFlowNodeContentFactory` sources scheduled for deletion:

  ~~~powershell
  rg -n "NodeCraft\.Flow\.Nodes|BuiltInNodePorts|\"node\.(string-value|integer-value|float-value|boolean-value|append-text|text-preview|json-serialize|add-number|multiply-number|subtract-number|divide-number|greater-than|less-than|equal|boolean-and|boolean-or|boolean-not|if)\"" NodeCraft NodeCraft.Flow NodeCraft.Tests NodeCraft.Vision NodeCraft.Communication NodeCraft.PluginSample NodeCraft.Cli NodeCraft.Cli.Tests -g "*.cs" -g "*.xaml"
  ~~~

- [ ] Step 6: Run the complete solution and tests while old and new implementations temporarily coexist. All active tests must exercise new keys. Commit:

  ~~~powershell
  dotnet build NodeCraft.sln
  dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows
  dotnet run --project NodeCraft.Cli.Tests/NodeCraft.Cli.Tests.csproj
  git diff --check
  git add NodeCraft.Tests
  git commit -m "test: migrate workflows to built-in plugin identities"
  ~~~

---

### Task 10: 删除核心内置节点、默认内容工厂和静态注册路径

**Files:** Create `NodeCraft.Tests/FlowCoreSeparationTests.cs`; modify `NodeCraft.Tests/Program.cs`, `NodeCraft.Flow/Flow/FlowNodeRegistry.cs`, `NodeExecutorFactory.cs`, `FlowPorts.cs`, `FlowCanvas.cs`, `NodeCraft/Pages/FlowPage.xaml.cs`, and comments/usings in core files; delete `NodeCraft.Flow/Flow/DefaultFlowNodeContentFactory.cs` and every file under `NodeCraft.Flow/Flow/Nodes/`.

**Interfaces:** `NodeExecutorFactory.Registry` starts empty; `BuildNodeContent` returns `null` for null arguments, missing registration, or missing factory; core knows no concrete node type.

- [ ] Step 1: Put `RunFlowCoreSeparationTests()` at the very start of `Main`, before `RegisterBuiltInPluginForTests()` and before any code touches `NodeExecutorFactory.Registry`. Write RED assertions:

  - the initial global registry has no palette categories and does not contain `node.string-value` or `nodecraft.builtin.string-value`;
  - a fresh registry with a valid node registration but no `ContentFactory` returns `null`;
  - null canvas, null node, and unknown node return `null`;
  - a supplied factory is called on every request and returns distinct instances;
  - `typeof(FlowPorts)` exposes only public static field `FlowIn`;
  - `DefaultFlowNodeContentFactory.cs`, `BuiltInNodeRegistration.cs`, and `BuiltInNodePorts.cs` do not exist, and `NodeCraft.Flow/Flow/Nodes` has no `.cs` files.

  Run the test runner. Expected RED: initial registry contains old nodes, fallback content is non-null, extra FlowPorts fields exist, and source files exist.

- [ ] Step 2: Change `NodeExecutorFactory` to:

  ~~~csharp
  public static FlowNodeRegistry Registry { get; } = new FlowNodeRegistry();
  ~~~

  Remove the static constructor and `using NodeCraft.Flow.Nodes`.

- [ ] Step 3: Finish `FlowNodeRegistry.BuildNodeContent`:

  ~~~csharp
  public object BuildNodeContent(FlowCanvas canvas, NodeModel node)
  {
      if (canvas == null || node == null
          || !TryResolve(node.ExecutorType, out var registration)
          || registration.ContentFactory == null)
      {
          return null;
      }

      canvas.NodeRegistry = this;
      return registration.ContentFactory(canvas, node);
  }
  ~~~

  Remove `ConditionalWeakTable`, its using, and every reference to `DefaultFlowNodeContentFactory`. Keep the registration-specific factory contract unchanged.

- [ ] Step 4: Delete `DefaultFlowNodeContentFactory.cs` and all concrete core node files with `apply_patch`. This includes 18 model files, 18 executor files, `BuiltInNodeRegistration.cs`, `BuiltInNodePorts.cs`, `BooleanNodePorts.cs`, and `NodeValueConverter.cs`. Remove `Condition`, `True`, and `False` from `FlowPorts`.

- [ ] Step 5: Remove now-unused `NodeCraft.Flow.Nodes` imports from `FlowCanvas.cs`, `FlowPage.xaml.cs`, and any other active source. Rewrite comments in `NodeView.cs`, `FlowSchema.cs`, or serializer/adapter code that name AddNumber/TextPreview as generic examples. Do not alter historical files under `docs/superpowers/specs` or prior plans.

- [ ] Step 6: Prove runtime registration has no bypass. Search active host/core source for `BuiltInPlugin`, `BuiltInNodeRegistration`, direct `RegisterDefaults`, and old TypeKeys. The only host reference to the new project may be MSBuild packaging metadata; C# runtime source must only call the existing `PluginLoader.LoadAll` path.

- [ ] Step 7: Run build and both test runners; then commit the deletion:

  ~~~powershell
  dotnet build NodeCraft.sln
  dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows
  dotnet run --project NodeCraft.Cli.Tests/NodeCraft.Cli.Tests.csproj
  rg -n "DefaultFlowNodeContentFactory|BuiltInNodeRegistration|NodeCraft\.Flow\.Nodes|BuiltInNodePorts" NodeCraft NodeCraft.Flow NodeCraft.Tests NodeCraft.Vision NodeCraft.Communication NodeCraft.PluginSample NodeCraft.Cli NodeCraft.Cli.Tests -g "*.cs" -g "*.xaml"
  git diff --check
  git add NodeCraft.Flow NodeCraft/Pages/FlowPage.xaml.cs NodeCraft.Tests/Program.cs NodeCraft.Tests/FlowCoreSeparationTests.cs
  git commit -m "refactor: remove built-in nodes from flow core"
  ~~~

  Expected search result: no matches. If it finds a non-historical active source, fix it before committing.

---

### Task 11: 更新插件文档并完成全量验证

**Files:** Modify `CLAUDE.md`, `docs/node-plugin-development-guide.md`; modify only implementation/test files required by final verification defects.

**Interfaces:** Documentation says missing `ContentFactory` yields empty node content, port IDs belong to each plugin, built-ins are a normal staged plugin, and icon metadata belongs to registrations.

- [ ] Step 1: Use `rg` to establish RED documentation references. Record the current matches for `BuiltInNodePorts`, `NodeCraft.Flow.Nodes`, “默认节点内容”, and wording that says core auto-registers built-ins.

- [ ] Step 2: Update `CLAUDE.md` architecture:

  - `NodeCraft.Flow` owns no concrete node implementation;
  - `NodeCraft.BuiltIn` is staged with the host but loaded only through `PluginLoader`;
  - all 18 built-in contents are independent embedded XAML resources;
  - old built-in XML/TypeKeys are intentionally unsupported.

- [ ] Step 3: Update `docs/node-plugin-development-guide.md` examples. Add a plugin-local `HelloPortIds` definition and replace all `BuiltInNodePorts` usage. Explain `PaletteIconKind`/`PaletteCategoryIconKind`, generic `ShapeOutline` fallback, and that omitted `ContentFactory` returns `null`/empty business content rather than a core-generated editor. Keep the Vision-style resource loading example accurate and retain the rule that every factory returns a fresh element.

- [ ] Step 4: Run documentation/source searches and require no active old API assumptions:

  ~~~powershell
  rg -n "BuiltInNodePorts|NodeCraft\.Flow\.Nodes|node\.(string-value|integer-value|float-value|boolean-value|append-text|text-preview|json-serialize|add-number|multiply-number|subtract-number|divide-number|greater-than|less-than|equal|boolean-and|boolean-or|boolean-not|if)" CLAUDE.md docs/node-plugin-development-guide.md NodeCraft NodeCraft.Flow NodeCraft.BuiltIn NodeCraft.Tests NodeCraft.PluginSample NodeCraft.Cli NodeCraft.Cli.Tests -g "*.md" -g "*.cs" -g "*.xaml"
  ~~~

  Expected: no matches. Historical design/plan documents are intentionally excluded.

- [ ] Step 5: Commit documentation separately:

  ~~~powershell
  git diff --check
  git add CLAUDE.md docs/node-plugin-development-guide.md
  git commit -m "docs: describe built-in nodes as a plugin"
  ~~~

- [ ] Step 6: Before claiming completion, invoke `superpowers:verification-before-completion` and run fresh verification from the repository root:

  ~~~powershell
  dotnet build NodeCraft.sln
  dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows
  dotnet run --project NodeCraft.Cli.Tests/NodeCraft.Cli.Tests.csproj
  dotnet build NodeCraft/NodeCraft.csproj -c Release
  Test-Path NodeCraft/bin/Release/net8.0-windows/Plugins/NodeCraft.BuiltIn/plugin.json
  Test-Path NodeCraft/bin/Release/net8.0-windows/Plugins/NodeCraft.BuiltIn/NodeCraft.BuiltIn.dll
  git diff --check
  git status --short
  ~~~

  Expected: both builds succeed, both test runners report zero failures, both `Test-Path` calls print `True`, `git diff --check` is silent, and status contains no unintended files.

- [ ] Step 7: Inspect the final diff against the approved spec. Confirm all 18 view pairs exist, package output is minimal, `NodeCraft.Flow/Flow/Nodes` and `DefaultFlowNodeContentFactory` are gone, old identities are absent from active code, and no direct host registration was introduced. Fix any discrepancy, rerun the affected test plus the complete verification sequence, and commit only if a real fix was needed.

---

## Implementation Self-Review Checklist

- [ ] Every production behavior has a preceding observed RED test or source-policy failure.
- [ ] Every task leaves the solution buildable and has one focused commit.
- [ ] All 18 registrations use new namespaced keys and fresh factories.
- [ ] All 18 business layouts live in independent XAML; code-behind contains no business control-tree construction.
- [ ] Input summary/swap logic uses `canvas.NodeRegistry`, stable Port IDs, and injected-control-port-aware slots.
- [ ] Text Preview result handling captures its definition and never queries the global registry.
- [ ] Host staging is build-time only; runtime registration is solely `PluginLoader`.
- [ ] Sample, CLI, tests and docs do not depend on removed built-in port APIs.
- [ ] Core has no concrete nodes, business content fallback, plugin icon switch, or old compatibility path.
- [ ] Fresh full verification evidence is recorded before completion.
