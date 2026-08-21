using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed partial class BooleanAndView : UserControl
    {
        private BooleanAndView(FlowCanvas canvas, BooleanAndNodeModel node)
        {
            InitializeComponent();
            BuiltInInputViewSupport.BindBinary(
                canvas,
                node,
                InputAValue,
                InputBValue,
                SwapInputsButton);
        }

        internal static FrameworkElement CreateContent(FlowCanvas canvas, NodeModel node)
        {
            if (node?.GetType() != typeof(BooleanAndNodeModel))
            {
                throw new InvalidOperationException("BooleanAndView requires a BooleanAndNodeModel.");
            }

            return new BooleanAndView(canvas, (BooleanAndNodeModel)node);
        }
    }
}
