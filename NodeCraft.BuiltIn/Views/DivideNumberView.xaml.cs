using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed partial class DivideNumberView : UserControl
    {
        private DivideNumberView(FlowCanvas canvas, DivideNumberNodeModel node)
        {
            InitializeComponent();
            BuiltInInputViewSupport.BindBinary(canvas, node, InputAValue, InputBValue, SwapInputsButton);
        }

        internal static FrameworkElement CreateContent(FlowCanvas canvas, NodeModel node)
        {
            if (node?.GetType() != typeof(DivideNumberNodeModel))
            {
                throw new InvalidOperationException("DivideNumberView requires a DivideNumberNodeModel.");
            }

            return new DivideNumberView(canvas, (DivideNumberNodeModel)node);
        }
    }
}
