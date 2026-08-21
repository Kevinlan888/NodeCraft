using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed class GreaterThanView : UserControl
    {
        private GreaterThanView(FlowCanvas canvas, GreaterThanNodeModel node)
        {
            var root = BuiltInXamlViewLoader.LoadAndAttach(this, nameof(GreaterThanView));
            BuiltInInputViewSupport.BindBinary(
                canvas,
                node,
                BuiltInXamlViewLoader.RequireElement<TextBlock>(root, nameof(GreaterThanView), "InputAValue"),
                BuiltInXamlViewLoader.RequireElement<TextBlock>(root, nameof(GreaterThanView), "InputBValue"),
                BuiltInXamlViewLoader.RequireElement<Button>(root, nameof(GreaterThanView), "SwapInputsButton"));
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
