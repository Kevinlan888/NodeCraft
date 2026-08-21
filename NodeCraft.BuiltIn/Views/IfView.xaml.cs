using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed partial class IfView : UserControl
    {
        private IfView()
        {
            InitializeComponent();
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
