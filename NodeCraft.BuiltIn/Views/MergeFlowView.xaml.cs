using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed partial class MergeFlowView : UserControl
    {
        private MergeFlowView()
        {
            InitializeComponent();
        }

        internal static FrameworkElement CreateContent(FlowCanvas canvas, NodeModel node)
        {
            if (node is not MergeFlowNodeModel)
            {
                throw new InvalidOperationException("MergeFlowView requires a MergeFlowNodeModel.");
            }

            return new MergeFlowView();
        }
    }
}
