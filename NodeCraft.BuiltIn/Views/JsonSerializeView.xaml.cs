using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed partial class JsonSerializeView : UserControl
    {
        private JsonSerializeView(FlowCanvas canvas, JsonSerializeNodeModel node)
        {
            if (canvas == null)
            {
                throw new ArgumentNullException(nameof(canvas));
            }

            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            InitializeComponent();
            InputValue.Text = BuiltInInputViewSupport.DescribeUnaryInput(canvas, node);
        }

        internal static FrameworkElement CreateContent(FlowCanvas canvas, NodeModel node)
        {
            if (node is not JsonSerializeNodeModel serializeNode)
            {
                throw new InvalidOperationException(
                    "JsonSerializeView requires a JsonSerializeNodeModel.");
            }

            return new JsonSerializeView(canvas, serializeNode);
        }
    }
}
