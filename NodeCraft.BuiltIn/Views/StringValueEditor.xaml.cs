using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed partial class StringValueEditor : UserControl
    {
        private readonly FlowCanvas _canvas;
        private readonly StringValueNodeModel _node;
        private readonly TextBox _valueEditor;
        private bool _initializing = true;

        private StringValueEditor(FlowCanvas canvas, StringValueNodeModel node)
        {
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            _node = node ?? throw new ArgumentNullException(nameof(node));
            InitializeComponent();
            _valueEditor = ValueEditor;
            _valueEditor.TextChanged += ValueEditor_TextChanged;
            _valueEditor.Text = _node.ValueText ?? string.Empty;
            _initializing = false;
        }

        internal static FrameworkElement CreateContent(FlowCanvas canvas, NodeModel node)
        {
            if (node is not StringValueNodeModel valueNode)
            {
                throw new InvalidOperationException(
                    "StringValueEditor requires a StringValueNodeModel.");
            }

            return new StringValueEditor(canvas, valueNode);
        }

        private void ValueEditor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_initializing)
            {
                return;
            }

            var value = _valueEditor.Text ?? string.Empty;
            if (string.Equals(_node.ValueText, value, StringComparison.Ordinal))
            {
                return;
            }

            _node.ValueText = value;
            _canvas.NotifyGraphChanged(false);
        }
    }
}
