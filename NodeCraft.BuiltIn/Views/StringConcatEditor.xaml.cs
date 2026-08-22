using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed partial class StringConcatEditor : UserControl
    {
        private readonly FlowCanvas _canvas;
        private readonly StringConcatNodeModel _node;
        private readonly TextBox _separatorEditor;
        private bool _initializing = true;

        private StringConcatEditor(FlowCanvas canvas, StringConcatNodeModel node)
        {
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            _node = node ?? throw new ArgumentNullException(nameof(node));
            InitializeComponent();
            _separatorEditor = SeparatorEditor;
            _separatorEditor.TextChanged += SeparatorEditor_TextChanged;
            _separatorEditor.Text = _node.Separator ?? string.Empty;
            _initializing = false;
        }

        internal static FrameworkElement CreateContent(FlowCanvas canvas, NodeModel node)
        {
            if (node is not StringConcatNodeModel concatNode)
            {
                throw new InvalidOperationException("StringConcatEditor requires a StringConcatNodeModel.");
            }

            return new StringConcatEditor(canvas, concatNode);
        }

        private void SeparatorEditor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_initializing)
            {
                return;
            }

            var value = _separatorEditor.Text ?? string.Empty;
            if (string.Equals(_node.Separator, value, StringComparison.Ordinal))
            {
                return;
            }

            _node.Separator = value;
            _canvas.NotifyGraphChanged(false);
        }
    }
}
