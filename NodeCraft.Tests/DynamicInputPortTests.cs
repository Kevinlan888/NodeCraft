using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NodeCraft.Flow;
using NodeCraft.Flow.Nodes;

internal static partial class Program
{
    private static void RunDynamicInputPortTests()
    {
        Run("dynamic template materializes ordered same-type ports", () =>
        {
            var definition = CreateDynamicDefinition(initialCount: 2, maxCount: null);
            var node = new NodeModel { ExecutorType = definition.TypeKey };

            FlowDynamicInputResolver.MaterializeNodePorts(node, definition);
            var ports = FlowDynamicInputResolver.ResolveNodeInputPorts(node, definition);

            return ports.Count == 3
                && ports[0].Definition.Id == FlowPorts.FlowIn
                && ports[1].Definition.IsDynamic
                && ports[2].Definition.IsDynamic
                && ports[1].RuntimePort.PortId == "input_1"
                && ports[2].RuntimePort.PortId == "input_2"
                && ports[1].Definition.DisplayName == "Input 1"
                && ports[2].Definition.DisplayName == "Input 2"
                && ports[1].Definition.DataType == FlowDataType.String
                && ports[2].Definition.DataType == FlowDataType.String
                && ports[1].Slot == 1
                && ports[2].Slot == 2;
        });

        Run("nodes without a dynamic template keep only fixed ports", () =>
        {
            var definition = CreateStaticDefinition();
            var node = new NodeModel { ExecutorType = definition.TypeKey };

            FlowDynamicInputResolver.MaterializeNodePorts(node, definition);
            return node.InputParameters.Count == definition.InputPorts.Count
                && node.InputParameters.All(port => !port.IsDynamic)
                && FlowDynamicInputResolver.GetDynamicPortIds(node).Count == 0;
        });

        Run("materialization preserves dynamic order and never renames surviving ports", () =>
        {
            var definition = CreateDynamicDefinition(initialCount: 1, maxCount: null);
            var node = new NodeModel
            {
                ExecutorType = definition.TypeKey,
                InputParameters = new List<PortParameter>
                {
                    CreateDynamicPort("input_2"),
                    new PortParameter { PortId = FlowPorts.FlowIn },
                    CreateDynamicPort("input_1"),
                },
            };

            FlowDynamicInputResolver.MaterializeNodePorts(node, definition);
            var ports = FlowDynamicInputResolver.ResolveNodeInputPorts(node, definition);

            return ports.Select(port => port.RuntimePort.PortId)
                .SequenceEqual(new[] { FlowPorts.FlowIn, "input_2", "input_1" })
                && FlowDynamicInputResolver.GetDynamicPortIds(node)
                    .SequenceEqual(new[] { "input_2", "input_1" });
        });

        Run("dynamic template validation rejects invalid bounds and collisions", () =>
        {
            var negativeMinimum = CreateDynamicDefinition(initialCount: 0, maxCount: null);
            negativeMinimum.DynamicInputTemplate.MinCount = -1;

            var invalidInitial = CreateDynamicDefinition(initialCount: 0, maxCount: null);
            invalidInitial.DynamicInputTemplate.MinCount = 2;

            var invalidMaximum = CreateDynamicDefinition(initialCount: 3, maxCount: 2);

            var collidingPrefix = CreateDynamicDefinition(initialCount: 1, maxCount: null);
            collidingPrefix.DynamicInputTemplate.PortIdPrefix = FlowPorts.FlowIn;

            return ThrowsInvalidTemplate(negativeMinimum, "MinCount")
                && ThrowsInvalidTemplate(invalidInitial, "InitialCount")
                && ThrowsInvalidTemplate(invalidMaximum, "MaxCount")
                && ThrowsInvalidTemplate(collidingPrefix, "flowIn");
        });

        Run("dynamic graph saves v5 and adapter preserves ordered links", () =>
        {
            EnsureDynamicTestRegistration();
            var graph = new GraphModel
            {
                Nodes = new List<NodeModel>
                {
                    CreateStringSourceNode("source-a"),
                    CreateStringSourceNode("source-b"),
                    CreateDynamicNode("target", "input_1", "input_2"),
                },
                Links = new List<GraphLink>
                {
                    new GraphLink
                    {
                        Id = "link-a",
                        OriginNodeId = "source-a",
                        OriginSlot = 0,
                        TargetNodeId = "target",
                        TargetSlot = 1,
                    },
                    new GraphLink
                    {
                        Id = "link-b",
                        OriginNodeId = "source-b",
                        OriginSlot = 0,
                        TargetNodeId = "target",
                        TargetSlot = 2,
                    },
                },
            };
            var path = Path.Combine(Path.GetTempPath(), "dynamic-input-v5-" + Guid.NewGuid().ToString("N") + ".flow.xml");

            try
            {
                GraphModelXmlSerializer.Save(graph, path);
                var xml = File.ReadAllText(path);
                var loaded = GraphModelXmlSerializer.LoadWithReport(path);
                var target = loaded.Graph.Nodes.Single(node => node.Id == "target");
                var workflow = GraphModelWorkflowAdapter.Convert(loaded.Graph);
                var workflowTarget = workflow.Nodes.Single(node => node.Id == "target");

                return xml.Contains("FormatVersion=\"5\"", StringComparison.Ordinal)
                    && xml.Contains("IsDynamic=\"true\"", StringComparison.Ordinal)
                    && loaded.FormatVersion == 5
                    && FlowDynamicInputResolver.GetDynamicPortIds(target)
                        .SequenceEqual(new[] { "input_1", "input_2" })
                    && target.InputParameters[1].LinkId == "link-a"
                    && target.InputParameters[2].LinkId == "link-b"
                    && workflowTarget.DynamicInputPortIds.SequenceEqual(new[] { "input_1", "input_2" })
                    && ((LinkRef)workflowTarget.Inputs["input_1"]).SourceNodeId == "source-a"
                    && ((LinkRef)workflowTarget.Inputs["input_2"]).SourceNodeId == "source-b";
            }
            finally
            {
                File.Delete(path);
            }
        });

        Run("v4 graphs load missing dynamic markers as fixed ports", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "dynamic-input-v4-" + Guid.NewGuid().ToString("N") + ".flow.xml");
            try
            {
                File.WriteAllText(path, CreateIntegerPreviewGraphXml(targetLinkId: null, targetSlot: 1));
                var result = GraphModelXmlSerializer.LoadWithReport(path);
                return result.FormatVersion == 4
                    && result.Graph.Nodes.SelectMany(node => node.InputParameters)
                        .All(port => !port.IsDynamic);
            }
            finally
            {
                File.Delete(path);
            }
        });

        Run("serializer rejects malformed dynamic markers", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "dynamic-input-invalid-marker-" + Guid.NewGuid().ToString("N") + ".flow.xml");
            try
            {
                var xml = CreateIntegerPreviewGraphXml(targetLinkId: null, targetSlot: 1)
                    .Replace(
                        "PortId=\"input\" Direction=\"Left\"",
                        "PortId=\"input\" Direction=\"Left\" IsDynamic=\"maybe\"",
                        StringComparison.Ordinal);
                File.WriteAllText(path, xml);

                try
                {
                    GraphModelXmlSerializer.LoadWithReport(path);
                    return false;
                }
                catch (InvalidOperationException exception)
                {
                    return exception.Message.Contains("IsDynamic", StringComparison.Ordinal)
                        && exception.Message.Contains("Boolean", StringComparison.Ordinal);
                }
            }
            finally
            {
                File.Delete(path);
            }
        });

        Run("reconciliation rejects unlinked dynamic metadata on a fixed node", () =>
        {
            var node = new AppendTextNodeModel
            {
                Id = "invalid-dynamic-node",
            };
            node.InputParameters.Add(CreateDynamicPort("input_1"));
            var graph = new GraphModel
            {
                Nodes = new List<NodeModel> { node },
                Links = new List<GraphLink>(),
            };

            return Throws<InvalidOperationException>(() => GraphModelLinkReconciler.Reconcile(graph));
        });
    }

    private const string DynamicTestTypeKey = "test.dynamic-input-ports";

    private static void EnsureDynamicTestRegistration()
    {
        if (NodeExecutorFactory.Registry.Contains(DynamicTestTypeKey))
        {
            return;
        }

        var definition = CreateDynamicDefinition(initialCount: 1, maxCount: null, isRequired: true);
        definition.TypeKey = DynamicTestTypeKey;
        NodeExecutorFactory.Registry.RegisterNode(
            new FlowNodeRegistration(definition, () => new DynamicTestExecutor()),
            typeof(DynamicTestNodeModel),
            () => new DynamicTestNodeModel(),
            showInPalette: false);
    }

    private static NodeModel CreateStringSourceNode(string nodeId)
    {
        var node = new StringValueNodeModel
        {
            Id = nodeId,
            Name = nodeId,
        };
        FlowDynamicInputResolver.MaterializeNodePorts(
            node,
            NodeExecutorFactory.Registry.Resolve(StringValueExecutor.FlowNodeTypeKey).Definition);
        return node;
    }

    private static NodeModel CreateDynamicNode(string nodeId, params string[] dynamicPortIds)
    {
        var node = new DynamicTestNodeModel
        {
            Id = nodeId,
            Name = nodeId,
            InputParameters = new List<PortParameter>
            {
                new PortParameter { PortId = FlowPorts.FlowIn },
            },
        };
        node.InputParameters.AddRange(dynamicPortIds.Select(CreateDynamicPort));
        FlowDynamicInputResolver.MaterializeNodePorts(
            node,
            NodeExecutorFactory.Registry.Resolve(DynamicTestTypeKey).Definition);
        return node;
    }

    private sealed class DynamicTestNodeModel : NodeModel
    {
        public DynamicTestNodeModel()
        {
            ExecutorType = DynamicTestTypeKey;
        }
    }

    private sealed class DynamicTestExecutor : IFlowNodeExecutor
    {
        public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyDictionary<string, object>>(
                new Dictionary<string, object> { ["output"] = string.Empty });
        }
    }

    private static FlowNodeDefinition CreateDynamicDefinition(
        int initialCount,
        int? maxCount,
        bool isRequired = false,
        string portIdPrefix = "input")
    {
        return new FlowNodeDefinition
        {
            TypeKey = "test.dynamic-input-definition",
            DisplayName = "Dynamic Input Definition",
            InputPorts =
            {
                new FlowPortDefinition
                {
                    Id = FlowPorts.FlowIn,
                    DisplayName = "Flow In",
                    IOType = EIOType.Input,
                    DataType = FlowDataType.Control,
                    PreferredDirection = EPortDirection.Top,
                },
            },
            OutputPorts =
            {
                new FlowPortDefinition
                {
                    Id = "output",
                    DisplayName = "Output",
                    IOType = EIOType.Output,
                    DataType = FlowDataType.String,
                    PreferredDirection = EPortDirection.Right,
                },
            },
            DynamicInputTemplate = new FlowDynamicInputTemplate
            {
                PortIdPrefix = portIdPrefix,
                DisplayNamePrefix = "Input",
                DataType = FlowDataType.String,
                PreferredDirection = EPortDirection.Left,
                IsRequired = isRequired,
                Availability = FlowPortAvailability.Iteration,
                MinCount = 1,
                InitialCount = initialCount,
                MaxCount = maxCount,
            },
        };
    }

    private static FlowNodeDefinition CreateStaticDefinition()
    {
        return new FlowNodeDefinition
        {
            TypeKey = "test.static-input-definition",
            InputPorts =
            {
                new FlowPortDefinition
                {
                    Id = FlowPorts.FlowIn,
                    IOType = EIOType.Input,
                    DataType = FlowDataType.Control,
                },
            },
        };
    }

    private static PortParameter CreateDynamicPort(string portId)
    {
        return new PortParameter
        {
            PortId = portId,
            IsDynamic = true,
            Parameter = new Parameter { ParameterType = FlowDataType.String.Key },
            PortDirection = EPortDirection.Left,
        };
    }

    private static bool ThrowsInvalidTemplate(FlowNodeDefinition definition, string expectedMessage)
    {
        try
        {
            FlowDynamicInputResolver.ValidateTemplate(definition);
            return false;
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message.Contains(expectedMessage, StringComparison.Ordinal);
        }
    }

}
