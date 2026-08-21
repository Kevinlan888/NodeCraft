using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using NodeCraft.Localization;

namespace NodeCraft.Flow
{
    /// <summary>
    /// Renders one labeled socket per input/output port of the bound <see cref="NodeModel"/>.
    /// Input sockets sit on the left edge with the label to the right; output sockets sit on
    /// the right edge with the label to the left. Sockets are rebuilt whenever the model changes.
    /// </summary>
    public class NodeView : ContentControl
    {
        internal FlowCanvas _parentCanvas;

        private StackPanel InputSocketsPanel;
        private StackPanel OutputSocketsPanel;
        private readonly List<Connector> _connectors = new List<Connector>();

        static NodeView()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(NodeView), new FrameworkPropertyMetadata(typeof(NodeView)));
        }

        // 依赖属性
        public static readonly DependencyProperty NodeModelProperty =
            DependencyProperty.Register("NodeModel", typeof(NodeModel), typeof(NodeView), new PropertyMetadata(null, OnNodeModelChanged));

        public NodeModel NodeModel
        {
            get => (NodeModel)GetValue(NodeModelProperty);
            set => SetValue(NodeModelProperty, value);
        }

        private static void OnNodeModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((NodeView)d).RebuildSockets();
        }

        public static readonly DependencyProperty IsResizableProperty =
            DependencyProperty.Register("IsResizable", typeof(bool), typeof(NodeView), new PropertyMetadata(true));

        public bool IsResizable
        {
            get => (bool)GetValue(IsResizableProperty);
            set => SetValue(IsResizableProperty, value);
        }

        public NodeView()
        {
            Focusable = true;
            IsTabStop = false;
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            var outerNode = (Border)GetTemplateChild("OuterNode");
            var innerNode = (Grid)GetTemplateChild("InnerNode");

            InputSocketsPanel = (StackPanel)GetTemplateChild("InputSocketsPanel");
            OutputSocketsPanel = (StackPanel)GetTemplateChild("OutputSocketsPanel");

            // Rebuild after the panels are resolved; the DP change callback may have fired
            // before the template was applied (panels were null then).
            RebuildSockets();

            var resizeThumb = (System.Windows.Controls.Primitives.Thumb)GetTemplateChild("ResizeThumb");
            if (resizeThumb != null)
            {
                resizeThumb.DragDelta += ResizeThumb_DragDelta;
                resizeThumb.DragCompleted += ResizeThumb_DragCompleted;
            }
        }

        private void ResizeThumb_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            var newWidth = Math.Max(MinWidth, ActualWidth + e.HorizontalChange);
            var newHeight = Math.Max(MinHeight, ActualHeight + e.VerticalChange);
            Width = newWidth;
            Height = newHeight;
            if (NodeModel != null)
            {
                NodeModel.Width = newWidth;
                NodeModel.Height = newHeight;
            }
            _parentCanvas?.NotifyNodeLayoutChanged();
        }

        private void ResizeThumb_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            _parentCanvas?.NotifyGraphChanged();
        }

        private void RebuildSockets()
        {
            InputSocketsPanel?.Children.Clear();
            OutputSocketsPanel?.Children.Clear();
            _connectors.Clear();

            if (NodeModel == null || InputSocketsPanel == null || OutputSocketsPanel == null)
            {
                return;
            }

            // 槽位必须按"定义端口顺序"计算，与 FlowCanvas.ResolveDefinitionSlot 的槽位语义保持一致：
            // EnsureControlInputPort 把 flowIn 注入到定义 InputPorts[0]，而运行时 InputParameters 由
            // ApplyPortDefinitions/迁移把缺失定义端口追加到末尾（运行时端口顺序可能与定义顺序不同）。
            // 因此插座按定义顺序构建，Slot = 定义下标，flowIn 才能渲染在最上方且连线命中的插座正确。
            if (!string.IsNullOrWhiteSpace(NodeModel.ExecutorType)
                && NodeExecutorFactory.Registry.TryResolve(NodeModel.ExecutorType, out var registration))
            {
                BuildSocketsFromDefinitions(registration);
            }
            else
            {
                // 未注册的节点类型：回退为运行时端口顺序 + 运行时下标（保持原行为）。
                BuildSocketsFromRuntime();
            }

            EnsureDynamicInputHeight();
        }

        private void BuildSocketsFromDefinitions(FlowNodeRegistration registration)
        {
            var definition = registration?.Definition;
            // ComfyUI-style rendering is definition-driven: every declared slot stays visible,
            // even when a legacy/partially loaded node has no matching runtime PortParameter yet.
            foreach (var socket in FlowSocketResolver.Resolve(NodeModel, definition, isInput: true))
            {
                InputSocketsPanel.Children.Add(CreateSocket(
                    socket.Slot,
                    isInput: true,
                    socket.RuntimePort,
                    socket.Definition));
            }

            if (definition?.DynamicInputTemplate != null)
            {
                InputSocketsPanel.Children.Add(CreateDynamicActionButton("Add input", null, "+"));
            }

            foreach (var socket in FlowSocketResolver.Resolve(NodeModel, definition, isInput: false))
            {
                OutputSocketsPanel.Children.Add(CreateSocket(
                    socket.Slot,
                    isInput: false,
                    socket.RuntimePort,
                    socket.Definition));
            }
        }

        private void BuildSocketsFromRuntime()
        {
            var inputPorts = NodeModel.InputParameters ?? new List<PortParameter>();
            var outputPorts = NodeModel.OutputParameters ?? new List<PortParameter>();

            for (int i = 0; i < inputPorts.Count; i++)
            {
                InputSocketsPanel.Children.Add(CreateSocket(i, isInput: true, inputPorts[i]));
            }

            for (int i = 0; i < outputPorts.Count; i++)
            {
                OutputSocketsPanel.Children.Add(CreateSocket(i, isInput: false, outputPorts[i]));
            }
        }

        private FrameworkElement CreateSocket(int slot, bool isInput, PortParameter port, FlowPortDefinition definition = null)
        {
            var style = FlowSocketResolver.ResolveVisualStyle(definition, port);
            var restingBackground = (Brush)FindResource(style.BrushResourceKey);

            // 控制插座与数据插座保持同等可见性，仅用主题色区分，避免未连接时像是缺少槽位。
            var connector = new Connector
            {
                Direction = isInput ? EPortDirection.Left : EPortDirection.Right,
                IOType = isInput ? EIOType.Input : EIOType.Output,
                Slot = slot,
                IsInput = isInput,
                Width = style.Diameter,
                Height = style.Diameter,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(
                    isInput ? 0 : 4,
                    2,
                    isInput ? 4 : 0,
                    2),
                Background = restingBackground,
                RestingBackground = restingBackground,
            };

            var label = new TextBlock
            {
                Text = ResolvePortLabel(port, definition),
                FontSize = style.LabelFontSize,
                Opacity = style.LabelOpacity,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(isInput ? 4 : 0, 0, isInput ? 0 : 4, 0),
            };

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = isInput ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            };
            if (isInput)
            {
                row.Children.Add(connector);
                row.Children.Add(label);
                if (definition?.IsDynamic == true && !string.IsNullOrWhiteSpace(port?.PortId))
                {
                    row.Children.Add(CreateDynamicActionButton("Remove input", port.PortId, "−"));
                }
            }
            else
            {
                row.Children.Add(label);
                row.Children.Add(connector);
            }

            _connectors.Add(connector);
            return row;
        }

        private Button CreateDynamicActionButton(string automationName, string portId, string content)
        {
            var button = new Button
            {
                Content = content,
                Tag = portId,
                ToolTip = automationName,
                Style = TryFindResource("FlowDynamicInputActionButtonStyle") as Style,
                Margin = new Thickness(3, 1, 0, 1),
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };
            AutomationProperties.SetName(button, automationName);
            button.Click += DynamicInputActionButton_Click;
            return button;
        }

        private void DynamicInputActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && _parentCanvas != null)
            {
                var automationName = AutomationProperties.GetName(button);
                if (string.Equals(automationName, "Add input", StringComparison.Ordinal))
                {
                    if (!_parentCanvas.TryAddDynamicInput(NodeModel, out var error))
                    {
                        _parentCanvas.RaiseConnectionCreateFailed(error);
                    }
                }
                else if (string.Equals(automationName, "Remove input", StringComparison.Ordinal))
                {
                    if (!_parentCanvas.TryRemoveDynamicInput(NodeModel, button.Tag as string, out var error))
                    {
                        _parentCanvas.RaiseConnectionCreateFailed(error);
                    }
                }
            }

            e.Handled = true;
        }

        internal void RefreshDynamicInputs()
        {
            RebuildSockets();
            InvalidateMeasure();
            InvalidateArrange();
            UpdateLayout();
            EnsureDynamicInputHeight();
        }

        internal void EnsureDynamicInputHeight()
        {
            if (NodeModel == null)
            {
                return;
            }

            var desiredHeight = GetDesiredDynamicInputHeight(NodeModel);
            if (desiredHeight <= 0)
            {
                return;
            }

            var explicitHeight = NodeModel.Height > 0 ? NodeModel.Height : Height;
            if (explicitHeight > 0 && desiredHeight > explicitHeight)
            {
                Height = desiredHeight;
                NodeModel.Height = desiredHeight;
            }
        }

        internal static double GetDesiredDynamicInputHeight(NodeModel node)
        {
            if (node == null
                || string.IsNullOrWhiteSpace(node.ExecutorType)
                || !NodeExecutorFactory.Registry.TryResolve(node.ExecutorType, out var registration)
                || registration.Definition.DynamicInputTemplate == null)
            {
                return 0;
            }

            var inputRows = FlowDynamicInputResolver.ResolveNodeInputPorts(node, registration.Definition).Count;
            const double headerAndContentHeight = 52;
            const double inputRowHeight = 22;
            const double addButtonHeight = 22;
            return headerAndContentHeight + (inputRows * inputRowHeight) + addButtonHeight;
        }

        private string ResolvePortLabel(PortParameter port, FlowPortDefinition definition = null)
        {
            return FlowSocketResolver.ResolveLabel(definition, port);
        }

        public Connector GetConnectorUnderPosition(Point mousePos)
        {
            return GetNearestConnector(mousePos, strict: true);
        }

        public Connector GetConnectorNearPosition(Point mousePos)
        {
            return GetNearestConnector(mousePos, strict: false);
        }

        private Connector GetNearestConnector(Point mousePos, bool strict)
        {
            Connector best = null;
            double bestDistance = double.MaxValue;
            foreach (var connector in _connectors)
            {
                var point = connector.TransformToVisual(this).Transform(new Point(connector.ActualWidth / 2, connector.ActualHeight / 2));
                var distance = (mousePos - point).Length;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = connector;
                }
            }

            var threshold = strict ? 14 : 28;
            return best != null && bestDistance <= threshold ? best : null;
        }

        internal Point GetNodeSocketPosition(int slot, bool isInput)
        {
            var connector = _connectors.FirstOrDefault(c => c.Slot == slot && c.IsInput == isInput);
            if (connector == null)
            {
                return new Point(isInput ? 0 : ActualWidth, ActualHeight / 2);
            }
            return connector.TransformToVisual(_parentCanvas).Transform(new Point(connector.ActualWidth / 2, connector.ActualHeight / 2));
        }
    }
}
