# Node 外壳 Delete 快捷键 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让选中节点的外壳在拥有键盘焦点时响应 `Delete`，同时保证节点内部编辑控件或其他焦点对象不会触发节点删除。

**Architecture:** `NodeView` 作为节点外壳设置为可聚焦但不参与 Tab 导航。`FlowCanvas` 在节点非交互区域被点击时将焦点交给 `NodeView`，并在模板视口的 `PreviewKeyDown` 路由上检查当前焦点是否正是已选中的 `NodeView`。键盘删除与右键菜单的多选删除共用同一个删除选中节点方法，底层继续使用已有的 `RemoveNode`。

**Tech Stack:** C# 9.0、WPF、`NodeCraft.Flow`、`NodeCraft.Tests` 自运行测试跑棒、.NET 8 Windows。

## Global Constraints

- 只有 `Keyboard.FocusedElement` 直接是已选中的 `NodeView` 时，`Key.Delete` 才能删除节点。
- `TextBox`、密码框、下拉框、按钮、滑块、滚动条、调整手柄等内部交互控件获得焦点时，不删除节点，也不消费 `Delete` 事件。
- 只新增 `Delete` 行为，不新增 `Backspace`、撤销/重做或窗口级删除命令。
- 多选删除必须复用 `RemoveNode`，继续清理相关连线和目标端口的 `LinkId`。
- `NodeView` 不得进入 Tab 导航顺序。
- 必须先观察失败测试，再写生产代码；测试项目使用 Windows STA 自运行模式。
- 不改变现有右键菜单删除、鼠标选择、拖拽、框选和连线交互。

---

## 文件结构与职责

- Modify: `NodeCraft.Flow/Flow/NodeView.cs` — 声明节点外壳的键盘焦点属性。
- Modify: `NodeCraft.Flow/Flow/FlowCanvas.cs` — 管理视口键盘事件、节点外壳聚焦和键盘删除；复用右键菜单的多选删除逻辑。
- Modify: `NodeCraft.Tests/Program.cs` — 增加 WPF 键盘事件辅助方法和外壳/内部控件/多选/无焦点回归测试。
- Create: `docs/superpowers/specs/2026-08-17-node-delete-keyboard-design.md` — 已提交的设计约束，作为实现边界。

## Task 1: 添加外壳焦点和 Delete 的失败测试

**Files:**
- Modify: `NodeCraft.Tests/Program.cs`，在现有 `FlowCanvas` 交互测试附近新增测试，在测试辅助方法附近新增 `RaiseKeyEvent`。

**Interfaces:**
- Consumes: 现有 `RunOnSta`、`RunWithTemplatedFlowCanvas`、`RaiseMouseButtonEvent`、`NodeModel`、`NodeView`。
- Produces: 失败测试固定键盘删除契约；后续生产代码必须让这些测试从 FAIL 变为 PASS。

- [ ] **Step 1: 写出外壳聚焦和单节点删除测试**

在 `Run("FlowCanvas deletes a selected node when its shell has focus", ...)` 中创建节点，使用节点视图的鼠标按下/抬起模拟非交互区域选中，然后发送 `PreviewKeyDown` 的 `Delete`：

```csharp
Run("FlowCanvas deletes a selected node when its shell has focus", () =>
    RunOnSta(() =>
        RunWithTemplatedFlowCanvas((canvas, _, worldCanvas) =>
        {
            var node = new NodeModel
            {
                Name = "Delete me",
                X = 16,
                Y = 16,
            };
            canvas.AddNode(node);
            canvas.UpdateLayout();

            var nodeView = worldCanvas.Children
                .OfType<NodeView>()
                .Single(item => item.NodeModel == node);

            RaiseMouseButtonEvent(nodeView, Mouse.PreviewMouseDownEvent, MouseButton.Left);
            RaiseMouseButtonEvent(nodeView, Mouse.PreviewMouseUpEvent, MouseButton.Left);
            var shellHasFocus = ReferenceEquals(Keyboard.FocusedElement, nodeView);

            var keyEvent = RaiseKeyEvent(
                nodeView,
                Keyboard.PreviewKeyDownEvent,
                Key.Delete);

            return shellHasFocus
                && keyEvent.Handled
                && !canvas.GraphModel.Nodes.Contains(node)
                && !worldCanvas.Children.Contains(nodeView);
        })));
```

- [ ] **Step 2: 添加键盘事件测试辅助方法**

在 `RaiseMouseButtonEvent` 后加入以下辅助方法，保持测试使用实际 WPF 路由事件而不是直接调用生产私有方法：

```csharp
private static KeyEventArgs RaiseKeyEvent(
    UIElement target,
    RoutedEvent routedEvent,
    Key key)
{
    var keyEvent = new KeyEventArgs(
        Keyboard.PrimaryDevice,
        PresentationSource.FromVisual(target)!,
        Environment.TickCount,
        key)
    {
        RoutedEvent = routedEvent,
    };
    target.RaiseEvent(keyEvent);
    return keyEvent;
}
```

- [ ] **Step 3: 运行单个测试场景并确认是预期失败**

运行：

```powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore
```

预期：新增测试失败，原因是节点外壳尚未成为键盘焦点，且 `FlowCanvas` 尚未处理 `Key.Delete`；记录失败名称和实际错误，不把失败归因于编译或测试辅助方法错误。

## Task 2: 实现外壳焦点与单节点删除

**Files:**
- Modify: `NodeCraft.Flow/Flow/NodeView.cs`，在构造函数中设置 `Focusable` 与 `IsTabStop`。
- Modify: `NodeCraft.Flow/Flow/FlowCanvas.cs`，增加视口键盘事件订阅/取消订阅、非交互点击后的外壳聚焦和键盘删除处理。

**Interfaces:**
- Consumes: Task 1 的外壳删除失败测试。
- Produces: `NodeView` 可以成为直接键盘焦点对象；`FlowCanvas` 通过 `Viewport_PreviewKeyDown` 处理严格限定的 `Delete` 行为。

- [ ] **Step 1: 让 NodeView 成为不可 Tab 导航的外壳焦点对象**

在 `NodeView()` 构造函数中加入：

```csharp
public NodeView()
{
    Focusable = true;
    IsTabStop = false;
}
```

- [ ] **Step 2: 注册和注销视口 PreviewKeyDown**

在 `FlowCanvas.OnApplyTemplate` 为 `_viewport` 增加：

```csharp
_viewport.PreviewKeyDown += Viewport_PreviewKeyDown;
```

在 `DetachViewportHandlers` 对应移除：

```csharp
_viewport.PreviewKeyDown -= Viewport_PreviewKeyDown;
```

这样模板重复应用时不会累积键盘处理器。

- [ ] **Step 3: 非交互节点点击时把焦点交给外壳**

在 `Canvas_PreviewMouseDown` 的节点分支中，保留现有 `IsInteractiveNodeContent` 判断：

```csharp
SetSelectedNode(_originalElement.NodeModel);

if (IsInteractiveNodeContent(originalSource))
{
    _startConnector = null;
    _mouseMode = EMouseMode.None;
}
else
{
    _originalElement.Focus();
    // 继续执行现有插座、绘制连线和拖拽模式判断。
}
```

不得在交互内容分支调用 `Focus()`，让 WPF 保持 `TextBox`、`ComboBox` 等控件自身的焦点。

- [ ] **Step 4: 添加严格的 Delete 焦点检查**

在 `FlowCanvas` 中加入：

```csharp
private void Viewport_PreviewKeyDown(object sender, KeyEventArgs e)
{
    if (e.Key != Key.Delete
        || Keyboard.FocusedElement is not NodeView focusedNode
        || !_selectedNodes.Contains(focusedNode))
    {
        return;
    }

    e.Handled = true;
    DeleteSelectedNodes();
}
```

该检查必须使用 `Keyboard.FocusedElement` 的直接对象，不使用“焦点位于节点后代”判断，确保节点内部编辑控件不会触发删除。

- [ ] **Step 5: 抽取键盘与右键菜单共用的多选删除方法**

加入：

```csharp
private void DeleteSelectedNodes()
{
    var nodeIds = _selectedNodes
        .Where(item => item?.NodeModel != null)
        .Select(item => item.NodeModel.Id)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    foreach (var nodeId in nodeIds)
    {
        RemoveNode(nodeId);
    }

    _selectedNodes.Clear();
    SetSelectedNode(null);
    ApplySelectionVisuals();
}
```

在 `DeleteMenu_Click` 的多选分支调用 `DeleteSelectedNodes()`，删除原先重复的循环和清理代码；右键点击未选中的单个节点仍保留现有“删除被右键点击节点”的行为。

- [ ] **Step 6: 运行外壳删除测试确认通过**

运行：

```powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore
```

预期：Task 1 的新增测试 PASS；如果出现测试跑棒中的既有环境失败，单独记录并确认新增测试本身已经通过。

## Task 3: 添加焦点保护与多选回归测试

**Files:**
- Modify: `NodeCraft.Tests/Program.cs`，在现有 FlowCanvas 测试区域增加焦点保护和多选测试。

**Interfaces:**
- Consumes: Task 2 提供的 `NodeView` 外壳焦点和 `FlowCanvas` 键盘删除行为。
- Produces: 回归覆盖内部控件、无外壳焦点、非 Delete 按键、多选和模板重复应用场景。

- [ ] **Step 1: 测试内部 TextBox 获得焦点时不删除**

使用 `canvas.NodeContentFactory` 返回同一个 `TextBox`，先通过 `NodeView` 非交互点击选中节点，再调用 `editor.Focus()`，发送 `Delete`，断言节点保留且事件未被节点删除处理标记：

```csharp
Run("FlowCanvas does not delete a node while its editor has focus", () =>
    RunOnSta(() =>
        RunWithTemplatedFlowCanvas((canvas, _, worldCanvas) =>
        {
            var editor = new TextBox { Text = "keep this node" };
            canvas.NodeContentFactory = _ => editor;
            var node = new NodeModel { Name = "Editable" };
            canvas.AddNode(node);
            canvas.UpdateLayout();

            var nodeView = worldCanvas.Children
                .OfType<NodeView>()
                .Single(item => item.NodeModel == node);
            RaiseMouseButtonEvent(nodeView, Mouse.PreviewMouseDownEvent, MouseButton.Left);
            RaiseMouseButtonEvent(nodeView, Mouse.PreviewMouseUpEvent, MouseButton.Left);

            editor.Focus();
            var keyEvent = RaiseKeyEvent(editor, Keyboard.PreviewKeyDownEvent, Key.Delete);

            return ReferenceEquals(Keyboard.FocusedElement, editor)
                && !keyEvent.Handled
                && canvas.GraphModel.Nodes.Contains(node);
        })));
```

- [ ] **Step 2: 测试无 NodeView 外壳焦点时不删除**

选中节点后调用 `Keyboard.ClearFocus()`，再发送 `Delete`，断言节点仍在模型中。测试必须把事件路由到模板视口或节点视图，以覆盖 `Viewport_PreviewKeyDown` 的“不满足焦点条件直接返回”分支：

```csharp
Keyboard.ClearFocus();
var keyEvent = RaiseKeyEvent(viewport, Keyboard.PreviewKeyDownEvent, Key.Delete);
return !keyEvent.Handled && canvas.GraphModel.Nodes.Contains(node);
```

- [ ] **Step 3: 测试非 Delete 按键不删除**

保持节点外壳拥有焦点，发送 `Key.Back`，断言节点仍存在且事件未被键盘删除逻辑消费。

- [ ] **Step 4: 测试多选删除**

创建两个节点和一条连接，使用测试辅助中的反射只准备真实的 `_selectedNodes` 集合，不调用生产删除方法：

```csharp
private static void SetSelectedNodesForTest(FlowCanvas canvas, params NodeView[] nodes)
{
    var field = typeof(FlowCanvas).GetField(
        "_selectedNodes",
        BindingFlags.Instance | BindingFlags.NonPublic);
    field!.SetValue(canvas, nodes.ToList());
}
```

让第一个已选外壳获得焦点，发送 `Delete`，断言两个节点、关联连接和画布中的两个 `NodeView` 都被删除。测试不直接调用 `DeleteSelectedNodes()`，只通过真实的键盘路由事件验证用户可见结果；该反射辅助只用于构造当前 UI 框选结果，避免合成鼠标位置依赖。

- [ ] **Step 5: 测试模板重复应用不会重复删除**

对同一个 `FlowCanvas` 调用 `ApplyTemplate()` 两次，重新获得节点外壳焦点并发送一次 `Delete`；断言节点只触发一次删除、模型中不存在该节点且没有重复异常。

- [ ] **Step 6: 运行完整 WPF 测试跑棒**

运行：

```powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore
```

预期：新增 Delete 快捷键测试全部 PASS；若仍有环境相关失败，列出测试名称、异常和与本功能的关系。

## Task 4: 构建、差异检查和提交

**Files:**
- Modify: `NodeCraft.Flow/Flow/NodeView.cs`
- Modify: `NodeCraft.Flow/Flow/FlowCanvas.cs`
- Modify: `NodeCraft.Tests/Program.cs`

**Interfaces:**
- Consumes: Task 3 的回归测试结果。
- Produces: 可构建的节点外壳 Delete 快捷键实现和清洁的工作区提交。

- [ ] **Step 1: 检查格式和未预期变更**

运行：

```powershell
git diff --check
git status --short
git diff --stat
```

确认只包含 `NodeView.cs`、`FlowCanvas.cs`、`Program.cs` 以及本计划文件的预期变更，不覆盖用户已有修改。

- [ ] **Step 2: 构建解决方案**

运行：

```powershell
dotnet build NodeCraft.sln --no-restore --verbosity minimal
```

预期：退出码为 0、0 errors；warning 数量如有变化需要记录。

- [ ] **Step 3: 复核需求覆盖**

逐项确认：外壳焦点删除、内部控件不删除、无外壳焦点不删除、仅 `Delete`、多选复用 `RemoveNode`、模板事件不重复订阅、Tab 导航不受影响。

- [ ] **Step 4: 提交实现**

```powershell
git add NodeCraft.Flow/Flow/NodeView.cs NodeCraft.Flow/Flow/FlowCanvas.cs NodeCraft.Tests/Program.cs
git commit -m "feat: delete selected nodes with Delete key"
```

提交前必须重新读取 `git diff --cached --check` 和最近测试/构建退出码；提交后用 `git status --short --branch` 确认工作区状态。
