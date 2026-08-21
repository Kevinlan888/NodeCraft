using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed class SubtractNumberView : UserControl
    {
        private SubtractNumberView(FlowCanvas canvas, SubtractNumberNodeModel node)
        {
            var root = BuiltInXamlViewLoader.LoadAndAttach(this, nameof(SubtractNumberView));
            var inputAValue = BuiltInXamlViewLoader.RequireElement<TextBlock>(root, nameof(SubtractNumberView), "InputAValue");
            var inputBValue = BuiltInXamlViewLoader.RequireElement<TextBlock>(root, nameof(SubtractNumberView), "InputBValue");
            var swapInputsButton = BuiltInXamlViewLoader.RequireElement<Button>(root, nameof(SubtractNumberView), "SwapInputsButton");
            BuiltInInputViewSupport.BindBinary(canvas, node, inputAValue, inputBValue, swapInputsButton);
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
