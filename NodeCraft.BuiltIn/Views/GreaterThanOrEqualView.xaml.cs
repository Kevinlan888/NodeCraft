using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed partial class GreaterThanOrEqualView : UserControl
    {
        private GreaterThanOrEqualView()
        {
            InitializeComponent();
        }

        internal static FrameworkElement CreateContent(FlowCanvas canvas, NodeModel node)
        {
            if (node is not GreaterThanOrEqualNodeModel)
            {
                throw new InvalidOperationException("GreaterThanOrEqualView requires a GreaterThanOrEqualNodeModel.");
            }

            return new GreaterThanOrEqualView();
        }
    }
}
