using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed partial class LessThanOrEqualView : UserControl
    {
        private LessThanOrEqualView()
        {
            InitializeComponent();
        }

        internal static FrameworkElement CreateContent(FlowCanvas canvas, NodeModel node)
        {
            if (node is not LessThanOrEqualNodeModel)
            {
                throw new InvalidOperationException("LessThanOrEqualView requires a LessThanOrEqualNodeModel.");
            }

            return new LessThanOrEqualView();
        }
    }
}
