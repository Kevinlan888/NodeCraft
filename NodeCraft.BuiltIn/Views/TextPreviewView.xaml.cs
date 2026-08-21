using System;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal sealed partial class TextPreviewView : UserControl
    {
        private const string PlaceholderText = "等待执行后显示文本结果";

        private TextPreviewView(TextPreviewNodeModel node)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            InitializeComponent();
            PreviewText.Text = string.IsNullOrEmpty(node.LastPreviewText)
                ? PlaceholderText
                : node.LastPreviewText;
        }

        internal static FrameworkElement CreateContent(FlowCanvas canvas, NodeModel node)
        {
            if (node is not TextPreviewNodeModel previewNode)
            {
                throw new InvalidOperationException(
                    "TextPreviewView requires a TextPreviewNodeModel.");
            }

            return new TextPreviewView(previewNode);
        }
    }
}
