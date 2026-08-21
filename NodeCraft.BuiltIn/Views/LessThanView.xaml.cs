using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed class LessThanView : UserControl
    {
        private LessThanView(FlowCanvas canvas, LessThanNodeModel node)
        {
            var root = BuiltInXamlViewLoader.LoadAndAttach(this, nameof(LessThanView));
            BuiltInInputViewSupport.BindBinary(
                canvas,
                node,
                BuiltInXamlViewLoader.RequireElement<TextBlock>(root, nameof(LessThanView), "InputAValue"),
                BuiltInXamlViewLoader.RequireElement<TextBlock>(root, nameof(LessThanView), "InputBValue"),
                BuiltInXamlViewLoader.RequireElement<Button>(root, nameof(LessThanView), "SwapInputsButton"));
        }

        internal static FrameworkElement CreateContent(FlowCanvas canvas, NodeModel node)
        {
            if (node?.GetType() != typeof(LessThanNodeModel))
            {
                throw new InvalidOperationException("LessThanView requires a LessThanNodeModel.");
            }

            return new LessThanView(canvas, (LessThanNodeModel)node);
        }
    }
}
