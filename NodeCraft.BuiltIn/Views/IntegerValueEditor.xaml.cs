using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed class IntegerValueEditor : UserControl
    {
        private readonly FlowCanvas _canvas;
        private readonly IntegerValueNodeModel _node;
        private readonly TextBox _integerEditor;
        private bool _initializing = true;

        private IntegerValueEditor(FlowCanvas canvas, IntegerValueNodeModel node)
        {
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            _node = node ?? throw new ArgumentNullException(nameof(node));
            var root = BuiltInXamlViewLoader.LoadAndAttach(this, nameof(IntegerValueEditor));
            _integerEditor = BuiltInXamlViewLoader.RequireElement<TextBox>(
                root,
                nameof(IntegerValueEditor),
                "IntegerEditor");
            _integerEditor.TextChanged += IntegerEditor_TextChanged;
            _integerEditor.Text = _node.IntegerValue.ToString(CultureInfo.InvariantCulture);
            _initializing = false;
        }

        internal static FrameworkElement CreateContent(FlowCanvas canvas, NodeModel node)
        {
            if (node is not IntegerValueNodeModel integerNode)
            {
                throw new InvalidOperationException(
                    "IntegerValueEditor requires an IntegerValueNodeModel.");
            }

            return new IntegerValueEditor(canvas, integerNode);
        }

        private void IntegerEditor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_initializing
                || !int.TryParse(
                    _integerEditor.Text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var value)
                || _node.IntegerValue == value)
            {
                return;
            }

            _node.IntegerValue = value;
            _canvas.NotifyGraphChanged(refreshNodeContents: false);
        }
    }
}
