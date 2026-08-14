# Smooth Connection Lines Implementation Plan

> For agentic workers: REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Replace grid-routed connection polylines with ComfyUI-style two-endpoint cubic Bézier links that render below nodes.

**Architecture:** Keep the existing ConnectionLine.Points endpoint contract. When exactly two points are supplied, ConnectionLine builds one cubic Bézier using the existing output-right/input-left port layout; multi-point input retains the current rounded-polyline fallback. FlowCanvas stops asking OrthogonalRouter for ordinary links and assigns all connection lines a lower Panel.ZIndex, while the graph model and router remain unchanged.

**Tech Stack:** C# 9.0, WPF Shape/StreamGeometry, Canvas/Panel.ZIndex, .NET 8 net8.0-windows, repository self-running STA tests.

## Global Constraints

- Preserve the public ConnectionLine.Points property and existing line hover, context-menu, delete, and connection-ID behavior.
- Use one cubic Bézier for two endpoints; controlDistance = Math.Max(30, distance * 0.25), with the first control point offset right and the second offset left.
- Keep the existing rounded-polyline behavior as a fallback when a caller supplies more than two points.
- Do not delete or redesign OrthogonalRouter; its existing tests remain valid for a future strict obstacle-routing mode.
- Put ordinary and temporary connection lines below node visuals, while preserving line hit testing outside nodes and keeping the temporary line non-hit-testable.
- Do not change graph-link data, port slots, node positions, serialization, plugin APIs, or theme colors.
- Use the repository NodeCraft.Tests STA/WPF harness and verify with dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows and dotnet build NodeCraft.sln.

---

### Task 1: Add failing spline and canvas-layer regression tests

**Files:**
- Modify: NodeCraft.Tests/Program.cs near the existing FlowCanvas/NodeView visual contract tests and near the helper classes at the end of the file.

**Interfaces:**
- Consumes: ConnectionLine, FlowCanvas, GraphModel, GraphLink, IntegerValueNodeModel, AddNumberNodeModel, Run, RunOnSta, and RunWithTemplatedFlowCanvas.
- Produces: executable regression tests for cubic geometry, two-point routing, and node-over-line stacking.

- [ ] **Step 1: Add a test-only subclass that exposes the defining geometry.**

Add this nested helper beside CaptureTestFlowCanvas at the end of Program:

~~~csharp
private sealed class InspectableConnectionLine : ConnectionLine
{
    public System.Windows.Media.Geometry Geometry => DefiningGeometry;
}
~~~

The subclass stays in the test executable so production ConnectionLine does not gain a test-only public geometry API.

- [ ] **Step 2: Write the failing cubic-geometry test.**

Add this test in the existing visual-contract section:

~~~csharp
Run("ConnectionLine uses a cubic spline for two endpoints", () =>
    RunOnSta(() =>
    {
        var line = new InspectableConnectionLine
        {
            Points = new System.Windows.Media.PointCollection
            {
                new System.Windows.Point(24, 40),
                new System.Windows.Point(224, 140),
            },
        };
        var path = System.Windows.Media.PathGeometry.CreateFromGeometry(line.Geometry);

        return path.Figures.Count == 1
            && path.Figures[0].Segments
                .OfType<System.Windows.Media.BezierSegment>()
                .Count() == 1;
    }));
~~~

The current two-point implementation uses PolyLineTo, so this test must fail before the production change.

- [ ] **Step 3: Write the failing FlowCanvas endpoint/layer test.**

Add this test using the existing themed template helper:

~~~csharp
Run("FlowCanvas uses two endpoint points and puts links below nodes", () =>
    RunOnSta(() =>
        RunWithTemplatedFlowCanvas((canvas, _, worldCanvas) =>
        {
            var source = new IntegerValueNodeModel
            {
                Id = "spline-source",
                X = 80,
                Y = 80,
            };
            var target = new AddNumberNodeModel
            {
                Id = "spline-target",
                X = 480,
                Y = 220,
            };
            var graph = new GraphModel
            {
                Nodes = new List<NodeModel> { source, target },
                Links = new List<GraphLink>
                {
                    new GraphLink
                    {
                        Id = "spline-link",
                        OriginNodeId = source.Id,
                        OriginSlot = 0,
                        TargetNodeId = target.Id,
                        TargetSlot = 0,
                    },
                },
            };

            canvas.LoadGraph(graph);
            canvas.Dispatcher.Invoke(
                System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new Action(() => { }));

            var line = worldCanvas.Children
                .OfType<ConnectionLine>()
                .SingleOrDefault();
            var nodeViews = worldCanvas.Children
                .OfType<NodeView>()
                .ToList();

            return line != null
                && line.Points.Count == 2
                && nodeViews.Count == 2
                && System.Windows.Controls.Panel.GetZIndex(line)
                    < nodeViews.Min(System.Windows.Controls.Panel.GetZIndex);
        })));
~~~

The current OrthogonalRouter path normally returns more than two points and current lines share the default z-index with nodes while being appended later, so this test must fail before implementation.

- [ ] **Step 4: Run the focused harness and verify the failures are specific.**

Run:

~~~powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows
~~~

Expected: the two new tests report FAIL; existing tests continue to run and no production code has changed. If the FlowCanvas test throws before its assertion, fix only the test fixture setup (node types, dispatcher drain, or template loading) until it reaches the intended assertion.

- [ ] **Step 5: Commit the failing tests.**

~~~powershell
git add -- NodeCraft.Tests/Program.cs
git -c user.name='kevin' -c user.email='kevin@kevinlan.com' commit -m "test: cover spline connection rendering"
~~~

### Task 2: Make two-point ConnectionLine geometry a cubic Bézier

**Files:**
- Modify: NodeCraft.Flow/Flow/ConnectionLine.cs in DefiningGeometry, BuildRoundedPolyline, and the arrow-head helper area.

**Interfaces:**
- Consumes: the existing Points, Fill, ArrowLength, ArrowWidth, and multi-point rounded-polyline behavior.
- Produces: two-point ConnectionLine geometry containing one cubic Bézier and an arrow whose base follows the curve's end tangent.

- [ ] **Step 1: Add a private control-point calculation with the fixed port directions.**

Add a private helper with this exact behavior:

~~~csharp
private static void GetSplineControlPoints(
    Point start,
    Point end,
    out Point controlStart,
    out Point controlEnd)
{
    var distance = (end - start).Length;
    var offset = Math.Max(30, distance * 0.25);
    controlStart = new Point(start.X + offset, start.Y);
    controlEnd = new Point(end.X - offset, end.Y);
}
~~~

This matches the existing fixed connector layout: output sockets leave to the right and input sockets arrive from the left.

- [ ] **Step 2: Add the cubic path builder.**

Add a helper that begins at start and emits exactly one cubic segment:

~~~csharp
private static void BuildSpline(
    StreamGeometryContext ctx,
    Point start,
    Point end,
    out Point controlEnd)
{
    GetSplineControlPoints(start, end, out var controlStart, out controlEnd);
    ctx.BeginFigure(start, false, false);
    ctx.BezierTo(controlStart, controlEnd, end, true, true);
}
~~~

Use the returned controlEnd as the point that defines the end tangent for the arrow.

- [ ] **Step 3: Branch DefiningGeometry between the spline and the existing fallback.**

Keep Geometry.Empty for fewer than two points. For exactly two points, call BuildSpline; if Fill != null, call BuildArrowHead with controlEnd and the endpoint. For more than two points, call the existing BuildRoundedPolyline; if Fill != null, keep using the last two points for the fallback arrow. Keep geometry.Freeze() and all existing dependency properties unchanged.

- [ ] **Step 4: Run the cubic test and the full harness.**

Run:

~~~powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows
~~~

Expected: ConnectionLine uses a cubic spline for two endpoints passes; the FlowCanvas endpoint/layer test remains failing until Task 3. Existing multi-point/router tests must remain unchanged.

- [ ] **Step 5: Commit the geometry implementation.**

~~~powershell
git add -- NodeCraft.Flow/Flow/ConnectionLine.cs
git -c user.name='kevin' -c user.email='kevin@kevinlan.com' commit -m "fix: render connection lines as cubic splines"
~~~

### Task 3: Route ordinary links with two endpoints and place them below nodes

**Files:**
- Modify: NodeCraft.Flow/Flow/FlowCanvas.cs in DrawingMoved, DrawStarted, DrawFinished, CreateArrowedLine, and Route.

**Interfaces:**
- Consumes: ConnectionLine's two-point spline branch, current socket-position calculation, current graph-link lifecycle, and the existing canvas child collection.
- Produces: all ordinary and temporary FlowCanvas links as two-point curves with explicit low z-index.

- [ ] **Step 1: Simplify the ordinary Route method to return endpoints only.**

Replace its obstacle collection and OrthogonalRouter.Route call with:

~~~csharp
private List<Point> Route(Point start, Point end)
{
    return new List<Point> { start, end };
}
~~~

Leave OrthogonalRouter.cs untouched; its standalone route API and tests remain available for a future strict obstacle-routing mode.

- [ ] **Step 2: Assign a low z-index in CreateArrowedLine.**

After constructing connectionLine and before returning it, add:

~~~csharp
Panel.SetZIndex(connectionLine, -1);
~~~

Keep all existing event handlers, context menu, Tag, stroke/fill, arrow dimensions, and hit-test settings.

- [ ] **Step 3: Assign the same low z-index to the temporary drag line.**

In DrawStarted, after constructing _tempLine and before adding it to _canvas, add:

~~~csharp
Panel.SetZIndex(_tempLine, -1);
~~~

Keep _tempLine.IsHitTestVisible = false, its dashed style, and the existing DrawingMoved route refresh.

- [ ] **Step 4: Run the endpoint/layer test and the full harness.**

Run:

~~~powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows
~~~

Expected: both new spline tests pass, the existing OrthogonalRouter test still passes, and the full harness reports no failures.

- [ ] **Step 5: Commit the FlowCanvas integration.**

~~~powershell
git add -- NodeCraft.Flow/Flow/FlowCanvas.cs
git -c user.name='kevin' -c user.email='kevin@kevinlan.com' commit -m "fix: place spline links below nodes"
~~~

### Task 4: Final verification and handoff

**Files:**
- Verify: NodeCraft.Flow/Flow/ConnectionLine.cs, NodeCraft.Flow/Flow/FlowCanvas.cs, NodeCraft.Tests/Program.cs, and the three commits created above.

**Interfaces:**
- Consumes: the passing tests and production changes from Tasks 1–3.
- Produces: a clean, buildable implementation with documented visual limitations.

- [ ] **Step 1: Run the complete Windows test harness.**

~~~powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows
~~~

Expected: process exit code 0 and no FAIL lines.

- [ ] **Step 2: Build the complete solution.**

~~~powershell
dotnet build NodeCraft.sln
~~~

Expected: process exit code 0 with no C# or XAML errors.

- [ ] **Step 3: Check whitespace and worktree state.**

~~~powershell
git diff --check
git status --short --branch
~~~

Expected: git diff --check is clean. Any pre-existing user changes must remain untouched; the intended implementation files are committed.

- [ ] **Step 4: Report the visual result and limitation.**

Report that ordinary links now use one two-endpoint cubic Bézier, the node layer covers links, and the temporary drag link follows the same geometry. Explicitly note that transparent node backgrounds are not strict geometric obstacle avoidance, matching the approved ComfyUI-style approach.

