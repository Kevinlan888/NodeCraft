using NodeCraft;
using NodeCraft.Execution;
using NodeCraft.Flow;
using NodeCraft.Flow.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace NodeCraft.Pages
{
    /// <summary>
    /// FlowPage.xaml 的交互逻辑
    /// </summary>
    public partial class FlowPage : UserControl
    {
        private readonly ILogger<FlowPage> _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly FlowCanvas _nodeCanvas;
        private readonly FlowExecutionController _executionController;
        private bool _starterLayoutLoaded;
        private int _nextNodeIndex;
        private string _currentGraphFilePath;

        public event EventHandler ExecutionStateChanged;

        public bool IsExecutionActive => _executionController.State != FlowRunState.Idle;

        public FlowPage(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            _logger = loggerFactory.CreateLogger<FlowPage>();
            _executionController = new FlowExecutionController();
            _executionController.StateChanged += ExecutionController_StateChanged;
            InitializeComponent();

            _nodeCanvas = new FlowCanvas(loggerFactory.CreateLogger<FlowCanvas>())
            {
                BorderThickness = new Thickness(1),
            };
            _nodeCanvas.SetResourceReference(Control.BackgroundProperty, "colorSubtleBackground");
            _nodeCanvas.SetResourceReference(Control.BorderBrushProperty, "colorNeutralStroke1");
            CanvasHost.Child = _nodeCanvas;

            _nodeCanvas.NodeContentFactory = node => NodeExecutorFactory.Registry.BuildNodeContent(_nodeCanvas, node);
            _nodeCanvas.ConnectionCreateFailed += NodeCanvas_ConnectionCreateFailed;
            InitializePalette();
        }

        private void NodeCanvas_ConnectionCreateFailed(object sender, FlowConnectionFailedEventArgs e)
        {
            TxtExecutionResult.Text = e.Message;
        }

        private void FlowPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (_starterLayoutLoaded)
            {
                return;
            }

            _starterLayoutLoaded = true;
            _currentGraphFilePath = null;
            UpdateCurrentFilePath();
            LoadGraph(new GraphModel
            {
                Nodes = new List<NodeModel>(),
                Links = new List<GraphLink>()
            });
        }

        private void InitializePalette()
        {
            NodePalette.Categories = NodeExecutorFactory.Registry.CreatePaletteCategories();
        }

        private void AddNodeFromRegistry(string typeKey)
        {
            if (!NodeExecutorFactory.Registry.TryCreateNodeByTypeKey(typeKey, out var node))
            {
                return;
            }

            AddNode(node, NodeExecutorFactory.Registry.GetDisplayName(typeKey));
        }

        public void SaveGraph()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_currentGraphFilePath))
                {
                    SaveGraphAsCore();
                }
                else
                {
                    SaveGraphToPath(_currentGraphFilePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save graph.");
                TxtExecutionResult.Text = ExecutionErrorFormatter.Format(
                    "Failed to save graph.",
                    ex,
                    512);
            }
        }

        public void SaveGraphAs()
        {
            try
            {
                SaveGraphAsCore();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save graph as.");
                TxtExecutionResult.Text = ExecutionErrorFormatter.Format(
                    "Failed to save graph as.",
                    ex,
                    512);
            }
        }

        public void LoadGraph()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Flow Graph (*.flow.xml)|*.flow.xml|XML Files (*.xml)|*.xml|All Files (*.*)|*.*",
                DefaultExt = ".flow.xml",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                TryLoadGraphFile(dialog.FileName);
            }
        }

        public bool TryLoadGraphFile(string filePath)
        {
            try
            {
                var loadResult = GraphModelXmlSerializer.LoadWithReport(filePath, _logger);
                LoadGraph(loadResult.Graph);
                _starterLayoutLoaded = true;
                _currentGraphFilePath = filePath;
                UpdateCurrentFilePath();
                TxtExecutionResult.Text = FormatLoadResult(filePath, loadResult);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load graph from '{FilePath}'.", filePath);
                TxtExecutionResult.Text = ExecutionErrorFormatter.Format(
                    "Failed to load graph.",
                    ex,
                    512);
                return false;
            }
        }

        public void NewGraph()
        {
            CreateStarterGraph();
            TxtExecutionResult.Text = "已新建空白流程。";
        }

        public void ClearGraph()
        {
            LoadGraph(new GraphModel
            {
                Nodes = new List<NodeModel>(),
                Links = new List<GraphLink>()
            });

            _currentGraphFilePath = null;
            UpdateCurrentFilePath();
            TxtExecutionResult.Text = "画布已清空。";
        }

        public void ValidateGraph()
        {
            try
            {
                var workflow = BuildWorkflowDocument();
                var executor = new GraphExecutor(workflow, logger: _loggerFactory.CreateLogger<GraphExecutor>());
                var validation = executor.Validate();
                TxtExecutionResult.Text = FormatValidation(validation, workflow);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to validate graph.");
                TxtExecutionResult.Text = ExecutionErrorFormatter.Format(
                    "Failed to validate graph.",
                    ex,
                    512);
            }
        }

        public async void RunGraph()
        {
            await RunOnceAsync().ConfigureAwait(true);
        }

        public Task RunOnceAsync(CancellationToken cancellationToken = default)
        {
            return RunWithControllerAsync(continuous: false, cancellationToken);
        }

        public Task RunContinuouslyAsync(CancellationToken cancellationToken = default)
        {
            return RunWithControllerAsync(continuous: true, cancellationToken);
        }

        public async Task StopExecutionAsync()
        {
            try
            {
                await _executionController.StopAsync().ConfigureAwait(true);
                if (!IsExecutionActive)
                {
                    TxtExecutionResult.Text = "已停止。";
                }
            }
            catch (Exception ex)
            {
                ReportExecutionFailure(ex, "Graph execution stop failed.");
                throw;
            }
        }

        private async Task RunWithControllerAsync(bool continuous, CancellationToken cancellationToken)
        {
            GraphExecutionSession session = null;
            try
            {
                var workflow = BuildWorkflowDocument();
                var executor = new GraphExecutor(workflow, logger: _loggerFactory.CreateLogger<GraphExecutor>());
                var validation = executor.Validate();
                if (!validation.IsValid)
                {
                    TxtExecutionResult.Text = FormatValidation(validation, workflow);
                    return;
                }

                session = executor.CreateSession();
                Func<FlowExecutionContext, long, TimeSpan, Task> callback = (context, iteration, elapsed) =>
                    Dispatcher.InvokeAsync(() =>
                    {
                        ApplyExecutionResults(context);
                        TxtExecutionResult.Text = FormatExecution(
                            context,
                            workflow,
                            continuous ? "持续运行" : "执行一次",
                            iteration,
                            elapsed);
                    }).Task;

                Task runTask;
                try
                {
                    runTask = continuous
                        ? _executionController.RunContinuouslyAsync(session, callback, cancellationToken)
                        : _executionController.RunOnceAsync(session, callback, cancellationToken);
                }
                catch
                {
                    await session.DisposeAsync().ConfigureAwait(true);
                    session = null;
                    throw;
                }

                session = null;
                await runTask.ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                TxtExecutionResult.Text = "已停止。";
            }
            catch (Exception ex)
            {
                ReportExecutionFailure(ex, "Graph execution failed.");
            }
            finally
            {
                if (session != null)
                {
                    await session.DisposeAsync().ConfigureAwait(true);
                }
            }
        }

        private void AddNode(NodeModel node, string displayName)
        {
            var position = GetNextNodePosition();
            node.Name = CreateNodeName(displayName);
            node.X = position.X;
            node.Y = position.Y;

            _nodeCanvas.AddNode(node);
            RefreshNodePresentation(node);
            _nextNodeIndex++;
        }

        private void LoadGraph(GraphModel graph)
        {
            _nodeCanvas.LoadGraph(graph, node => NodeExecutorFactory.Registry.BuildNodeContent(_nodeCanvas, node));
            _nextNodeIndex = _nodeCanvas.GraphModel?.Nodes?.Count ?? 0;
        }

        private void CreateStarterGraph()
        {
            _currentGraphFilePath = null;
            UpdateCurrentFilePath();

            LoadGraph(new GraphModel
            {
                Nodes = new List<NodeModel>(),
                Links = new List<GraphLink>()
            });

        }

        private void SaveGraphAsCore()
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Flow Graph (*.flow.xml)|*.flow.xml|XML Files (*.xml)|*.xml|All Files (*.*)|*.*",
                DefaultExt = ".flow.xml",
                AddExtension = true,
                FileName = Path.GetFileName(_currentGraphFilePath) ?? "untitled.flow.xml"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            SaveGraphToPath(dialog.FileName);
        }

        private void SaveGraphToPath(string filePath)
        {
            GraphModelXmlSerializer.Save(_nodeCanvas.GraphModel, filePath, _logger);
            _currentGraphFilePath = filePath;
            UpdateCurrentFilePath();
            TxtExecutionResult.Text = $"已保存: {filePath}";
        }

        private void UpdateCurrentFilePath()
        {
            TxtCurrentFilePath.Text = string.IsNullOrWhiteSpace(_currentGraphFilePath)
                ? "当前文件: 未保存"
                : $"当前文件: {_currentGraphFilePath}";
        }

        private string CreateNodeName(string displayName)
        {
            return $"{displayName} {_nextNodeIndex + 1}";
        }

        private Point GetNextNodePosition()
        {
            var column = _nextNodeIndex % 3;
            var row = _nextNodeIndex / 3;

            return new Point(
                48 + (column * 240),
                48 + (row * 160) + (column * 24));
        }

        private WorkflowDocument BuildWorkflowDocument()
        {
            return GraphModelWorkflowAdapter.Convert(_nodeCanvas.GraphModel);
        }

        private static string FormatValidation(FlowValidationResult result, WorkflowDocument workflow)
        {
            if (result == null || result.IsValid)
            {
                return "校验通过。";
            }

            var nodeLookup = workflow?.Nodes?.ToDictionary(node => node.Id, node => node) ?? new Dictionary<string, WorkflowNode>();
            var builder = new StringBuilder();
            builder.AppendLine("Validation Errors:");
            foreach (var error in result.Errors)
            {
                var nodeLabel = ResolveNodeLabel(nodeLookup, error.NodeId);
                if (!string.IsNullOrWhiteSpace(error.NodeId))
                {
                    builder.Append($"- [{error.Code}] {nodeLabel}");

                    if (!string.IsNullOrWhiteSpace(error.PortId))
                    {
                        builder.Append($" / {ResolvePortLabel(nodeLookup, error.NodeId, error.PortId)}");
                    }

                    builder.Append(": ");
                }
                else
                {
                    builder.Append($"- [{error.Code}] ");
                }

                builder.AppendLine(error.Message);
            }

            return builder.ToString();
        }

        private static string FormatExecution(
            FlowExecutionContext context,
            WorkflowDocument workflow,
            string runMode,
            long iteration,
            TimeSpan elapsed)
        {
            var nodeLookup = workflow?.Nodes?.ToDictionary(node => node.Id, node => node) ?? new Dictionary<string, WorkflowNode>();
            var builder = new StringBuilder();
            builder.AppendLine($"运行模式: {runMode}");
            builder.AppendLine($"迭代: {iteration}");
            builder.AppendLine($"耗时: {elapsed.TotalMilliseconds:0.0} ms");
            builder.AppendLine();

            foreach (var node in workflow?.Nodes ?? Enumerable.Empty<WorkflowNode>())
            {
                var status = context.Statuses.TryGetValue(node.Id, out var executionStatus)
                    ? executionStatus
                    : FlowNodeExecutionStatus.Pending;

                builder.AppendLine($"[{ResolveNodeLabel(nodeLookup, node.Id)}]");
                builder.AppendLine($"Status: {status}");

                var values = context.Values
                    .Where(item => string.Equals(item.Key.Item1, node.Id, StringComparison.Ordinal))
                    .OrderBy(item => item.Key.Item2)
                    .ToList();

                if (values.Count == 0)
                {
                    builder.AppendLine("Outputs: (none)");
                }
                else
                {
                    builder.AppendLine("Outputs:");
                    foreach (var value in values)
                    {
                        builder.AppendLine($"- {ResolvePortLabel(nodeLookup, node.Id, value.Key.Item2)} = {FormatValue(value.Value)}");
                    }
                }

                if (context.Errors.TryGetValue(node.Id, out var exception))
                {
                    builder.AppendLine($"Error: {exception.Message}");
                }

                builder.AppendLine();
            }

            var orphanStatuses = context.Statuses.Keys
                .Where(nodeId => workflow == null || workflow.Nodes.All(node => !string.Equals(node.Id, nodeId, StringComparison.Ordinal)))
                .OrderBy(nodeId => nodeId)
                .ToList();

            if (orphanStatuses.Count > 0)
            {
                builder.AppendLine("Other Node Status:");
                foreach (var nodeId in orphanStatuses)
                {
                    builder.AppendLine($"- {nodeId}: {context.Statuses[nodeId]}");
                }
            }

            return builder.ToString().TrimEnd();
        }

        private static string ResolveNodeLabel(IReadOnlyDictionary<string, WorkflowNode> nodeLookup, string nodeId)
        {
            if (!string.IsNullOrWhiteSpace(nodeId) && nodeLookup != null && nodeLookup.TryGetValue(nodeId, out var node))
            {
                return string.IsNullOrWhiteSpace(node.DisplayName)
                    ? node.Id
                    : $"{node.DisplayName} ({node.Id})";
            }

            return string.IsNullOrWhiteSpace(nodeId) ? "Workflow" : nodeId;
        }

        private static string ResolvePortLabel(IReadOnlyDictionary<string, WorkflowNode> nodeLookup, string nodeId, string portId)
        {
            if (string.IsNullOrWhiteSpace(portId))
            {
                return "Port";
            }

            if (!string.IsNullOrWhiteSpace(nodeId)
                && nodeLookup != null
                && nodeLookup.TryGetValue(nodeId, out var node)
                && NodeExecutorFactory.Registry.TryResolve(node.TypeKey, out var registration))
            {
                var port = registration.Definition.GetInputPort(portId) ?? registration.Definition.GetOutputPort(portId);
                if (port != null)
                {
                    return string.IsNullOrWhiteSpace(port.DisplayName)
                        ? port.Id
                        : $"{port.DisplayName} ({port.Id})";
                }
            }

            return portId;
        }

        private static string ResolvePortLabel(IReadOnlyDictionary<string, WorkflowNode> nodeLookup, string nodeId, int slot)
        {
            if (!string.IsNullOrWhiteSpace(nodeId)
                && nodeLookup != null
                && nodeLookup.TryGetValue(nodeId, out var node)
                && NodeExecutorFactory.Registry.TryResolve(node.TypeKey, out var registration)
                && slot >= 0
                && slot < registration.Definition.OutputPorts.Count)
            {
                var port = registration.Definition.OutputPorts[slot];
                if (port != null)
                {
                    return string.IsNullOrWhiteSpace(port.DisplayName)
                        ? port.Id
                        : $"{port.DisplayName} ({port.Id})";
                }
            }

            return $"Slot {slot}";
        }

        private static string FormatValue(object value)
        {
            if (value == null)
            {
                return "<null>";
            }

            if (value is FlowImage image)
            {
                return $"{image.Kind} {image.Width}x{image.Height} {image.PixelFormat}, frame {image.FrameId}";
            }

            if (value is CameraCalibration calibration)
            {
                return $"Calibration {calibration.ImageWidth}x{calibration.ImageHeight}, left-reference={calibration.IsLeftReference}";
            }

            if (value is System.Collections.IEnumerable enumerable && value is not string)
            {
                var items = new List<string>();
                foreach (var item in enumerable)
                {
                    items.Add(item?.ToString() ?? "<null>");
                }

                return $"[{string.Join(", ", items)}]";
            }

            return value.ToString();
        }

        private static string FormatLoadResult(string filePath, GraphLoadResult loadResult)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"已加载: {filePath}");
            builder.AppendLine($"格式版本: v{loadResult.FormatVersion}");
            return builder.ToString();
        }

        private void RefreshNodePresentation(NodeModel node)
        {
            _nodeCanvas.RefreshNode(node, NodeExecutorFactory.Registry.BuildNodeContent(_nodeCanvas, node));
        }

        private void ApplyExecutionResults(FlowExecutionContext context)
        {
            var updatedNodes = NodeExecutorFactory.Registry.ApplyExecutionResults(_nodeCanvas.GraphModel?.Nodes ?? Enumerable.Empty<NodeModel>(), context);
            foreach (var node in updatedNodes)
            {
                if (NodeExecutorFactory.Registry.ShouldRefreshContentAfterExecution(node))
                {
                    RefreshNodePresentation(node);
                }
            }
        }

        private void ExecutionController_StateChanged(object sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                _ = Dispatcher.InvokeAsync(() => ExecutionController_StateChanged(sender, e));
                return;
            }

            if (ExecutionInputBlocker != null)
            {
                ExecutionInputBlocker.Visibility = IsExecutionActive
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            ExecutionStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ReportExecutionFailure(Exception exception, string message)
        {
            _logger.LogError(exception, message);
            TxtExecutionResult.Text = ExecutionErrorFormatter.Format(message, exception, 512);
        }
    }
}
