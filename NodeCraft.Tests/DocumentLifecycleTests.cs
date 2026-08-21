using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using NodeCraft.Flow;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Pages;

internal static partial class Program
{
    private static void RunDocumentLifecycleTests()
    {
        Run("FlowPage keeps a loaded graph clean and tracks edits until save", () =>
            RunOnSta(() =>
            {
                var page = new FlowPage(NullLoggerFactory.Instance);
                var filePath = CreateDocumentLifecycleTestFile();

                try
                {
                    if (!page.TryLoadGraphFile(filePath) || page.HasUnsavedChanges)
                    {
                        return false;
                    }

                    GetNodeCanvas(page).NotifyGraphChanged(refreshNodeContents: false);
                    var dirtyIndicatorShown = GetCurrentFilePath(page).Text.Contains(
                        "未保存修改",
                        StringComparison.Ordinal);
                    if (!page.HasUnsavedChanges || !dirtyIndicatorShown)
                    {
                        return false;
                    }

                    page.SaveGraph();
                    return !page.HasUnsavedChanges
                        && !GetCurrentFilePath(page).Text.Contains(
                            "未保存修改",
                            StringComparison.Ordinal);
                }
                finally
                {
                    DeleteTestFile(filePath);
                }
            }));

        Run("FlowCanvas reports a committed node drag as a graph change", () =>
            RunOnSta(() =>
                RunWithTemplatedFlowCanvas((canvas, _, worldCanvas) =>
                {
                    var node = new NodeModel
                    {
                        X = 16,
                        Y = 16,
                    };
                    canvas.AddNode(node);
                    canvas.UpdateLayout();

                    var nodeView = worldCanvas.Children
                        .OfType<NodeView>()
                        .Single(item => item.NodeModel == node);
                    var graphChanges = 0;
                    canvas.GraphChanged += (_, __) => graphChanges++;

                    var dragOverlays = new System.Collections.Generic.Dictionary<
                        SimpleCircleAdorner,
                        System.Windows.Point>
                    {
                        [new SimpleCircleAdorner(nodeView)] = new System.Windows.Point(16, 16),
                    };
                    typeof(FlowCanvas)
                        .GetField("_dragOverlayElements", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?.SetValue(canvas, dragOverlays);
                    typeof(FlowCanvas)
                        .GetField("_dragWorldOffset", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?.SetValue(canvas, new System.Windows.Vector(16, 0));

                    var dragFinished = typeof(FlowCanvas).GetMethod(
                        "DragFinished",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    dragFinished?.Invoke(canvas, new object[] { false });

                    return node.X == 32
                        && node.Y == 16
                        && graphChanges == 1;
                })));

        Run("FlowPage closes the current graph and clears its document state", () =>
            RunOnSta(() =>
            {
                var page = new FlowPage(NullLoggerFactory.Instance);
                var filePath = CreateDocumentLifecycleTestFile();

                try
                {
                    if (!page.TryLoadGraphFile(filePath))
                    {
                        return false;
                    }

                    GetNodeCanvas(page).NotifyGraphChanged(refreshNodeContents: false);
                    if (!page.HasUnsavedChanges)
                    {
                        return false;
                    }

                    page.CloseGraph();
                    return !page.HasUnsavedChanges
                        && GetCurrentFilePath(page).Text == "当前文件: 未保存";
                }
                finally
                {
                    DeleteTestFile(filePath);
                }
            }));

        Run("MainWindow file menu exposes close solution", () =>
        {
            var path = FindRepositoryFile("NodeCraft", "MainWindow.xaml");
            var document = XDocument.Load(path);
            XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

            var closeMenu = document
                .Descendants(XName.Get("MenuItem", "http://schemas.microsoft.com/winfx/2006/xaml/presentation"))
                .SingleOrDefault(element => (string?)element.Attribute(xaml + "Name") == "MenuCloseGraph");

            return closeMenu != null
                && (string?)closeMenu.Attribute("Header") == "关闭方案"
                && (string?)closeMenu.Attribute("Click") == "MenuCloseGraph_Click";
        });
    }

    private static string CreateDocumentLifecycleTestFile()
    {
        var filePath = Path.Combine(
            Path.GetTempPath(),
            $"nodecraft-document-lifecycle-{Guid.NewGuid():N}.flow.xml");

        GraphModelXmlSerializer.Save(
            new GraphModel
            {
                Nodes = new System.Collections.Generic.List<NodeModel>
                {
                    new StringValueNodeModel
                    {
                        Id = "document-lifecycle-node",
                        Name = "Document lifecycle",
                        ValueText = "test",
                    },
                },
                Links = new System.Collections.Generic.List<GraphLink>(),
            },
            filePath);

        return filePath;
    }

    private static void DeleteTestFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
        }
    }
}
