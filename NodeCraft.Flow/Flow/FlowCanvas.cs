using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NodeCraft.Flow.Nodes;

namespace NodeCraft.Flow
{
    /// <summary>
    /// 按照步骤 1a 或 1b 操作，然后执行步骤 2 以在 XAML 文件中使用此自定义控件。
    ///
    /// 步骤 1a) 在当前项目中存在的 XAML 文件中使用该自定义控件。
    /// 将此 XmlNamespace 特性添加到要使用该特性的标记文件的根
    /// 元素中:
    ///
    ///     xmlns:MyNamespace="clr-namespace:NodeCraft.Flow"
    ///
    ///
    /// 步骤 1b) 在其他项目中存在的 XAML 文件中使用该自定义控件。
    /// 将此 XmlNamespace 特性添加到要使用该特性的标记文件的根
    /// 元素中:
    ///
    ///     xmlns:MyNamespace="clr-namespace:NodeCraft.Flow;assembly=NodeCraft.Flow"
    ///
    /// 您还需要添加一个从 XAML 文件所在的项目到此项目的项目引用，
    /// 并重新生成以避免编译错误:
    ///
    ///     在解决方案资源管理器中右击目标项目，然后依次单击
    ///     “添加引用”->“项目”->[浏览查找并选择此项目]
    ///
    ///
    /// 步骤 2)
    /// 继续操作并在 XAML 文件中使用控件。
    ///
    ///     <MyNamespace:FlowCanvas/>
    ///
    /// </summary>
    [TemplatePart(Name = "CanvasViewport", Type = typeof(Panel))]
    [TemplatePart(Name = "CanFlow", Type = typeof(Canvas))]
    public class FlowCanvas : Control
    {
        private readonly ILogger<FlowCanvas> _logger;

        private const double WorldCanvasSize = 10000;
        private const double DefaultNodeWidth = 180;
        private const double DefaultNodeHeight = 72;
        private const double WheelZoomFactorPerDetent = 1.1;
        private const double MinimumSecondaryGridSpacing = 8;
        private const int MajorGridLineInterval = 4;

        private NodeView _originalElement;
        private NodeModel _selectedNode;
        private Connector _startConnector;
        private int _startSlot;
        private bool _startIsInput;
        private Connector _hoverConnector;
        private Rectangle _selectionRect;

        private Dictionary<SimpleCircleAdorner, Point> _dragOverlayElements;
        private DragBounds _dragBounds;

        private List<NodeView> _selectedNodes;

        private Point _startPoint;
        private Point _startViewportPoint;
        private Point _nodePoint;
        private Vector _dragWorldOffset;

        private ConnectionLine _tempLine;

        private Canvas _canvas;
        private Panel _viewport;

        private readonly FlowCanvasViewportTransform _viewportTransform = new FlowCanvasViewportTransform();
        private bool _isPanning;
        private Point _panStartPoint;

        private ContextMenu _lineContextMenu;

        private GraphModel _graphModel;

        private EMouseMode _mouseMode;

        private bool _canvasUpdateQueued;

        private enum EMouseMode
        {
            None,
            PreDragMode,
            DragMode,
            DrawingMode,
            SelectionMode,
        }

        public double CellSize { get; set; } = 16;

        public Brush GridBrush { get; set; } = Brushes.Gray;

        public double GridThickness { get; set; } = 0.5;

        public double Zoom => _viewportTransform.Zoom;

        public GraphModel GraphModel 
        {
            get => _graphModel;
            set => _graphModel = value; 
        }

        public NodeModel SelectedNode => _selectedNode;

        public Func<NodeModel, object> NodeContentFactory { get; set; }

        public event EventHandler SelectedNodeChanged;

        public event EventHandler GraphChanged;

        public event EventHandler<FlowConnectionFailedEventArgs> ConnectionCreateFailed;

        static FlowCanvas()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(FlowCanvas), new FrameworkPropertyMetadata(typeof(FlowCanvas)));
        }

        public FlowCanvas()
            : this(NullLogger<FlowCanvas>.Instance)
        {
        }

        public FlowCanvas(ILogger<FlowCanvas> logger)
        {
            _logger = logger ?? NullLogger<FlowCanvas>.Instance;

            _mouseMode = EMouseMode.None;

            GraphModel = new()
            {
                Nodes = new List<NodeModel>(),
                Links = new List<GraphLink>()
            };

            _selectedNodes = new List<NodeView>();
            _dragOverlayElements = new Dictionary<SimpleCircleAdorner, Point>();
        }

        public override void OnApplyTemplate()
        {
            CancelActiveInteraction();
            DetachViewportHandlers();

            base.OnApplyTemplate();

            _viewport = GetTemplateChild("CanvasViewport") as Panel;
            _canvas = GetTemplateChild("CanFlow") as Canvas;

            if (_viewport == null || _canvas == null)
            {
                return;
            }

            _canvas.Width = WorldCanvasSize;
            _canvas.Height = WorldCanvasSize;
            _viewport.ClipToBounds = true;

            _viewport.Loaded += Canvas_Loaded;
            _viewport.PreviewMouseDown += Canvas_PreviewMouseDown;
            _viewport.PreviewMouseMove += Canvas_PreviewMouseMove;
            _viewport.PreviewMouseUp += Canvas_PreviewMouseUp;
            _viewport.PreviewMouseWheel += Viewport_PreviewMouseWheel;
            _viewport.LostMouseCapture += Viewport_LostMouseCapture;
            _viewport.DragEnter += Canvas_DragEnter;
            _viewport.DragLeave += Canvas_DragLeave;
            _viewport.DragOver += Canvas_DragOver;
            _viewport.Drop += Canvas_Drop;

            var deleteMenu = new MenuItem();
            deleteMenu.Header = "Delete";
            deleteMenu.Click += DeleteMenu_Click;

            _lineContextMenu = new ContextMenu();
            _lineContextMenu.Items.Add(deleteMenu);

            GridBrush = (Brush)FindResource("colorNeutralStroke1");
            ApplyViewportTransform();
        }

        public void AddNode(NodeModel nodeInfo)
        {
            if (CellSize > 0 && !double.IsNaN(CellSize) && !double.IsInfinity(CellSize))
            {
                nodeInfo.X = Math.Round(nodeInfo.X / CellSize) * CellSize;
                nodeInfo.Y = Math.Round(nodeInfo.Y / CellSize) * CellSize;
            }

            InitializeNodePorts(nodeInfo);

            var node = CreateNodeView(nodeInfo, NodeContentFactory?.Invoke(nodeInfo));
            _canvas.Children.Add(node);

            Canvas.SetLeft(node, nodeInfo.X);
            Canvas.SetTop(node, nodeInfo.Y);

            GraphModel.Nodes.Add(nodeInfo);
            RaiseGraphChanged();

            _logger.LogDebug("Added node '{NodeId}'.", nodeInfo.Id);
        }

        public void LoadGraph(GraphModel graph, Func<NodeModel, object> contentFactory = null)
        {
            _logger.LogDebug("Loading graph with {NodeCount} nodes and {LinkCount} links.", graph?.Nodes?.Count ?? 0, graph?.Links?.Count ?? 0);

            NodeContentFactory = contentFactory ?? NodeContentFactory;

            GraphModel = graph ?? new GraphModel
            {
                Nodes = new List<NodeModel>(),
                Links = new List<GraphLink>()
            };

            GraphModel.Links ??= new List<GraphLink>();
            GraphModel.Nodes ??= new List<NodeModel>();

            foreach (var nodeInfo in GraphModel.Nodes)
            {
                InitializeNodePorts(nodeInfo);
            }

            GraphModelLinkReconciler.Reconcile(GraphModel);

            if (_canvas == null)
            {
                return;
            }

            ClearCanvas();

            foreach (var nodeInfo in GraphModel.Nodes)
            {
                var node = CreateNodeView(nodeInfo, NodeContentFactory?.Invoke(nodeInfo));
                _canvas.Children.Add(node);

                Canvas.SetLeft(node, nodeInfo.X);
                Canvas.SetTop(node, nodeInfo.Y);
            }

            SetSelectedNode(null);
            _selectedNodes.Clear();
            ApplySelectionVisuals();
            UpdateCanvas();
            RaiseGraphChanged();
        }

        public bool RemoveNode(string nodeId)
        {
            var nodeInfo = GraphModel.Nodes.Where(s => s.Id == nodeId).FirstOrDefault();
            var nodeView = GetNodeViewByNodeID(nodeId);

            if (nodeInfo != null)
            {
                GraphModel.Nodes.Remove(nodeInfo);
            }

            if (nodeView != null)
            {
                _canvas.Children.Remove(nodeView);
                _selectedNodes.Remove(nodeView);
            }

            if (GraphModel.Links != null)
            {
                GraphModel.Links.RemoveAll(s => string.Equals(s.OriginNodeId, nodeId, StringComparison.Ordinal)
                    || string.Equals(s.TargetNodeId, nodeId, StringComparison.Ordinal));
            }

            // 清除相邻节点输入端口上指向被删连线的 LinkId（连向已删除节点的连线已被移除）。
            foreach (var node in GraphModel.Nodes ?? Enumerable.Empty<NodeModel>())
            {
                foreach (var port in node.InputParameters ?? Enumerable.Empty<PortParameter>())
                {
                    if (port.LinkId != null
                        && GraphModel.Links != null
                        && GraphModel.Links.All(link => !string.Equals(link.Id, port.LinkId, StringComparison.Ordinal)))
                    {
                        port.LinkId = null;
                    }
                }
            }

            if (_selectedNode != null && _selectedNode.Id == nodeId)
            {
                SetSelectedNode(null);
            }

            ApplySelectionVisuals();

            UpdateCanvas();
            RaiseGraphChanged();

            return true;
        }

        public void NotifyGraphChanged()
        {
            NotifyGraphChanged(refreshNodeContents: true);
        }

        public void NotifyGraphChanged(bool refreshNodeContents)
        {
            UpdateCanvas();
            RaiseGraphChanged(refreshNodeContents);
        }

        internal void NotifyNodeLayoutChanged()
        {
            UpdateCanvas();
        }

        internal static void PersistNodePosition(NodeModel node, double x, double y)
        {
            if (node == null)
            {
                return;
            }

            node.X = x;
            node.Y = y;
        }

        public void RefreshNode(NodeModel nodeInfo, object content = null)
        {
            if (nodeInfo == null)
            {
                return;
            }

            var nodeView = GetNodeViewByNodeID(nodeInfo.Id);
            if (nodeView == null)
            {
                return;
            }

            nodeView.Content = content ?? nodeInfo.Name;
        }

        private NodeView CreateNodeView(NodeModel nodeInfo, object content)
        {
            var node = new NodeView();
            node._parentCanvas = this;
            node.NodeModel = nodeInfo;
            node.Content = content ?? nodeInfo.Name;
            node.ContextMenu = _lineContextMenu;
            if (nodeInfo.Width > 0)
            {
                node.Width = nodeInfo.Width;
            }
            if (nodeInfo.Height > 0)
            {
                node.Height = nodeInfo.Height;
            }
            return node;
        }

        private void ClearCanvas()
        {
            _canvas.Children.Clear();
            _tempLine = null;
            _selectionRect = null;
            _hoverConnector = null;
            _originalElement = null;
            _startConnector = null;
            _startSlot = 0;
            _startIsInput = false;
            _mouseMode = EMouseMode.None;
        }

        private void DetachViewportHandlers()
        {
            if (_viewport == null)
            {
                return;
            }

            _viewport.Loaded -= Canvas_Loaded;
            _viewport.PreviewMouseDown -= Canvas_PreviewMouseDown;
            _viewport.PreviewMouseMove -= Canvas_PreviewMouseMove;
            _viewport.PreviewMouseUp -= Canvas_PreviewMouseUp;
            _viewport.PreviewMouseWheel -= Viewport_PreviewMouseWheel;
            _viewport.LostMouseCapture -= Viewport_LostMouseCapture;
            _viewport.DragEnter -= Canvas_DragEnter;
            _viewport.DragLeave -= Canvas_DragLeave;
            _viewport.DragOver -= Canvas_DragOver;
            _viewport.Drop -= Canvas_Drop;
        }

        private void CancelActiveInteraction()
        {
            StopPanning();
            CancelLeftInteraction();
        }

        private void CancelLeftInteraction()
        {
            _mouseMode = EMouseMode.None;

            if (_tempLine != null)
            {
                _canvas?.Children.Remove(_tempLine);
                _tempLine = null;
            }

            if (_selectionRect != null)
            {
                _canvas?.Children.Remove(_selectionRect);
                _selectionRect = null;
            }

            foreach (var overlay in _dragOverlayElements.Keys.ToList())
            {
                AdornerLayer.GetAdornerLayer(overlay.AdornedElement)?.Remove(overlay);
            }

            _dragOverlayElements.Clear();
            _dragBounds = null;
            _dragWorldOffset = default;
            _hoverConnector?.Unhighlight();
            _hoverConnector = null;
            _originalElement = null;
            _startConnector = null;
            _startSlot = 0;
            _startIsInput = false;

            if (_canvas != null && ReferenceEquals(Mouse.Captured, _canvas))
            {
                _canvas.ReleaseMouseCapture();
            }
        }

        private void Canvas_Loaded(object sender, RoutedEventArgs e)
        {

        }

        protected void Canvas_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            base.OnPreviewMouseDown(e);

            if (IsPanningButton(e.ChangedButton))
            {
                CancelLeftInteraction();
                StopPanning();
                _panStartPoint = e.GetPosition(_viewport);
                _isPanning = CaptureViewportMouse();
                e.Handled = true;
                return;
            }

            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            if (ShouldBlockLeftInteraction(_isPanning, e.MiddleButton, Mouse.Captured, _viewport))
            {
                e.Handled = true;
                return;
            }

            var originalSource = e.OriginalSource as DependencyObject;
            _originalElement = FindAncestor<NodeView>(originalSource);

            if (_originalElement == null && !IsBlankCanvasTarget(originalSource, _viewport, _canvas))
            {
                _mouseMode = EMouseMode.None;
                return;
            }

            _startViewportPoint = e.GetPosition(_viewport);
            _startPoint = _viewportTransform.ToWorld(_startViewportPoint);

            if (_originalElement == null)
            {
                _selectedNodes.Clear();
                ApplySelectionVisuals();
                SetSelectedNode(null);
                SelectionStart();
            }
            else
            {
                _nodePoint = e.GetPosition(_originalElement);

                if (!_selectedNodes.Contains(_originalElement))
                {
                    _selectedNodes.Clear();
                    _selectedNodes.Add(_originalElement);
                    ApplySelectionVisuals();
                }

                SetSelectedNode(_originalElement.NodeModel);

                if (IsInteractiveNodeContent(originalSource))
                {
                    _startConnector = null;
                    _mouseMode = EMouseMode.None;
                }
                else
                {
                    _startConnector = _originalElement.GetConnectorUnderPosition(_nodePoint);
                    if (_startConnector != null)
                    {
                        _startSlot = _startConnector.Slot;
                        _startIsInput = _startConnector.IsInput;
                        if (_startIsInput)
                        {
                            RaiseConnectionCreateFailed("请从输出插座开始拖拽连线。");
                            _startConnector = null;
                            _mouseMode = EMouseMode.None;
                        }
                        else
                        {
                            DrawStarted();
                        }
                    }
                    else
                    {
                        _mouseMode = EMouseMode.PreDragMode;
                    }
                }

                Debug.WriteLine($"Node point: X: {_nodePoint.X}, Y: {_nodePoint.Y}");
            }
        }

        protected void Canvas_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            base.OnPreviewMouseUp(e);

            if (IsPanningButton(e.ChangedButton))
            {
                StopPanning();
                e.Handled = true;
                return;
            }

            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            switch (_mouseMode)
            {
                case EMouseMode.DrawingMode:
                    DrawFinished();
                    break;
                case EMouseMode.PreDragMode:
                    _mouseMode = EMouseMode.None;
                    break;
                case EMouseMode.DragMode:
                    DragFinished();
                    break;
                case EMouseMode.SelectionMode:
                    SelectionFinished();
                    break;
                default:
                    break;
            }

            CancelLeftInteraction();
        }

        protected void Canvas_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                if (!CanContinuePanning(e.MiddleButton, Mouse.Captured, _viewport))
                {
                    StopPanning();
                    return;
                }

                var currentPoint = e.GetPosition(_viewport);
                _viewportTransform.PanBy(currentPoint - _panStartPoint);
                _panStartPoint = currentPoint;
                ApplyViewportTransform();
                e.Handled = true;
                return;
            }

            switch (_mouseMode)
            {
                case EMouseMode.DrawingMode:
                    DrawingMoved();
                    break;
                case EMouseMode.PreDragMode:
                {
                    var currentPosition = e.GetPosition(_viewport);
                    if (HasExceededViewportDragThreshold(
                        _startViewportPoint,
                        currentPosition,
                        SystemParameters.MinimumHorizontalDragDistance,
                        SystemParameters.MinimumVerticalDragDistance))
                    {
                        DragStarted();
                    }

                    break;
                }
                case EMouseMode.DragMode:
                {
                    DragMoved();
                    break;
                }
                case EMouseMode.SelectionMode:
                    SelectionMove();
                    break;
                default:
                    break;
            }
        }

        private void Viewport_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (!ReferenceEquals(Mouse.Captured, _viewport))
            {
                _isPanning = false;
                _panStartPoint = default;
            }

            if (ShouldCancelLeftInteractionOnCaptureLoss(
                _mouseMode != EMouseMode.None,
                Mouse.Captured,
                _canvas))
            {
                CancelLeftInteraction();
            }
        }

        private void StopPanning()
        {
            _isPanning = false;
            _panStartPoint = default;
            if (_viewport != null && ReferenceEquals(Mouse.Captured, _viewport))
            {
                _viewport.ReleaseMouseCapture();
            }
        }

        protected virtual bool CaptureViewportMouse()
        {
            return _viewport?.CaptureMouse() == true;
        }

        protected virtual bool CaptureCanvasMouse()
        {
            return _canvas?.CaptureMouse() == true;
        }

        private void Viewport_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            _viewportTransform.ZoomAt(
                Mouse.GetPosition(_viewport),
                GetWheelZoomFactor(e.Delta));
            ApplyViewportTransform();
            e.Handled = true;
        }

        private void Canvas_DragEnter(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.None;

            if (e.Data.GetDataPresent(DataFormats.StringFormat))
            {
                string dataString = (string)e.Data.GetData(DataFormats.StringFormat);
                if (CanCreateNodeFromPaletteData(NodeExecutorFactory.Registry, dataString))
                {
                    e.Effects = DragDropEffects.Copy;
                }
            }

            e.Handled = true;
        }

        private void Canvas_DragLeave(object sender, DragEventArgs e)
        {

        }

        private void Canvas_DragOver(object sender, DragEventArgs e)
        {

        }

        private void Canvas_Drop(object sender, DragEventArgs e)
        {
            var point = _viewportTransform.ToWorld(e.GetPosition(_viewport));

            if (e.Data.GetDataPresent(DataFormats.StringFormat))
            {
                string dataString = (string)e.Data.GetData(DataFormats.StringFormat);
                if (TryCreateNodeFromPaletteData(NodeExecutorFactory.Registry, dataString, out var nodeInfo))
                {
                    nodeInfo.Name = nodeInfo.GetType().Name + (GraphModel.Nodes.Count + 1);
                    var nodeSize = new Size(
                        GetFinitePositiveOrDefault(nodeInfo.Width, DefaultNodeWidth),
                        GetFinitePositiveOrDefault(nodeInfo.Height, DefaultNodeHeight));
                    var dropPosition = ClampDropPositionToWorld(point, nodeSize, CellSize);
                    nodeInfo.X = dropPosition.X;
                    nodeInfo.Y = dropPosition.Y;
                    AddNode(nodeInfo);
                }
            }

            e.Handled = true;
        }

        internal static bool CanCreateNodeFromPaletteData(FlowNodeRegistry registry, string dataString)
        {
            if (registry == null || string.IsNullOrWhiteSpace(dataString))
            {
                return false;
            }

            if (registry.TryResolve(dataString, out var registration)
                && registration.NodeFactory != null)
            {
                return true;
            }

            var type = Type.GetType(dataString, throwOnError: false);
            return type != null
                && !type.IsAbstract
                && typeof(NodeModel).IsAssignableFrom(type);
        }

        internal static bool TryCreateNodeFromPaletteData(
            FlowNodeRegistry registry,
            string dataString,
            out NodeModel node)
        {
            node = null;
            if (!CanCreateNodeFromPaletteData(registry, dataString))
            {
                return false;
            }

            if (registry.TryCreateNodeByTypeKey(dataString, out node)
                && node != null)
            {
                return true;
            }

            var type = Type.GetType(dataString, throwOnError: false);
            if (type == null
                || type.IsAbstract
                || !typeof(NodeModel).IsAssignableFrom(type))
            {
                node = null;
                return false;
            }

            try
            {
                node = Activator.CreateInstance(type) as NodeModel;
                return node != null;
            }
            catch
            {
                node = null;
                return false;
            }
        }

        private Point GetMouseWorldPosition()
        {
            return _viewportTransform.ToWorld(Mouse.GetPosition(_viewport));
        }

        internal static Point ConvertSocketPositionToWorld(
            Point socketPositionInFlowCanvas,
            Point viewportOriginInFlowCanvas,
            FlowCanvasViewportTransform viewportTransform)
        {
            if (viewportTransform == null)
            {
                throw new ArgumentNullException(nameof(viewportTransform));
            }

            var socketPositionInViewport = new Point(
                socketPositionInFlowCanvas.X - viewportOriginInFlowCanvas.X,
                socketPositionInFlowCanvas.Y - viewportOriginInFlowCanvas.Y);
            return viewportTransform.ToWorld(socketPositionInViewport);
        }

        internal static bool HasExceededViewportDragThreshold(
            Point startViewportPoint,
            Point currentViewportPoint,
            double minimumHorizontalDistance,
            double minimumVerticalDistance)
        {
            return Math.Abs(currentViewportPoint.X - startViewportPoint.X) > minimumHorizontalDistance
                || Math.Abs(currentViewportPoint.Y - startViewportPoint.Y) > minimumVerticalDistance;
        }

        internal static Vector ToViewportDragOffset(Vector worldOffset, double zoom)
        {
            return new Vector(worldOffset.X * zoom, worldOffset.Y * zoom);
        }

        internal static bool IsBlankCanvasTarget(
            DependencyObject source,
            DependencyObject viewport,
            DependencyObject worldCanvas)
        {
            return ReferenceEquals(source, viewport) || ReferenceEquals(source, worldCanvas);
        }

        internal static bool CanContinuePanning(
            MouseButtonState middleButton,
            IInputElement capturedElement,
            IInputElement viewport)
        {
            return middleButton == MouseButtonState.Pressed
                && ReferenceEquals(capturedElement, viewport);
        }

        internal static bool IsPanningButton(MouseButton button)
        {
            return button == MouseButton.Middle;
        }

        internal static bool ShouldBlockLeftInteraction(
            bool isPanning,
            MouseButtonState middleButton,
            IInputElement capturedElement,
            IInputElement viewport)
        {
            return isPanning
                || middleButton == MouseButtonState.Pressed
                || ReferenceEquals(capturedElement, viewport);
        }

        internal static bool ShouldCancelLeftInteractionOnCaptureLoss(
            bool hasActiveLeftInteraction,
            IInputElement capturedElement,
            IInputElement worldCanvas)
        {
            return hasActiveLeftInteraction && !ReferenceEquals(capturedElement, worldCanvas);
        }

        internal static Point ClampDropPositionToWorld(Point position, Size nodeSize, double cellSize)
        {
            var maxX = Math.Max(0, WorldCanvasSize - Math.Max(0, nodeSize.Width));
            var maxY = Math.Max(0, WorldCanvasSize - Math.Max(0, nodeSize.Height));
            return new Point(
                ClampSnappedCoordinate(position.X, maxX, cellSize),
                ClampSnappedCoordinate(position.Y, maxY, cellSize));
        }

        internal static int GetGridLineStride(double cellSize, double zoom)
        {
            return cellSize * zoom < MinimumSecondaryGridSpacing
                ? MajorGridLineInterval
                : 1;
        }

        internal static bool IsMajorGridLine(long lineIndex)
        {
            return lineIndex % MajorGridLineInterval == 0;
        }

        internal static double GetWheelZoomFactor(int delta)
        {
            return Math.Pow(WheelZoomFactorPerDetent, delta / 120.0);
        }

        private static double ClampSnappedCoordinate(double value, double maximum, double cellSize)
        {
            if (cellSize <= 0 || double.IsNaN(cellSize) || double.IsInfinity(cellSize))
            {
                return ClampCoordinate(value, maximum);
            }

            var maximumSnapped = Math.Floor(maximum / cellSize) * cellSize;
            return ClampCoordinate(
                Math.Round(value / cellSize) * cellSize,
                maximumSnapped);
        }

        private static double ClampCoordinate(double value, double maximum)
        {
            if (double.IsNaN(value) || double.IsNegativeInfinity(value))
            {
                return 0;
            }

            if (double.IsPositiveInfinity(value))
            {
                return maximum;
            }

            return Math.Max(0, Math.Min(maximum, value));
        }

        private static double GetFinitePositiveOrDefault(double value, double fallback)
        {
            return value > 0 && !double.IsInfinity(value) ? value : fallback;
        }

        private void ApplyViewportTransform()
        {
            _canvas.RenderTransform = new MatrixTransform(new Matrix(
                _viewportTransform.Zoom,
                0,
                0,
                _viewportTransform.Zoom,
                _viewportTransform.PanOffset.X,
                _viewportTransform.PanOffset.Y));
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            if (CellSize <= 0 || double.IsNaN(CellSize) || double.IsInfinity(CellSize))
            {
                return;
            }

            if (_viewport == null || _viewport.ActualWidth <= 0 || _viewport.ActualHeight <= 0)
            {
                return;
            }

            var viewportBounds = _viewport
                .TransformToVisual(this)
                .TransformBounds(new Rect(new Point(), _viewport.RenderSize));
            var topLeft = _viewportTransform.ToWorld(new Point(0, 0));
            var bottomRight = _viewportTransform.ToWorld(
                new Point(_viewport.ActualWidth, _viewport.ActualHeight));
            var minX = Math.Max(0, Math.Min(topLeft.X, bottomRight.X));
            var maxX = Math.Min(WorldCanvasSize, Math.Max(topLeft.X, bottomRight.X));
            var minY = Math.Max(0, Math.Min(topLeft.Y, bottomRight.Y));
            var maxY = Math.Min(WorldCanvasSize, Math.Max(topLeft.Y, bottomRight.Y));

            if (minX > maxX || minY > maxY)
            {
                return;
            }

            var thickness = GridThickness * Zoom;
            if (double.IsNaN(thickness) || double.IsInfinity(thickness))
            {
                thickness = 0.1;
            }

            var minorPen = new Pen(GridBrush, Math.Max(0.1, thickness));
            var majorPen = new Pen(GridBrush, Math.Max(minorPen.Thickness, Math.Max(0.2, thickness * 2)));
            var stride = GetGridLineStride(CellSize, Zoom);
            var firstXIndex = AlignGridLineIndex((long)Math.Ceiling(minX / CellSize), stride);
            var lastXIndex = (long)Math.Floor(maxX / CellSize);
            var firstYIndex = AlignGridLineIndex((long)Math.Ceiling(minY / CellSize), stride);
            var lastYIndex = (long)Math.Floor(maxY / CellSize);

            drawingContext.PushClip(new RectangleGeometry(viewportBounds));
            try
            {
                for (var lineIndex = firstXIndex; lineIndex <= lastXIndex; lineIndex += stride)
                {
                    var x = lineIndex * CellSize;
                    drawingContext.DrawLine(
                        IsMajorGridLine(lineIndex) ? majorPen : minorPen,
                        _viewport.TranslatePoint(_viewportTransform.ToViewport(new Point(x, minY)), this),
                        _viewport.TranslatePoint(_viewportTransform.ToViewport(new Point(x, maxY)), this));
                }

                for (var lineIndex = firstYIndex; lineIndex <= lastYIndex; lineIndex += stride)
                {
                    var y = lineIndex * CellSize;
                    drawingContext.DrawLine(
                        IsMajorGridLine(lineIndex) ? majorPen : minorPen,
                        _viewport.TranslatePoint(_viewportTransform.ToViewport(new Point(minX, y)), this),
                        _viewport.TranslatePoint(_viewportTransform.ToViewport(new Point(maxX, y)), this));
                }
            }
            finally
            {
                drawingContext.Pop();
            }
        }

        private static long AlignGridLineIndex(long lineIndex, int stride)
        {
            var remainder = lineIndex % stride;
            return remainder == 0 ? lineIndex : lineIndex + stride - remainder;
        }

        private void DeleteMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Parent is ContextMenu contextMenu)
            {
                if (contextMenu.PlacementTarget is ConnectionLine line)
                {
                    _canvas.Children.Remove(line);

                    var connectionId = line.Tag as string;

                    if (!string.IsNullOrEmpty(connectionId))
                    {
                        var link = GraphModel.Links?.FirstOrDefault(item => string.Equals(item.Id, connectionId, StringComparison.Ordinal));
                        if (link != null)
                        {
                            ClearTargetPortLinkId(link);
                            GraphModel.Links.RemoveAll(item => string.Equals(item.Id, connectionId, StringComparison.Ordinal));

                            _logger.LogDebug("Deleted link '{LinkId}'.", connectionId);
                        }

                        RaiseGraphChanged();
                    }
                }
                else if (contextMenu.PlacementTarget is NodeView nodeView)
                {
                    var deleteSelection = _selectedNodes.Count > 1 && _selectedNodes.Contains(nodeView);

                    if (deleteSelection)
                    {
                        foreach (var selectedNode in _selectedNodes.Select(item => item.NodeModel.Id).ToList())
                        {
                            RemoveNode(selectedNode);
                        }

                        _selectedNodes.Clear();
                        SetSelectedNode(null);
                        ApplySelectionVisuals();
                    }
                    else
                    {
                        _selectedNodes.Clear();
                        SetSelectedNode(null);
                        ApplySelectionVisuals();
                        RemoveNode(nodeView.NodeModel.Id);
                    }
                }
            }
        }

        private void DragStarted()
        {
            Debug.WriteLine("drag start");

            CaptureCanvasMouse();

            _mouseMode = EMouseMode.DragMode;
            _dragWorldOffset = default;

            var nodeList = new List<NodeView>();

            if (_selectedNodes.Count > 0)
            {
                nodeList.AddRange(_selectedNodes);
            }
            else
            {
                nodeList.Add(_originalElement);
            }

            if (nodeList.Count > 0)
            {
                var minX = double.MaxValue;
                var minY = double.MaxValue;
                var maxX = double.MinValue;
                var maxY = double.MinValue;
                var maxWidth = double.MinValue;
                var maxHeight = double.MinValue;

                foreach (var node in nodeList)
                {
                    var left = Canvas.GetLeft(node);
                    var top = Canvas.GetTop(node);

                    if (left < minX)
                    {
                        minX = left;
                    }

                    if (left > maxX)
                    {
                        maxX = left;
                    }

                    if (top < minY)
                    {
                        minY = top;
                    }

                    if (top > maxY)
                    {
                        maxY = top;
                    }

                    if (node.ActualWidth > maxWidth)
                    {
                        maxWidth = node.ActualWidth;
                    }

                    if (node.ActualHeight > maxHeight)
                    {
                        maxHeight = node.ActualHeight;
                    }

                    var overlayElement = new SimpleCircleAdorner(node);

                    _dragOverlayElements.Add(overlayElement, new Point(left, top));
                    var layer = AdornerLayer.GetAdornerLayer(node);
                    layer.Add(overlayElement);
                }

                _dragBounds = new DragBounds();
                _dragBounds.MinX = minX;
                _dragBounds.MinY = minY;
                _dragBounds.MaxX = maxX;
                _dragBounds.MaxY = maxY;
                _dragBounds.MaxWidth = maxWidth;
                _dragBounds.MaxHeight = maxHeight;
            }
        }

        private void DragMoved()
        {
            var currentPosition = GetMouseWorldPosition();

            if (_dragOverlayElements.Count > 0 && _dragBounds != null)
            {
                double dx = currentPosition.X - _startPoint.X;
                double dy = currentPosition.Y - _startPoint.Y;
                
                Debug.WriteLine($"origin x y: {_dragBounds.MinX} {_dragBounds.MinY}");

                if (_dragBounds.MinX + dx < 0)
                {
                    dx = -_dragBounds.MinX;
                }

                if (_dragBounds.MinY + dy < 0)
                {
                    dy = -_dragBounds.MinY;
                }

                if (_dragBounds.MaxX + _dragBounds.MaxWidth + dx > WorldCanvasSize)
                {
                    dx = WorldCanvasSize - (_dragBounds.MaxX + _dragBounds.MaxWidth);
                }

                if (_dragBounds.MaxY + _dragBounds.MaxHeight + dy > WorldCanvasSize)
                {
                    dy = WorldCanvasSize - (_dragBounds.MaxY + _dragBounds.MaxHeight);
                }

                // 吸附
                double newX = Math.Round(dx / CellSize) * CellSize;
                double newY = Math.Round(dy / CellSize) * CellSize;


                Debug.WriteLine($"move offset: {newX} {newY}");

                _dragWorldOffset = new Vector(newX, newY);
                var previewOffset = ToViewportDragOffset(_dragWorldOffset, Zoom);

                foreach (var overlay in _dragOverlayElements.Keys)
                {
                    overlay.LeftOffset = previewOffset.X;
                    overlay.TopOffset = previewOffset.Y;
                }
            }
        }

        private void DragFinished(bool cancelled = false)
        {
            Debug.WriteLine("drag end");

            if (_dragOverlayElements.Count > 0)
            {
                foreach (var overlay in _dragOverlayElements)
                {
                    AdornerLayer.GetAdornerLayer(overlay.Key.AdornedElement)?.Remove(overlay.Key);

                    if (cancelled == false)
                    {
                        var adornedElement = overlay.Key.AdornedElement;
                        var newTop = overlay.Value.Y + _dragWorldOffset.Y;
                        var newLeft = overlay.Value.X + _dragWorldOffset.X;

                        Canvas.SetTop(adornedElement, newTop);
                        Canvas.SetLeft(adornedElement, newLeft);
                        PersistNodePosition((adornedElement as NodeView)?.NodeModel, newLeft, newTop);
                    }
                }
            }

            UpdateCanvas();

            _dragOverlayElements.Clear();
            _dragBounds = null;
            _dragWorldOffset = default;
            _mouseMode = EMouseMode.None;
        }

        private void DrawStarted()
        {
            Debug.WriteLine("draw start");

            if (_originalElement.NodeModel.OutputParameters.Count == 0)
            {
                return;
            }

            CaptureCanvasMouse();
            _mouseMode = EMouseMode.DrawingMode;
            _startPoint = GetNodeSocketPosition(_originalElement, _startSlot, isInput: false);

            _tempLine = new ConnectionLine
            {
                Stroke = (Brush)FindResource("colorBrandStroke1"),
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                CornerRadius = 10,
                IsHitTestVisible = false
            };

            _canvas.Children.Add(_tempLine);
        }

        private void DrawingMoved()
        {
            if (_tempLine == null) return;

            var currentPosition = GetMouseWorldPosition();

            var points = Route(_startPoint, currentPosition);

            _tempLine.Points = new PointCollection(points);

            var node = GetNodeUnderPosition(currentPosition);
            var connector = node?.GetConnectorNearPosition(Mouse.GetPosition(node));
            if (connector != null)
            {
                _hoverConnector?.Unhighlight();
                _hoverConnector = connector;
                _hoverConnector.Highlight();
            }
            else
            {
                _hoverConnector?.Unhighlight();
                _hoverConnector = null;
            }
        }

        private void DrawFinished()
        {
            Debug.WriteLine("draw end");

            if (_tempLine != null)
            {
                _canvas.Children.Remove(_tempLine);
            }

            do
            {
                var endPoint = GetMouseWorldPosition();
                var hitNode = GetNodeUnderPosition(endPoint);
                if (hitNode == null || hitNode == _originalElement || hitNode.NodeModel.InputParameters.Count == 0)
                {
                    if (hitNode == _originalElement)
                    {
                        RaiseConnectionCreateFailed("不能连接到自身节点。");
                    }
                    else if (hitNode != null && hitNode.NodeModel.InputParameters.Count == 0)
                    {
                        RaiseConnectionCreateFailed("目标节点没有可用输入端口。");
                    }
                    break;
                }

                var endConnector = hitNode.GetConnectorNearPosition(Mouse.GetPosition(hitNode));
                if (endConnector == null || !endConnector.IsInput)
                {
                    RaiseConnectionCreateFailed("未命中目标节点的输入插座。");
                    break;
                }

                var sourceNode = _originalElement.NodeModel;
                var targetNode = hitNode.NodeModel;
                var sourceSlot = _startSlot;
                var targetSlot = endConnector.Slot;

                // 重复连接检测：按目标输入槽位占用判断（AllowMultipleConnections 的槽位允许复用）。
                var targetAllowsMultiple = IsSlotAllowingMultipleConnections(targetNode, targetSlot);
                var occupied = !targetAllowsMultiple
                    && (GraphModel.Links ?? Enumerable.Empty<GraphLink>()).Any(s =>
                        string.Equals(s.TargetNodeId, targetNode.Id, StringComparison.Ordinal)
                        && s.TargetSlot == targetSlot);

                if (occupied)
                {
                    RaiseConnectionCreateFailed("该输入槽位已有连接，请先删除旧连接。");
                    break;
                }

                if (!TryResolveSlotTypes(sourceNode, sourceSlot, targetNode, targetSlot, out var sourceType, out var targetType)
                    || !sourceType.IsCompatibleWith(targetType))
                {
                    RaiseConnectionCreateFailed($"连接失败：输出类型 [{sourceType?.Key}] 与目标输入类型 [{targetType?.Key}] 不兼容。");
                    break;
                }

                var link = new GraphLink
                {
                    Id = Guid.NewGuid().ToString(),
                    OriginNodeId = sourceNode.Id,
                    OriginSlot = sourceSlot,
                    TargetNodeId = targetNode.Id,
                    TargetSlot = targetSlot,
                };
                GraphModel.Links.Add(link);

                _logger.LogDebug("Created link '{LinkId}' from '{OriginNodeId}' to '{TargetNodeId}'.", link.Id, link.OriginNodeId, link.TargetNodeId);

                var targetPort = targetNode.InputParameters?.FirstOrDefault(p => p.PortId == ResolveInputPortId(targetNode, targetSlot));
                if (targetPort != null)
                {
                    targetPort.LinkId = link.Id;
                }

                var sourcePoint = GetNodeSocketPosition(_originalElement, sourceSlot, isInput: false);
                var targetPoint = GetNodeSocketPosition(hitNode, targetSlot, isInput: true);
                var points = Route(sourcePoint, targetPoint);
                var line = CreateArrowedLine(new PointCollection(points), link.Id);
                _canvas.Children.Add(line);
                RaiseGraphChanged();
                UpdateCanvas();
            }
            while (false);

            _tempLine = null;
            _hoverConnector?.Unhighlight();
            _hoverConnector = null;
            _mouseMode = EMouseMode.None;
        }

        private void SelectionStart()
        {
            CaptureCanvasMouse();

            _mouseMode = EMouseMode.SelectionMode;

            _selectionRect = new Rectangle
            {
                Stroke = Brushes.DodgerBlue,
                StrokeThickness = 1,
                Fill = new SolidColorBrush(Color.FromArgb(60, 30, 144, 255)), // 半透明蓝色
                Width = 0,
                Height = 0
            };

            Canvas.SetLeft(_selectionRect, _startPoint.X);
            Canvas.SetTop(_selectionRect, _startPoint.Y);

            _canvas.Children.Add(_selectionRect);
        }

        private void SelectionMove()
        {
            if (_selectionRect != null)
            {
                Point pos = GetMouseWorldPosition();
                double x = Math.Min(pos.X, _startPoint.X);
                double y = Math.Min(pos.Y, _startPoint.Y);
                double w = Math.Abs(pos.X - _startPoint.X);
                double h = Math.Abs(pos.Y - _startPoint.Y);

                Canvas.SetLeft(_selectionRect, x);
                Canvas.SetTop(_selectionRect, y);
                _selectionRect.Width = w;
                _selectionRect.Height = h;
            }
        }

        private void SelectionFinished()
        {
            _mouseMode = EMouseMode.None;

            if (_selectionRect != null)
            {
                Rect selectRect = new Rect(
                        Canvas.GetLeft(_selectionRect),
                        Canvas.GetTop(_selectionRect),
                        _selectionRect.Width,
                        _selectionRect.Height);

                _selectedNodes.Clear();

                foreach (var child in _canvas.Children)
                {
                    if (child is NodeView nodeView)
                    {
                        double left = Canvas.GetLeft(nodeView);
                        double top = Canvas.GetTop(nodeView);
                        Rect itemRect = new Rect(left, top, nodeView.ActualWidth, nodeView.ActualHeight);

                        if (selectRect.IntersectsWith(itemRect))
                        {
                            _selectedNodes.Add(nodeView);
                        }
                    }
                }

                ApplySelectionVisuals();
                SetSelectedNode(_selectedNodes.Count == 1 ? _selectedNodes[0].NodeModel : null);

                _canvas.Children.Remove(_selectionRect);
                _selectionRect = null;
            }
        }

        private void ApplySelectionVisuals()
        {
            foreach (var nodeView in _canvas.Children.OfType<NodeView>())
            {
                if (_selectedNodes.Contains(nodeView))
                {
                    nodeView.BorderThickness = new Thickness(4);
                    nodeView.BorderBrush = (Brush)FindResource("colorBrandForeground2Hover");
                }
                else
                {
                    nodeView.BorderThickness = new Thickness(2);
                    nodeView.BorderBrush = (Brush)FindResource("colorBrandForeground1");
                }
            }
        }

        private void SetSelectedNode(NodeModel node)
        {
            if (ReferenceEquals(_selectedNode, node))
            {
                return;
            }

            _selectedNode = node;
            SelectedNodeChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateCanvas()
        {
            if (_canvasUpdateQueued)
            {
                return;
            }

            _canvasUpdateQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _canvasUpdateQueued = false;
                RedrawConnections();
            }), DispatcherPriority.Render);
        }

        private List<Point> Route(Point start, Point end)
        {
            var obstacles = new List<Rect>();

            foreach (var child in _canvas.Children)
            {
                if (child is NodeView node)
                {
                    var bounds = new Rect(
                        Canvas.GetLeft(node),
                        Canvas.GetTop(node),
                        node.ActualWidth,
                        node.ActualHeight);
                    obstacles.Add(bounds);
                }
            }

            var canvasBounds = new Rect(0, 0, WorldCanvasSize, WorldCanvasSize);
            var result = OrthogonalRouter.Route(start, end, obstacles, canvasBounds, CellSize, padding: 6);
            if (result.Success)
            {
                return result.Points;
            }

            return new List<Point> { start, end };
        }

        private ConnectionLine CreateArrowedLine(PointCollection points, string connectionId)
        {
            var connectionLine = new ConnectionLine
            {
                Points = points,
                Stroke = (Brush)FindResource("colorBrandStroke1"),
                Fill = (Brush)FindResource("colorBrandStroke1"),
                StrokeThickness = 3.2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                ArrowLength = 14,
                ArrowWidth = 5,
                CornerRadius = 12,
                IsHitTestVisible = true,
                Tag = connectionId,
            };

            connectionLine.MouseEnter += Line_MouseEnter;
            connectionLine.MouseLeave += Line_MouseLeave;
            connectionLine.ContextMenu = _lineContextMenu;

            return connectionLine;
        }

        private void InitializeNodePorts(NodeModel nodeInfo)
        {
            if (nodeInfo == null || string.IsNullOrWhiteSpace(nodeInfo.ExecutorType))
            {
                return;
            }

            if (!NodeExecutorFactory.Registry.TryResolve(nodeInfo.ExecutorType, out var registration))
            {
                return;
            }

            ApplyPortDefinitions(nodeInfo.InputParameters, registration.Definition.InputPorts);
            ApplyPortDefinitions(nodeInfo.OutputParameters, registration.Definition.OutputPorts);
        }

        private static void ApplyPortDefinitions(List<PortParameter> runtimePorts, IReadOnlyList<FlowPortDefinition> definitionPorts)
        {
            if (runtimePorts == null || definitionPorts == null)
            {
                return;
            }

            foreach (var runtimePort in runtimePorts)
            {
                if (runtimePort == null)
                {
                    continue;
                }

                runtimePort.Parameter ??= new Parameter();
                var definitionPort = definitionPorts.FirstOrDefault(port => string.Equals(port.Id, runtimePort.PortId, StringComparison.Ordinal));
                if (definitionPort == null)
                {
                    continue;
                }

                ApplyPortDefinition(runtimePort, definitionPort);
            }

            foreach (var definitionPort in definitionPorts)
            {
                if (runtimePorts.Any(port => port != null && string.Equals(port.PortId, definitionPort.Id, StringComparison.Ordinal)))
                {
                    continue;
                }

                runtimePorts.Add(CreatePortParameter(definitionPort));
            }
        }

        private static void ApplyPortDefinition(PortParameter runtimePort, FlowPortDefinition definitionPort)
        {
            runtimePort.PortId = definitionPort.Id;
            runtimePort.Parameter ??= new Parameter();
            runtimePort.Parameter.ParameterType = definitionPort.DataType?.Key ?? string.Empty;

            if (runtimePort.PortDirection == EPortDirection.None)
            {
                runtimePort.PortDirection = definitionPort.PreferredDirection;
            }
        }

        private static PortParameter CreatePortParameter(FlowPortDefinition definitionPort)
        {
            return new PortParameter
            {
                PortId = definitionPort.Id,
                Parameter = new Parameter { ParameterType = definitionPort.DataType?.Key ?? string.Empty },
                PortDirection = definitionPort.PreferredDirection,
            };
        }

        private static bool TryResolveSlotTypes(NodeModel sourceNode, int sourceSlot, NodeModel targetNode, int targetSlot, out FlowDataType sourceType, out FlowDataType targetType)
        {
            sourceType = null;
            targetType = null;
            if (!NodeExecutorFactory.Registry.TryResolve(sourceNode.ExecutorType, out var srcReg)
                || !NodeExecutorFactory.Registry.TryResolve(targetNode.ExecutorType, out var tgtReg))
            {
                return false;
            }

            if (sourceSlot < 0 || sourceSlot >= srcReg.Definition.OutputPorts.Count
                || targetSlot < 0 || targetSlot >= tgtReg.Definition.InputPorts.Count)
            {
                return false;
            }

            sourceType = srcReg.Definition.OutputPorts[sourceSlot].DataType;
            targetType = tgtReg.Definition.InputPorts[targetSlot].DataType;
            return sourceType != null && targetType != null;
        }

        private static string ResolveInputPortId(NodeModel node, int slot)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.ExecutorType)
                || !NodeExecutorFactory.Registry.TryResolve(node.ExecutorType, out var registration))
            {
                return null;
            }

            return slot >= 0 && slot < registration.Definition.InputPorts.Count
                ? registration.Definition.InputPorts[slot].Id
                : null;
        }

        private static bool IsSlotAllowingMultipleConnections(NodeModel node, int slot)
        {
            if (node == null || slot < 0
                || string.IsNullOrWhiteSpace(node.ExecutorType)
                || !NodeExecutorFactory.Registry.TryResolve(node.ExecutorType, out var registration))
            {
                return false;
            }

            var port = registration.Definition.InputPorts?
                .ElementAtOrDefault(slot);
            return port?.AllowMultipleConnections == true;
        }

        private void ClearTargetPortLinkId(GraphLink link)
        {
            if (link == null)
            {
                return;
            }

            var targetNode = GraphModel?.Nodes?
                .FirstOrDefault(node => string.Equals(node.Id, link.TargetNodeId, StringComparison.Ordinal));
            if (targetNode == null)
            {
                return;
            }

            var targetPortId = ResolveInputPortId(targetNode, link.TargetSlot);
            if (string.IsNullOrWhiteSpace(targetPortId))
            {
                return;
            }

            var port = targetNode.InputParameters?
                .FirstOrDefault(item => string.Equals(item.PortId, targetPortId, StringComparison.Ordinal));
            if (port != null && string.Equals(port.LinkId, link.Id, StringComparison.Ordinal))
            {
                port.LinkId = null;
            }
        }

        private void RedrawConnections()
        {
            _canvas.Children.OfType<ConnectionLine>().ToList().ForEach(p => _canvas.Children.Remove(p));

            foreach (var link in GraphModel?.Links ?? Enumerable.Empty<GraphLink>())
            {
                var sourceNodeView = GetNodeViewByNodeID(link.OriginNodeId);
                var targetNodeView = GetNodeViewByNodeID(link.TargetNodeId);
                if (sourceNodeView == null || targetNodeView == null)
                {
                    continue;
                }

                var sourcePoint = GetNodeSocketPosition(sourceNodeView, link.OriginSlot, isInput: false);
                var targetPoint = GetNodeSocketPosition(targetNodeView, link.TargetSlot, isInput: true);
                var points = Route(sourcePoint, targetPoint);
                _canvas.Children.Add(CreateArrowedLine(new PointCollection(points), link.Id));
            }
        }

        private Point GetNodeSocketPosition(NodeView nodeView, int slot, bool isInput)
        {
            var socketPositionInFlowCanvas = nodeView.GetNodeSocketPosition(slot, isInput);
            var viewportOriginInFlowCanvas = _viewport.TranslatePoint(new Point(), this);
            return ConvertSocketPositionToWorld(
                socketPositionInFlowCanvas,
                viewportOriginInFlowCanvas,
                _viewportTransform);
        }

        private void Line_MouseEnter(object sender, MouseEventArgs e)
        {
            var line = sender as ConnectionLine;
            var highlight = (Brush)FindResource("colorBrandForeground2Hover");
            line.Stroke = highlight;
            line.Fill = highlight;
            line.StrokeThickness = 4.6;
        }

        private void Line_MouseLeave(object sender, MouseEventArgs e)
        {
            var line = sender as ConnectionLine;
            var normal = (Brush)FindResource("colorBrandStroke1");
            line.Stroke = normal;
            line.Fill = normal;
            line.StrokeThickness = 3.2;
        }

        private NodeView GetNodeUnderPosition(Point pos)
        {
            foreach (var child in _canvas.Children)
            {
                if (child is NodeView node)
                {
                    var bounds = new Rect(Canvas.GetLeft(node), Canvas.GetTop(node),
                                          node.ActualWidth, node.ActualHeight);
                    if (bounds.Contains(pos))
                        return node;
                }
            }
            return null;
        }

        private NodeView GetNodeViewByNodeID(string nodeId)
        {
            foreach (var nodeView in _canvas.Children.OfType<NodeView>())
            {
                if (nodeView.NodeModel.Id == nodeId)
                {
                    return nodeView;
                }
            }

            return null;
        }

        private static T FindAncestor<T>(DependencyObject source) where T : DependencyObject
        {
            while (source != null)
            {
                if (source is T target)
                {
                    return target;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return null;
        }

        private static bool IsInteractiveNodeContent(DependencyObject source)
        {
            return FindAncestor<TextBoxBase>(source) != null
                || FindAncestor<PasswordBox>(source) != null
                || FindAncestor<Selector>(source) != null
                || FindAncestor<ButtonBase>(source) != null
                || FindAncestor<Slider>(source) != null
                || FindAncestor<ScrollBar>(source) != null
                || FindAncestor<System.Windows.Controls.Primitives.Thumb>(source) != null;
        }

        private void RaiseGraphChanged()
        {
            RaiseGraphChanged(refreshNodeContents: true);
        }

        private void RaiseGraphChanged(bool refreshNodeContents)
        {
            if (refreshNodeContents)
            {
                RefreshNodeContents();
            }

            GraphChanged?.Invoke(this, EventArgs.Empty);
        }

        private void RaiseConnectionCreateFailed(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            _logger.LogDebug("Connection rejected: {Reason}", message);

            ConnectionCreateFailed?.Invoke(this, new FlowConnectionFailedEventArgs(message));
        }

        private void RefreshNodeContents()
        {
            if (_canvas == null)
            {
                return;
            }

            foreach (var nodeView in _canvas.Children.OfType<NodeView>())
            {
                var nodeModel = nodeView.NodeModel;
                nodeView.Content = NodeContentFactory?.Invoke(nodeModel) ?? nodeModel?.Name;
            }
        }
    }

    public class FlowConnectionFailedEventArgs : EventArgs
    {
        public FlowConnectionFailedEventArgs(string message)
        {
            Message = message ?? string.Empty;
        }

        public string Message { get; }
    }
}
