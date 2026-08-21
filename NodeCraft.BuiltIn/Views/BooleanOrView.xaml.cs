using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed partial class BooleanOrView : UserControl
    {
        private BooleanOrView(FlowCanvas canvas, BooleanOrNodeModel node)
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
            if (node?.GetType() != typeof(BooleanOrNodeModel))
            {
                throw new InvalidOperationException("BooleanOrView requires a BooleanOrNodeModel.");
            }

            return new BooleanOrView(canvas, (BooleanOrNodeModel)node);
        }
    }
}
