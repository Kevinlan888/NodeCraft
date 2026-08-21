using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed class IfView : UserControl
    {
        private IfView()
        {
            var root = BuiltInXamlViewLoader.LoadAndAttach(this, nameof(IfView));
            BuiltInXamlViewLoader.RequireElement<TextBlock>(root, nameof(IfView), "IF");
            BuiltInXamlViewLoader.RequireElement<TextBlock>(root, nameof(IfView), "TrueLabel");
            BuiltInXamlViewLoader.RequireElement<TextBlock>(root, nameof(IfView), "FalseLabel");
        }

        internal static FrameworkElement CreateContent(FlowCanvas canvas, NodeModel node)
        {
            if (node?.GetType() != typeof(IfNodeModel))
            {
                throw new InvalidOperationException("IfView requires an IfNodeModel.");
            }

            return new IfView();
        }
    }
}
