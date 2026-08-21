using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed partial class BooleanNotView : UserControl
    {
        private BooleanNotView(FlowCanvas canvas, BooleanNotNodeModel node)
        {
            InitializeComponent();
            BuiltInInputViewSupport.BindUnary(
                canvas,
                node,
                InputValue);
        }

        internal static FrameworkElement CreateContent(FlowCanvas canvas, NodeModel node)
        {
            if (node?.GetType() != typeof(BooleanNotNodeModel))
            {
                throw new InvalidOperationException("BooleanNotView requires a BooleanNotNodeModel.");
            }

            return new BooleanNotView(canvas, (BooleanNotNodeModel)node);
        }
    }
}
