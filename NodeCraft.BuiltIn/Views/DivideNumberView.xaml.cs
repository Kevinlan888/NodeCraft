using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed class DivideNumberView : UserControl
    {
        private DivideNumberView(FlowCanvas canvas, DivideNumberNodeModel node)
        {
            var root = BuiltInXamlViewLoader.LoadAndAttach(this, nameof(DivideNumberView));
            var inputAValue = BuiltInXamlViewLoader.RequireElement<TextBlock>(root, nameof(DivideNumberView), "InputAValue");
            var inputBValue = BuiltInXamlViewLoader.RequireElement<TextBlock>(root, nameof(DivideNumberView), "InputBValue");
            var swapInputsButton = BuiltInXamlViewLoader.RequireElement<Button>(root, nameof(DivideNumberView), "SwapInputsButton");
            BuiltInInputViewSupport.BindBinary(canvas, node, inputAValue, inputBValue, swapInputsButton);
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
