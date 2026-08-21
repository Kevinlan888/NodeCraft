using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed partial class GreaterThanView : UserControl
    {
        private GreaterThanView(FlowCanvas canvas, GreaterThanNodeModel node)
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
            if (node?.GetType() != typeof(GreaterThanNodeModel))
            {
                throw new InvalidOperationException("GreaterThanView requires a GreaterThanNodeModel.");
            }

            return new GreaterThanView(canvas, (GreaterThanNodeModel)node);
        }
    }
}
