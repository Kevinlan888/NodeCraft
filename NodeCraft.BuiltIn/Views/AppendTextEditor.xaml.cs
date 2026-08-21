using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed partial class AppendTextEditor : UserControl
    {
        private readonly FlowCanvas _canvas;
        private readonly AppendTextNodeModel _node;
        private readonly TextBox _suffixEditor;
        private bool _initializing = true;

        private AppendTextEditor(FlowCanvas canvas, AppendTextNodeModel node)
        {
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            _node = node ?? throw new ArgumentNullException(nameof(node));
            InitializeComponent();
            _suffixEditor = SuffixEditor;
            _suffixEditor.TextChanged += SuffixEditor_TextChanged;
            _suffixEditor.Text = _node.SuffixText ?? string.Empty;
            _initializing = false;
        }

        internal static FrameworkElement CreateContent(FlowCanvas canvas, NodeModel node)
        {
            if (node is not AppendTextNodeModel appendNode)
            {
                throw new InvalidOperationException(
                    "AppendTextEditor requires an AppendTextNodeModel.");
            }

            return new AppendTextEditor(canvas, appendNode);
        }

        private void SuffixEditor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_initializing)
            {
                return;
            }

            var value = _suffixEditor.Text ?? string.Empty;
            if (string.Equals(_node.SuffixText, value, StringComparison.Ordinal))
            {
                return;
            }

            _node.SuffixText = value;
            _canvas.NotifyGraphChanged(false);
        }
    }
}
