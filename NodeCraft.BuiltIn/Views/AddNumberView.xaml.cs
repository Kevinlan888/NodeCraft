using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed partial class AddNumberView : UserControl
    {
        private AddNumberView(FlowCanvas canvas, AddNumberNodeModel node)
        {
            InitializeComponent();
            BuiltInInputViewSupport.BindBinary(canvas, node, InputAValue, InputBValue, SwapInputsButton);
        }

        internal static FrameworkElement CreateContent(FlowCanvas canvas, NodeModel node)
        {
            if (node?.GetType() != typeof(AddNumberNodeModel))
            {
                throw new InvalidOperationException("AddNumberView requires an AddNumberNodeModel.");
            }

            return new AddNumberView(canvas, (AddNumberNodeModel)node);
        }
    }
}
