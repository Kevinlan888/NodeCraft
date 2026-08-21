using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed class FloatValueEditor : UserControl
    {
        private readonly FlowCanvas _canvas;
        private readonly FloatValueNodeModel _node;
        private readonly TextBox _floatEditor;
        private bool _initializing = true;

        private FloatValueEditor(FlowCanvas canvas, FloatValueNodeModel node)
        {
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            _node = node ?? throw new ArgumentNullException(nameof(node));
            var root = BuiltInXamlViewLoader.LoadAndAttach(this, nameof(FloatValueEditor));
            _floatEditor = BuiltInXamlViewLoader.RequireElement<TextBox>(
                root,
                nameof(FloatValueEditor),
                "FloatEditor");
            _floatEditor.TextChanged += FloatEditor_TextChanged;
            _floatEditor.Text = _node.FloatValue.ToString(
                "F3",
                CultureInfo.InvariantCulture);
            _initializing = false;
        }

        internal static FrameworkElement CreateContent(FlowCanvas canvas, NodeModel node)
        {
            if (node is not FloatValueNodeModel floatNode)
            {
                throw new InvalidOperationException(
                    "FloatValueEditor requires a FloatValueNodeModel.");
            }

            return new FloatValueEditor(canvas, floatNode);
        }

        private void FloatEditor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_initializing
                || !double.TryParse(
                    _floatEditor.Text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value)
                || !double.IsFinite(value)
                || _node.FloatValue.Equals(value))
            {
                return;
            }

            _node.FloatValue = value;
            _canvas.NotifyGraphChanged(refreshNodeContents: false);
        }
    }
}
