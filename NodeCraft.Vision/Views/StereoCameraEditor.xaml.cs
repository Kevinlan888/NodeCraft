using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.Flow;
using NodeCraft.Vision.StereoCamera.Nodes;

namespace NodeCraft.Vision.StereoCamera.Views
{
    internal sealed partial class StereoCameraEditor : UserControl
    {
        private readonly FlowCanvas _canvas;
        private readonly StereoCameraNodeModel _node;
        private readonly TextBox _ipAddressEditor;
        private bool _initializing = true;

        private StereoCameraEditor(FlowCanvas canvas, StereoCameraNodeModel node)
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
            if (!(node is StereoCameraNodeModel cameraNode))
            {
                throw new InvalidOperationException("StereoCameraEditor requires a StereoCameraNodeModel.");
            }

            return new StereoCameraEditor(canvas, cameraNode);
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
