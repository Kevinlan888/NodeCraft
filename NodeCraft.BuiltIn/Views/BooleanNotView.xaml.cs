using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed class BooleanNotView : UserControl
    {
        private BooleanNotView(FlowCanvas canvas, BooleanNotNodeModel node)
        {
            var root = BuiltInXamlViewLoader.LoadAndAttach(this, nameof(BooleanNotView));
            BuiltInInputViewSupport.BindUnary(
                canvas,
                node,
                BuiltInXamlViewLoader.RequireElement<TextBlock>(
                    root,
                    nameof(BooleanNotView),
                    "InputValue"));
        }

        internal static FrameworkElement CreateContent(FlowCanvas canvas, NodeModel node)
        {
            if (node?.GetType() != typeof(BooleanNotNodeModel))
            {
                throw new InvalidOperationException("BooleanNotView requires a BooleanNotNodeModel.");
            }

            return new BooleanNotView(canvas, (BooleanNotNodeModel)node);
        }
    }
}
