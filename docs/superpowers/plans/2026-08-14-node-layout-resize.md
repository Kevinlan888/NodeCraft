# Node Layout and Resizable Content Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make NodeView ports top-aligned on the left/right edges and let node content, especially image previews, fill the area created by resizing.

**Architecture:** Keep the existing three-column NodeView template and port/connection model unchanged. Change only the template alignment/content host so custom content receives a stretchable area; select a `Grid` root for image previews while ordinary node content keeps its vertical `StackPanel` root.

**Tech Stack:** C# 9.0, WPF, .NET 8 `net8.0-windows`, XAML resource dictionaries, the repository's self-running STA test harness.

## Global Constraints

- Preserve input ports on the left and output ports on the right; do not move ports to the top/bottom edges.
- Preserve port definition order, slot numbers, connector hit testing, connection routing, and node size serialization.
- `NodeCraft.Flow` uses C# language version 9.0 with nullable disabled; test code uses nullable enabled.
- Use existing `DynamicResource` theme keys for any new themed visual values; do not introduce hard-coded theme colors.
- Do not change the `ContentFactory` API or require plugin content roots to be a specific panel type.
- Use the existing `NodeCraft.Tests` STA/WPF test helpers and run tests on Windows with `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows`.

---

### Task 1: Add failing layout and content regression tests

**Files:**
- Modify: `NodeCraft.Tests/Program.cs` near the existing FlowCanvas/NodeView visual contract tests and the existing `NodeView.IsResizable` test.

**Interfaces:**
- Consumes: `FindRepositoryFile`, `Run`, `RunOnSta`, `RunWithThemedWindow`, `NodeView`, `DefaultFlowNodeContentFactory`, `ImagePreviewNodeModel`, and `StringValueNodeModel`.
- Produces: regression tests named `NodeView template top-aligns sockets and stretches content`, `NodeView content area grows when node is resized`, and `image preview uses a fill Grid while regular content keeps a StackPanel`.

- [ ] **Step 1: Write the failing XAML contract test.**

Add a `Run` test that loads `NodeCraft.Flow/Themes/Flow.xaml`, finds the `Style` whose `TargetType` is `{x:Type flow:NodeView}`, and asserts the following exact attributes:

```csharp
Run("NodeView template top-aligns sockets and stretches content", () =>
{
    var root = XDocument.Load(FindRepositoryFile("NodeCraft.Flow", "Themes", "Flow.xaml"));
    XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    var style = root.Root?
        .Elements(presentation + "Style")
        .Single(element => (string?)element.Attribute("TargetType") == "{x:Type flow:NodeView}");
    var template = style?
        .Descendants(presentation + "ControlTemplate")
        .Single();
    var inputPanel = template?.Descendants(presentation + "StackPanel")
        .Single(element => (string?)element.Attribute(xaml + "Name") == "InputSocketsPanel");
    var outputPanel = template?.Descendants(presentation + "StackPanel")
        .Single(element => (string?)element.Attribute(xaml + "Name") == "OutputSocketsPanel");
    var innerNode = template?.Descendants(presentation + "Grid")
        .Single(element => (string?)element.Attribute(xaml + "Name") == "InnerNode");
    var presenter = innerNode?.Element(presentation + "ContentPresenter");

    return (string?)style?.Attribute("HorizontalContentAlignment") == "Stretch"
        && (string?)style?.Attribute("VerticalContentAlignment") == "Stretch"
        && (string?)inputPanel?.Attribute("VerticalAlignment") == "Top"
        && (string?)outputPanel?.Attribute("VerticalAlignment") == "Top"
        && (string?)innerNode?.Attribute("HorizontalAlignment")
            == "{TemplateBinding HorizontalContentAlignment}"
        && (string?)innerNode?.Attribute("VerticalAlignment")
            == "{TemplateBinding VerticalContentAlignment}"
        && (string?)presenter?.Attribute("HorizontalAlignment")
            == "{TemplateBinding HorizontalContentAlignment}"
        && (string?)presenter?.Attribute("VerticalAlignment")
            == "{TemplateBinding VerticalContentAlignment}";
});
```

- [ ] **Step 2: Write the failing STA resize test.**

Use the existing themed-window helper to host a `NodeView` with a `Grid` content root. Capture the `InnerNode` actual size, change the node from `260x180` to `360x280`, call `UpdateLayout`, and require both dimensions to grow:

```csharp
Run("NodeView content area grows when node is resized", () =>
    RunWithThemedWindow(window =>
    {
        var node = new NodeView
        {
            Width = 260,
            Height = 180,
            Content = new System.Windows.Controls.Grid
            {
                MinWidth = 40,
                MinHeight = 40,
            },
        };
        window.Content = node;
        window.UpdateLayout();

        var inner = node.Template?.FindName("InnerNode", node)
            as System.Windows.Controls.Grid;
        if (inner == null)
        {
            return false;
        }

        var initialWidth = inner.ActualWidth;
        var initialHeight = inner.ActualHeight;
        node.Width = 360;
        node.Height = 280;
        window.UpdateLayout();

        return inner.ActualWidth > initialWidth
            && inner.ActualHeight > initialHeight;
    }));
```

- [ ] **Step 3: Write the failing content-factory test.**

Build an image-preview node and a normal string-value node. Assert that the image content is a root `Grid` whose second row is `*`, its preview `Border` has no fixed `Width`/`Height`, and the normal content remains a vertical `StackPanel`:

```csharp
Run("image preview uses a fill Grid while regular content keeps a StackPanel", () =>
{
    var factory = new DefaultFlowNodeContentFactory(new FlowCanvas());
    var imageContent = factory.Build(new ImagePreviewNodeModel());
    var regularContent = factory.Build(new StringValueNodeModel());
    var imageGrid = imageContent as System.Windows.Controls.Grid;
    var previewBorder = FindLogicalDescendant<System.Windows.Controls.Border>(imageContent);

    return regularContent is System.Windows.Controls.StackPanel regularPanel
        && regularPanel.Orientation == System.Windows.Controls.Orientation.Vertical
        && imageGrid != null
        && imageGrid.RowDefinitions.Count >= 2
        && imageGrid.RowDefinitions[1].Height.IsStar
        && previewBorder != null
        && double.IsNaN(previewBorder.Width)
        && double.IsNaN(previewBorder.Height)
        && previewBorder.HorizontalAlignment == System.Windows.HorizontalAlignment.Stretch
        && previewBorder.VerticalAlignment == System.Windows.VerticalAlignment.Stretch;
});
```

- [ ] **Step 4: Run the tests and verify the failure is caused by the missing behavior.**

Run:

```powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows
```

Expected: the new NodeView contract test fails because the current style uses `Center`, the resize test fails because `InnerNode` remains content-sized, and the image content test fails because `BuildImagePreview` is nested under the outer `StackPanel` and its `Border` has fixed dimensions. Existing unrelated tests must continue to report their prior results.

- [ ] **Step 5: Commit the failing tests.**

```powershell
git add -- NodeCraft.Tests/Program.cs
git -c user.name='kevin' -c user.email='kevin@kevinlan.com' commit -m "test: cover resizable node layout"
```

### Task 2: Make the NodeView template stretch and top-align ports

**Files:**
- Modify: `NodeCraft.Flow/Themes/Flow.xaml:44-95` in the `flow:NodeView` style.

**Interfaces:**
- Consumes: The failing tests from Task 1 and the existing `NodeView` template parts `InputSocketsPanel`, `OutputSocketsPanel`, `InnerNode`, `ContentPresenter`, and `ResizeThumb`.
- Produces: A template in which the content host expands with the node and both side socket lists start at the top.

- [ ] **Step 1: Set the NodeView content alignment defaults to stretch.**

Change the two style setters from `Center` to `Stretch`:

```xml
<Setter Property="HorizontalContentAlignment" Value="Stretch" />
<Setter Property="VerticalContentAlignment" Value="Stretch" />
```

- [ ] **Step 2: Top-align both socket panels without changing their columns.**

Keep `InputSocketsPanel` in column 0 and `OutputSocketsPanel` in column 2, but change each `VerticalAlignment="Center"` to `VerticalAlignment="Top"`. Keep their existing horizontal alignment and margins.

- [ ] **Step 3: Make the content host and presenter use the content alignment bindings.**

Keep `InnerNode` in row 1/column 1 with its existing padding margin. Ensure both the host and presenter have these bindings:

```xml
HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
VerticalAlignment="{TemplateBinding VerticalContentAlignment}"
```

On `ContentPresenter`, also retain the existing content through the standard template bindings (`Content`, `ContentTemplate`, and `ContentStringFormat`) so custom plugin content continues to render.

- [ ] **Step 4: Run the Task 1 tests and verify the template behavior is green.**

Run:

```powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows
```

Expected: the XAML contract and NodeView resize tests pass; the image content-factory test remains the only new failure until Task 3.

- [ ] **Step 5: Commit the template change.**

```powershell
git add -- NodeCraft.Flow/Themes/Flow.xaml
git -c user.name='kevin' -c user.email='kevin@kevinlan.com' commit -m "fix: stretch node content and align sockets"
```

### Task 3: Make image preview content fill its resized node area

**Files:**
- Modify: `NodeCraft.Flow/Flow/DefaultFlowNodeContentFactory.cs:14-129,643-741`.

**Interfaces:**
- Consumes: `ImagePreviewNodeModel`, the existing image error/placeholder branches, and the Task 1 content-factory regression test.
- Produces: `Build(ImagePreviewNodeModel)` returns a root `Grid` with a star-sized image row; all other supported node types retain a vertical `StackPanel` root.

- [ ] **Step 1: Return the image preview before creating the normal StackPanel.**

At the start of `Build(NodeModel node)`, branch image preview nodes directly:

```csharp
if (node is ImagePreviewNodeModel imagePreviewNode)
{
    return BuildImagePreview(imagePreviewNode);
}
```

Remove the old `else if (node is ImagePreviewNodeModel ...)` branch from the normal container population chain. Keep all other node branches unchanged.

- [ ] **Step 2: Keep ordinary content as a vertical, stretchable StackPanel.**

Set the normal container's `Orientation = Orientation.Vertical`, `HorizontalAlignment = HorizontalAlignment.Stretch`, and `VerticalAlignment = VerticalAlignment.Stretch`. Do not change the order or behavior of its existing child builders.

- [ ] **Step 3: Rebuild `BuildImagePreview` around a Grid.**

Create a `Grid` root with stretch alignment and these rows:

```csharp
panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
if (!string.IsNullOrWhiteSpace(node.LastImagePath))
{
    panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
}
```

Place the existing `Image` title in row 0. Create the preview `Border` in row 1 with `MinWidth = 180`, `MinHeight = 120`, `HorizontalAlignment = Stretch`, and `VerticalAlignment = Stretch`; remove its fixed `Width` and `Height`. Keep its existing rounded corners, clipping, background, and error/placeholder child logic.

- [ ] **Step 4: Make the image and optional path use the new grid rows.**

When a bitmap is loaded, create the `Image` with `HorizontalAlignment = Stretch`, `VerticalAlignment = Stretch`, and the existing `Stretch = Stretch.UniformToFill`. Place the optional path `TextBlock` in row 2, remove `MaxWidth = 180`, and keep its wrapping, opacity, font size, and top margin. The no-input and load-failure text blocks remain centered inside the row-1 border.

- [ ] **Step 5: Run the full test harness and verify all new behavior passes.**

Run:

```powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows
```

Expected: the NodeView template, resize, image-preview, existing port, connection, preview, and plugin tests all pass with `ALL PASS` (or the repository's equivalent success summary).

- [ ] **Step 6: Commit the image layout change.**

```powershell
git add -- NodeCraft.Flow/Flow/DefaultFlowNodeContentFactory.cs
git -c user.name='kevin' -c user.email='kevin@kevinlan.com' commit -m "fix: let image previews fill node content"
```

### Task 4: Full verification and handoff

**Files:**
- Verify: `NodeCraft.Flow/Themes/Flow.xaml`, `NodeCraft.Flow/Flow/DefaultFlowNodeContentFactory.cs`, `NodeCraft.Tests/Program.cs`.

**Interfaces:**
- Consumes: The three committed implementation/test changes from Tasks 1–3.
- Produces: A verified working tree with no whitespace errors and passing Windows build/test commands.

- [ ] **Step 1: Run the complete Windows test harness again.**

```powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows
```

Expected: process exit code 0 and the harness reports no failures.

- [ ] **Step 2: Build the complete solution.**

```powershell
dotnet build NodeCraft.sln
```

Expected: process exit code 0 with no compile or XAML resource errors.

- [ ] **Step 3: Check the final diff and working tree.**

```powershell
git diff --check
git status --short --branch
```

Expected: no whitespace errors; only the intended committed changes are present.

- [ ] **Step 4: Report the implementation and verification results.**

Include the changed files, the three user-visible layout behaviors, the exact test/build commands run, and any limitation that visual inspection was performed through STA layout assertions rather than a screenshot capture.
