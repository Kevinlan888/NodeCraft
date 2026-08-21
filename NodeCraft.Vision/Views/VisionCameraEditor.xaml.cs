using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.Flow;
using NodeCraft.Vision.Nodes;

namespace NodeCraft.Vision.Views
{
    internal sealed partial class VisionCameraEditor : UserControl
    {
        private readonly FlowCanvas _canvas;
        private readonly VisionCameraNodeModel _node;
        private readonly TextBox _ipAddressEditor;
        private bool _initializing = true;

        private VisionCameraEditor(FlowCanvas canvas, VisionCameraNodeModel node)
        {
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            _node = node ?? throw new ArgumentNullException(nameof(node));

            InitializeComponent();
            _ipAddressEditor = IpAddressEditor;
            _ipAddressEditor.TextChanged += IpAddressEditor_TextChanged;
            _ipAddressEditor.Text = _node.IpAddress ?? string.Empty;
            _initializing = false;
        }

        internal static FrameworkElement CreateContent(FlowCanvas canvas, NodeModel node)
        {
            if (!(node is VisionCameraNodeModel cameraNode))
            {
                throw new InvalidOperationException("VisionCameraEditor requires a VisionCameraNodeModel.");
            }

            return new VisionCameraEditor(canvas, cameraNode);
        }

        private void IpAddressEditor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_initializing)
            {
                return;
            }

            _node.IpAddress = _ipAddressEditor.Text ?? string.Empty;
            _canvas.NotifyGraphChanged(refreshNodeContents: false);
        }
    }
}
