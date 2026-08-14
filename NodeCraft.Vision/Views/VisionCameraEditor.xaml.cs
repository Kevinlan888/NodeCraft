using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using NodeCraft.Flow;
using NodeCraft.Vision.Nodes;

namespace NodeCraft.Vision.Views
{
    internal sealed class VisionCameraEditor : UserControl
    {
        private readonly FlowCanvas _canvas;
        private readonly VisionCameraNodeModel _node;
        private readonly TextBox _ipAddressEditor;
        private bool _initializing = true;

        private VisionCameraEditor(FlowCanvas canvas, VisionCameraNodeModel node)
        {
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            _node = node ?? throw new ArgumentNullException(nameof(node));

            var root = LoadEditorRoot();
            var parsedContent = root.Content;
            root.Content = null;
            Content = parsedContent;
            _ipAddressEditor = root.FindName("IpAddressEditor") as TextBox
                ?? throw new InvalidOperationException("VisionCameraEditor is missing IpAddressEditor.");
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

        private static UserControl LoadEditorRoot()
        {
            var assembly = typeof(VisionCameraEditor).Assembly;
            using var stream = assembly.GetManifestResourceStream(
                "NodeCraft.Vision.Views.VisionCameraEditor.xaml");
            if (stream == null)
            {
                throw new InvalidOperationException("VisionCameraEditor.xaml was not embedded into the plugin assembly.");
            }

            using var reader = new StreamReader(stream);
            return XamlReader.Parse(reader.ReadToEnd()) as UserControl
                ?? throw new InvalidOperationException("VisionCameraEditor.xaml did not produce a UserControl root.");
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
