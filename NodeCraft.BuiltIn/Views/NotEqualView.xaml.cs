using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed partial class NotEqualView : UserControl
    {
        private NotEqualView()
        {
            InitializeComponent();
        }

        internal static FrameworkElement CreateContent(FlowCanvas canvas, NodeModel node)
        {
            if (node is not NotEqualNodeModel)
            {
                throw new InvalidOperationException("NotEqualView requires a NotEqualNodeModel.");
            }

            return new NotEqualView();
        }
    }
}
