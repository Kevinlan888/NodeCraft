using System;
using System.Linq;
using System.Windows.Controls;
using NodeCraft.Flow;
using NodeCraft.Flow.Nodes;

internal static partial class Program
{
    private static void RunFlowNodeRegistryPresentationTests()
    {
        Run("palette registrations provide category and item icon metadata", () =>
        {
            var registry = new FlowNodeRegistry();
            var first = CreatePresentationRegistration(
                "test.presentation.first",
                "First",
                typeof(FirstPresentationNodeModel));
            var second = CreatePresentationRegistration(
                "test.presentation.second",
                "Second",
                typeof(SecondPresentationNodeModel));
            second.PaletteCategoryIconKind = "FolderOutline";
            second.PaletteIconKind = "StarOutline";

            registry.RegisterPlugin("test.presentation", new[] { first, second });
            var category = registry.CreatePaletteCategories().Single();
            var firstItem = category.Items.Single(item => item.TypeKey == first.Definition.TypeKey);
            var secondItem = category.Items.Single(item => item.TypeKey == second.Definition.TypeKey);

            return category.IconKind == "FolderOutline"
                && firstItem.IconKind == "FolderOutline"
                && secondItem.IconKind == "StarOutline";
        });

        Run("node content factories receive their originating registry", () => RunOnSta(() =>
        {
            var registry = new FlowNodeRegistry();
            var sawRegistry = false;
            var registration = CreatePresentationRegistration(
                "test.presentation.content",
                "Content",
                typeof(ContentPresentationNodeModel));
            registration.ContentFactory = (canvas, node) =>
            {
                sawRegistry = ReferenceEquals(canvas.NodeRegistry, registry);
                return new Border();
            };
            registry.RegisterPlugin("test.presentation", new[] { registration });
            var canvas = new FlowCanvas();
            var node = registration.NodeFactory();
            var content = registry.BuildNodeContent(canvas, node);
            return sawRegistry && content is Border;
        }));
    }

    private static FlowNodeRegistration CreatePresentationRegistration(
        string typeKey,
        string displayName,
        Type nodeModelType)
    {
        return new FlowNodeRegistration(
            new FlowNodeDefinition
            {
                TypeKey = typeKey,
                DisplayName = displayName,
                Category = "Presentation",
            },
            () => new StringValueExecutor())
        {
            NodeModelType = nodeModelType,
            NodeFactory = () =>
            {
                var node = (NodeModel)Activator.CreateInstance(nodeModelType)!;
                node.ExecutorType = typeKey;
                return node;
            },
            PaletteDisplayName = displayName,
        };
    }

    private sealed class FirstPresentationNodeModel : NodeModel
    {
    }

    private sealed class SecondPresentationNodeModel : NodeModel
    {
    }

    private sealed class ContentPresentationNodeModel : NodeModel
    {
    }
}
