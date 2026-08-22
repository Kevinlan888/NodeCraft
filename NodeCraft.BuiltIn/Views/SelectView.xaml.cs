using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed partial class SelectView : UserControl
    {
        private SelectView()
        {
            InitializeComponent();
        }

        internal static FrameworkElement CreateContent(FlowCanvas canvas, NodeModel node)
        {
            if (node is not SelectNodeModel)
            {
                throw new InvalidOperationException("SelectView requires a SelectNodeModel.");
            }

            return new SelectView();
        }
    }
}
