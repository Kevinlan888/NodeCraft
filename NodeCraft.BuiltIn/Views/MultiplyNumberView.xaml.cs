using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed partial class MultiplyNumberView : UserControl
    {
        private MultiplyNumberView(FlowCanvas canvas, MultiplyNumberNodeModel node)
        {
            InitializeComponent();
            BuiltInInputViewSupport.BindBinary(canvas, node, InputAValue, InputBValue, SwapInputsButton);
        }

        internal static FrameworkElement CreateContent(FlowCanvas canvas, NodeModel node)
        {
            if (node?.GetType() != typeof(MultiplyNumberNodeModel))
            {
                throw new InvalidOperationException("MultiplyNumberView requires a MultiplyNumberNodeModel.");
            }

            return new MultiplyNumberView(canvas, (MultiplyNumberNodeModel)node);
        }
    }
}
