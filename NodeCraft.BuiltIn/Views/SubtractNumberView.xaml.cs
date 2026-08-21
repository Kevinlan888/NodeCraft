using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed partial class SubtractNumberView : UserControl
    {
        private SubtractNumberView(FlowCanvas canvas, SubtractNumberNodeModel node)
        {
            InitializeComponent();
            BuiltInInputViewSupport.BindBinary(canvas, node, InputAValue, InputBValue, SwapInputsButton);
        }

        internal static FrameworkElement CreateContent(FlowCanvas canvas, NodeModel node)
        {
            if (node?.GetType() != typeof(SubtractNumberNodeModel))
            {
                throw new InvalidOperationException("SubtractNumberView requires a SubtractNumberNodeModel.");
            }

            return new SubtractNumberView(canvas, (SubtractNumberNodeModel)node);
        }
    }
}
