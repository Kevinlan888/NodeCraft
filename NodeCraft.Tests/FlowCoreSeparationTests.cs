using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using NodeCraft.Flow;

internal static partial class Program
{
    private static void RunFlowCoreSeparationTests()
    {
        Run("flow core starts with an empty global registry", () =>
        {
            var registry = NodeExecutorFactory.Registry;
            return !registry.Contains("node.string-value")
                && !registry.Contains("nodecraft.builtin.string-value")
                && !registry.CreatePaletteCategories().Any();
        });

        Run("flow registry returns null content when no content factory is registered", () =>
            RunOnSta(() =>
            {
                var registry = new FlowNodeRegistry();
                var registration = CreateCoreSeparationRegistration(
                    "test.separation.no-content",
                    () => new CoreSeparationExecutor());
                registry.Register(registration);
                var canvas = new FlowCanvas();
                var node = new NodeModel { ExecutorType = registration.Definition.TypeKey };
                return registry.BuildNodeContent(canvas, node) == null;
            }));

        Run("flow registry returns null content for null or unknown nodes", () =>
            RunOnSta(() =>
            {
                var registry = new FlowNodeRegistry();
                var registration = CreateCoreSeparationRegistration(
                    "test.separation.null",
                    () => new CoreSeparationExecutor());
                registry.Register(registration);

                var canvas = new FlowCanvas();
                var knownNode = new NodeModel { ExecutorType = registration.Definition.TypeKey };
                var unknownNode = new NodeModel { ExecutorType = "test.separation.unknown" };

                return registry.BuildNodeContent(null, knownNode) == null
                    && registry.BuildNodeContent(canvas, null) == null
                    && registry.BuildNodeContent(canvas, unknownNode) == null;
            }));

        Run("flow registry invokes a supplied content factory for every request and returns distinct views", () =>
            RunOnSta(() =>
            {
                var registry = new FlowNodeRegistry();
                var calls = 0;
                var registration = CreateCoreSeparationRegistration(
                    "test.separation.factory",
                    () => new CoreSeparationExecutor());
                registration.ContentFactory = (_, _) =>
                {
                    calls++;
                    return new Border();
                };
                registry.Register(registration);

                var canvas = new FlowCanvas();
                var node = new NodeModel { ExecutorType = registration.Definition.TypeKey };
                var first = registry.BuildNodeContent(canvas, node);
                var second = registry.BuildNodeContent(canvas, node);
                return calls == 2
                    && first is Border
                    && second is Border
                    && !ReferenceEquals(first, second);
            }));

        Run("flow ports expose only the flow-in control port", () =>
        {
            var fields = typeof(FlowPorts)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(field => field.Name)
                .ToArray();
            return fields.Length == 1 && fields[0] == "FlowIn";
        });

        Run("flow core ships no concrete node sources", () =>
        {
            var repoRoot = Path.GetDirectoryName(FindRepositoryFile("NodeCraft.sln"));
            var flowRoot = Path.Combine(repoRoot, "NodeCraft.Flow");
            var nodesDirectory = Path.Combine(flowRoot, "Flow", "Nodes");

            return !File.Exists(Path.Combine(flowRoot, "Flow", "DefaultFlowNodeContentFactory.cs"))
                && !File.Exists(Path.Combine(nodesDirectory, "BuiltInNodeRegistration.cs"))
                && !File.Exists(Path.Combine(nodesDirectory, "BuiltInNodePorts.cs"))
                && (!Directory.Exists(nodesDirectory)
                    || !Directory.EnumerateFiles(nodesDirectory, "*.cs").Any());
        });
    }

    private static FlowNodeRegistration CreateCoreSeparationRegistration(
        string typeKey,
        Func<IFlowNodeExecutor> executorFactory)
    {
        return new FlowNodeRegistration(
            new FlowNodeDefinition
            {
                TypeKey = typeKey,
                DisplayName = typeKey,
                Category = "Separation",
            },
            executorFactory);
    }

    private sealed class CoreSeparationExecutor : IFlowNodeExecutor
    {
        public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyDictionary<string, object>>(
                new Dictionary<string, object>());
        }
    }
}