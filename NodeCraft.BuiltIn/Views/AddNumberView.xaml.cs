using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed class AddNumberView : UserControl
    {
        private AddNumberView(FlowCanvas canvas, AddNumberNodeModel node)
        {
            var root = BuiltInXamlViewLoader.LoadAndAttach(this, nameof(AddNumberView));
            var inputAValue = BuiltInXamlViewLoader.RequireElement<TextBlock>(root, nameof(AddNumberView), "InputAValue");
            var inputBValue = BuiltInXamlViewLoader.RequireElement<TextBlock>(root, nameof(AddNumberView), "InputBValue");
            var swapInputsButton = BuiltInXamlViewLoader.RequireElement<Button>(root, nameof(AddNumberView), "SwapInputsButton");
            BuiltInInputViewSupport.BindBinary(canvas, node, inputAValue, inputBValue, swapInputsButton);
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
