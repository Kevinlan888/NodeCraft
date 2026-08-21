using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed partial class LessThanView : UserControl
    {
        private LessThanView(FlowCanvas canvas, LessThanNodeModel node)
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
            if (node?.GetType() != typeof(LessThanNodeModel))
            {
                throw new InvalidOperationException("LessThanView requires a LessThanNodeModel.");
            }

            return new LessThanView(canvas, (LessThanNodeModel)node);
        }
    }
}
