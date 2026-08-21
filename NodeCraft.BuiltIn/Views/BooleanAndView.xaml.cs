using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed class BooleanAndView : UserControl
    {
        private BooleanAndView(FlowCanvas canvas, BooleanAndNodeModel node)
        {
            var root = BuiltInXamlViewLoader.LoadAndAttach(this, nameof(BooleanAndView));
            BuiltInInputViewSupport.BindBinary(
                canvas,
                node,
                BuiltInXamlViewLoader.RequireElement<TextBlock>(root, nameof(BooleanAndView), "InputAValue"),
                BuiltInXamlViewLoader.RequireElement<TextBlock>(root, nameof(BooleanAndView), "InputBValue"),
                BuiltInXamlViewLoader.RequireElement<Button>(root, nameof(BooleanAndView), "SwapInputsButton"));
        }

        internal static FrameworkElement CreateContent(FlowCanvas canvas, NodeModel node)
        {
            if (node?.GetType() != typeof(BooleanAndNodeModel))
            {
                throw new InvalidOperationException("BooleanAndView requires a BooleanAndNodeModel.");
            }

            return new BooleanAndView(canvas, (BooleanAndNodeModel)node);
        }
    }
}
