using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NLog;
using NLog.Config;
using NLog.Extensions.Logging;
using NLog.Targets;
using NodeCraft.Localization;
using System.Windows.Input;
using NodeCraft.Flow;
using NodeCraft.Flow.Nodes;
using NodeCraft;
using NodeCraft.Pages;
using NodeCraft.Plugins;

internal static partial class Program
{
    private static int _failures;
    private static readonly HashSet<string> DeferredCleanupDirectories
        = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    static Program()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            lock (DeferredCleanupDirectories)
            {
                foreach (var path in DeferredCleanupDirectories.ToArray())
                {
                    try
                    {
                        if (Directory.Exists(path))
                        {
                            Directory.Delete(path, recursive: true);
                        }
                    }
                    catch
                    {
                    }
                }
            }
        };
    }

    private static async Task<int> Main()
    {
        RunVisualContractTests();
        RunStereoCameraProjectTests();
        RunVendorInteropTests();
        RunStereoCameraPackagingTests();
        await RunLatestFrameMailboxTestsAsync();
        RunVendorStereoCameraDeviceTests();
        await RunStereoCameraCaptureTestsAsync();
        await RunStereoCameraPluginTestsAsync();
        await RunFlowImagePreviewTestsAsync();
        await RunGraphExecutionSessionLifecycleTestsAsync();
        await RunGraphExecutionSessionIterationTestsAsync();
        await RunFlowExecutionControllerTestsAsync();

        Run("NodeCraft Flow owns its localization resources", () =>
        {
            var original = LanguageManager.Language;
            try
            {
                LanguageManager.Language = SupportedLanguage.zh_CN;
                var zh = LanguageManager.GetString("FlowNodePalette_Title");
                LanguageManager.Language = SupportedLanguage.en_US;
                var en = LanguageManager.GetString("FlowNodePalette_Title");
                LanguageManager.Language = SupportedLanguage.ko_KR;
                var ko = LanguageManager.GetString("FlowNodePalette_Title");

                return zh == "节点面板"
                    && en == "Node Palette"
                    && ko == "노드 팔레트";
            }
            finally
            {
                LanguageManager.Language = original;
            }
        });

        Run("NodeCraft Flow localization providers refresh", () =>
            RunOnSta(() =>
            {
                var original = LanguageManager.Language;
                try
                {
                    LanguageManager.Language = SupportedLanguage.zh_CN;
                    using var provider = new LocalizationProvider("FlowNodePalette_Add");
                    var changed = false;
                    provider.PropertyChanged += (_, args) =>
                        changed |= args.PropertyName == nameof(LocalizationProvider.Value);

                    LanguageManager.Language = SupportedLanguage.en_US;
                    return changed && provider.Value == "Add";
                }
                finally
                {
                    LanguageManager.Language = original;
                }
            }));

        Run("NodeCraft Flow owns its localization resource catalog", () =>
        {
            var flowOnlyKeys = new[]
            {
                "FlowNodePalette_Title",
                "FlowNodePalette_Description",
                "FlowNodePalette_Add",
                "FlowPort_flowIn",
                "FlowPort_condition",
                "FlowPort_true",
                "FlowPort_false",
                "FlowPort_inputA",
                "FlowPort_inputB",
                "FlowPort_input",
                "FlowPort_output",
                "FlowPort_value",
                "FlowPort_suffix",
                "NodeModel_DefaultName",
            };
            var flowDocument = XDocument.Load(FindRepositoryFile("NodeCraft.Flow", "Themes", "Flow.xaml"));
            var nodeCraftRoot = FindRepositoryFile("NodeCraft.Flow", "NodeCraft.Flow.csproj");
            var nodeCraftResourceFiles = Directory
                .EnumerateFiles(Path.GetDirectoryName(nodeCraftRoot)!, "*.resx", SearchOption.AllDirectories)
                .Where(path => !IsBuildOutputPath(path))
                .ToArray();
            var nodeCraftKeys = nodeCraftResourceFiles
                .SelectMany(path => XDocument.Load(path).Descendants("data"))
                .Select(element => (string?)element.Attribute("name"))
                .Where(name => name != null)
                .ToHashSet(StringComparer.Ordinal);

            return flowDocument.Root?.GetNamespaceOfPrefix("l")?.NamespaceName
                    == "clr-namespace:NodeCraft.Localization"
                && nodeCraftResourceFiles.Length == 3
                && flowOnlyKeys.All(nodeCraftKeys.Contains);
        });

        Run("NodeCraft Flow localization has no CommonControls source references", () =>
        {
            var nodeCraftRoot = Path.GetDirectoryName(FindRepositoryFile("NodeCraft.Flow", "NodeCraft.Flow.csproj"))!;
            var sourceFiles = Directory
                .EnumerateFiles(nodeCraftRoot, "*", SearchOption.AllDirectories)
                .Where(path => !IsBuildOutputPath(path))
                .Where(path => new[] { ".cs", ".xaml" }
                    .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));

            return sourceFiles.All(path => !File.ReadAllText(path)
                .Contains("CommonControls.WPF.Localization", StringComparison.Ordinal));
        });

        Run("palette categories use compact initial state and icon metadata", () =>
        {
            var categories = NodeExecutorFactory.Registry.CreatePaletteCategories();
            var categoryIconProperty = typeof(FlowNodePaletteCategory).GetProperty("IconKind");
            var itemIconProperty = typeof(FlowNodePaletteItem).GetProperty("IconKind");
            var items = categories.SelectMany(category => category.Items).ToList();

            return categories.Count > 1
                && categories[0].IsExpanded
                && categories.Skip(1).All(category => !category.IsExpanded)
                && categoryIconProperty != null
                && itemIconProperty != null
                && categories.All(category => !string.IsNullOrWhiteSpace(categoryIconProperty.GetValue(category) as string))
                && items.All(item => !string.IsNullOrWhiteSpace(itemIconProperty.GetValue(item) as string))
                && items.Any(item => item.Description == "固定字符串输出");
        });

        Run("palette creates nodes through dragging only", () =>
        {
            var source = File.ReadAllText(FindRepositoryFile("NodeCraft.Flow", "Flow", "FlowNodePalette.cs"));
            var theme = File.ReadAllText(FindRepositoryFile("NodeCraft.Flow", "Themes", "Flow.xaml"));
            var document = XDocument.Parse(theme);
            var paletteTemplate = document.Descendants()
                .SingleOrDefault(element => element.Name.LocalName == "DataTemplate"
                    && element.Attributes().Any(attribute => attribute.Name.LocalName == "DataType"
                        && attribute.Value == "{x:Type flow:FlowNodePaletteItem}"));
            var dragSource = paletteTemplate?.Descendants()
                .SingleOrDefault(element => element.Name.LocalName == "DraggableButton");
            var headerToggle = document.Descendants()
                .SingleOrDefault(element => element.Name.LocalName == "ToggleButton"
                    && element.Attributes().Any(attribute => attribute.Name.LocalName == "Name"
                        && attribute.Value == "HeaderToggle"));
            return !source.Contains("MouseDoubleClick", StringComparison.Ordinal)
                && !source.Contains("AddItemRequested", StringComparison.Ordinal)
                && dragSource?.Attributes().Any(attribute => attribute.Name.LocalName == "Tag"
                    && attribute.Value == "{Binding TypeKey}") == true
                && headerToggle?.Attributes().Any(attribute => attribute.Name.LocalName == "HorizontalContentAlignment"
                    && attribute.Value == "Stretch") == true;
        });

        Run("palette theme uses compact icon tiles and tooltips", () =>
        {
            var theme = File.ReadAllText(FindRepositoryFile("NodeCraft.Flow", "Themes", "Flow.xaml"));
            return theme.Contains("Kind=\"{Binding IconKind}\"", StringComparison.Ordinal)
                && theme.Contains("ToolTip=\"{Binding Description}\"", StringComparison.Ordinal)
                && theme.Contains("WrapPanel", StringComparison.Ordinal)
                && !theme.Contains("AddPaletteItem", StringComparison.Ordinal)
                && !theme.Contains("FlowNodePalette_Title", StringComparison.Ordinal)
                && !theme.Contains("FlowNodePalette_Description", StringComparison.Ordinal);
        });

        Run("FlowCanvas template declares a clipped viewport", () =>
        {
            var genericPath = FindRepositoryFile("NodeCraft.Flow", "Themes", "Flow.xaml");
            var document = XDocument.Load(genericPath);
            XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

            var style = document.Root?
                .Elements(presentation + "Style")
                .SingleOrDefault(element => (string?)element.Attribute("TargetType") == "{x:Type flow:FlowCanvas}");
            var template = style?
                .Descendants(presentation + "ControlTemplate")
                .SingleOrDefault();
            var border = template?.Elements(presentation + "Border").SingleOrDefault();
            var decorator = border?.Elements(presentation + "AdornerDecorator").SingleOrDefault();
            var viewport = decorator?
                .Elements(presentation + "Grid")
                .SingleOrDefault(element => (string?)element.Attribute(xaml + "Name") == "CanvasViewport");
            var worldCanvas = viewport?
                .Elements(presentation + "Canvas")
                .SingleOrDefault(element => (string?)element.Attribute(xaml + "Name") == "CanFlow");

            return (string?)border?.Attribute("ClipToBounds") == "True"
                && (string?)viewport?.Attribute("ClipToBounds") == "True"
                && (string?)viewport?.Attribute("Background") == "Transparent"
                && (string?)viewport?.Attribute("AllowDrop") == "True"
                && (string?)worldCanvas?.Attribute("Width") == "10000"
                && (string?)worldCanvas?.Attribute("Height") == "10000"
                && (string?)worldCanvas?.Attribute("HorizontalAlignment") == "Left"
                && (string?)worldCanvas?.Attribute("VerticalAlignment") == "Top";
        });

        Run("FlowCanvas starts at the default zoom", () =>
            RunOnSta(() =>
            {
                var canvas = new FlowCanvas();
                return canvas.Zoom == 1.0;
            }));

        Run("FlowCanvas applies the themed viewport template and handles routed wheel zoom", () =>
            RunOnSta(() =>
            {
                var window = new System.Windows.Window
                {
                    Width = 640,
                    Height = 480,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    WindowStyle = System.Windows.WindowStyle.None,
                };
                window.Resources.MergedDictionaries.Add(new CommonControls.WPF.CommonControlTheme
                {
                    Theme = CommonControls.WPF.CommonControlTheme.BaseTheme.Light,
                });
                window.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/CommonControls.WPF;component/Themes/FluentDesign.Defaults.xaml",
                        UriKind.Absolute),
                });
                window.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/NodeCraft.Flow;component/Themes/Flow.xaml",
                        UriKind.Absolute),
                });

                var canvas = new FlowCanvas
                {
                    Width = 400,
                    Height = 300,
                };
                window.Content = canvas;

                try
                {
                    window.Show();
                    canvas.ApplyTemplate();
                    window.UpdateLayout();

                    var template = canvas.Template;
                    var viewport = template?.FindName("CanvasViewport", canvas)
                        as System.Windows.Controls.Grid;
                    var worldCanvas = template?.FindName("CanFlow", canvas)
                        as System.Windows.Controls.Canvas;
                    if (viewport == null
                        || worldCanvas == null
                        || !ReferenceEquals(
                            System.Windows.Media.VisualTreeHelper.GetParent(worldCanvas),
                            viewport))
                    {
                        return false;
                    }

                    var wheel = new System.Windows.Input.MouseWheelEventArgs(
                        System.Windows.Input.Mouse.PrimaryDevice,
                        Environment.TickCount,
                        120)
                    {
                        RoutedEvent = System.Windows.Input.Mouse.PreviewMouseWheelEvent,
                    };
                    viewport.RaiseEvent(wheel);
                    window.UpdateLayout();

                    var middleDown = new System.Windows.Input.MouseButtonEventArgs(
                        System.Windows.Input.Mouse.PrimaryDevice,
                        Environment.TickCount,
                        System.Windows.Input.MouseButton.Middle)
                    {
                        RoutedEvent = System.Windows.Input.Mouse.PreviewMouseDownEvent,
                    };
                    viewport.RaiseEvent(middleDown);

                    var middleUp = new System.Windows.Input.MouseButtonEventArgs(
                        System.Windows.Input.Mouse.PrimaryDevice,
                        Environment.TickCount,
                        System.Windows.Input.MouseButton.Middle)
                    {
                        RoutedEvent = System.Windows.Input.Mouse.PreviewMouseUpEvent,
                    };
                    viewport.RaiseEvent(middleUp);

                    var transform = worldCanvas.RenderTransform as System.Windows.Media.MatrixTransform;
                    return viewport.ClipToBounds
                        && viewport.AllowDrop
                        && viewport.Background is System.Windows.Media.SolidColorBrush viewportBackground
                        && viewportBackground.Color.A == 0
                        && viewport.ActualWidth == 400
                        && viewport.ActualHeight == 300
                        && worldCanvas.AllowDrop
                        && worldCanvas.Width == 10000
                        && worldCanvas.Height == 10000
                        && worldCanvas.ActualWidth == 10000
                        && worldCanvas.ActualHeight == 10000
                        && worldCanvas.HorizontalAlignment == System.Windows.HorizontalAlignment.Left
                        && worldCanvas.VerticalAlignment == System.Windows.VerticalAlignment.Top
                        && wheel.Handled
                        && middleDown.Handled
                        && middleUp.Handled
                        && Math.Abs(canvas.Zoom - 1.1) < 0.000001
                        && transform != null
                        && Math.Abs(transform.Matrix.M11 - canvas.Zoom) < 0.000001
                        && Math.Abs(transform.Matrix.M22 - canvas.Zoom) < 0.000001;
                }
                finally
                {
                    window.Close();
                }
            }));

        Run("FlowCanvas converts transformed socket endpoints back to world coordinates once", () =>
        {
            var transform = new FlowCanvasViewportTransform();
            transform.SetZoom(2);
            transform.PanBy(new System.Windows.Vector(30, -10));

            var worldSocket = new System.Windows.Point(120, 80);
            var viewportOrigin = new System.Windows.Point(7, 11);
            var socketInFlowCanvas = new System.Windows.Point(277, 161);

            return FlowCanvas.ConvertSocketPositionToWorld(
                socketInFlowCanvas,
                viewportOrigin,
                transform) == worldSocket;
        });

        Run("FlowCanvas drag activation uses viewport pixel distances", () =>
            !FlowCanvas.HasExceededViewportDragThreshold(
                new System.Windows.Point(100, 100),
                new System.Windows.Point(103.9, 103.9),
                4,
                4)
            && FlowCanvas.HasExceededViewportDragThreshold(
                new System.Windows.Point(100, 100),
                new System.Windows.Point(104.1, 100),
                4,
                4));

        Run("FlowCanvas scales world drag offsets for the adorner preview", () =>
            FlowCanvas.ToViewportDragOffset(new System.Windows.Vector(16, -8), 2)
                == new System.Windows.Vector(32, -16));

        Run("FlowCanvas starts selection only on viewport or world canvas", () =>
            RunOnSta(() =>
            {
                var viewport = new System.Windows.Controls.Grid();
                var worldCanvas = new System.Windows.Controls.Canvas();
                var connection = new ConnectionLine();
                return FlowCanvas.IsBlankCanvasTarget(viewport, viewport, worldCanvas)
                    && FlowCanvas.IsBlankCanvasTarget(worldCanvas, viewport, worldCanvas)
                    && !FlowCanvas.IsBlankCanvasTarget(connection, viewport, worldCanvas);
            }));

        Run("FlowCanvas continues panning only while middle capture is held", () =>
            RunOnSta(() =>
            {
                var viewport = new System.Windows.Controls.Grid();
                var other = new System.Windows.Controls.Grid();
                return FlowCanvas.CanContinuePanning(
                        System.Windows.Input.MouseButtonState.Pressed,
                        viewport,
                        viewport)
                    && !FlowCanvas.CanContinuePanning(
                        System.Windows.Input.MouseButtonState.Released,
                        viewport,
                        viewport)
                    && !FlowCanvas.CanContinuePanning(
                        System.Windows.Input.MouseButtonState.Pressed,
                        other,
                        viewport);
            }));

        Run("FlowCanvas blocks left selection while middle-button panning", () =>
            RunOnSta(() =>
                RunWithTemplatedFlowCanvas((_, viewport, worldCanvas) =>
                {
                    var middleDown = RaiseMouseButtonEvent(
                        viewport,
                        System.Windows.Input.Mouse.PreviewMouseDownEvent,
                        MouseButton.Middle);
                    var leftDown = RaiseMouseButtonEvent(
                        viewport,
                        System.Windows.Input.Mouse.PreviewMouseDownEvent,
                        MouseButton.Left);
                    var leftWasBlocked = leftDown.Handled
                        && !worldCanvas.Children.OfType<System.Windows.Shapes.Rectangle>().Any();

                    RaiseMouseButtonEvent(
                        viewport,
                        System.Windows.Input.Mouse.PreviewMouseUpEvent,
                        MouseButton.Middle);
                    return middleDown.Handled
                        && leftWasBlocked
                        && !worldCanvas.Children.OfType<System.Windows.Shapes.Rectangle>().Any();
                })));

        Run("FlowCanvas middle press cancels an active left selection", () =>
            RunOnSta(() =>
                RunWithTemplatedFlowCanvas((_, viewport, worldCanvas) =>
                {
                    var leftDown = RaiseMouseButtonEvent(
                        viewport,
                        System.Windows.Input.Mouse.PreviewMouseDownEvent,
                        MouseButton.Left);
                    var selectionStarted = !leftDown.Handled
                        && worldCanvas.Children.OfType<System.Windows.Shapes.Rectangle>().Count() == 1;

                    var middleDown = RaiseMouseButtonEvent(
                        viewport,
                        System.Windows.Input.Mouse.PreviewMouseDownEvent,
                        MouseButton.Middle);
                    var selectionWasCancelled = middleDown.Handled
                        && !worldCanvas.Children.OfType<System.Windows.Shapes.Rectangle>().Any();

                    RaiseMouseButtonEvent(
                        viewport,
                        System.Windows.Input.Mouse.PreviewMouseUpEvent,
                        MouseButton.Middle);
                    return selectionStarted && selectionWasCancelled;
                })));

        Run("FlowCanvas capture loss cancels an active left selection", () =>
            RunOnSta(() =>
                RunWithTemplatedFlowCanvas((_, viewport, worldCanvas) =>
                {
                    RaiseMouseButtonEvent(
                        viewport,
                        System.Windows.Input.Mouse.PreviewMouseDownEvent,
                        MouseButton.Left);
                    var selectionStarted = worldCanvas.Children.OfType<System.Windows.Shapes.Rectangle>().Count() == 1;

                    var lostCapture = new System.Windows.Input.MouseEventArgs(
                        System.Windows.Input.Mouse.PrimaryDevice,
                        Environment.TickCount)
                    {
                        RoutedEvent = System.Windows.Input.Mouse.LostMouseCaptureEvent,
                    };
                    viewport.RaiseEvent(lostCapture);

                    return selectionStarted
                        && !worldCanvas.Children.OfType<System.Windows.Shapes.Rectangle>().Any();
                })));

        Run("FlowCanvas clamps panned drops to snapped world bounds including node size", () =>
        {
            var lowTransform = new FlowCanvasViewportTransform();
            lowTransform.PanBy(new System.Windows.Vector(100, 100));
            var low = FlowCanvas.ClampDropPositionToWorld(
                lowTransform.ToWorld(new System.Windows.Point(50, 50)),
                new System.Windows.Size(180, 72),
                16);

            var highTransform = new FlowCanvasViewportTransform();
            highTransform.PanBy(new System.Windows.Vector(-10000, -10000));
            var high = FlowCanvas.ClampDropPositionToWorld(
                highTransform.ToWorld(new System.Windows.Point(100, 100)),
                new System.Windows.Size(180, 72),
                16);
            var invalidCellSize = FlowCanvas.ClampDropPositionToWorld(
                new System.Windows.Point(-1, 10050),
                new System.Windows.Size(0, 0),
                0);
            var nonFiniteCellSize = FlowCanvas.ClampDropPositionToWorld(
                new System.Windows.Point(10050, -1),
                new System.Windows.Size(0, 0),
                double.NaN);

            return low == new System.Windows.Point(0, 0)
                && high == new System.Windows.Point(9808, 9920)
                && high.X + 180 <= 10000
                && high.Y + 72 <= 10000
                && invalidCellSize == new System.Windows.Point(0, 10000)
                && nonFiniteCellSize == new System.Windows.Point(10000, 0);
        });

        Run("FlowCanvas keeps node positions stable when CellSize is invalid", () =>
            RunOnSta(() =>
                RunWithTemplatedFlowCanvas((canvas, _, _) =>
                {
                    canvas.CellSize = 0;
                    var node = new NodeModel { X = 123.4, Y = 87.6 };
                    canvas.AddNode(node);
                    return node.X == 123.4 && node.Y == 87.6;
                })));

        Run("FlowCanvas thins secondary grid lines at minimum zoom and retains majors", () =>
            FlowCanvas.GetGridLineStride(16, FlowCanvasViewportTransform.MinZoom) == 4
            && FlowCanvas.GetGridLineStride(16, 0.5) == 1
            && FlowCanvas.IsMajorGridLine(0)
            && FlowCanvas.IsMajorGridLine(4)
            && !FlowCanvas.IsMajorGridLine(1));

        Run("FlowCanvas wheel zoom factor honors Delta magnitude", () =>
        {
            var oneDetent = FlowCanvas.GetWheelZoomFactor(120);
            var twoDetents = FlowCanvas.GetWheelZoomFactor(240);
            return Math.Abs(oneDetent - 1.1) < 0.000001
                && Math.Abs(twoDetents - 1.21) < 0.000001
                && twoDetents > oneDetent;
        });

        Run("viewport transform maps world and viewport coordinates", () =>
        {
            var transform = new FlowCanvasViewportTransform();
            transform.SetZoom(2);
            transform.PanBy(new System.Windows.Vector(30, -10));

            var world = new System.Windows.Point(12, 25);
            var viewport = transform.ToViewport(world);
            var restored = transform.ToWorld(viewport);

            return viewport == new System.Windows.Point(54, 40)
                && restored == world;
        });

        Run("viewport transform keeps the cursor anchored while zooming", () =>
        {
            var transform = new FlowCanvasViewportTransform();
            transform.SetZoom(1);
            transform.PanBy(new System.Windows.Vector(20, 15));

            var cursor = new System.Windows.Point(180, 120);
            var worldUnderCursor = transform.ToWorld(cursor);
            transform.ZoomAt(cursor, 2);

            return transform.Zoom == 2
                && transform.ToViewport(worldUnderCursor) == cursor;
        });

        Run("viewport transform clamps zoom and keeps pan independent", () =>
        {
            var transform = new FlowCanvasViewportTransform();
            transform.SetZoom(0.01);
            var lowerClamped = transform.Zoom == FlowCanvasViewportTransform.MinZoom;
            transform.SetZoom(99);
            var upperClamped = transform.Zoom == FlowCanvasViewportTransform.MaxZoom;

            var node = new NodeModel { X = 120, Y = 80 };
            transform.PanBy(new System.Windows.Vector(-40, 25));
            return lowerClamped
                && upperClamped
                && node.X == 120
                && node.Y == 80;
        });

        Run("viewport transform rejects non-finite zoom values", () =>
        {
            var transform = new FlowCanvasViewportTransform();
            transform.SetZoom(1.5);
            transform.SetZoom(double.NaN);
            var nanRejected = transform.Zoom == 1.5;
            transform.SetZoom(double.PositiveInfinity);
            var positiveInfinityClamped = transform.Zoom == 2.0;
            transform.SetZoom(double.NegativeInfinity);
            var negativeInfinityClamped = transform.Zoom == 0.25;

            return nanRejected && positiveInfinityClamped && negativeInfinityClamped;
        });

        Run("viewport transform ignores non-positive and non-finite zoom factors", () =>
        {
            var transform = new FlowCanvasViewportTransform();
            transform.PanBy(new System.Windows.Vector(20, 15));
            var cursor = new System.Windows.Point(180, 120);
            var initialZoom = transform.Zoom;
            var initialPan = transform.PanOffset;

            transform.ZoomAt(cursor, 0);
            var zeroNoOp = transform.Zoom == initialZoom && transform.PanOffset == initialPan;
            transform.ZoomAt(cursor, -1);
            var negativeNoOp = transform.Zoom == initialZoom && transform.PanOffset == initialPan;
            transform.ZoomAt(cursor, double.NaN);
            var nanNoOp = transform.Zoom == initialZoom && transform.PanOffset == initialPan;
            transform.ZoomAt(cursor, double.PositiveInfinity);
            var positiveInfinityNoOp = transform.Zoom == initialZoom && transform.PanOffset == initialPan;
            transform.ZoomAt(cursor, double.NegativeInfinity);
            var negativeInfinityNoOp = transform.Zoom == initialZoom && transform.PanOffset == initialPan;

            return zeroNoOp && negativeNoOp && nanNoOp
                && positiveInfinityNoOp && negativeInfinityNoOp;
        });

        Run("viewport transform preserves a clamped zoom cursor anchor", () =>
        {
            var transform = new FlowCanvasViewportTransform();
            transform.PanBy(new System.Windows.Vector(20, 15));
            var cursor = new System.Windows.Point(180, 120);
            var worldUnderCursor = transform.ToWorld(cursor);
            transform.ZoomAt(cursor, 99);

            return transform.Zoom == FlowCanvasViewportTransform.MaxZoom
                && transform.ToViewport(worldUnderCursor) == cursor;
        });

        Run("NodeCraft ignores empty startup args", () =>
            StartupGraphPathResolver.TryResolve(Array.Empty<string>()) == null);
        Run("NodeCraft accepts a flow XML argument", () =>
            StartupGraphPathResolver.TryResolve(new[] { "sample.flow.xml" }) == "sample.flow.xml");
        Run("NodeCraft ignores non-flow arguments", () =>
            StartupGraphPathResolver.TryResolve(new[] { "sample.xml" }) == null);
        Run("NodeCraft starts with an empty unsaved canvas", () =>
            RunOnSta(() =>
            {
                var page = new FlowPage(NullLoggerFactory.Instance);
                page.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.FrameworkElement.LoadedEvent));
                var canvas = GetNodeCanvas(page);
                return canvas.GraphModel?.Nodes?.Count == 0
                    && GetCurrentFilePath(page).Text == "当前文件: 未保存";
            }));
        Run("NodeCraft preserves a loaded startup graph after FlowPage Loaded", () =>
            RunOnSta(() =>
            {
                var path = Path.Combine(Path.GetTempPath(), "nodecraft-startup-" + Guid.NewGuid().ToString("N") + ".flow.xml");
                try
                {
                    GraphModelXmlSerializer.Save(CreateStartupGraph(), path);

                    var page = new FlowPage(NullLoggerFactory.Instance);
                    if (!page.TryLoadGraphFile(path))
                    {
                        return false;
                    }

                    var loadedHandler = typeof(FlowPage).GetMethod(
                        "FlowPage_Loaded",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    if (loadedHandler == null)
                    {
                        return false;
                    }

                    loadedHandler.Invoke(page, new object[] { page, new System.Windows.RoutedEventArgs() });
                    var canvas = GetNodeCanvas(page);
                    return canvas.GraphModel?.Nodes?.Count == 1
                        && canvas.GraphModel.Nodes[0].Id == "loaded-startup";
                }
                finally
                {
                    File.Delete(path);
                }
            }));
        Run("NodeCraft reports a missing graph and remains loadable", () =>
            RunOnSta(() =>
            {
                var missingPath = Path.Combine(Path.GetTempPath(), "nodecraft-missing-" + Guid.NewGuid().ToString("N") + ".flow.xml");
                var validPath = Path.Combine(Path.GetTempPath(), "nodecraft-recovery-" + Guid.NewGuid().ToString("N") + ".flow.xml");
                try
                {
                    GraphModelXmlSerializer.Save(CreateStartupGraph(), validPath);
                    var page = new FlowPage(NullLoggerFactory.Instance);
                    var missingLoadFailed = !page.TryLoadGraphFile(missingPath);
                    var errorText = GetExecutionResult(page).Text;
                    var recoveryLoadSucceeded = page.TryLoadGraphFile(validPath);
                    var canvas = GetNodeCanvas(page);
                    return missingLoadFailed
                        && errorText.Contains("FileNotFoundException")
                        && recoveryLoadSucceeded
                        && canvas.GraphModel?.Nodes?.SingleOrDefault()?.Id == "loaded-startup";
                }
                finally
                {
                    File.Delete(validPath);
                }
            }));
        Run("NodeCraft main window exposes the formal menu and theme control", () =>
            RunOnSta(() =>
            {
                var app = new System.Windows.Application();
                var theme = new CommonControls.WPF.CommonControlTheme
                {
                    Theme = CommonControls.WPF.CommonControlTheme.BaseTheme.Light,
                };
                app.Resources.MergedDictionaries.Add(theme);
                var operationPath = Path.Combine(
                    Path.GetTempPath(),
                    "nodecraft-menu-" + Guid.NewGuid().ToString("N") + ".flow.xml");
                GraphModelXmlSerializer.Save(CreateStartupGraph(), operationPath);

                try
                {
                    var window = new MainWindow(new FlowPage(NullLoggerFactory.Instance));
                    var menu = FindLogicalDescendant<System.Windows.Controls.Menu>(window);
                    var topLevelHeaders = menu?.Items
                        .OfType<System.Windows.Controls.MenuItem>()
                        .Select(item => item.Header?.ToString())
                        .ToArray();
                    var operationHeaders = menu?.Items
                        .OfType<System.Windows.Controls.MenuItem>()
                        .SelectMany(item => item.Items.OfType<System.Windows.Controls.MenuItem>())
                        .Select(item => item.Header?.ToString())
                        .ToArray();
                    var darkThemeMenuItem = GetFieldValue<System.Windows.Controls.MenuItem>(window, "DarkThemeMenuItem");

                    if (topLevelHeaders == null
                        || operationHeaders == null
                        || !topLevelHeaders.SequenceEqual(new[] { "文件", "流程", "视图" })
                        || !new[] { "新建", "清空", "加载", "保存", "另存为", "退出", "校验", "执行一次", "持续运行", "停止", "深色主题" }
                            .All(header => operationHeaders.Contains(header))
                        || darkThemeMenuItem == null
                        || !darkThemeMenuItem.IsCheckable
                        || darkThemeMenuItem.IsChecked)
                    {
                        return false;
                    }

                    window.Show();
                    window.UpdateLayout();
                    var flowEditor = GetFieldValue<FlowPage>(window, "FlowEditor");
                    var canvas = flowEditor == null ? null : GetNodeCanvas(flowEditor);
                    if (flowEditor == null || canvas?.GraphModel?.Nodes?.Count != 0)
                    {
                        return false;
                    }

                    if (!flowEditor.TryLoadGraphFile(operationPath)
                        || GetCurrentFilePath(flowEditor).Text == "当前文件: 未保存")
                    {
                        return false;
                    }

                    flowEditor.NewGraph();
                    var starterNodeCount = canvas.GraphModel?.Nodes?.Count ?? 0;
                    if (starterNodeCount == 0
                        || GetCurrentFilePath(flowEditor).Text != "当前文件: 未保存"
                        || !flowEditor.TryLoadGraphFile(operationPath))
                    {
                        return false;
                    }

                    var loadedPath = GetCurrentFilePath(flowEditor).Text;
                    flowEditor.ClearGraph();
                    if (loadedPath == "当前文件: 未保存"
                        || canvas.GraphModel?.Nodes?.Count != 0
                        || GetCurrentFilePath(flowEditor).Text != "当前文件: 未保存")
                    {
                        return false;
                    }

                    darkThemeMenuItem.IsChecked = true;
                    var darkApplied = theme.Theme == CommonControls.WPF.CommonControlTheme.BaseTheme.Dark;
                    darkThemeMenuItem.IsChecked = false;
                    window.Close();
                    return darkApplied && theme.Theme == CommonControls.WPF.CommonControlTheme.BaseTheme.Light;
                }
                finally
                {
                    File.Delete(operationPath);
                    app.Shutdown();
                }
            }));
        Run("NodeCraft editor XAML contains no Demo wording", () =>
        {
            var flowPageXaml = File.ReadAllText(FindRepositoryFile("NodeCraft", "Pages", "FlowPage.xaml"));
            var mainWindowXaml = File.ReadAllText(FindRepositoryFile("NodeCraft", "MainWindow.xaml"));
            return flowPageXaml.Contains("NodeCraft Flow Editor")
                && !flowPageXaml.Contains("Demo", StringComparison.OrdinalIgnoreCase)
                && !flowPageXaml.Contains("flow-demo", StringComparison.OrdinalIgnoreCase)
                && !flowPageXaml.Contains("测试", StringComparison.Ordinal)
                && !mainWindowXaml.Contains("Demo", StringComparison.OrdinalIgnoreCase);
        });
        Run("NodeCraft App merges its Flow resource dictionary", () =>
        {
            var document = XDocument.Load(FindRepositoryFile("NodeCraft", "App.xaml"));
            return document
                .Descendants()
                .Attributes("Source")
                .Any(attribute => string.Equals(
                    attribute.Value,
                    "/NodeCraft.Flow;component/Themes/Flow.xaml",
                    StringComparison.Ordinal));
        });
        Run("NodeCraft Flow resources qualify NodeCraft localization and CommonControls assembly namespaces", () =>
        {
            var root = XDocument.Load(FindRepositoryFile("NodeCraft.Flow", "Themes", "Flow.xaml")).Root;
            return root?.GetNamespaceOfPrefix("local")?.NamespaceName == "clr-namespace:CommonControls.WPF;assembly=CommonControls.WPF"
                && root.GetNamespaceOfPrefix("converters")?.NamespaceName == "clr-namespace:CommonControls.WPF.Converters;assembly=CommonControls.WPF"
                && root.GetNamespaceOfPrefix("l")?.NamespaceName == "clr-namespace:NodeCraft.Localization";
        });
        Run("NodeCraft creates the FlowCanvas viewport in code", () =>
        {
            var pagePath = FindRepositoryFile("NodeCraft", "Pages", "FlowPage.xaml");
            var pageXaml = File.ReadAllText(pagePath);
            var codePath = FindRepositoryFile("NodeCraft", "Pages", "FlowPage.xaml.cs");
            var code = File.ReadAllText(codePath);
            var document = XDocument.Load(pagePath);
            XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
            var canvasColumn = document.Root?
                .Element(presentation + "Grid")?
                .Elements(presentation + "Grid")
                .SingleOrDefault(element => (string?)element.Attribute("Grid.Column") == "2");
            var rightSideBorder = canvasColumn?
                .Elements(presentation + "Border")
                .SingleOrDefault(element => (string?)element.Attribute(xaml + "Name") == "CanvasHost");
            var inputBlocker = canvasColumn?
                .Elements(presentation + "Border")
                .SingleOrDefault(element => (string?)element.Attribute(xaml + "Name") == "ExecutionInputBlocker");
            var directChildren = rightSideBorder?.Elements().ToArray() ?? Array.Empty<XElement>();

            return pageXaml.Contains("Text=\"4. 使用鼠标滚轮缩放画布，按住鼠标中键拖动视图。\"")
                && !pageXaml.Contains("Width=\"1800\"")
                && !pageXaml.Contains("Height=\"1200\"")
                && rightSideBorder != null
                && (string?)rightSideBorder.Attribute(xaml + "Name") == "CanvasHost"
                && inputBlocker != null
                && (string?)inputBlocker.Attribute("Background") == "Transparent"
                && directChildren.Length == 0
                && code.Contains("new FlowCanvas(", StringComparison.Ordinal)
                && code.Contains("CanvasHost.Child", StringComparison.Ordinal)
                && !rightSideBorder.Descendants(presentation + "ScrollViewer").Any();
        });
        Run("NodeCraft exposes awaited single and continuous flow controls", () =>
        {
            var mainWindowPath = FindRepositoryFile("NodeCraft", "MainWindow.xaml");
            var mainWindowCodePath = FindRepositoryFile("NodeCraft", "MainWindow.xaml.cs");
            var flowPageCodePath = FindRepositoryFile("NodeCraft", "Pages", "FlowPage.xaml.cs");
            var hostProjectPath = FindRepositoryFile("NodeCraft", "NodeCraft.csproj");
            var mainWindowXaml = File.ReadAllText(mainWindowPath);
            var mainWindowCode = File.ReadAllText(mainWindowCodePath);
            var flowPageCode = File.ReadAllText(flowPageCodePath);
            var project = XDocument.Load(hostProjectPath);
            var propertyGroup = project.Root?.Elements("PropertyGroup").FirstOrDefault();

            return mainWindowXaml.Contains("Header=\"执行一次\"", StringComparison.Ordinal)
                && mainWindowXaml.Contains("Header=\"持续运行\"", StringComparison.Ordinal)
                && mainWindowXaml.Contains("Header=\"停止\"", StringComparison.Ordinal)
                && mainWindowXaml.Contains("Closing=\"MainWindow_Closing\"", StringComparison.Ordinal)
                && mainWindowCode.Contains("async void MainWindow_Closing", StringComparison.Ordinal)
                && mainWindowCode.Contains("await FlowEditor.StopExecutionAsync()", StringComparison.Ordinal)
                && mainWindowCode.Contains("MenuRunOnce.IsEnabled = idle", StringComparison.Ordinal)
                && mainWindowCode.Contains("MenuStop.IsEnabled = !idle", StringComparison.Ordinal)
                && flowPageCode.Contains("Task RunOnceAsync", StringComparison.Ordinal)
                && flowPageCode.Contains("Task RunContinuouslyAsync", StringComparison.Ordinal)
                && flowPageCode.Contains("Task StopExecutionAsync", StringComparison.Ordinal)
                && string.Equals((string?)propertyGroup?.Element("PlatformTarget"), "x64", StringComparison.OrdinalIgnoreCase)
                && string.Equals((string?)propertyGroup?.Element("Prefer32Bit"), "false", StringComparison.OrdinalIgnoreCase);
        });
        Run("Flow node registrations can preserve visual content after execution", () =>
        {
            var registration = CreateTestRegistration("test.refresh-policy");
            var defaultPolicy = registration.RefreshContentAfterExecution;
            registration.RefreshContentAfterExecution = false;
            var registry = new FlowNodeRegistry();
            registry.Register(registration);
            var node = new NodeModel { ExecutorType = "test.refresh-policy" };
            return defaultPolicy && !registry.ShouldRefreshContentAfterExecution(node);
        });
        Run("plugin registration accepts multiple nodes atomically", () =>
        {
            var registry = new FlowNodeRegistry();
            var registrations = new List<FlowNodeRegistration>
            {
                CreateTestRegistration("test.plugin.first"),
                CreateTestRegistration("test.plugin.second"),
            };

            registry.RegisterPlugin("test.plugin", registrations);
            return registry.Contains("test.plugin.first")
                && registry.Contains("test.plugin.second");
        });
        Run("plugin registration rejects duplicate keys without partial commit", () =>
        {
            var registry = new FlowNodeRegistry();
            registry.Register(CreateTestRegistration("test.plugin.existing"));

            try
            {
                registry.RegisterPlugin("test.plugin", new List<FlowNodeRegistration>
                {
                    CreateTestRegistration("test.plugin.new"),
                    CreateTestRegistration("test.plugin.existing"),
                });
                return false;
            }
            catch (InvalidOperationException)
            {
                return !registry.Contains("test.plugin.new");
            }
        });
        Run("plugin registration context stages nodes in order", () =>
        {
            var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
            var version = new Version(1, 0);
            var context = new PluginRegistrationContext(logger, version);
            var first = CreateTestRegistration("test.plugin.first");
            var second = CreateTestRegistration("test.plugin.second");

            context.Register(first);
            context.Register(second);

            return ReferenceEquals(context.Nodes, context)
                && ReferenceEquals(context.Logger, logger)
                && Equals(context.HostApiVersion, version)
                && context.Registrations.Count == 2
                && ReferenceEquals(context.Registrations[0], first)
                && ReferenceEquals(context.Registrations[1], second);
        });
        Run("plugin registration context rejects null and duplicate staged nodes", () =>
        {
            var context = new PluginRegistrationContext(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, new Version(1, 0));
            context.Register(CreateTestRegistration("test.plugin.first"));

            try
            {
                context.Register(null);
                return false;
            }
            catch (ArgumentNullException)
            {
            }

            try
            {
                context.Register(CreateTestRegistration("test.plugin.first"));
                return false;
            }
            catch (InvalidOperationException)
            {
                return context.Registrations.Count == 1;
            }
        });
        Run("plugin manifest reader accepts valid manifest and lib path", () =>
        {
            var pluginDirectory = CreateTemporaryPluginDirectory("nodecraft-plugin-valid-");
            try
            {
                var pluginRoot = Path.GetFullPath(pluginDirectory);
                var entryAssemblyName = Path.GetFileName(Assembly.GetExecutingAssembly().Location);
                var entryAssemblyPath = CopyFileToDirectory(
                    Assembly.GetExecutingAssembly().Location,
                    pluginRoot);
                var privateLibraryDirectory = Path.Combine(pluginRoot, "lib");
                Directory.CreateDirectory(privateLibraryDirectory);

                var manifestPath = WritePluginManifest(
                    pluginRoot,
                    "company.valid.plugin",
                    entryAssemblyName,
                    "Company.Valid.Plugin",
                    "1.0",
                    "lib");

                var manifest = PluginManifestReader.Read(manifestPath, new Version(1, 0));

                return manifest.Id == "company.valid.plugin"
                    && manifest.EntryAssembly == entryAssemblyName
                    && manifest.EntryType == "Company.Valid.Plugin"
                    && manifest.ApiVersion == "1.0"
                    && manifest.PrivateLibraryPath == "lib"
                    && string.Equals(
                        manifest.PluginDirectory,
                        pluginRoot,
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        PluginPathResolver.ResolveEntryAssembly(manifest),
                        Path.GetFullPath(entryAssemblyPath),
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        PluginPathResolver.ResolvePrivateLibraryDirectory(manifest),
                        Path.GetFullPath(privateLibraryDirectory),
                        StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                DeleteDirectoryIfExists(pluginDirectory);
            }
        });
        Run("plugin manifest reader rejects a missing entry assembly file", () =>
        {
            var pluginDirectory = CreateTemporaryPluginDirectory("nodecraft-plugin-missing-entry-");
            try
            {
                Directory.CreateDirectory(Path.Combine(pluginDirectory, "lib"));
                var manifestPath = WritePluginManifest(
                    pluginDirectory,
                    "company.missing.entry",
                    "MissingPlugin.dll",
                    "Company.Missing.EntryPlugin",
                    "1.0",
                    "lib");
                return AssertInvalidManifest(manifestPath, pluginDirectory, "entryAssembly");
            }
            finally
            {
                DeleteDirectoryIfExists(pluginDirectory);
            }
        });
        Run("plugin manifest reader rejects a missing manifest file", () =>
        {
            var pluginDirectory = CreateTemporaryPluginDirectory("nodecraft-plugin-missing-manifest-");
            try
            {
                var manifestPath = Path.Combine(pluginDirectory, "plugin.json");
                return AssertManifestReadFailure(
                    manifestPath,
                    pluginDirectory,
                    typeof(FileNotFoundException));
            }
            finally
            {
                DeleteDirectoryIfExists(pluginDirectory);
            }
        });
        Run("plugin manifest reader rejects an entry assembly path outside the plugin root", () =>
        {
            var pluginDirectory = CreateTemporaryPluginDirectory("nodecraft-plugin-entry-escape-");
            try
            {
                Directory.CreateDirectory(Path.Combine(pluginDirectory, "lib"));
                var manifestPath = WritePluginManifest(
                    pluginDirectory,
                    "company.entry.escape",
                    "..\\NodeCraft.exe",
                    "Company.Entry.EscapePlugin",
                    "1.0",
                    "lib");
                return AssertInvalidManifest(manifestPath, pluginDirectory, "entryAssembly");
            }
            finally
            {
                DeleteDirectoryIfExists(pluginDirectory);
            }
        });
        Run("plugin manifest reader rejects a missing entry type", () =>
        {
            var pluginDirectory = CreateTemporaryPluginDirectory("nodecraft-plugin-missing-type-");
            try
            {
                var entryAssemblyName = Path.GetFileName(Assembly.GetExecutingAssembly().Location);
                CopyFileToDirectory(Assembly.GetExecutingAssembly().Location, pluginDirectory);
                Directory.CreateDirectory(Path.Combine(pluginDirectory, "lib"));

                var manifestPath = WritePluginManifest(
                    pluginDirectory,
                    "company.missing.type",
                    entryAssemblyName,
                    string.Empty,
                    "1.0",
                    "lib");
                return AssertInvalidManifest(manifestPath, pluginDirectory, "entryType");
            }
            finally
            {
                DeleteDirectoryIfExists(pluginDirectory);
            }
        });
        Run("plugin manifest reader rejects an unsupported major api version", () =>
        {
            var pluginDirectory = CreateTemporaryPluginDirectory("nodecraft-plugin-api-version-");
            try
            {
                var entryAssemblyName = Path.GetFileName(Assembly.GetExecutingAssembly().Location);
                CopyFileToDirectory(Assembly.GetExecutingAssembly().Location, pluginDirectory);
                Directory.CreateDirectory(Path.Combine(pluginDirectory, "lib"));

                var manifestPath = WritePluginManifest(
                    pluginDirectory,
                    "company.bad.api",
                    entryAssemblyName,
                    "Company.Bad.ApiPlugin",
                    "2.0",
                    "lib");
                return AssertInvalidManifest(manifestPath, pluginDirectory, "apiVersion");
            }
            finally
            {
                DeleteDirectoryIfExists(pluginDirectory);
            }
        });
        Run("plugin manifest reader rejects a private library path outside the plugin root", () =>
        {
            var pluginDirectory = CreateTemporaryPluginDirectory("nodecraft-plugin-lib-escape-");
            try
            {
                var entryAssemblyName = Path.GetFileName(Assembly.GetExecutingAssembly().Location);
                CopyFileToDirectory(Assembly.GetExecutingAssembly().Location, pluginDirectory);
                Directory.CreateDirectory(Path.Combine(pluginDirectory, "lib"));

                var manifestPath = WritePluginManifest(
                    pluginDirectory,
                    "company.lib.escape",
                    entryAssemblyName,
                    "Company.Lib.EscapePlugin",
                    "1.0",
                    "..\\shared");
                return AssertInvalidManifest(manifestPath, pluginDirectory, "privateLibraryPath");
            }
            finally
            {
                DeleteDirectoryIfExists(pluginDirectory);
            }
        });
        Run("plugin load context is collectible and resolves shared assemblies from the default context", () =>
        {
            var pluginDirectory = CreateTemporaryPluginDirectory("nodecraft-plugin-context-shared-");
            PluginLoadContext context = null;
            try
            {
                var pluginRoot = Path.GetFullPath(pluginDirectory);
                var entryAssemblyPath = CopyFileToDirectory(
                    Assembly.GetExecutingAssembly().Location,
                    pluginRoot);
                var privateLibraryDirectory = Path.Combine(pluginRoot, "lib");
                Directory.CreateDirectory(privateLibraryDirectory);

                context = new PluginLoadContext(
                    entryAssemblyPath,
                    pluginRoot,
                    privateLibraryDirectory,
                    CreateSharedAssemblyNames());

                var flowAssembly = context.LoadFromAssemblyName(
                    new AssemblyName(typeof(IFlowPlugin).Assembly.GetName().Name));
                var commonControlsAssembly = context.LoadFromAssemblyName(
                    new AssemblyName(typeof(CommonControls.WPF.CommonControlTheme).Assembly.GetName().Name));

                return context.IsCollectible
                    && ReferenceEquals(flowAssembly, typeof(IFlowPlugin).Assembly)
                    && ReferenceEquals(
                        commonControlsAssembly,
                        typeof(CommonControls.WPF.CommonControlTheme).Assembly);
            }
            finally
            {
                UnloadAssemblyLoadContext(context);
                DeleteDirectoryIfExists(pluginDirectory);
            }
        });
        Run("plugin load context does not return native probe paths outside staged directories", () =>
        {
            var pluginDirectory = CreateTemporaryPluginDirectory("nodecraft-plugin-context-native-");
            PluginLoadContext context = null;
            try
            {
                var pluginRoot = Path.GetFullPath(pluginDirectory);
                var entryAssemblyDirectory = Path.Combine(pluginRoot, "bin");
                var entryAssemblyPath = CopyFileToDirectory(
                    Assembly.GetExecutingAssembly().Location,
                    entryAssemblyDirectory);
                var privateLibraryDirectory = Path.Combine(pluginRoot, "lib");
                Directory.CreateDirectory(privateLibraryDirectory);
                var escapedNativePath = Path.Combine(pluginRoot, "escaped-native.dll");
                File.WriteAllText(escapedNativePath, string.Empty);

                context = new PluginLoadContext(
                    entryAssemblyPath,
                    pluginRoot,
                    privateLibraryDirectory,
                    CreateSharedAssemblyNames());

                return InvokeNativeProbePath(context, Path.GetFullPath(escapedNativePath)) == null
                    && InvokeNativeProbePath(context, "..\\escaped-native.dll") == null;
            }
            finally
            {
                UnloadAssemblyLoadContext(context);
                DeleteDirectoryIfExists(pluginDirectory);
            }
        });
        Run("plugin load context rejects resolver managed paths outside the plugin root", () =>
        {
            var pluginDirectory = CreateTemporaryPluginDirectory("nodecraft-plugin-context-resolver-escape-");
            PluginLoadContext? context = null;
            try
            {
                var pluginRoot = Path.Combine(pluginDirectory, "plugin");
                var privateLibraryDirectory = Path.Combine(pluginRoot, "lib");
                Directory.CreateDirectory(privateLibraryDirectory);

                context = new PluginLoadContext(
                    Assembly.GetExecutingAssembly().Location,
                    pluginRoot,
                    privateLibraryDirectory,
                    CreateSharedAssemblyNames());

                try
                {
                    InvokeManagedLoad(context, new AssemblyName("NLog"));
                    return false;
                }
                catch (TargetInvocationException ex)
                {
                    return ex.InnerException is FileLoadException fileLoad
                        && fileLoad.Message.Contains(Path.GetFullPath(pluginRoot), StringComparison.OrdinalIgnoreCase)
                        && fileLoad.Message.Contains("outside", StringComparison.OrdinalIgnoreCase);
                }
            }
            finally
            {
                UnloadAssemblyLoadContext(context!);
                DeleteDirectoryIfExists(pluginDirectory);
            }
        });
        Run("plugin load context resolves System.Text.Json from the default trusted platform context", () =>
        {
            var pluginDirectory = CreateTemporaryPluginDirectory("nodecraft-plugin-context-framework-");
            PluginLoadContext? context = null;
            try
            {
                var pluginRoot = Path.GetFullPath(pluginDirectory);
                var entryAssemblyPath = CopyFileToDirectory(
                    Assembly.GetExecutingAssembly().Location,
                    pluginRoot);
                var privateLibraryDirectory = Path.Combine(pluginRoot, "lib");
                Directory.CreateDirectory(privateLibraryDirectory);

                context = new PluginLoadContext(
                    entryAssemblyPath,
                    pluginRoot,
                    privateLibraryDirectory,
                    CreateSharedAssemblyNames());

                var trustedPlatformAssembly = typeof(System.Text.Json.JsonSerializer).Assembly;
                var resolvedAssembly = context.LoadFromAssemblyName(trustedPlatformAssembly.GetName());

                return ReferenceEquals(resolvedAssembly, trustedPlatformAssembly)
                    && ReferenceEquals(
                        AssemblyLoadContext.GetLoadContext(resolvedAssembly),
                        AssemblyLoadContext.Default);
            }
            finally
            {
                UnloadAssemblyLoadContext(context!);
                DeleteDirectoryIfExists(pluginDirectory);
            }
        });
        Run("plugin load context uses only the framework fallback for unverified app-local framework assemblies", () =>
        {
            var appLocalDirectory = CreateTemporaryPluginDirectory("nodecraft-plugin-context-app-local-tpa-");
            try
            {
                var sharedFrameworkDirectory = Path.Combine(
                    appLocalDirectory,
                    "shared",
                    "Microsoft.NETCore.App",
                    "8.0.0");
                Directory.CreateDirectory(sharedFrameworkDirectory);
                var systemLinqPath = Path.Combine(sharedFrameworkDirectory, "System.Linq.dll");
                var systemComponentModelPath = Path.Combine(sharedFrameworkDirectory, "System.ComponentModel.dll");
                var systemEventsPath = Path.Combine(sharedFrameworkDirectory, "Microsoft.Win32.SystemEvents.dll");
                File.WriteAllBytes(systemLinqPath, Array.Empty<byte>());
                File.WriteAllBytes(systemComponentModelPath, Array.Empty<byte>());
                File.WriteAllBytes(systemEventsPath, Array.Empty<byte>());

                var trustedPlatformAssemblies = string.Join(
                    Path.PathSeparator.ToString(),
                    systemLinqPath,
                    systemComponentModelPath,
                    systemEventsPath);
                var missingDepsDirectory = InvokeVerifiedSharedFrameworkDirectory(
                    sharedFrameworkDirectory,
                    new[] { appLocalDirectory });
                File.WriteAllText(
                    Path.Combine(sharedFrameworkDirectory, "Microsoft.NETCore.App.deps.json"),
                    "{}");
                var trustedAssemblyNames = InvokeTrustedPlatformAssemblyInventory(
                    trustedPlatformAssemblies,
                    new[] { sharedFrameworkDirectory });

                return missingDepsDirectory == null
                    && trustedAssemblyNames != null
                    && !trustedAssemblyNames.Contains("System.Linq", StringComparer.OrdinalIgnoreCase)
                    && !trustedAssemblyNames.Contains("System.ComponentModel", StringComparer.OrdinalIgnoreCase)
                    && !trustedAssemblyNames.Contains("Microsoft.Win32.SystemEvents", StringComparer.OrdinalIgnoreCase)
                    && InvokeTrustedPlatformAssemblyName("System.Linq", trustedAssemblyNames)
                    && InvokeTrustedPlatformAssemblyName("System.ComponentModel", trustedAssemblyNames)
                    && InvokeTrustedPlatformAssemblyName("Microsoft.CSharp", trustedAssemblyNames)
                    && InvokeTrustedPlatformAssemblyName("Microsoft.Win32.Primitives", trustedAssemblyNames)
                    && InvokeTrustedPlatformAssemblyName("mscorlib", trustedAssemblyNames)
                    && InvokeTrustedPlatformAssemblyName("PresentationFramework", trustedAssemblyNames)
                    && !InvokeTrustedPlatformAssemblyName(
                        "Microsoft.Win32.SystemEvents",
                        trustedAssemblyNames);
            }
            finally
            {
                DeleteDirectoryIfExists(appLocalDirectory);
            }
        });
        Run("plugin load context rejects host-private Microsoft managed dependencies from the trusted platform list", () =>
        {
            var pluginDirectory = CreateTemporaryPluginDirectory("nodecraft-plugin-context-managed-miss-");
            PluginLoadContext? context = null;
            try
            {
                var pluginRoot = Path.GetFullPath(pluginDirectory);
                var entryAssemblyPath = CopyFileToDirectory(
                    Assembly.GetExecutingAssembly().Location,
                    pluginRoot);
                var privateLibraryDirectory = Path.Combine(pluginRoot, "lib");
                Directory.CreateDirectory(privateLibraryDirectory);

                context = new PluginLoadContext(
                    entryAssemblyPath,
                    pluginRoot,
                    privateLibraryDirectory,
                    CreateSharedAssemblyNames());

                try
                {
                    InvokeManagedLoad(
                        context,
                        new AssemblyName("Microsoft.Win32.SystemEvents"));
                    return false;
                }
                catch (TargetInvocationException ex)
                {
                    return ex.InnerException is FileNotFoundException fileNotFound
                        && fileNotFound.Message.Contains(Path.GetFullPath(pluginRoot), StringComparison.OrdinalIgnoreCase)
                        && fileNotFound.Message.Contains(Path.GetFullPath(privateLibraryDirectory), StringComparison.OrdinalIgnoreCase);
                }
            }
            finally
            {
                UnloadAssemblyLoadContext(context!);
                DeleteDirectoryIfExists(pluginDirectory);
            }
        });
        Run("plugin load context throws instead of using global native search after contained probes miss", () =>
        {
            var pluginDirectory = CreateTemporaryPluginDirectory("nodecraft-plugin-context-native-miss-");
            PluginLoadContext? context = null;
            try
            {
                var pluginRoot = Path.GetFullPath(pluginDirectory);
                var entryAssemblyPath = CopyFileToDirectory(
                    Assembly.GetExecutingAssembly().Location,
                    pluginRoot);
                var privateLibraryDirectory = Path.Combine(pluginRoot, "lib");
                Directory.CreateDirectory(privateLibraryDirectory);

                context = new PluginLoadContext(
                    entryAssemblyPath,
                    pluginRoot,
                    privateLibraryDirectory,
                    CreateSharedAssemblyNames());

                var loadMethod = typeof(PluginLoadContext).GetMethod(
                    "LoadUnmanagedDll",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (loadMethod == null)
                {
                    return false;
                }

                try
                {
                    loadMethod.Invoke(context, new object[] { "nodecraft-native-missing-for-isolation-test" });
                    return false;
                }
                catch (TargetInvocationException ex)
                {
                    return ex.InnerException is DllNotFoundException dllNotFound
                        && dllNotFound.Message.Contains(Path.GetFullPath(pluginRoot), StringComparison.OrdinalIgnoreCase)
                        && dllNotFound.Message.Contains(Path.GetFullPath(privateLibraryDirectory), StringComparison.OrdinalIgnoreCase);
                }
            }
            finally
            {
                UnloadAssemblyLoadContext(context!);
                DeleteDirectoryIfExists(pluginDirectory);
            }
        });
        Run("plugin load context probes managed private libraries from lib", () =>
        {
            var pluginDirectory = CreateTemporaryPluginDirectory("nodecraft-plugin-context-private-");
            PluginLoadContext context = null;
            try
            {
                var pluginRoot = Path.GetFullPath(pluginDirectory);
                var entryAssemblyPath = CopyFileToDirectory(
                    Assembly.GetExecutingAssembly().Location,
                    pluginRoot);
                var privateLibraryDirectory = Path.Combine(pluginRoot, "lib");
                Directory.CreateDirectory(privateLibraryDirectory);
                var privateAssemblyPath = CopyFileToDirectory(
                    FindBuiltPrivateDependencyAssembly(),
                    privateLibraryDirectory);

                context = new PluginLoadContext(
                    entryAssemblyPath,
                    pluginRoot,
                    privateLibraryDirectory,
                    CreateSharedAssemblyNames());

                return AssertPrivateAssemblyLoadedFromContext(
                    context,
                    "NodeCraft.PluginSample.PrivateDependency",
                    Path.GetFullPath(privateAssemblyPath));
            }
            finally
            {
                UnloadAssemblyLoadContext(context);
                DeleteDirectoryIfExists(pluginDirectory);
            }
        });
        Run("plugin logging writes plugin id and exception text", () =>
        {
            var logDirectory = Path.Combine(
                Path.GetTempPath(),
                "nodecraft-plugin-log-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(logDirectory);

            try
            {
                var nlogConfig = new LoggingConfiguration();
                var fileTarget = new FileTarget("plugin-log-file")
                {
                    FileName = Path.Combine(logDirectory, "nodecraft-${shortdate}.log"),
                    Layout = "${longdate}|${level:uppercase=true}|${logger}|${message}${onexception:inner= |${exception:format=tostring}}",
                };
                nlogConfig.AddTarget(fileTarget);
                nlogConfig.AddRule(NLog.LogLevel.Debug, NLog.LogLevel.Fatal, fileTarget);

                using var loggerFactory = LoggerFactory.Create(builder => builder.AddNLog(nlogConfig));
                var logger = loggerFactory.CreateLogger("NodeCraft.Plugin.company.bad");
                logger.LogError(new InvalidOperationException("broken plugin"), "Registration failed.");
                LogManager.Shutdown();

                var logPath = Directory.EnumerateFiles(logDirectory, "*.log").SingleOrDefault();
                if (logPath == null)
                {
                    return false;
                }

                var contents = File.ReadAllText(logPath);
                return contents.Contains("company.bad", StringComparison.Ordinal)
                    && contents.Contains("Registration failed.", StringComparison.Ordinal)
                    && contents.Contains("broken plugin", StringComparison.Ordinal);
            }
            finally
            {
                LogManager.Shutdown();
                if (Directory.Exists(logDirectory))
                {
                    Directory.Delete(logDirectory, recursive: true);
                }
            }
        });
        Run("plugin load report preserves ordered failures", () =>
        {
            var context = new AssemblyLoadContext("plugin-load-report-test", isCollectible: true);
            try
            {
                var success = PluginLoadResult.Succeeded("company.good", context);
                var failure = PluginLoadResult.Failed(
                    "company.bad",
                    "registration",
                    new InvalidOperationException("broken"));
                var report = new PluginLoadReport(new[] { success, failure });

                return report.Results.Count == 2
                    && ReferenceEquals(report.Results[0], success)
                    && ReferenceEquals(report.Results[1], failure)
                    && report.Failures.Count == 1
                    && ReferenceEquals(report.Failures[0], failure)
                    && success.IsSuccess
                    && !failure.IsSuccess
                    && ReferenceEquals(success.Context, context)
                    && failure.Context == null
                    && string.Equals(failure.Phase, "registration", StringComparison.Ordinal)
                    && failure.Exception is InvalidOperationException;
            }
            finally
            {
                context.Unload();
            }
        });
        Run("plugin startup notification summarizes failures", () =>
        {
            var message = PluginStartupNotification.BuildMessage(new[]
            {
                PluginLoadResult.Failed("company.bad", "registration", new InvalidOperationException("broken")),
                PluginLoadResult.Failed("company.missing", "dependency load", new FileNotFoundException("missing")),
            });

            return message.Contains("2 plugin", StringComparison.Ordinal)
                && message.Contains("company.bad", StringComparison.Ordinal)
                && message.Contains("company.missing", StringComparison.Ordinal)
                && message.Contains("registration", StringComparison.Ordinal)
                && message.Contains("dependency load", StringComparison.Ordinal)
                && message.Contains("See the NodeCraft log for details.", StringComparison.Ordinal)
                && !message.Contains("broken", StringComparison.Ordinal);
        });
        Run("plugin loader treats a missing Plugins directory as empty", () =>
        {
            var pluginsDirectory = Path.Combine(
                Path.GetTempPath(),
                "nodecraft-plugin-missing-root-" + Guid.NewGuid().ToString("N"));

            try
            {
                var loader = new PluginLoader(
                    new FlowNodeRegistry(),
                    new Version(1, 0),
                    NullLoggerFactory.Instance);
                var report = loader.LoadAll(pluginsDirectory);
                return report.Results.Count == 0
                    && report.Failures.Count == 0;
            }
            finally
            {
                DeleteDirectoryIfExists(pluginsDirectory);
            }
        });
        Run("plugin loader reserves failed plugin ids before validating later duplicates and keeps registration atomic", () =>
        {
            var pluginsRoot = CreateTemporaryPluginDirectory("nodecraft-plugin-loader-");
            var pluginsDirectory = Path.Combine(pluginsRoot, "Plugins");
            FlowNodeRegistry registry = null;
            PluginLoader loader = null;
            PluginLoadReport report = null;

            try
            {
                StageTaskFiveFixturePackages(pluginsDirectory);
                registry = new FlowNodeRegistry();
                registry.Register(CreateTestRegistration("node.string-value"));

                loader = new PluginLoader(
                    registry,
                    new Version(1, 0),
                    NullLoggerFactory.Instance);
                report = loader.LoadAll(pluginsDirectory);

                var passed = report.Results.Count == 6
                    && report.Failures.Count == 5
                    && report.Results.Select(result => result.PluginId).SequenceEqual(new[]
                    {
                        "Alpha-invalid-manifest",
                        "test.failed-duplicate.plugin",
                        "test.throwing.plugin",
                        "test.valid.plugin",
                        "test.failed-duplicate.plugin",
                        "test.duplicate.plugin",
                    })
                    && report.Results.Select(result => result.Phase ?? "success").SequenceEqual(new[]
                    {
                        "manifest",
                        "registration",
                        "registration",
                        "success",
                        "validation",
                        "registration",
                    })
                    && report.Results.Count(result => result.IsSuccess) == 1
                    && report.Results.Count(result => result.Context != null) == 1
                    && report.Results.Single(result => result.IsSuccess).Context?.IsCollectible == true
                    && registry.Contains("node.string-value")
                    && registry.Contains("test.valid.node")
                    && !registry.Contains("test.throwing.node")
                    && !report.Failures.Any(result => result.IsSuccess)
                    && report.Failures.Any(result =>
                        result.PluginId == "Alpha-invalid-manifest"
                        && result.Exception is InvalidDataException)
                    && report.Failures.Any(result =>
                        result.PluginId == "test.failed-duplicate.plugin"
                        && string.Equals(result.Phase, "registration", StringComparison.Ordinal)
                        && result.Exception is FileNotFoundException)
                    && report.Failures.Any(result =>
                        result.PluginId == "test.throwing.plugin"
                        && result.Exception is InvalidOperationException
                        && result.Exception.Message.Contains("fixture registration failed", StringComparison.Ordinal))
                    && report.Failures.Any(result =>
                        result.PluginId == "test.failed-duplicate.plugin"
                        && string.Equals(result.Phase, "validation", StringComparison.Ordinal)
                        && result.Exception is InvalidOperationException
                        && result.Exception.Message.Contains("duplicated within the plugin scan", StringComparison.Ordinal))
                    && report.Failures.Count(result => result.PluginId == "test.failed-duplicate.plugin") == 2
                    && report.Failures.Any(result =>
                        result.PluginId == "test.duplicate.plugin"
                        && result.Exception is InvalidOperationException)
                    && registry.TryCreateNodeByTypeKey("test.valid.node", out var node)
                    && node != null
                    && node.GetType().Name == nameof(FixtureNodeModel)
                    && node.ExecutorType == "test.valid.node";

                return passed;
            }
            finally
            {
                loader = null;
                registry = null;
                report = null;
            }
        });
        Run("sample plugin package output contains the manifest, entry assembly, and only a private lib copy", () =>
        {
            var outputDirectory = FindBuiltSamplePluginOutputDirectory();
            var manifestPath = Path.Combine(outputDirectory, "plugin.json");
            var privateLibraryDirectory = Path.Combine(outputDirectory, "lib");
            var rootFiles = Directory.EnumerateFileSystemEntries(outputDirectory)
                .Select(Path.GetFileName)
                .ToArray();
            var libFiles = Directory.Exists(privateLibraryDirectory)
                ? Directory.EnumerateFiles(privateLibraryDirectory)
                    .Select(Path.GetFileName)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : Array.Empty<string>();
            var manifest = PluginManifestReader.Read(manifestPath, new Version(1, 0));

            return manifest.Id == "company.sample.nodes"
                && manifest.EntryAssembly == "NodeCraft.PluginSample.dll"
                && manifest.EntryType == "NodeCraft.PluginSample.Plugin.SamplePlugin"
                && manifest.ApiVersion == "1.0"
                && manifest.PrivateLibraryPath == "lib"
                && rootFiles.Contains("NodeCraft.PluginSample.dll", StringComparer.OrdinalIgnoreCase)
                && rootFiles.Contains("plugin.json", StringComparer.OrdinalIgnoreCase)
                && rootFiles.Contains("lib", StringComparer.OrdinalIgnoreCase)
                && !rootFiles.Contains("NodeCraft.Flow.dll", StringComparer.OrdinalIgnoreCase)
                && !rootFiles.Contains("CommonControls.WPF.dll", StringComparer.OrdinalIgnoreCase)
                && libFiles.SequenceEqual(new[] { "NodeCraft.PluginSample.PrivateDependency.dll" })
                && !File.Exists(Path.Combine(outputDirectory, "NodeCraft.PluginSample.PrivateDependency.dll"))
                && !File.Exists(Path.Combine(privateLibraryDirectory, "NodeCraft.Flow.dll"))
                && !File.Exists(Path.Combine(privateLibraryDirectory, "CommonControls.WPF.dll"))
                && !File.Exists(Path.Combine(AppContext.BaseDirectory, "NodeCraft.PluginSample.dll"))
                && !File.Exists(Path.Combine(AppContext.BaseDirectory, "NodeCraft.PluginSample.PrivateDependency.dll"));
        });
        Run("plugin palette exposes a stable type key for drag creation", () =>
        {
            var stagedPlugins = new StagedPluginTestRoot("nodecraft-sample-plugin-palette-");
            PluginLoadReport report = null;

            try
            {
                var registry = new FlowNodeRegistry();
                var loader = new PluginLoader(
                    registry,
                    new Version(1, 0),
                    NullLoggerFactory.Instance);
                stagedPlugins.StageSamplePluginPackage();
                report = loader.LoadAll(stagedPlugins.LoadPluginsDirectory);

                var paletteItem = registry.CreatePaletteCategories()
                    .SelectMany(category => category.Items)
                    .SingleOrDefault(item => item.DisplayName == "Sample Value");
                var typeKeyProperty = paletteItem?.GetType().GetProperty("TypeKey");
                var typeKey = typeKeyProperty?.GetValue(paletteItem) as string;
                var canCreate = FlowCanvas.CanCreateNodeFromPaletteData(registry, typeKey);
                var created = FlowCanvas.TryCreateNodeFromPaletteData(registry, typeKey, out var node);

                return report.Failures.Count == 0
                    && string.Equals(typeKey, "company.sample.nodes.value", StringComparison.Ordinal)
                    && canCreate
                    && created
                    && node != null;
            }
            finally
            {
                UnloadPluginLoadContexts(ref report);
                stagedPlugins.Dispose();
            }
        });
        await RunAsync("sample plugin loads successfully, renders custom content, and executes through its private formatter", async () =>
        {
            var stagedPlugins = new StagedPluginTestRoot("nodecraft-sample-plugin-load-");

            try
            {
                var passed = false;
                {
                    FlowNodeRegistry registry = null;
                    PluginLoader loader = null;
                    PluginLoadReport report = null;
                    NodeModel valueNode = null;
                    NodeModel previewNode = null;

                    try
                    {
                        var packageDirectory = stagedPlugins.StageSamplePluginPackage();
                        registry = new FlowNodeRegistry();
                        loader = new PluginLoader(
                            registry,
                            new Version(1, 0),
                            NullLoggerFactory.Instance);
                        report = loader.LoadAll(stagedPlugins.LoadPluginsDirectory);

                        if (report.Failures.Count != 0
                            || !registry.Contains("company.sample.nodes.value")
                            || !registry.Contains("company.sample.nodes.preview")
                            || !registry.TryCreateNodeByTypeKey("company.sample.nodes.value", out valueNode)
                            || valueNode == null
                            || !registry.TryCreateNodeByTypeKey("company.sample.nodes.preview", out previewNode)
                            || previewNode == null)
                        {
                            return false;
                        }

                        valueNode.Name = "Sample Value";
                        previewNode.Id = "preview";

                        var contentCreated = RunOnSta(() =>
                            RunWithThemedWindow(window =>
                            {
                                var canvas = new FlowCanvas
                                {
                                    GraphModel = new GraphModel
                                    {
                                        Nodes = new List<NodeModel> { valueNode },
                                        Links = new List<GraphLink>(),
                                    },
                                };

                                return registry.BuildNodeContent(canvas, valueNode) is System.Windows.FrameworkElement;
                            }));
                        if (!contentCreated)
                        {
                            return false;
                        }

                        var workflow = new WorkflowDocument();
                        workflow.Nodes.Add(new WorkflowNode
                        {
                            Id = "value",
                            TypeKey = "company.sample.nodes.value",
                            DisplayName = "Sample Value",
                            Inputs = { [BuiltInNodePorts.Value] = "task-six" },
                        });
                        workflow.Nodes.Add(new WorkflowNode
                        {
                            Id = "preview",
                            TypeKey = "company.sample.nodes.preview",
                            DisplayName = "Sample Preview",
                            Inputs =
                            {
                                [BuiltInNodePorts.Input] = new LinkRef
                                {
                                    SourceNodeId = "value",
                                    SourceSlot = 0,
                                },
                            },
                        });

                        var executor = new GraphExecutor(workflow, registry);
                        var context = await executor.ExecuteAsync();
                        registry.ApplyExecutionResults(new[] { previewNode }, context);

                        var previewText = previewNode.GetType()
                            .GetProperty("LastPreviewText", BindingFlags.Instance | BindingFlags.Public)?
                            .GetValue(previewNode) as string;

                        passed = report.Results.Count == 1
                            && report.Results[0].IsSuccess
                            && File.Exists(Path.Combine(packageDirectory, "plugin.json"))
                            && File.Exists(Path.Combine(packageDirectory, "NodeCraft.PluginSample.dll"))
                            && context.TryGetPortValue("value", 0, out var valueOutput)
                            && valueOutput is string valueText
                            && valueText.Contains("private:", StringComparison.Ordinal)
                            && context.TryGetPortValue("preview", 0, out var previewOutput)
                            && string.Equals(previewOutput as string, "private:task-six", StringComparison.Ordinal)
                            && string.Equals(previewText, "private:task-six", StringComparison.Ordinal)
                            && File.Exists(Path.Combine(packageDirectory, "lib", "NodeCraft.PluginSample.PrivateDependency.dll"))
                            && !File.Exists(Path.Combine(packageDirectory, "NodeCraft.Flow.dll"))
                            && !File.Exists(Path.Combine(packageDirectory, "CommonControls.WPF.dll"));
                    }
                    finally
                    {
                        valueNode = null;
                        previewNode = null;
                        loader = null;
                        registry = null;
                        UnloadPluginLoadContexts(ref report);
                    }
                }

                return passed;
            }
            finally
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                stagedPlugins.Dispose();
            }
        });
        Run("sample value node content creates an editor that updates the model and style switch", () =>
            RunOnSta(() =>
            {
                var stagedPlugins = new StagedPluginTestRoot("nodecraft-sample-plugin-ui-");

                try
                {
                    var passed = false;
                    {
                        FlowNodeRegistry registry = null;
                        PluginLoader loader = null;
                        PluginLoadReport report = null;
                        NodeModel node = null;

                        try
                        {
                            stagedPlugins.StageSamplePluginPackage();
                            registry = new FlowNodeRegistry();
                            loader = new PluginLoader(
                                registry,
                                new Version(1, 0),
                                NullLoggerFactory.Instance);
                            report = loader.LoadAll(stagedPlugins.LoadPluginsDirectory);
                            if (report.Failures.Count != 0
                                || !registry.TryCreateNodeByTypeKey("company.sample.nodes.value", out node)
                                || node == null)
                            {
                                return false;
                            }

                            node.Name = "Sample Value";
                            var valueProperty = node.GetType().GetProperty("ValueText", BindingFlags.Instance | BindingFlags.Public);
                            var accentProperty = node.GetType().GetProperty("UseAccentStyle", BindingFlags.Instance | BindingFlags.Public);
                            if (valueProperty == null || accentProperty == null)
                            {
                                return false;
                            }

                            passed = RunWithThemedWindow(window =>
                            {
                                var canvas = new FlowCanvas
                                {
                                    GraphModel = new GraphModel
                                    {
                                        Nodes = new List<NodeModel> { node },
                                        Links = new List<GraphLink>(),
                                    },
                                };
                                var content = registry.BuildNodeContent(canvas, node) as System.Windows.FrameworkElement;
                                if (content == null || !string.Equals(content.GetType().Name, "SampleValueEditor", StringComparison.Ordinal))
                                {
                                    return false;
                                }

                                window.Content = content;
                                window.UpdateLayout();

                                var editorCard = content.FindName("EditorCard") as System.Windows.Controls.Border;
                                var textBox = FindLogicalDescendant<System.Windows.Controls.TextBox>(content);
                                var styleSwitch = FindLogicalDescendant<System.Windows.Controls.CheckBox>(content);
                                if (editorCard == null || textBox == null || styleSwitch == null)
                                {
                                    return false;
                                }

                                textBox.Text = "custom ui";
                                window.UpdateLayout();

                                var originalBrush = editorCard.BorderBrush;
                                styleSwitch.IsChecked = true;
                                window.UpdateLayout();

                                return string.Equals(valueProperty.GetValue(node) as string, "custom ui", StringComparison.Ordinal)
                                    && Equals(accentProperty.GetValue(node), true)
                                    && editorCard.BorderBrush != null
                                    && !ReferenceEquals(editorCard.BorderBrush, originalBrush);
                            });
                        }
                        finally
                        {
                            node = null;
                            loader = null;
                            registry = null;
                            UnloadPluginLoadContexts(ref report);
                        }
                    }

                    return passed;
                }
                finally
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    stagedPlugins.Dispose();
                }
            }));
        Run("broken plugins do not block the sample plugin and detailed errors stay in the log", () =>
        {
            var stagedPlugins = new StagedPluginTestRoot("nodecraft-plugin-failure-isolation-");

            try
            {
                var passed = false;
                {
                    var logDirectory = Path.Combine(stagedPlugins.RootDirectory, "Logs");
                    FlowNodeRegistry registry = null;
                    PluginLoader loader = null;
                    PluginLoadReport report = null;

                    try
                    {
                        stagedPlugins.StageSamplePluginPackage();
                        stagedPlugins.StageAssemblyPluginPackage(
                            "Zulu.Broken.Sample",
                            "test.nested-throwing.plugin",
                            "NestedThrowingFixturePlugin");

                        registry = CreateRegistryWithBuiltInStringValue();
                        var nlogConfig = new LoggingConfiguration();
                        var fileTarget = new FileTarget("plugin-failure-log")
                        {
                            FileName = Path.Combine(logDirectory, "nodecraft-${shortdate}.log"),
                            Layout = "${longdate}|${level:uppercase=true}|${logger}|${message}${onexception:inner= |${exception:format=tostring}}",
                        };
                        nlogConfig.AddTarget(fileTarget);
                        nlogConfig.AddRule(NLog.LogLevel.Debug, NLog.LogLevel.Fatal, fileTarget);
                        using var loggerFactory = LoggerFactory.Create(builder => builder.AddNLog(nlogConfig));
                        loader = new PluginLoader(
                            registry,
                            new Version(1, 0),
                            loggerFactory);
                        report = loader.LoadAll(stagedPlugins.LoadPluginsDirectory);
                        LogManager.Shutdown();

                        var failure = report.Failures.SingleOrDefault(result =>
                            string.Equals(result.PluginId, "test.nested-throwing.plugin", StringComparison.Ordinal));
                        var message = PluginStartupNotification.BuildMessage(report.Failures);
                        var logPath = Directory.EnumerateFiles(logDirectory, "nodecraft-*.log")
                            .OrderByDescending(File.GetLastWriteTimeUtc)
                            .FirstOrDefault();
                        var logContents = logPath == null
                            ? string.Empty
                            : File.ReadAllText(logPath);

                        passed = report.Results.Count == 2
                            && report.Failures.Count == 1
                            && failure != null
                            && string.Equals(failure.Phase, "registration", StringComparison.Ordinal)
                            && registry.Contains("company.sample.nodes.value")
                            && registry.Contains("company.sample.nodes.preview")
                            && registry.TryResolve("node.string-value", out _)
                            && message.Contains("test.nested-throwing.plugin", StringComparison.Ordinal)
                            && !message.Contains("fixture registration failed", StringComparison.Ordinal)
                            && !message.Contains("System.InvalidOperationException", StringComparison.Ordinal)
                            && logContents.Contains("NodeCraft.Plugin.test.nested-throwing.plugin", StringComparison.Ordinal)
                            && logContents.Contains("Plugin load failed during registration.", StringComparison.Ordinal)
                            && logContents.Contains("fixture registration failed", StringComparison.Ordinal)
                            && logContents.Contains("fixture inner failure", StringComparison.Ordinal);
                    }
                    finally
                    {
                        LogManager.Shutdown();
                        loader = null;
                        registry = null;
                        UnloadPluginLoadContexts(ref report);
                    }
                }

                return passed;
            }
            finally
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                stagedPlugins.Dispose();
            }
        });
        Run("duplicate sample type keys are rejected without replacing the original plugin registration and shared API identity stays unified", () =>
        {
            var stagedPlugins = new StagedPluginTestRoot("nodecraft-plugin-duplicate-sample-");

            try
            {
                var passed = false;
                {
                    FlowNodeRegistry registry = null;
                    PluginLoader loader = null;
                    PluginLoadReport report = null;
                    NodeModel valueNode = null;

                    try
                    {
                        stagedPlugins.StageSamplePluginPackage();
                        stagedPlugins.StageAssemblyPluginPackage(
                            "Zulu.Duplicate.Sample",
                            "company.sample.duplicate",
                            "DuplicateSampleTypeKeyPlugin");

                        registry = new FlowNodeRegistry();
                        loader = new PluginLoader(
                            registry,
                            new Version(1, 0),
                            NullLoggerFactory.Instance);
                        report = loader.LoadAll(stagedPlugins.LoadPluginsDirectory);

                        if (!registry.TryCreateNodeByTypeKey("company.sample.nodes.value", out valueNode)
                            || valueNode == null)
                        {
                            return false;
                        }

                        var successfulLoad = report.Results.SingleOrDefault(result =>
                            string.Equals(result.PluginId, "company.sample.nodes", StringComparison.Ordinal)
                            && result.IsSuccess);
                        var duplicateFailure = report.Failures.SingleOrDefault(result =>
                            string.Equals(result.PluginId, "company.sample.duplicate", StringComparison.Ordinal));
                        var sampleEntryAssembly = successfulLoad?.Context?.Assemblies
                            .SingleOrDefault(assembly =>
                                string.Equals(
                                    assembly.GetName().Name,
                                    "NodeCraft.PluginSample",
                                    StringComparison.Ordinal));
                        var sampleEntryType = sampleEntryAssembly?.GetType(
                            "NodeCraft.PluginSample.Plugin.SamplePlugin",
                            throwOnError: false,
                            ignoreCase: false);
                        var pluginContractType = sampleEntryType?.GetInterfaces()
                            .SingleOrDefault(candidate =>
                                string.Equals(candidate.FullName, typeof(IFlowPlugin).FullName, StringComparison.Ordinal));
                        var registration = registry.Resolve("company.sample.nodes.value");

                        passed = report.Results.Count == 2
                            && report.Results.Count(result => result.IsSuccess) == 1
                            && duplicateFailure != null
                            && string.Equals(duplicateFailure.Phase, "registration", StringComparison.Ordinal)
                            && duplicateFailure.Exception.Message.Contains("company.sample.nodes.value", StringComparison.Ordinal)
                            && registration.NodeModelType != null
                            && string.Equals(
                                registration.NodeModelType.FullName,
                                "NodeCraft.PluginSample.Nodes.SampleValueNodeModel",
                                StringComparison.Ordinal)
                            && string.Equals(
                                valueNode.GetType().FullName,
                                "NodeCraft.PluginSample.Nodes.SampleValueNodeModel",
                                StringComparison.Ordinal)
                            && pluginContractType != null
                            && ReferenceEquals(pluginContractType.Assembly, typeof(IFlowPlugin).Assembly);
                    }
                    finally
                    {
                        valueNode = null;
                        loader = null;
                        registry = null;
                        UnloadPluginLoadContexts(ref report);
                    }
                }

                return passed;
            }
            finally
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                stagedPlugins.Dispose();
            }
        });
        Run("equal types", () => FlowTypeValidator.ValidateNodeInput("STRING", "STRING", strict: false) == true);
        Run("any wildcard received", () => FlowTypeValidator.ValidateNodeInput("*", "IMAGE", strict: false) == true);
        Run("any wildcard input", () => FlowTypeValidator.ValidateNodeInput("IMAGE", "*", strict: false) == true);
        Run("union overlap non-strict", () => FlowTypeValidator.ValidateNodeInput("STRING,BOOLEAN", "STRING,INT", strict: false) == true);
        Run("strict subset", () => FlowTypeValidator.ValidateNodeInput("STRING", "STRING,INT", strict: true) == true);
        Run("strict no subset", () => FlowTypeValidator.ValidateNodeInput("STRING,INT", "INT", strict: true) == false);
        Run("match type", () => FlowTypeValidator.ValidateNodeInput("MATCH_TYPE", "INT", strict: false) == true);
        Run("no overlap", () => FlowTypeValidator.ValidateNodeInput("STRING", "IMAGE", strict: false) == false);
        Run("IsCompatibleWith object wildcard + control-only pairing", () =>
            FlowDataType.String.IsCompatibleWith(FlowDataType.Object)
            && !FlowDataType.Control.IsCompatibleWith(FlowDataType.String));
        Run("socket slots always follow node definition", () =>
        {
            var definition = new FlowNodeDefinition
            {
                InputPorts =
                {
                    new FlowPortDefinition { Id = "flowIn", DisplayName = "Flow In", DataType = FlowDataType.Control },
                    new FlowPortDefinition { Id = "condition", DisplayName = "Condition", DataType = FlowDataType.Boolean },
                },
                OutputPorts =
                {
                    new FlowPortDefinition { Id = "true", DisplayName = "True", DataType = FlowDataType.Control },
                    new FlowPortDefinition { Id = "false", DisplayName = "False", DataType = FlowDataType.Control },
                },
            };
            var node = new NodeModel
            {
                ExecutorType = "test.if",
                InputParameters = new System.Collections.Generic.List<PortParameter>(),
                OutputParameters = new System.Collections.Generic.List<PortParameter>(),
            };

            var inputs = FlowSocketResolver.Resolve(node, definition, isInput: true);
            var outputs = FlowSocketResolver.Resolve(node, definition, isInput: false);
            return inputs.Count == 2
                && inputs[0].Slot == 0
                && inputs[1].Slot == 1
                && inputs[0].Definition.Id == "flowIn"
                && outputs.Count == 2
                && outputs[0].Slot == 0
                && outputs[1].Slot == 1
                && outputs[1].Definition.DisplayName == "False";
        });
        Run("socket label prefers definition display name", () =>
        {
            var definition = new FlowPortDefinition { Id = "output", DisplayName = "Sum" };
            return FlowSocketResolver.ResolveLabel(definition, null) == "Sum";
        });
        Run("control socket uses visible resting style", () =>
        {
            var style = FlowSocketResolver.ResolveVisualStyle(
                new FlowPortDefinition { DataType = FlowDataType.Control },
                null);
            return style.Diameter == 12
                && style.BrushResourceKey == "colorStatusWarningBackground3"
                && style.LabelOpacity == 1;
        });
        Run("all flow nodes are resizable by default", () =>
            NodeView.IsResizableProperty.DefaultMetadata.DefaultValue is bool value && value);
        Run("dragged node position is persisted to model", () =>
        {
            var node = new NodeModel { X = 12, Y = 24 };
            FlowCanvas.PersistNodePosition(node, 144, 160);
            return node.X == 144 && node.Y == 160;
        });
        Run("router keeps socket path outside endpoint nodes", () =>
        {
            var sourceBounds = new System.Windows.Rect(3, 54, 374, 162);
            var targetBounds = new System.Windows.Rect(843, 127, 517, 300);
            var result = OrthogonalRouter.Route(
                new System.Windows.Point(367, 138),
                new System.Windows.Point(855, 268),
                new System.Collections.Generic.List<System.Windows.Rect>
                {
                    sourceBounds,
                    targetBounds,
                },
                new System.Windows.Rect(0, 0, 1450, 560));
            var intermediatePoints = result.Points.Skip(1).Take(result.Points.Count - 2);
            return result.Success
                && result.Points.Count >= 2
                && result.Points[0] == new System.Windows.Point(367, 138)
                && result.Points[result.Points.Count - 1] == new System.Windows.Point(855, 268)
                && intermediatePoints.All(point => !sourceBounds.Contains(point) && !targetBounds.Contains(point));
        });
        Run("graph link roundtrip fields", () =>
        {
            var link = new GraphLink
            {
                Id = "l1",
                OriginNodeId = "n1",
                OriginSlot = 0,
                TargetNodeId = "n2",
                TargetSlot = 1,
            };
            var graph = new GraphModel
            {
                Links = new System.Collections.Generic.List<GraphLink> { link },
            };
            var port = new PortParameter { PortId = "inputA", LinkId = "l1" };
            return graph.Links[0].TargetSlot == 1
                && port.LinkId == "l1"
                && graph.Links[0].OriginSlot == 0;
        });

        Run("serialize v4 roundtrip", () =>
        {
            _ = NodeExecutorFactory.Registry;
            var graph = new GraphModel
            {
                Nodes = new System.Collections.Generic.List<NodeModel>
                {
                    new IntegerValueNodeModel
                    {
                        Id = "n1", Name = "IntA", X = 10, Y = 20,
                        InputParameters = new System.Collections.Generic.List<PortParameter>
                        {
                            new PortParameter { PortId = "flowIn", Parameter = new Parameter { ParameterType = "control" } },
                        },
                    },
                    new StringValueNodeModel
                    {
                        Id = "n2", Name = "Str", X = 200, Y = 20,
                        ValueText = "Hello",
                        InputParameters = new System.Collections.Generic.List<PortParameter>
                        {
                            new PortParameter { PortId = "flowIn", Parameter = new Parameter { ParameterType = "control" }, LinkId = "l1" },
                        },
                    },
                },
                Links = new System.Collections.Generic.List<GraphLink>
                {
                    new GraphLink { Id = "l1", OriginNodeId = "n1", OriginSlot = 0, TargetNodeId = "n2", TargetSlot = 0 },
                },
            };
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "flow-v4-" + System.Guid.NewGuid().ToString("N") + ".flow.xml");
            GraphModelXmlSerializer.Save(graph, path);
            var loaded = GraphModelXmlSerializer.Load(path);
            System.IO.File.Delete(path);
            return loaded.Links.Count == 1
                && loaded.Links[0].OriginSlot == 0
                && loaded.Nodes[1].InputParameters[0].LinkId == "l1";
        });

        await RunAsync("v4 links reconcile a missing target LinkId and execute", () =>
            AssertReconciledV4LinkExecutesAsync(targetLinkId: null));

        await RunAsync("v4 links replace an inconsistent target LinkId and execute", () =>
            AssertReconciledV4LinkExecutesAsync(targetLinkId: "stale-link"));

        Run("rejects a v4 link with an unknown target slot", () =>
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "flow-v4-invalid-target-slot-" + Guid.NewGuid().ToString("N") + ".flow.xml");

            try
            {
                File.WriteAllText(path, CreateIntegerPreviewGraphXml(targetLinkId: null, targetSlot: 99));
                try
                {
                    GraphModelXmlSerializer.LoadWithReport(path);
                    return false;
                }
                catch (InvalidOperationException ex)
                {
                    return ex.Message.Contains("target slot 99", StringComparison.Ordinal);
                }
            }
            finally
            {
                File.Delete(path);
            }
        });

        Run("rejects a v4 link with a malformed target slot", () =>
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "flow-v4-malformed-target-slot-" + Guid.NewGuid().ToString("N") + ".flow.xml");
            var xml = CreateIntegerPreviewGraphXml(targetLinkId: null, targetSlot: 1)
                .Replace("TargetSlot=\"1\"", "TargetSlot=\"not-an-integer\"", StringComparison.Ordinal);

            try
            {
                File.WriteAllText(path, xml);
                try
                {
                    GraphModelXmlSerializer.LoadWithReport(path);
                    return false;
                }
                catch (InvalidOperationException ex)
                {
                    return ex.Message.Contains("TargetSlot", StringComparison.Ordinal)
                        && ex.Message.Contains("valid integer", StringComparison.Ordinal);
                }
            }
            finally
            {
                File.Delete(path);
            }
        });

        Run("rejects v3 graph format", () =>
        {
            var xml = @"<Graph FormatVersion=""3"">
  <Nodes />
  <Links />
</Graph>";
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "flow-v3-rejected-" + System.Guid.NewGuid().ToString("N") + ".flow.xml");

            try
            {
                System.IO.File.WriteAllText(path, xml);
                try
                {
                    GraphModelXmlSerializer.LoadWithReport(path);
                    return false;
                }
                catch (InvalidOperationException ex)
                {
                    return ex.Message.Contains("Current format is v4");
                }
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        });

        Run("rejects v4 graph containing legacy Connections", () =>
        {
            var xml = @"<Graph FormatVersion=""4"">
  <Nodes />
  <Links />
  <Connections />
</Graph>";
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "flow-v4-connections-rejected-" + System.Guid.NewGuid().ToString("N") + ".flow.xml");

            try
            {
                System.IO.File.WriteAllText(path, xml);
                try
                {
                    GraphModelXmlSerializer.LoadWithReport(path);
                    return false;
                }
                catch (InvalidOperationException ex)
                {
                    return ex.Message.Contains("Legacy Connections graphs are unsupported");
                }
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        });

        Run("v4 loads a registered node by stable ExecutorType when ModelType was renamed", () =>
        {
            var xml = @"<Graph FormatVersion=""4"">
  <Nodes>
    <Node ModelType=""Renamed.Plugin.IntegerValueNodeModel, Renamed.Plugin"" Id=""stable"" Name=""Stable"" X=""11"" Y=""12"" Width=""210"" Height=""130"" ExecutorType=""node.integer-value""><InputPorts /><OutputPorts /><Properties><Property Name=""IntegerValue"" Type=""System.Int32"" Value=""42"" /></Properties></Node>
  </Nodes>
  <Links />
</Graph>";
            var path = Path.Combine(
                Path.GetTempPath(),
                "flow-v4-stable-executor-type-" + Guid.NewGuid().ToString("N") + ".flow.xml");

            try
            {
                File.WriteAllText(path, xml);
                var result = GraphModelXmlSerializer.LoadWithReport(path);
                var node = result.Graph.Nodes.SingleOrDefault();
                return result.FormatVersion == 4
                    && node is IntegerValueNodeModel integerNode
                    && node.Id == "stable"
                    && node.Name == "Stable"
                    && node.X == 11
                    && node.Y == 12
                    && node.Width == 210
                    && node.Height == 130
                    && node.ExecutorType == "node.integer-value"
                    && integerNode.IntegerValue == 42;
            }
            finally
            {
                File.Delete(path);
            }
        });

        Run("graph serializer logs save, load, and failure through ILogger", () =>
        {
            var logDirectory = Path.Combine(
                Path.GetTempPath(),
                "nodecraft-serializer-log-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(logDirectory);
            var graphPath = Path.Combine(logDirectory, "graph.flow.xml");

            try
            {
                var nlogConfig = new LoggingConfiguration();
                var fileTarget = new FileTarget("serializer-log")
                {
                    FileName = Path.Combine(logDirectory, "serializer.log"),
                    Layout = "${logger}|${level:uppercase=true}|${message}",
                };
                nlogConfig.AddTarget(fileTarget);
                nlogConfig.AddRule(NLog.LogLevel.Debug, NLog.LogLevel.Fatal, fileTarget);

                using var loggerFactory = LoggerFactory.Create(builder => builder.AddNLog(nlogConfig));
                var logger = loggerFactory.CreateLogger("NodeCraft.Flow.GraphModelXmlSerializer");

                var graph = new GraphModel
                {
                    Nodes = new List<NodeModel>(),
                    Links = new List<GraphLink>(),
                };
                GraphModelXmlSerializer.Save(graph, graphPath, logger);
                var loadResult = GraphModelXmlSerializer.LoadWithReport(graphPath, logger);

                var missingPath = Path.Combine(logDirectory, "missing.flow.xml");
                var threw = false;
                try
                {
                    GraphModelXmlSerializer.LoadWithReport(missingPath, logger);
                }
                catch (FileNotFoundException)
                {
                    threw = true;
                }

                LogManager.Shutdown();
                var contents = File.ReadAllText(Path.Combine(logDirectory, "serializer.log"));
                return threw
                    && loadResult.FormatVersion == GraphModelXmlSerializer.CurrentFormatVersion
                    && contents.Contains("Saved graph to", StringComparison.Ordinal)
                    && contents.Contains("Loaded graph from", StringComparison.Ordinal)
                    && contents.Contains("Failed to load graph from", StringComparison.Ordinal);
            }
            finally
            {
                LogManager.Shutdown();
                if (Directory.Exists(logDirectory))
                {
                    Directory.Delete(logDirectory, recursive: true);
                }
            }
        });

        await RunAsync("executor if false branch skipped", async () =>
        {
            var workflow = BuildIfWorkflow(43); // 42 > 43 = false
            var executor = new GraphExecutor(workflow);
            var validation = executor.Validate();
            if (!validation.IsValid) return false;
            var context = await executor.ExecuteAsync();
            return context.Statuses["n5"] == FlowNodeExecutionStatus.Skipped;
        });

        await RunAsync("executor if true branch runs", async () =>
        {
            var workflow = BuildIfWorkflow(40); // 42 > 40 = true
            var executor = new GraphExecutor(workflow);
            var context = await executor.ExecuteAsync();
            return context.Statuses["n5"] == FlowNodeExecutionStatus.Succeeded
                && context.TryGetPortValue("n5", 0, out var value)
                && (string)value == "YES";
        });

        await RunAsync("executor logs execution through ILogger", async () =>
        {
            var logDirectory = Path.Combine(
                Path.GetTempPath(),
                "nodecraft-executor-log-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(logDirectory);

            try
            {
                var nlogConfig = new LoggingConfiguration();
                var fileTarget = new FileTarget("executor-log")
                {
                    FileName = Path.Combine(logDirectory, "executor.log"),
                    Layout = "${logger}|${level:uppercase=true}|${message}",
                };
                nlogConfig.AddTarget(fileTarget);
                nlogConfig.AddRule(NLog.LogLevel.Debug, NLog.LogLevel.Fatal, fileTarget);

                using var loggerFactory = LoggerFactory.Create(builder => builder.AddNLog(nlogConfig));
                var workflow = BuildIfWorkflow(40);
                var executor = new GraphExecutor(workflow, logger: loggerFactory.CreateLogger<GraphExecutor>());
                var context = await executor.ExecuteAsync();

                LogManager.Shutdown();
                var contents = File.ReadAllText(Path.Combine(logDirectory, "executor.log"));
                return context.Statuses["n5"] == FlowNodeExecutionStatus.Succeeded
                    && contents.Contains("Graph execution started", StringComparison.Ordinal)
                    && contents.Contains("Graph execution finished", StringComparison.Ordinal)
                    && contents.Contains("Executing node", StringComparison.Ordinal);
            }
            finally
            {
                LogManager.Shutdown();
                if (Directory.Exists(logDirectory))
                {
                    Directory.Delete(logDirectory, recursive: true);
                }
            }
        });

        Run("FlowPage logs graph validation failure through ILogger", () =>
            RunOnSta(() =>
            {
                var logDirectory = Path.Combine(
                    Path.GetTempPath(),
                    "nodecraft-flowpage-validate-log-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(logDirectory);

                try
                {
                    var nlogConfig = new LoggingConfiguration();
                    var fileTarget = new FileTarget("flowpage-validate-log")
                    {
                        FileName = Path.Combine(logDirectory, "flowpage.log"),
                        Layout = "${logger}|${level:uppercase=true}|${message}${onexception:inner= |${exception:format=tostring}}",
                    };
                    nlogConfig.AddTarget(fileTarget);
                    nlogConfig.AddRule(NLog.LogLevel.Debug, NLog.LogLevel.Fatal, fileTarget);

                    using var loggerFactory = LoggerFactory.Create(builder => builder.AddNLog(nlogConfig));
                    var page = new FlowPage(loggerFactory);
                    var canvas = GetNodeCanvas(page);
                    canvas.GraphModel = CreateDuplicateIdGraph();
                    page.ValidateGraph();

                    LogManager.Shutdown();
                    var contents = File.ReadAllText(Path.Combine(logDirectory, "flowpage.log"));
                    return contents.Contains("Failed to validate graph", StringComparison.Ordinal)
                        && contents.Contains("duplicate node Id", StringComparison.Ordinal)
                        && GetExecutionResult(page).Text.Contains("duplicate node Id", StringComparison.Ordinal);
                }
                finally
                {
                    LogManager.Shutdown();
                    if (Directory.Exists(logDirectory))
                    {
                        Directory.Delete(logDirectory, recursive: true);
                    }
                }
            }));

        Run("FlowPage logs graph run failure through ILogger", () =>
            RunOnSta(() =>
            {
                var logDirectory = Path.Combine(
                    Path.GetTempPath(),
                    "nodecraft-flowpage-run-log-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(logDirectory);

                try
                {
                    var nlogConfig = new LoggingConfiguration();
                    var fileTarget = new FileTarget("flowpage-run-log")
                    {
                        FileName = Path.Combine(logDirectory, "flowpage.log"),
                        Layout = "${logger}|${level:uppercase=true}|${message}${onexception:inner= |${exception:format=tostring}}",
                    };
                    nlogConfig.AddTarget(fileTarget);
                    nlogConfig.AddRule(NLog.LogLevel.Debug, NLog.LogLevel.Fatal, fileTarget);

                    using var loggerFactory = LoggerFactory.Create(builder => builder.AddNLog(nlogConfig));
                    var page = new FlowPage(loggerFactory);
                    var canvas = GetNodeCanvas(page);
                    canvas.GraphModel = CreateDuplicateIdGraph();
                    page.RunGraph();

                    LogManager.Shutdown();
                    var contents = File.ReadAllText(Path.Combine(logDirectory, "flowpage.log"));
                    return contents.Contains("Graph execution failed", StringComparison.Ordinal)
                        && contents.Contains("duplicate node Id", StringComparison.Ordinal)
                        && GetExecutionResult(page).Text.Contains("duplicate node Id", StringComparison.Ordinal);
                }
                finally
                {
                    LogManager.Shutdown();
                    if (Directory.Exists(logDirectory))
                    {
                        Directory.Delete(logDirectory, recursive: true);
                    }
                }
            }));

        Run("NodeCraft logs unhandled exceptions with their source", () =>
        {
            var logDirectory = Path.Combine(
                Path.GetTempPath(),
                "nodecraft-unhandled-log-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(logDirectory);

            try
            {
                var nlogConfig = new LoggingConfiguration();
                var fileTarget = new FileTarget("unhandled-log")
                {
                    FileName = Path.Combine(logDirectory, "unhandled.log"),
                    Layout = "${logger}|${level:uppercase=true}|${message}${onexception:inner= |${exception:format=tostring}}",
                };
                nlogConfig.AddTarget(fileTarget);
                nlogConfig.AddRule(NLog.LogLevel.Debug, NLog.LogLevel.Fatal, fileTarget);

                using var loggerFactory = LoggerFactory.Create(builder => builder.AddNLog(nlogConfig));
                var logger = loggerFactory.CreateLogger("NodeCraft.App");
                var method = typeof(App).GetMethod(
                    "LogUnhandledException",
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (method == null)
                {
                    return false;
                }

                method.Invoke(null, new object[] { logger, "Dispatcher", new InvalidOperationException("boom-marker") });
                LogManager.Shutdown();

                var contents = File.ReadAllText(Path.Combine(logDirectory, "unhandled.log"));
                return contents.Contains("Unhandled exception (Dispatcher)", StringComparison.Ordinal)
                    && contents.Contains("boom-marker", StringComparison.Ordinal);
            }
            finally
            {
                LogManager.Shutdown();
                if (Directory.Exists(logDirectory))
                {
                    Directory.Delete(logDirectory, recursive: true);
                }
            }
        });

        Run("NodeCraft fallback logging configuration writes exceptions to the log directory", () =>
        {
            var method = typeof(App).GetMethod(
                "BuildFallbackLoggingConfiguration",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null)
            {
                return false;
            }

            var fallbackConfiguration = method.Invoke(null, null) as LoggingConfiguration;
            if (fallbackConfiguration == null)
            {
                return false;
            }

            var target = fallbackConfiguration.FindTargetByName<FileTarget>("nodecraft-fallback-file");
            if (target == null)
            {
                return false;
            }

            var layoutEvent = new LogEventInfo(NLog.LogLevel.Error, "NodeCraft.Test", "Layout test.");
            layoutEvent.Exception = new InvalidOperationException("layout-marker");
            var renderedLayout = target.Layout.Render(layoutEvent);
            var layoutIncludesProcessContext = renderedLayout.Contains(
                Environment.ProcessId.ToString(),
                StringComparison.Ordinal)
                && renderedLayout.Contains("layout-marker", StringComparison.Ordinal);

            LogManager.Configuration = fallbackConfiguration;
            LogManager.GetLogger("NodeCraft.Logging")
                .Error(new InvalidOperationException("fallback-marker"), "Fallback log write.");
            LogManager.Shutdown();

            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NodeCraft",
                "Logs",
                "nodecraft-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log");

            if (!File.Exists(logPath))
            {
                return false;
            }

            var contents = File.ReadAllText(logPath);
            return layoutIncludesProcessContext
                && contents.Contains("Fallback log write.", StringComparison.Ordinal)
                && contents.Contains("fallback-marker", StringComparison.Ordinal);
        });

        Console.WriteLine(_failures == 0 ? "ALL PASS" : $"{_failures} FAILURES");
        return _failures == 0 ? 0 : 1;
    }

    private static bool AssertInvalidManifest(string manifestPath, string pluginDirectory, string fieldName)
    {
        try
        {
            PluginManifestReader.Read(manifestPath, new Version(1, 0));
            return false;
        }
        catch (InvalidDataException ex)
        {
            var canonicalPluginDirectory = Path.GetFullPath(pluginDirectory);
            return ex.Message.Contains(canonicalPluginDirectory, StringComparison.OrdinalIgnoreCase)
                && ex.Message.Contains(fieldName, StringComparison.Ordinal);
        }
    }

    private static bool AssertManifestReadFailure(
        string manifestPath,
        string pluginDirectory,
        Type expectedInnerExceptionType)
    {
        try
        {
            PluginManifestReader.Read(manifestPath, new Version(1, 0));
            return false;
        }
        catch (InvalidDataException ex)
        {
            var canonicalManifestPath = Path.GetFullPath(manifestPath);
            var canonicalPluginDirectory = Path.GetFullPath(pluginDirectory);
            return ex.Message.Contains(canonicalPluginDirectory, StringComparison.OrdinalIgnoreCase)
                && ex.Message.Contains(canonicalManifestPath, StringComparison.OrdinalIgnoreCase)
                && ex.Message.Contains("manifest", StringComparison.Ordinal)
                && ex.InnerException != null
                && expectedInnerExceptionType.IsAssignableFrom(ex.InnerException.GetType());
        }
    }

    private static string CreateTemporaryPluginDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CopyFileToDirectory(string sourcePath, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        var targetPath = Path.Combine(targetDirectory, Path.GetFileName(sourcePath));
        File.Copy(sourcePath, targetPath, overwrite: true);
        return targetPath;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        Exception lastFailure = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException ex)
            {
                lastFailure = ex;
            }
            catch (UnauthorizedAccessException ex)
            {
                lastFailure = ex;
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            System.Threading.Thread.Sleep(50);
        }

        if (Directory.Exists(path))
        {
            throw new IOException("Failed to delete temporary test directory: " + path, lastFailure);
        }
    }

    private static void RegisterDeferredCleanup(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        lock (DeferredCleanupDirectories)
        {
            DeferredCleanupDirectories.Add(path);
        }
    }

    private static void UnloadPluginLoadContexts(ref PluginLoadReport report)
    {
        if (report == null)
        {
            return;
        }

        var contexts = report.Results
            .Where(result => result?.Context != null)
            .Select(result => result.Context)
            .ToArray();
        var weakReferences = new WeakReference[contexts.Length];
        for (var index = 0; index < contexts.Length; index++)
        {
            contexts[index].Unload();
            weakReferences[index] = new WeakReference(contexts[index]);
            contexts[index] = null;
        }

        report = null;

        for (var attempt = 0; attempt < 10 && weakReferences.Any(reference => reference.IsAlive); attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    private static void UnloadAssemblyLoadContext(AssemblyLoadContext context)
    {
        if (context == null)
        {
            return;
        }

        var weakReference = new WeakReference(context);
        context.Unload();
        for (var attempt = 0; weakReference.IsAlive && attempt < 10; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    private static bool AssertPrivateAssemblyLoadedFromContext(
        AssemblyLoadContext context,
        string assemblyName,
        string expectedPath)
    {
        var privateAssembly = context.LoadFromAssemblyName(new AssemblyName(assemblyName));
        return privateAssembly != null
            && string.Equals(
                privateAssembly.Location,
                expectedPath,
                StringComparison.OrdinalIgnoreCase)
            && ReferenceEquals(
                AssemblyLoadContext.GetLoadContext(privateAssembly),
                context);
    }

    private static string FindBuiltPrivateDependencyAssembly()
    {
        return FindRepositoryFile(
            "NodeCraft.PluginSample",
            "PrivateDependency",
            "bin",
            GetBuildMetadata("BuildConfiguration"),
            GetBuildMetadata("BuildTargetFramework"),
            "NodeCraft.PluginSample.PrivateDependency.dll");
    }

    private static string FindBuiltSamplePluginAssembly()
    {
        return Path.Combine(
            FindBuiltSamplePluginOutputDirectory(),
            "NodeCraft.PluginSample.dll");
    }

    private static string FindBuiltSamplePluginManifest()
    {
        return Path.Combine(
            FindBuiltSamplePluginOutputDirectory(),
            "plugin.json");
    }

    private static string FindBuiltSamplePluginOutputDirectory()
    {
        var assemblyPath = FindRepositoryFile(
            "NodeCraft.PluginSample",
            "bin",
            GetBuildMetadata("BuildConfiguration"),
            GetBuildMetadata("BuildTargetFramework"),
            "NodeCraft.PluginSample.dll");
        return Path.GetDirectoryName(assemblyPath)
            ?? throw new InvalidOperationException("Sample plugin output directory was not found.");
    }

    private static string GetBuildMetadata(string key)
    {
        var value = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal))?
            .Value;
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Build metadata '{key}' was not generated.");
    }

    private static string StageSamplePluginPackage(string pluginsDirectory, string directoryName = "Company.Sample.Nodes")
    {
        var pluginDirectory = Path.Combine(pluginsDirectory, directoryName);
        Directory.CreateDirectory(pluginDirectory);
        CopyFileToDirectory(FindBuiltSamplePluginAssembly(), pluginDirectory);
        CopyFileToDirectory(FindBuiltSamplePluginManifest(), pluginDirectory);
        CopyFileToDirectory(FindBuiltPrivateDependencyAssembly(), Path.Combine(pluginDirectory, "lib"));
        return pluginDirectory;
    }

    private static FlowNodeRegistry CreateRegistryWithBuiltInStringValue()
    {
        var registry = new FlowNodeRegistry();
        registry.Register(NodeExecutorFactory.ResolveRegistration("node.string-value"));
        return registry;
    }

    private static string InvokeNativeProbePath(PluginLoadContext context, string unmanagedDllName)
    {
        var probeMethod = typeof(PluginLoadContext).GetMethod(
            "ProbeNativeLibraryPath",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return probeMethod == null
            ? null
            : probeMethod.Invoke(context, new object[] { unmanagedDllName }) as string;
    }

    private static Assembly? InvokeManagedLoad(PluginLoadContext context, AssemblyName assemblyName)
    {
        var loadMethod = typeof(PluginLoadContext).GetMethod(
            "Load",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(AssemblyName) },
            modifiers: null);
        return loadMethod?.Invoke(context, new object[] { assemblyName }) as Assembly;
    }

    private static IReadOnlyCollection<string>? InvokeTrustedPlatformAssemblyInventory(
        string trustedPlatformAssemblies,
        IEnumerable<string> frameworkDirectoryCandidates)
    {
        var inventoryMethod = typeof(PluginLoadContext).GetMethod(
            "CreateTrustedPlatformAssemblyNames",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(string), typeof(IEnumerable<string>) },
            modifiers: null);
        return inventoryMethod?.Invoke(
            null,
            new object[] { trustedPlatformAssemblies, frameworkDirectoryCandidates })
            as IReadOnlyCollection<string>;
    }

    private static string? InvokeVerifiedSharedFrameworkDirectory(
        string frameworkDirectory,
        IReadOnlyCollection<string> knownDotnetInstallationRoots)
    {
        var verificationMethod = typeof(PluginLoadContext).GetMethod(
            "TryGetVerifiedSharedFrameworkDirectory",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(string), typeof(IReadOnlyCollection<string>) },
            modifiers: null);
        return verificationMethod?.Invoke(
            null,
            new object[] { frameworkDirectory, knownDotnetInstallationRoots })
            as string;
    }

    private static bool InvokeTrustedPlatformAssemblyName(
        string assemblyName,
        IReadOnlyCollection<string> trustedAssemblyNames)
    {
        var trustedAssemblyMethod = typeof(PluginLoadContext).GetMethod(
            "IsTrustedPlatformAssemblyName",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(string), typeof(IReadOnlyCollection<string>) },
            modifiers: null);
        return trustedAssemblyMethod?.Invoke(
            null,
            new object[] { assemblyName, trustedAssemblyNames })
            is true;
    }

    private static string WritePluginManifest(
        string pluginDirectory,
        string id,
        string entryAssembly,
        string entryType,
        string apiVersion,
        string privateLibraryPath)
    {
        var manifestPath = Path.Combine(pluginDirectory, "plugin.json");
        File.WriteAllText(
            manifestPath,
            "{\n"
            + "  \"id\": \"" + EscapeJson(id) + "\",\n"
            + "  \"entryAssembly\": \"" + EscapeJson(entryAssembly) + "\",\n"
            + "  \"entryType\": \"" + EscapeJson(entryType) + "\",\n"
            + "  \"apiVersion\": \"" + EscapeJson(apiVersion) + "\",\n"
            + "  \"privateLibraryPath\": \"" + EscapeJson(privateLibraryPath) + "\"\n"
            + "}");
        return manifestPath;
    }

    private static void StageTaskFiveFixturePackages(string pluginsDirectory)
    {
        var entryAssemblyPath = Assembly.GetExecutingAssembly().Location;
        var entryAssemblyName = Path.GetFileName(entryAssemblyPath);
        var privateDependencyPath = FindBuiltPrivateDependencyAssembly();

        StageFixturePluginPackage(
            Path.Combine(pluginsDirectory, "Alpha-invalid-manifest"),
            entryAssemblyPath,
            privateDependencyPath,
            manifestWriter: pluginDirectory =>
                File.WriteAllText(Path.Combine(pluginDirectory, "plugin.json"), "{ invalid json"));

        StageFixturePluginPackage(
            Path.Combine(pluginsDirectory, "bravo-missing-dependency"),
            entryAssemblyPath,
            privateDependencyPath,
            includePrivateDependency: false,
            manifestWriter: pluginDirectory => WritePluginManifest(
                pluginDirectory,
                "test.failed-duplicate.plugin",
                entryAssemblyName,
                "MissingDependencyFixturePlugin",
                "1.0",
                "lib"));

        StageFixturePluginPackage(
            Path.Combine(pluginsDirectory, "charlie-throwing"),
            entryAssemblyPath,
            privateDependencyPath,
            manifestWriter: pluginDirectory => WritePluginManifest(
                pluginDirectory,
                "test.throwing.plugin",
                entryAssemblyName,
                "ThrowingFixturePlugin",
                "1.0",
                "lib"));

        StageFixturePluginPackage(
            Path.Combine(pluginsDirectory, "delta-valid"),
            entryAssemblyPath,
            privateDependencyPath,
            manifestWriter: pluginDirectory => WritePluginManifest(
                pluginDirectory,
                "test.valid.plugin",
                entryAssemblyName,
                "ValidFixturePlugin",
                "1.0",
                "lib"));

        StageFixturePluginPackage(
            Path.Combine(pluginsDirectory, "echo-duplicate"),
            entryAssemblyPath,
            privateDependencyPath,
            manifestWriter: pluginDirectory => WritePluginManifest(
                pluginDirectory,
                "test.failed-duplicate.plugin",
                entryAssemblyName,
                "DuplicateFailedIdFixturePlugin",
                "1.0",
                "lib"));

        StageFixturePluginPackage(
            Path.Combine(pluginsDirectory, "foxtrot-duplicate-node"),
            entryAssemblyPath,
            privateDependencyPath,
            manifestWriter: pluginDirectory => WritePluginManifest(
                pluginDirectory,
                "test.duplicate.plugin",
                entryAssemblyName,
                "DuplicateFixturePlugin",
                "1.0",
                "lib"));
    }

    private static void StageFixturePluginPackage(
        string pluginDirectory,
        string entryAssemblyPath,
        string privateDependencyPath,
        Action<string> manifestWriter,
        bool includePrivateDependency = true)
    {
        Directory.CreateDirectory(pluginDirectory);
        CopyFileToDirectory(entryAssemblyPath, pluginDirectory);

        if (includePrivateDependency)
        {
            CopyFileToDirectory(
                privateDependencyPath,
                Path.Combine(pluginDirectory, "lib"));
        }
        else
        {
            Directory.CreateDirectory(Path.Combine(pluginDirectory, "lib"));
        }

        manifestWriter(pluginDirectory);
    }

    private static string EscapeJson(string value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static IReadOnlyCollection<string> CreateSharedAssemblyNames()
    {
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            typeof(IFlowPlugin).Assembly.GetName().Name,
            typeof(CommonControls.WPF.CommonControlTheme).Assembly.GetName().Name,
            typeof(System.Windows.FrameworkElement).Assembly.GetName().Name,
            typeof(System.Windows.DependencyObject).Assembly.GetName().Name,
            typeof(System.Windows.Markup.MarkupExtension).Assembly.GetName().Name,
        };
    }

    private static XElement CreateSerializedPort(string portId, string direction, string parameterType, string linkId)
    {
        return new XElement("Port",
            new XAttribute("PortId", portId),
            new XAttribute("Direction", direction),
            new XAttribute("ParameterType", parameterType),
            new XAttribute("Value", string.Empty),
            new XAttribute("LinkId", linkId));
    }

    private static XElement CreateSerializedProperty(string name, string type, string value)
    {
        return new XElement("Property",
            new XAttribute("Name", name),
            new XAttribute("Type", type),
            new XAttribute("Value", value));
    }

    private static async Task<bool> AssertReconciledV4LinkExecutesAsync(string? targetLinkId)
    {
        _ = NodeExecutorFactory.Registry;
        var path = Path.Combine(
            Path.GetTempPath(),
            "flow-v4-reconciled-link-" + Guid.NewGuid().ToString("N") + ".flow.xml");

        try
        {
            File.WriteAllText(path, CreateIntegerPreviewGraphXml(targetLinkId, targetSlot: 1));
            var graph = GraphModelXmlSerializer.Load(path);
            var targetPort = graph.Nodes
                .Single(node => node.Id == "preview")
                .InputParameters
                .Single(port => port.PortId == "input");
            if (targetPort.LinkId != "value-preview")
            {
                return false;
            }

            var workflow = GraphModelWorkflowAdapter.Convert(graph);
            var workflowInput = workflow.Nodes.Single(node => node.Id == "preview").Inputs["input"] as LinkRef;
            if (workflowInput?.SourceNodeId != "value" || workflowInput.SourceSlot != 0)
            {
                return false;
            }

            var executor = new GraphExecutor(workflow);
            if (!executor.Validate().IsValid)
            {
                return false;
            }

            var context = await executor.ExecuteAsync();
            return context.Statuses["value"] == FlowNodeExecutionStatus.Succeeded
                && context.Statuses["preview"] == FlowNodeExecutionStatus.Succeeded
                && context.TryGetPortValue("preview", 0, out var value)
                && value is int integerValue
                && integerValue == 42;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateIntegerPreviewGraphXml(string? targetLinkId, int targetSlot)
    {
        var targetPort = new XElement("Port",
            new XAttribute("PortId", "input"),
            new XAttribute("Direction", "Left"),
            new XAttribute("ParameterType", "object"),
            new XAttribute("Value", string.Empty));
        if (targetLinkId != null)
        {
            targetPort.Add(new XAttribute("LinkId", targetLinkId));
        }

        return new XDocument(
            new XElement("Graph",
                new XAttribute("FormatVersion", "4"),
                new XElement("Nodes",
                    new XElement("Node",
                        new XAttribute("ModelType", "NodeCraft.Flow.Nodes.IntegerValueNodeModel, NodeCraft.Flow"),
                        new XAttribute("Id", "value"),
                        new XAttribute("Name", "Value"),
                        new XAttribute("X", "0"),
                        new XAttribute("Y", "0"),
                        new XAttribute("Width", "200"),
                        new XAttribute("Height", "120"),
                        new XAttribute("ExecutorType", "node.integer-value"),
                        new XElement("InputPorts",
                            CreateSerializedPort("flowIn", "Top", "control", string.Empty)),
                        new XElement("OutputPorts",
                            CreateSerializedPort("output", "Right", "number", string.Empty)),
                        new XElement("Properties",
                            CreateSerializedProperty("IntegerValue", "System.Int32", "42"))),
                    new XElement("Node",
                        new XAttribute("ModelType", "NodeCraft.Flow.Nodes.TextPreviewNodeModel, NodeCraft.Flow"),
                        new XAttribute("Id", "preview"),
                        new XAttribute("Name", "Preview"),
                        new XAttribute("X", "240"),
                        new XAttribute("Y", "0"),
                        new XAttribute("Width", "200"),
                        new XAttribute("Height", "120"),
                        new XAttribute("ExecutorType", "node.text-preview"),
                        new XElement("InputPorts",
                            CreateSerializedPort("flowIn", "Top", "control", string.Empty),
                            targetPort),
                        new XElement("OutputPorts",
                            CreateSerializedPort("output", "Right", "object", string.Empty)),
                        new XElement("Properties"))),
                new XElement("Links",
                    new XElement("Link",
                        new XAttribute("Id", "value-preview"),
                        new XAttribute("OriginNodeId", "value"),
                        new XAttribute("OriginSlot", "0"),
                        new XAttribute("TargetNodeId", "preview"),
                        new XAttribute("TargetSlot", targetSlot)))))
            .ToString(SaveOptions.DisableFormatting);
    }

    private static WorkflowDocument BuildIfWorkflow(int valueB)
    {
        // n1: IntegerValue(42); n2: IntegerValue(valueB); n3: GreaterThan; n4: If; n5: StringValue on true; n6: TextPreview
        _ = NodeExecutorFactory.Registry;
        var doc = new WorkflowDocument();

        doc.Nodes.Add(new WorkflowNode { Id = "n1", TypeKey = "node.integer-value", DisplayName = "IntA", Inputs = { ["value"] = 42 } });
        doc.Nodes.Add(new WorkflowNode { Id = "n2", TypeKey = "node.integer-value", DisplayName = "IntB", Inputs = { ["value"] = valueB } });
        doc.Nodes.Add(new WorkflowNode
        {
            Id = "n3",
            TypeKey = "node.greater-than",
            DisplayName = "GT",
            Inputs =
            {
                ["inputA"] = new LinkRef { SourceNodeId = "n1", SourceSlot = 0 },
                ["inputB"] = new LinkRef { SourceNodeId = "n2", SourceSlot = 0 },
            },
        });
        doc.Nodes.Add(new WorkflowNode
        {
            Id = "n4",
            TypeKey = "node.if",
            DisplayName = "If",
            Inputs =
            {
                ["condition"] = new LinkRef { SourceNodeId = "n3", SourceSlot = 0 },
            },
        });
        doc.Nodes.Add(new WorkflowNode
        {
            Id = "n5",
            TypeKey = "node.string-value",
            DisplayName = "TrueStr",
            Inputs =
            {
                ["value"] = "YES",
                ["flowIn"] = new LinkRef { SourceNodeId = "n4", SourceSlot = 0 },
            },
        });

        return doc;
    }

    private static void Run(string name, Func<bool> assertion)
    {
        try
        {
            if (assertion())
            {
                Console.WriteLine($"PASS {name}");
            }
            else
            {
                _failures++;
                Console.WriteLine($"FAIL {name}");
            }
        }
        catch (Exception ex)
        {
            _failures++;
            Console.WriteLine($"FAIL {name} ({ex.GetType().Name}: {ex.Message})");
        }
    }

    private static async Task RunAsync(string name, Func<Task<bool>> assertion)
    {
        try
        {
            if (await assertion())
            {
                Console.WriteLine($"PASS {name}");
            }
            else
            {
                _failures++;
                Console.WriteLine($"FAIL {name}");
            }
        }
        catch (Exception ex)
        {
            _failures++;
            Console.WriteLine($"FAIL {name} ({ex.GetType().Name}: {ex.Message})");
        }
    }

    private static GraphModel CreateStartupGraph()
    {
        return new GraphModel
        {
            Nodes = new System.Collections.Generic.List<NodeModel>
            {
                new StringValueNodeModel
                {
                    Id = "loaded-startup",
                    Name = "Loaded startup graph",
                    X = 100,
                    Y = 100,
                    ValueText = "preserve this graph",
                },
            },
            Links = new System.Collections.Generic.List<GraphLink>(),
        };
    }

    private static GraphModel CreateDuplicateIdGraph()
    {
        return new GraphModel
        {
            Nodes = new System.Collections.Generic.List<NodeModel>
            {
                new StringValueNodeModel
                {
                    Id = "dup",
                    Name = "A",
                },
                new StringValueNodeModel
                {
                    Id = "dup",
                    Name = "B",
                },
            },
            Links = new System.Collections.Generic.List<GraphLink>(),
        };
    }

    private static FlowNodeRegistration CreateTestRegistration(string typeKey)
    {
        return new FlowNodeRegistration(
            new FlowNodeDefinition
            {
                TypeKey = typeKey,
                DisplayName = typeKey,
                Category = "Test",
                OutputPorts =
                {
                    new FlowPortDefinition
                    {
                        Id = "output",
                        DisplayName = "Output",
                        IOType = EIOType.Output,
                        DataType = FlowDataType.String,
                        PreferredDirection = EPortDirection.Right,
                    },
                },
            },
            () => new StringValueExecutor())
        {
            ShowInPalette = false,
        };
    }

    private static FlowCanvas GetNodeCanvas(FlowPage page)
    {
        var field = typeof(FlowPage)
            .GetField("_nodeCanvas", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("FlowPage _nodeCanvas field was not generated.");
        return field.GetValue(page) as FlowCanvas
            ?? throw new InvalidOperationException("FlowPage _nodeCanvas was not initialized.");
    }

    private static System.Windows.Controls.TextBox GetExecutionResult(FlowPage page)
    {
        var field = typeof(FlowPage)
            .GetField("TxtExecutionResult", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("FlowPage result field was not generated.");
        return field.GetValue(page) as System.Windows.Controls.TextBox
            ?? throw new InvalidOperationException("FlowPage result text box was not initialized.");
    }

    private static System.Windows.Controls.TextBlock GetCurrentFilePath(FlowPage page)
    {
        var field = typeof(FlowPage)
            .GetField("TxtCurrentFilePath", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("FlowPage current file path field was not generated.");
        return field.GetValue(page) as System.Windows.Controls.TextBlock
            ?? throw new InvalidOperationException("FlowPage current file path text block was not initialized.");
    }

    private static T? GetFieldValue<T>(object target, string fieldName)
        where T : class
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(target) as T;
    }

    private static T? FindLogicalDescendant<T>(System.Windows.DependencyObject root)
        where T : class
    {
        if (root is T match)
        {
            return match;
        }

        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root))
        {
            if (child is not System.Windows.DependencyObject dependencyObject)
            {
                continue;
            }

            var descendant = FindLogicalDescendant<T>(dependencyObject);
            if (descendant != null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static string FindRepositoryFile(params string[] pathSegments)
    {
        foreach (var startPath in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(startPath);
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, Path.Combine(pathSegments));
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }
        }

        throw new FileNotFoundException($"Could not locate repository file: {string.Join(Path.DirectorySeparatorChar, pathSegments)}");
    }

    private static bool IsBuildOutputPath(string path)
    {
        return path
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
    }

    private static bool RunOnSta(Func<bool> assertion)
    {
        var result = false;
        Exception? failure = null;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                result = assertion();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null)
        {
            throw new InvalidOperationException(failure.ToString(), failure);
        }

        return result;
    }

    private static bool RunWithTemplatedFlowCanvas(
        Func<FlowCanvas, System.Windows.Controls.Grid, System.Windows.Controls.Canvas, bool> assertion)
    {
        var window = new System.Windows.Window
        {
            Width = 640,
            Height = 480,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = System.Windows.WindowStyle.None,
        };
        window.Resources.MergedDictionaries.Add(new CommonControls.WPF.CommonControlTheme
        {
            Theme = CommonControls.WPF.CommonControlTheme.BaseTheme.Light,
        });
        window.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/CommonControls.WPF;component/Themes/FluentDesign.Defaults.xaml",
                UriKind.Absolute),
        });
        window.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/NodeCraft.Flow;component/Themes/Flow.xaml",
                UriKind.Absolute),
        });

        var canvas = new CaptureTestFlowCanvas
        {
            Width = 400,
            Height = 300,
        };
        window.Content = canvas;

        try
        {
            window.Show();
            canvas.ApplyTemplate();
            window.UpdateLayout();

            var viewport = canvas.Template?.FindName("CanvasViewport", canvas)
                as System.Windows.Controls.Grid;
            var worldCanvas = canvas.Template?.FindName("CanFlow", canvas)
                as System.Windows.Controls.Canvas;
            return viewport != null
                && worldCanvas != null
                && assertion(canvas, viewport, worldCanvas);
        }
        finally
        {
            window.Close();
        }
    }

    private static bool RunWithThemedWindow(Func<System.Windows.Window, bool> assertion)
    {
        var window = new System.Windows.Window
        {
            Width = 640,
            Height = 480,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = System.Windows.WindowStyle.None,
        };
        window.Resources.MergedDictionaries.Add(new CommonControls.WPF.CommonControlTheme
        {
            Theme = CommonControls.WPF.CommonControlTheme.BaseTheme.Light,
        });
        window.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/CommonControls.WPF;component/Themes/FluentDesign.Defaults.xaml",
                UriKind.Absolute),
        });
        window.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/NodeCraft.Flow;component/Themes/Flow.xaml",
                UriKind.Absolute),
        });

        try
        {
            window.Show();
            return assertion(window);
        }
        finally
        {
            window.Close();
        }
    }

    private static System.Windows.Input.MouseButtonEventArgs RaiseMouseButtonEvent(
        System.Windows.UIElement target,
        System.Windows.RoutedEvent routedEvent,
        MouseButton button)
    {
        var mouseEvent = new System.Windows.Input.MouseButtonEventArgs(
            System.Windows.Input.Mouse.PrimaryDevice,
            Environment.TickCount,
            button)
        {
            RoutedEvent = routedEvent,
        };
        target.RaiseEvent(mouseEvent);
        return mouseEvent;
    }

    private sealed class CaptureTestFlowCanvas : FlowCanvas
    {
        protected override bool CaptureViewportMouse()
        {
            return true;
        }

        protected override bool CaptureCanvasMouse()
        {
            return true;
        }
    }

    private sealed class StagedPluginTestRoot : IDisposable
    {
        public StagedPluginTestRoot(string prefix)
        {
            RootDirectory = CreateTemporaryPluginDirectory(prefix);
            PluginsDirectory = Path.Combine(RootDirectory, "Plugins");
            Directory.CreateDirectory(PluginsDirectory);
            LoadRootDirectory = CreateTemporaryPluginDirectory(prefix + "shadow-");
            LoadPluginsDirectory = Path.Combine(LoadRootDirectory, "Plugins");
            Directory.CreateDirectory(LoadPluginsDirectory);
        }

        public string RootDirectory { get; }

        public string PluginsDirectory { get; }

        public string LoadRootDirectory { get; }

        public string LoadPluginsDirectory { get; }

        public string StageSamplePluginPackage(string directoryName = "Company.Sample.Nodes")
        {
            var pluginDirectory = Program.StageSamplePluginPackage(PluginsDirectory, directoryName);
            Program.StageSamplePluginPackage(LoadPluginsDirectory, directoryName);
            return pluginDirectory;
        }

        public string StageAssemblyPluginPackage(
            string directoryName,
            string pluginId,
            string entryType,
            bool includePrivateDependency = true)
        {
            var pluginDirectory = Path.Combine(PluginsDirectory, directoryName);
            var entryAssemblyPath = Assembly.GetExecutingAssembly().Location;
            StageFixturePluginPackage(
                pluginDirectory,
                entryAssemblyPath,
                FindBuiltPrivateDependencyAssembly(),
                manifestWriter: directory => WritePluginManifest(
                    directory,
                    pluginId,
                    Path.GetFileName(entryAssemblyPath),
                    entryType,
                    "1.0",
                    "lib"),
                includePrivateDependency: includePrivateDependency);
            StageFixturePluginPackage(
                Path.Combine(LoadPluginsDirectory, directoryName),
                entryAssemblyPath,
                FindBuiltPrivateDependencyAssembly(),
                manifestWriter: directory => WritePluginManifest(
                    directory,
                    pluginId,
                    Path.GetFileName(entryAssemblyPath),
                    entryType,
                    "1.0",
                    "lib"),
                includePrivateDependency: includePrivateDependency);
            return pluginDirectory;
        }

        public void Dispose()
        {
            DeleteDirectoryIfExists(RootDirectory);
            try
            {
                DeleteDirectoryIfExists(LoadRootDirectory);
            }
            catch
            {
                RegisterDeferredCleanup(LoadRootDirectory);
            }
        }
    }

}

public sealed class NestedThrowingFixturePlugin : IFlowPlugin
{
    public PluginMetadata Metadata { get; } = new PluginMetadata
    {
        Id = "test.nested-throwing.plugin",
        DisplayName = "Nested Throwing Test Plugin",
        Version = new Version(1, 0, 0),
    };

    public void Register(IPluginContext context)
    {
        throw new InvalidOperationException(
            "fixture registration failed",
            new InvalidDataException("fixture inner failure"));
    }
}

public sealed class DuplicateSampleTypeKeyPlugin : IFlowPlugin
{
    public PluginMetadata Metadata { get; } = new PluginMetadata
    {
        Id = "company.sample.duplicate",
        DisplayName = "Duplicate Sample TypeKey Plugin",
        Version = new Version(1, 0, 0),
    };

    public void Register(IPluginContext context)
    {
        context.Nodes.Register(new FlowNodeRegistration(
            new FlowNodeDefinition
            {
                TypeKey = "company.sample.nodes.value",
                DisplayName = "Duplicate Sample Value",
                Category = "Tests",
                OutputPorts =
                {
                    new FlowPortDefinition
                    {
                        Id = BuiltInNodePorts.Output,
                        DisplayName = "Output",
                        IOType = EIOType.Output,
                        DataType = FlowDataType.String,
                        PreferredDirection = EPortDirection.Right,
                    },
                },
            },
            () => new DuplicateSampleTypeKeyExecutor())
        {
            NodeModelType = typeof(DuplicateSampleTypeKeyNodeModel),
            NodeFactory = () => new DuplicateSampleTypeKeyNodeModel(),
            PaletteDisplayName = "Duplicate Sample Value",
            PaletteDescription = "Attempts to replace the sample value node.",
        });
    }
}

internal sealed class DuplicateSampleTypeKeyNodeModel : NodeModel
{
    public DuplicateSampleTypeKeyNodeModel()
    {
        ExecutorType = "company.sample.nodes.value";
        Name = "Duplicate Sample Value";
    }
}

internal sealed class DuplicateSampleTypeKeyExecutor : IFlowNodeExecutor
{
    public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
        FlowExecutionContext context,
        WorkflowNode node,
        FlowNodeDefinition definition,
        IReadOnlyDictionary<string, object> inputs,
        System.Threading.CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyDictionary<string, object>>(
            new Dictionary<string, object>());
    }
}
