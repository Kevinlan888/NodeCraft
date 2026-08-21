using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed class BooleanOrView : UserControl
    {
        private BooleanOrView(FlowCanvas canvas, BooleanOrNodeModel node)
        {
            var root = BuiltInXamlViewLoader.LoadAndAttach(this, nameof(BooleanOrView));
            BuiltInInputViewSupport.BindBinary(
                canvas,
                node,
                BuiltInXamlViewLoader.RequireElement<TextBlock>(root, nameof(BooleanOrView), "InputAValue"),
                BuiltInXamlViewLoader.RequireElement<TextBlock>(root, nameof(BooleanOrView), "InputBValue"),
                BuiltInXamlViewLoader.RequireElement<Button>(root, nameof(BooleanOrView), "SwapInputsButton"));
        }

        internal static FrameworkElement CreateContent(FlowCanvas canvas, NodeModel node)
        {
            if (node?.GetType() != typeof(BooleanOrNodeModel))
            {
                throw new InvalidOperationException("BooleanOrView requires a BooleanOrNodeModel.");
            }

            return new BooleanOrView(canvas, (BooleanOrNodeModel)node);
        }
    }
}
