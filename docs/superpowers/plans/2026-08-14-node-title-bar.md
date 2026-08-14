# Node Title Bar Implementation Plan

> For agentic workers: REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Add a full-width NodeView title bar that displays NodeModel.Name and serves as an obvious drag surface, while keeping the existing socket, resize, and stretch-content behavior.

**Architecture:** Extend the existing NodeView template with a title row above the three-column socket/content row. Bind the title directly to the existing NodeModel.Name property and let FlowCanvas continue to own all mouse capture and node movement state. Remove the content-factory title rows so the template owns the only node title and image previews receive the full content area.

**Tech Stack:** C# 9.0, WPF, .NET 8 net8.0-windows, XAML resource dictionaries, the repository self-running STA/WPF test harness.

## Global Constraints

- Preserve input ports on the left and output ports on the right; do not change port order, slot numbers, connector hit testing, or link routing.
- Keep NodeModel, graph XML serialization, and the ContentFactory API unchanged.
- Reuse FlowCanvas.Canvas_PreviewMouseDown and its existing PreDragMode/DragMode state machine; do not add a second drag implementation or capture mouse from the title bar.
- Use existing DynamicResource keys for title-bar background, separator, and foreground values.
- NodeCraft.Flow uses C# language version 9.0 with nullable disabled; test code uses nullable enabled.
- Use the existing NodeCraft.Tests STA/WPF helpers and run tests with dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows.
- Run dotnet build NodeCraft.sln before claiming the change is complete.

## File Map

- Modify NodeCraft.Tests/Program.cs near the existing NodeView template, resize, and content-factory contract tests. This file owns the red-green regression coverage.
- Modify NodeCraft.Flow/Themes/Flow.xaml in the flow:NodeView style. This file owns the title-bar visual tree, title binding, and drag cursor.
- Modify NodeCraft.Flow/Flow/DefaultFlowNodeContentFactory.cs in Build and BuildImagePreview. This file owns ordinary and image-preview content roots; it must stop rendering a second node title.

---

## Acceptance Checklist

- NodeHeader is the only node title owner and binds NodeModel.Name.
- NodeHeader spans the full node width, has a hit-testable surface, and uses Cursor SizeAll.
- Existing FlowCanvas preview mouse handling remains responsible for moving nodes from the title bar.
- Input/output sockets, top alignment, resize, image fill, editors, and plugin content remain unchanged in contract.
- The full Windows test command ends with ALL PASS.
- The solution build succeeds with 0 errors.

---

### Task 1: Add failing title-bar and content-root tests

**Files:**
- Modify: NodeCraft.Tests/Program.cs near the existing NodeView template contract test, resize test, and image preview content test.

**Interfaces:**
- Consumes: FindRepositoryFile, Run, RunOnSta, RunWithThemedWindow, NodeView, NodeModel, DefaultFlowNodeContentFactory, ImagePreviewNodeModel, and StringValueNodeModel.
- Produces: failing tests for the NodeHeader template contract, rendered NodeModel.Name binding, and removal of duplicate content titles.

- [ ] Step 1: Extend the XAML contract test with the title-bar assertions.

Read Flow.xaml with XDocument, select the flow:NodeView style and its ControlTemplate, then find the Border with x:Name NodeHeader and its TextBlock child with x:Name NodeTitle. Add assertions equivalent to:

    var header = template?.Descendants(presentation + "Border")
        .Single(element => (string?)element.Attribute(xaml + "Name") == "NodeHeader");
    var title = header?.Descendants(presentation + "TextBlock")
        .Single(element => (string?)element.Attribute(xaml + "Name") == "NodeTitle");

    return (string?)header?.Attribute("Grid.Row") == "0"
        && (string?)header?.Attribute("Grid.Column") == "0"
        && (string?)header?.Attribute("Grid.ColumnSpan") == "3"
        && (string?)header?.Attribute("Background")
            == "{DynamicResource colorSubtleBackground}"
        && (string?)header?.Attribute("BorderBrush")
            == "{DynamicResource colorNeutralStroke1}"
        && (string?)header?.Attribute("BorderThickness") == "0,0,0,1"
        && (string?)header?.Attribute("Cursor") == "SizeAll"
        && (string?)title?.Attribute("Text")
            == "{Binding NodeModel.Name, RelativeSource={RelativeSource TemplatedParent}}";

Keep the existing assertions for HorizontalContentAlignment, VerticalContentAlignment, top-aligned socket panels, and stretch bindings in the same test.

- [ ] Step 2: Add a failing STA binding test.

Create a NodeView with a NodeModel named Title Test, place it in the existing themed window helper, update layout, find the NodeTitle template part, and require the rendered text to equal the model name:

    Run("NodeView title bar displays the node name", () =>
        RunOnSta(() =>
            RunWithThemedWindow(window =>
            {
                var node = new NodeView
                {
                    NodeModel = new NodeModel { Name = "Title Test" },
                    Content = new System.Windows.Controls.Grid(),
                    Width = 240,
                    Height = 160,
                };
                window.Content = node;
                window.UpdateLayout();

                var title = node.Template?.FindName("NodeTitle", node)
                    as System.Windows.Controls.TextBlock;
                return title != null && title.Text == "Title Test";
            })));

The test must fail because the current template has no NodeTitle part.

- [ ] Step 3: Update the content-factory regression test to require one title owner.

Build named ordinary and image-preview nodes. Keep the existing assertions that ordinary content is a vertical StackPanel and image content is a Grid with a stretch border, then add:

    var ordinaryNode = new StringValueNodeModel { Name = "Ordinary Header" };
    var ordinaryContent = factory.Build(ordinaryNode);
    var imageNode = new ImagePreviewNodeModel { Name = "Image Header" };
    var imageContent = factory.Build(imageNode);
    var ordinaryPanel = ordinaryContent as System.Windows.Controls.StackPanel;
    var imageGrid = imageContent as System.Windows.Controls.Grid;

    return ordinaryPanel != null
        && ordinaryPanel.Children
            .OfType<System.Windows.Controls.TextBlock>()
            .All(text => text.Text != "Ordinary Header")
        && imageGrid != null
        && imageGrid.RowDefinitions.Count >= 1
        && imageGrid.RowDefinitions[0].Height.IsStar
        && !imageGrid.Children
            .OfType<System.Windows.Controls.TextBlock>()
            .Any(text => text.Text == "Image");

The image preview assertion must account for the optional path/status row while requiring the first row to be the star-sized preview row. The combined test must fail against the current internal title rows.

- [ ] Step 4: Run the suite and verify the failure is expected.

Run:

    dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows

Expected red results:

- the title-bar contract test fails because NodeHeader and NodeTitle do not exist;
- the STA title binding test fails because the template exposes no NodeTitle;
- the content-root test fails because the image grid still has a title row and ordinary content still contains the node name.

Existing unrelated tests must keep their prior results.

- [ ] Step 5: Commit only the failing tests.

    git add -- NodeCraft.Tests/Program.cs
    git -c user.name='kevin' -c user.email='kevin@kevinlan.com' commit -m "test: cover node title bar"

### Task 2: Add the full-width draggable title bar to the NodeView template

**Files:**
- Modify: NodeCraft.Flow/Themes/Flow.xaml in the flow:NodeView ControlTemplate.

**Interfaces:**
- Consumes: The failing NodeHeader and NodeTitle tests from Task 1, the existing three-column node grid, and FlowCanvas preview mouse routing.
- Produces: A title row that is visible, bound to NodeModel.Name, hit-testable across the node width, and marked with a move cursor.

- [ ] Step 1: Add the title bar to row 0 and span all columns.

Insert this Border as the first child of the template's outer Grid:

    <Border x:Name="NodeHeader"
            Grid.Row="0"
            Grid.Column="0"
            Grid.ColumnSpan="3"
            MinHeight="28"
            Padding="8,4"
            Background="{DynamicResource colorSubtleBackground}"
            BorderBrush="{DynamicResource colorNeutralStroke1}"
            BorderThickness="0,0,0,1"
            Cursor="SizeAll">
        <TextBlock x:Name="NodeTitle"
                   Text="{Binding NodeModel.Name, RelativeSource={RelativeSource TemplatedParent}}"
                   Foreground="{DynamicResource colorNeutralForeground1}"
                   FontWeight="SemiBold"
                   HorizontalAlignment="Stretch"
                   TextAlignment="Center"
                   TextTrimming="CharacterEllipsis"
                   VerticalAlignment="Center" />
    </Border>

Keep the existing row definitions with the header in the Auto row, socket/content elements in row 1, and ResizeThumb in the final Auto row.

- [ ] Step 2: Run the focused visual-contract tests.

Run:

    dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows

Expected: the title-bar contract and rendered title tests pass; the content-root test remains the only new failure because duplicate content titles have not yet been removed.

- [ ] Step 3: Commit the template change.

    git add -- NodeCraft.Flow/Themes/Flow.xaml
    git -c user.name='kevin' -c user.email='kevin@kevinlan.com' commit -m "feat: add node title bar"

### Task 3: Remove duplicate titles and let image previews use the full content area

**Files:**
- Modify: NodeCraft.Flow/Flow/DefaultFlowNodeContentFactory.cs in Build and BuildImagePreview.

**Interfaces:**
- Consumes: The content-root failure from Task 1 and the NodeView title ownership established in Task 2.
- Produces: Ordinary StackPanel content without a node-name TextBlock and an image-preview Grid whose first row is the fill row.

- [ ] Step 1: Remove the ordinary content title.

Delete the first container child that assigns Text = node.Name. Keep the ordinary root as a vertical StackPanel with its existing stretch alignments, then leave the existing editor, operation, and preview-specific children unchanged.

- [ ] Step 2: Remove the image-preview label row and renumber the remaining rows.

In BuildImagePreview:

- remove the initial Auto RowDefinition;
- remove the TextBlock whose text is Image;
- keep the preview row as a star RowDefinition;
- place the preview Border in Grid row 0;
- when LastImagePath is present, add one Auto row and place the path TextBlock in Grid row 1;
- preserve the existing min dimensions, Stretch.UniformToFill, error/placeholder children, path wrapping, and stretch alignments.

The result must still be a Grid root and must not set fixed Width or Height on the preview Border.

- [ ] Step 3: Run the full suite and verify the content test turns green.

Run:

    dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows

Expected: the new title-bar, title-binding, and content-root tests pass, the output ends with ALL PASS, and no unrelated test fails.

- [ ] Step 4: Commit the content-factory change.

    git add -- NodeCraft.Flow/Flow/DefaultFlowNodeContentFactory.cs
    git -c user.name='kevin' -c user.email='kevin@kevinlan.com' commit -m "fix: move node titles into header"

### Task 4: Final verification and handoff

**Files:**
- Verify: NodeCraft.Tests/Program.cs, NodeCraft.Flow/Themes/Flow.xaml, NodeCraft.Flow/Flow/DefaultFlowNodeContentFactory.cs.

**Interfaces:**
- Consumes: The green implementation from Tasks 1 through 3.
- Produces: A clean, buildable feature branch with the title-bar design and regression coverage.

- [ ] Step 1: Run the complete test command again.

    dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows

Expected: exit code 0 and final output ALL PASS.

- [ ] Step 2: Build the entire solution.

    dotnet build NodeCraft.sln

Expected: Build succeeded with 0 errors. Record any pre-existing warnings separately; do not describe the build as warning-free unless the output reports 0 warnings.

- [ ] Step 3: Check the patch and worktree.

    git diff --check
    git status --short --branch
    git diff --stat e6414b7..HEAD

Expected: diff check has no output, only the intended template/content/test files are changed since the prior layout plan base, and the worktree is clean.

- [ ] Step 4: Review the acceptance checklist.

Confirm from the code and tests that:

- NodeHeader is the only node title owner and binds NodeModel.Name.
- NodeHeader spans the full node width and uses Cursor SizeAll.
- Existing FlowCanvas preview mouse handling remains responsible for moving nodes.
- Input/output sockets, resize, image fill, editors, and plugin content remain unchanged in contract.

- [ ] Step 5: Commit any final test-only adjustment before handoff.

If the final verification requires a test assertion correction, run the full suite again after the correction and commit it with:

    git add -- NodeCraft.Tests/Program.cs
    git -c user.name='kevin' -c user.email='kevin@kevinlan.com' commit -m "test: verify node title bar"

Do not merge, push, or delete the feature worktree until the integration choice is confirmed.
