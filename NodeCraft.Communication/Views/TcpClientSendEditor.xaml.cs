using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using NodeCraft.Communication.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.Communication.Views
{
    internal sealed class TcpClientSendEditor : UserControl
    {
        private readonly FlowCanvas _canvas;
        private readonly TcpClientSendNodeModel _node;
        private readonly TextBox _hostEditor;
        private readonly TextBox _portEditor;
        private readonly TextBox _connectTimeoutEditor;
        private readonly CheckBox _stopOnSendFailureEditor;
        private bool _initializing = true;

        private TcpClientSendEditor(FlowCanvas canvas, TcpClientSendNodeModel node)
        {
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            _node = node ?? throw new ArgumentNullException(nameof(node));

            var root = LoadEditorRoot();
            var parsedContent = root.Content;
            root.Content = null;
            Content = parsedContent;
            _hostEditor = Find<TextBox>(root, "HostEditor");
            _portEditor = Find<TextBox>(root, "PortEditor");
            _connectTimeoutEditor = Find<TextBox>(root, "ConnectTimeoutEditor");
            _stopOnSendFailureEditor = Find<CheckBox>(root, "StopOnSendFailureEditor");

            _hostEditor.TextChanged += HostEditor_TextChanged;
            _portEditor.TextChanged += PortEditor_TextChanged;
            _connectTimeoutEditor.TextChanged += ConnectTimeoutEditor_TextChanged;
            _stopOnSendFailureEditor.Checked += StopOnSendFailureEditor_Changed;
            _stopOnSendFailureEditor.Unchecked += StopOnSendFailureEditor_Changed;

            _hostEditor.Text = _node.Host ?? string.Empty;
            _portEditor.Text = _node.Port.ToString(CultureInfo.InvariantCulture);
            _connectTimeoutEditor.Text = _node.ConnectTimeoutMilliseconds
                .ToString(CultureInfo.InvariantCulture);
            _stopOnSendFailureEditor.IsChecked = _node.StopOnSendFailure;
            _initializing = false;
        }

        internal static FrameworkElement CreateContent(FlowCanvas canvas, NodeModel node)
        {
            if (!(node is TcpClientSendNodeModel tcpNode))
            {
                throw new InvalidOperationException(
                    "TcpClientSendEditor requires a TcpClientSendNodeModel.");
            }

            return new TcpClientSendEditor(canvas, tcpNode);
        }

        private static T Find<T>(UserControl root, string name)
            where T : FrameworkElement
        {
            return root.FindName(name) as T
                ?? throw new InvalidOperationException(
                    $"TcpClientSendEditor is missing {name}.");
        }

        private static UserControl LoadEditorRoot()
        {
            var assembly = typeof(TcpClientSendEditor).Assembly;
            using var stream = assembly.GetManifestResourceStream(
                "NodeCraft.Communication.Views.TcpClientSendEditor.xaml");
            if (stream == null)
            {
                throw new InvalidOperationException(
                    "TcpClientSendEditor.xaml was not embedded into the plugin assembly.");
            }

            using var reader = new StreamReader(stream);
            return XamlReader.Parse(reader.ReadToEnd()) as UserControl
                ?? throw new InvalidOperationException(
                    "TcpClientSendEditor.xaml did not produce a UserControl root.");
        }

        private void HostEditor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_initializing)
            {
                return;
            }

            _node.Host = _hostEditor.Text ?? string.Empty;
            NotifyChanged();
        }

        private void PortEditor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_initializing
                || !int.TryParse(
                    _portEditor.Text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var value)
                || value < 1
                || value > 65535)
            {
                return;
            }

            _node.Port = value;
            NotifyChanged();
        }

        private void ConnectTimeoutEditor_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            if (_initializing
                || !int.TryParse(
                    _connectTimeoutEditor.Text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var value)
                || value <= 0)
            {
                return;
            }

            _node.ConnectTimeoutMilliseconds = value;
            NotifyChanged();
        }

        private void StopOnSendFailureEditor_Changed(object sender, RoutedEventArgs e)
        {
            if (_initializing)
            {
                return;
            }

            _node.StopOnSendFailure = _stopOnSendFailureEditor.IsChecked == true;
            NotifyChanged();
        }

        private void NotifyChanged()
        {
            _canvas.NotifyGraphChanged(refreshNodeContents: false);
        }
    }
}
