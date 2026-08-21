using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed partial class BooleanValueEditor : UserControl
    {
        private readonly FlowCanvas _canvas;
        private readonly BooleanValueNodeModel _node;
        private readonly CheckBox _booleanEditor;
        private bool _initializing = true;

        private BooleanValueEditor(FlowCanvas canvas, BooleanValueNodeModel node)
        {
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            _node = node ?? throw new ArgumentNullException(nameof(node));
            InitializeComponent();
            _booleanEditor = BooleanEditor;
            _booleanEditor.Checked += BooleanEditor_Changed;
            _booleanEditor.Unchecked += BooleanEditor_Changed;
            _booleanEditor.IsChecked = _node.BooleanValue;
            SynchronizeContent(_node.BooleanValue);
            _initializing = false;
        }

        internal static FrameworkElement CreateContent(FlowCanvas canvas, NodeModel node)
        {
            if (node is not BooleanValueNodeModel booleanNode)
            {
                throw new InvalidOperationException(
                    "BooleanValueEditor requires a BooleanValueNodeModel.");
            }

            return new BooleanValueEditor(canvas, booleanNode);
        }

        private void BooleanEditor_Changed(object sender, RoutedEventArgs e)
        {
            var value = _booleanEditor.IsChecked == true;
            SynchronizeContent(value);
            if (_initializing || _node.BooleanValue == value)
            {
                return;
            }

            _node.BooleanValue = value;
            _canvas.NotifyGraphChanged(refreshNodeContents: false);
        }

        private void SynchronizeContent(bool value)
        {
            _booleanEditor.Content = value ? "True" : "False";
        }
    }
}
