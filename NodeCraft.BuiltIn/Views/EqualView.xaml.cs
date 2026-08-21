using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed class EqualView : UserControl
    {
        private EqualView(FlowCanvas canvas, EqualNodeModel node)
        {
            var root = BuiltInXamlViewLoader.LoadAndAttach(this, nameof(EqualView));
            BuiltInInputViewSupport.BindBinary(
                canvas,
                node,
                BuiltInXamlViewLoader.RequireElement<TextBlock>(root, nameof(EqualView), "InputAValue"),
                BuiltInXamlViewLoader.RequireElement<TextBlock>(root, nameof(EqualView), "InputBValue"),
                BuiltInXamlViewLoader.RequireElement<Button>(root, nameof(EqualView), "SwapInputsButton"));
        }

        internal static FrameworkElement CreateContent(FlowCanvas canvas, NodeModel node)
        {
            if (node?.GetType() != typeof(EqualNodeModel))
            {
                throw new InvalidOperationException("EqualView requires an EqualNodeModel.");
            }

            return new EqualView(canvas, (EqualNodeModel)node);
        }
    }
}
