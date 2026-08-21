using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed class MultiplyNumberView : UserControl
    {
        private MultiplyNumberView(FlowCanvas canvas, MultiplyNumberNodeModel node)
        {
            var root = BuiltInXamlViewLoader.LoadAndAttach(this, nameof(MultiplyNumberView));
            var inputAValue = BuiltInXamlViewLoader.RequireElement<TextBlock>(root, nameof(MultiplyNumberView), "InputAValue");
            var inputBValue = BuiltInXamlViewLoader.RequireElement<TextBlock>(root, nameof(MultiplyNumberView), "InputBValue");
            var swapInputsButton = BuiltInXamlViewLoader.RequireElement<Button>(root, nameof(MultiplyNumberView), "SwapInputsButton");
            BuiltInInputViewSupport.BindBinary(canvas, node, inputAValue, inputBValue, swapInputsButton);
        }

        internal static FrameworkElement CreateContent(FlowCanvas canvas, NodeModel node)
        {
            if (node?.GetType() != typeof(MultiplyNumberNodeModel))
            {
                throw new InvalidOperationException("MultiplyNumberView requires a MultiplyNumberNodeModel.");
            }

            return new MultiplyNumberView(canvas, (MultiplyNumberNodeModel)node);
        }
    }
}
