using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed partial class EqualView : UserControl
    {
        private EqualView(FlowCanvas canvas, EqualNodeModel node)
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
            if (node?.GetType() != typeof(EqualNodeModel))
            {
                throw new InvalidOperationException("EqualView requires an EqualNodeModel.");
            }

            return new EqualView(canvas, (EqualNodeModel)node);
        }
    }
}
