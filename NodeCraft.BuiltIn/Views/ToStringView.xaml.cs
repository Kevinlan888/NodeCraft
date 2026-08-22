using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed partial class ToStringView : UserControl
    {
        private ToStringView()
        {
            InitializeComponent();
        }

        internal static FrameworkElement CreateContent(FlowCanvas canvas, NodeModel node)
        {
            if (node is not ToStringNodeModel)
            {
                throw new InvalidOperationException("ToStringView requires a ToStringNodeModel.");
            }

            return new ToStringView();
        }
    }
}
