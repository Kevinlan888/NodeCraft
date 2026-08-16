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

        Run("dynamic materialization creates initial ports with fixed runtime ports", () =>
        {
            var definition = CreateDynamicDefinition(initialCount: 2, maxCount: null);
            var node = new NodeModel
            {
                ExecutorType = definition.TypeKey,
                InputParameters = new List<PortParameter>
                {
                    new PortParameter { PortId = FlowPorts.FlowIn },
                },
            };

            FlowDynamicInputResolver.MaterializeNodePorts(node, definition);
            return FlowDynamicInputResolver.GetDynamicPortIds(node)
                .SequenceEqual(new[] { "input_1", "input_2" });
        });

        Run("dynamic materialization preserves an explicitly empty dynamic list", () =>
        {
            var definition = CreateDynamicDefinition(initialCount: 2, maxCount: null);
            definition.DynamicInputTemplate.MinCount = 0;
            var node = new NodeModel { ExecutorType = definition.TypeKey };

            FlowDynamicInputResolver.MaterializeNodePorts(node, definition);
            var removedFirst = FlowDynamicInputResolver.TryRemoveDynamicPort(
                node,
                definition,
                "input_1",
                out _,
                out _);
            var removedSecond = FlowDynamicInputResolver.TryRemoveDynamicPort(
                node,
                definition,
                "input_2",
                out _,
                out _);
            FlowDynamicInputResolver.MaterializeNodePorts(node, definition);

            return removedFirst
                && removedSecond
                && FlowDynamicInputResolver.GetDynamicPortIds(node).Count == 0;
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

        Run("executor resolves dynamic inputs in declared order", () =>
        {
            EnsureDynamicTestRegistration();
            DynamicTestExecutor.ObservedInputIds.Clear();
            var workflow = new WorkflowDocument();
            workflow.Nodes.Add(new WorkflowNode
            {
                Id = "dynamic",
                TypeKey = DynamicTestTypeKey,
                DynamicInputPortIds = { "input_1", "input_2" },
                Inputs =
                {
                    ["input_1"] = "first",
                    ["input_2"] = "second",
                },
            });

            var executor = new GraphExecutor(workflow);
            if (!executor.Validate().IsValid)
            {
                return false;
            }

            var context = executor.ExecuteAsync().GetAwaiter().GetResult();
            return context.TryGetPortValue("dynamic", 0, out var value)
                && string.Equals(value as string, "first|second", StringComparison.Ordinal)
                && DynamicTestExecutor.ObservedInputIds.SequenceEqual(new[] { "input_1", "input_2" });
        });

        Run("executor validates required dynamic inputs", () =>
        {
            EnsureDynamicTestRegistration();
            var workflow = new WorkflowDocument();
            workflow.Nodes.Add(new WorkflowNode
            {
                Id = "dynamic",
                TypeKey = DynamicTestTypeKey,
                DynamicInputPortIds = { "input_1", "input_2" },
                Inputs =
                {
                    ["input_1"] = "only one",
                },
            });

            var validation = new GraphExecutor(workflow).Validate();
            return validation.Errors.Count(error => error.Code == "MissingRequiredInput"
                    && error.PortId == "input_2") == 1;
        });

        Run("executor rejects dynamic keys missing from workflow metadata", () =>
        {
            EnsureDynamicTestRegistration();
            var workflow = new WorkflowDocument();
            workflow.Nodes.Add(new WorkflowNode
            {
                Id = "dynamic",
                TypeKey = DynamicTestTypeKey,
                DynamicInputPortIds = { "input_1" },
                Inputs =
                {
                    ["input_1"] = "known",
                    ["input_2"] = "not declared",
                },
            });

            var validation = new GraphExecutor(workflow).Validate();
            return validation.Errors.Any(error => error.Code == "UnknownPort"
                && error.NodeId == "dynamic"
                && error.PortId == "input_2");
        });

        Run("executor rejects dynamic metadata outside template bounds", () =>
        {
            EnsureBoundedDynamicTestRegistration();
            var belowMinimum = new WorkflowDocument();
            belowMinimum.Nodes.Add(new WorkflowNode
            {
                Id = "below-minimum",
                TypeKey = BoundedDynamicTestTypeKey,
            });

            var aboveMaximum = new WorkflowDocument();
            aboveMaximum.Nodes.Add(new WorkflowNode
            {
                Id = "above-maximum",
                TypeKey = BoundedDynamicTestTypeKey,
                DynamicInputPortIds = { "input_1", "input_2", "input_3" },
            });

            var belowValidation = new GraphExecutor(belowMinimum).Validate();
            var aboveValidation = new GraphExecutor(aboveMaximum).Validate();
            return belowValidation.Errors.Any(error => error.Code == "InvalidDynamicInputs")
                && aboveValidation.Errors.Any(error => error.Code == "InvalidDynamicInputs");
        });

        Run("executor validates dynamic source compatibility", () =>
        {
            EnsureDynamicTestRegistration();
            var workflow = new WorkflowDocument();
            workflow.Nodes.Add(new WorkflowNode
            {
                Id = "source",
                TypeKey = "node.integer-value",
                Inputs = { ["value"] = 7 },
            });
            workflow.Nodes.Add(new WorkflowNode
            {
                Id = "dynamic",
                TypeKey = DynamicTestTypeKey,
                DynamicInputPortIds = { "input_1" },
                Inputs =
                {
                    ["input_1"] = new LinkRef
                    {
                        SourceNodeId = "source",
                        SourceSlot = 0,
                    },
                },
            });

            var validation = new GraphExecutor(workflow).Validate();
            return validation.Errors.Any(error => error.Code == "IncompatiblePortTypes"
                    && error.NodeId == "dynamic"
                    && error.PortId == "input_1")
                && !validation.Errors.Any(error => error.Code == "UnknownPort"
                    && error.NodeId == "dynamic");
        });

        Run("socket resolver exposes effective dynamic input slots", () =>
        {
            EnsureDynamicTestRegistration();
            var node = CreateDynamicNode("socket-target", "input_1", "input_2");
            var definition = NodeExecutorFactory.Registry.Resolve(DynamicTestTypeKey).Definition;
            var sockets = FlowSocketResolver.Resolve(node, definition, isInput: true);

            return sockets.Count == 3
                && sockets[1].Slot == 1
                && sockets[1].Definition.IsDynamic
                && sockets[1].Definition.Id == "input_1"
                && sockets[2].Definition.Id == "input_2"
                && sockets[2].RuntimePort?.IsDynamic == true;
        });

        Run("canvas adds and removes dynamic inputs without a realized view", () =>
            RunOnSta(() =>
            {
            EnsureDynamicTestRegistration();
            var canvas = CreateHeadlessCanvas();
            var node = CreateDynamicNode("headless-target", "input_1", "input_2");
            canvas.GraphModel.Nodes.Add(node);
            var changedCount = 0;
            canvas.GraphChanged += (_, _) => changedCount++;

            var added = canvas.TryAddDynamicInput(node, out var addError);
            var removed = canvas.TryRemoveDynamicInput(node, "input_1", out var removeError);

            return added
                && removed
                && string.IsNullOrEmpty(addError)
                && string.IsNullOrEmpty(removeError)
                && FlowDynamicInputResolver.GetDynamicPortIds(node)
                    .SequenceEqual(new[] { "input_2", "input_3" })
                && changedCount == 2;
            }));

        Run("canvas rejects removal from a malformed graph before mutation", () =>
            RunOnSta(() =>
            {
            EnsureDynamicTestRegistration();
            var canvas = CreateHeadlessCanvas();
            var node = CreateDynamicNode("malformed-target", "input_1", "input_2");
            canvas.GraphModel.Nodes.Add(node);
            canvas.GraphModel.Links.Add(null);

            var removed = canvas.TryRemoveDynamicInput(node, "input_1", out var error);
            return !removed
                && error.Contains("null link", StringComparison.OrdinalIgnoreCase)
                && FlowDynamicInputResolver.GetDynamicPortIds(node)
                    .SequenceEqual(new[] { "input_1", "input_2" });
            }));

        Run("canvas enforces dynamic input bounds and fixed-port protection", () =>
            RunOnSta(() =>
            {
            EnsureBoundedDynamicTestRegistration();
            var canvas = CreateHeadlessCanvas();
            var bounded = CreateDynamicNodeForType(
                BoundedDynamicTestTypeKey,
                "bounded-target",
                "input_1",
                "input_2");
            var minNode = CreateDynamicNode("minimum-target", "input_1");
            canvas.GraphModel.Nodes.Add(bounded);
            canvas.GraphModel.Nodes.Add(minNode);

            var addedAtMaximum = canvas.TryAddDynamicInput(bounded, out var maximumError);
            var removedAtMinimum = canvas.TryRemoveDynamicInput(minNode, "input_1", out var minimumError);
            var removedFixed = canvas.TryRemoveDynamicInput(minNode, FlowPorts.FlowIn, out var fixedError);

            return !addedAtMaximum
                && maximumError.Contains("maximum", StringComparison.OrdinalIgnoreCase)
                && !removedAtMinimum
                && minimumError.Contains("at least", StringComparison.OrdinalIgnoreCase)
                && !removedFixed
                && fixedError.Contains("fixed", StringComparison.OrdinalIgnoreCase);
            }));

        Run("canvas removes a connected dynamic input and reindexes later links", () =>
            RunOnSta(() =>
            {
            EnsureDynamicTestRegistration();
            var canvas = CreateHeadlessCanvas();
            var target = CreateDynamicNode("reindex-target", "input_1", "input_2", "input_3");
            var sourceA = CreateStringSourceNode("reindex-source-a");
            var sourceB = CreateStringSourceNode("reindex-source-b");
            canvas.GraphModel.Nodes.Add(sourceA);
            canvas.GraphModel.Nodes.Add(sourceB);
            canvas.GraphModel.Nodes.Add(target);
            canvas.GraphModel.Links.Add(new GraphLink
            {
                Id = "reindex-a",
                OriginNodeId = sourceA.Id,
                OriginSlot = 0,
                TargetNodeId = target.Id,
                TargetSlot = 1,
            });
            canvas.GraphModel.Links.Add(new GraphLink
            {
                Id = "reindex-b",
                OriginNodeId = sourceB.Id,
                OriginSlot = 0,
                TargetNodeId = target.Id,
                TargetSlot = 3,
            });
            GraphModelLinkReconciler.Reconcile(canvas.GraphModel);

            var removed = canvas.TryRemoveDynamicInput(target, "input_1", out var error);
            var remaining = canvas.GraphModel.Links.SingleOrDefault(link => link.Id == "reindex-b");

            return removed
                && string.IsNullOrEmpty(error)
                && canvas.GraphModel.Links.All(link => link.Id != "reindex-a")
                && remaining?.TargetSlot == 2
                && FlowDynamicInputResolver.GetDynamicPortIds(target)
                    .SequenceEqual(new[] { "input_2", "input_3" })
                && target.InputParameters.Single(port => port.PortId == "input_2").LinkId == null
                && target.InputParameters.Single(port => port.PortId == "input_3").LinkId == "reindex-b";
            }));

        Run("canvas grows a too-small explicit node height for dynamic rows", () =>
            RunOnSta(() =>
            {
            EnsureDynamicTestRegistration();
            var canvas = CreateHeadlessCanvas();
            var node = CreateDynamicNode("height-target", "input_1", "input_2");
            node.Height = 40;
            canvas.GraphModel.Nodes.Add(node);

            var added = canvas.TryAddDynamicInput(node, out _);
            return added && node.Height > 40;
            }));

        Run("dynamic nodes render add/remove controls while static nodes do not", () =>
            RunOnSta(() => RunWithTemplatedFlowCanvas((canvas, _, worldCanvas) =>
            {
                EnsureDynamicTestRegistration();
                var dynamicNode = CreateDynamicNode("visual-dynamic", "input_1", "input_2");
                var staticNode = new AppendTextNodeModel { Id = "visual-static" };
                canvas.LoadGraph(new GraphModel
                {
                    Nodes = new List<NodeModel> { dynamicNode, staticNode },
                    Links = new List<GraphLink>(),
                });
                canvas.UpdateLayout();

                var dynamicView = worldCanvas.Children.OfType<NodeView>()
                    .Single(view => view.NodeModel.Id == dynamicNode.Id);
                var staticView = worldCanvas.Children.OfType<NodeView>()
                    .Single(view => view.NodeModel.Id == staticNode.Id);
                var dynamicButtons = FindVisualDescendants<System.Windows.Controls.Button>(dynamicView).ToList();
                var staticButtons = FindVisualDescendants<System.Windows.Controls.Button>(staticView).ToList();

                return dynamicButtons.Count(button =>
                        string.Equals(
                            System.Windows.Automation.AutomationProperties.GetName(button),
                            "Add input",
                            StringComparison.Ordinal)) == 1
                    && dynamicButtons.Count(button =>
                        string.Equals(
                            System.Windows.Automation.AutomationProperties.GetName(button),
                            "Remove input",
                            StringComparison.Ordinal)) == 2
                    && !staticButtons.Any(button =>
                        !string.IsNullOrWhiteSpace(
                            System.Windows.Automation.AutomationProperties.GetName(button)));
            })));

        Run("generic dynamic nodes render effective input bindings", () =>
            RunOnSta(() =>
            {
            EnsureDynamicTestRegistration();
            var canvas = CreateHeadlessCanvas();
            var source = CreateStringSourceNode("generic-source-a");
            var secondSource = CreateStringSourceNode("generic-source-b");
            var target = CreateDynamicNode("generic-target", "input_1", "input_2");
            canvas.GraphModel.Nodes.Add(source);
            canvas.GraphModel.Nodes.Add(secondSource);
            canvas.GraphModel.Nodes.Add(target);
            canvas.GraphModel.Links.Add(new GraphLink
            {
                Id = "generic-link-a",
                OriginNodeId = source.Id,
                OriginSlot = 0,
                TargetNodeId = target.Id,
                TargetSlot = 1,
            });
            canvas.GraphModel.Links.Add(new GraphLink
            {
                Id = "generic-link-b",
                OriginNodeId = secondSource.Id,
                OriginSlot = 0,
                TargetNodeId = target.Id,
                TargetSlot = 2,
            });
            GraphModelLinkReconciler.Reconcile(canvas.GraphModel);

            var content = (System.Windows.FrameworkElement)NodeExecutorFactory.Registry.BuildNodeContent(canvas, target);
            var labels = FindLogicalDescendants<System.Windows.Controls.TextBlock>(content)
                .Select(textBlock => textBlock.Text)
                .ToList();

            return labels.Contains("Input 1", StringComparer.Ordinal)
                && labels.Contains("Input 2", StringComparer.Ordinal)
                && labels.Any(text => text.Contains("generic-source-a", StringComparison.Ordinal))
                && labels.Any(text => text.Contains("generic-source-b", StringComparison.Ordinal));
            }));
    }

    private const string DynamicTestTypeKey = "test.dynamic-input-ports";
    private const string BoundedDynamicTestTypeKey = "test.dynamic-input-bounded";

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
        return CreateDynamicNodeForType(DynamicTestTypeKey, nodeId, dynamicPortIds);
    }

    private static NodeModel CreateDynamicNodeForType(
        string typeKey,
        string nodeId,
        params string[] dynamicPortIds)
    {
        var node = new NodeModel
        {
            Id = nodeId,
            Name = nodeId,
            ExecutorType = typeKey,
            InputParameters = new List<PortParameter>
            {
                new PortParameter { PortId = FlowPorts.FlowIn },
            },
        };
        node.InputParameters.AddRange(dynamicPortIds.Select(CreateDynamicPort));
        FlowDynamicInputResolver.MaterializeNodePorts(
            node,
            NodeExecutorFactory.Registry.Resolve(typeKey).Definition);
        return node;
    }

    private static FlowCanvas CreateHeadlessCanvas()
    {
        return new FlowCanvas
        {
            GraphModel = new GraphModel
            {
                Nodes = new List<NodeModel>(),
                Links = new List<GraphLink>(),
            },
        };
    }

    private static void EnsureBoundedDynamicTestRegistration()
    {
        if (NodeExecutorFactory.Registry.Contains(BoundedDynamicTestTypeKey))
        {
            return;
        }

        var definition = CreateDynamicDefinition(initialCount: 2, maxCount: 2, isRequired: true);
        definition.TypeKey = BoundedDynamicTestTypeKey;
        NodeExecutorFactory.Registry.Register(
            new FlowNodeRegistration(definition, () => new DynamicTestExecutor()));
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
        public static List<string> ObservedInputIds { get; } = new List<string>();

        public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            var values = new List<string>();
            foreach (var inputPort in definition.InputPorts.Where(port => port.IsDynamic))
            {
                ObservedInputIds.Add(inputPort.Id);
                values.Add(inputs.TryGetValue(inputPort.Id, out var value)
                    ? value as string ?? string.Empty
                    : string.Empty);
            }

            return Task.FromResult<IReadOnlyDictionary<string, object>>(
                new Dictionary<string, object> { ["output"] = string.Join("|", values) });
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

    private static IEnumerable<T> FindVisualDescendants<T>(System.Windows.DependencyObject root)
        where T : System.Windows.DependencyObject
    {
        if (root == null)
        {
            yield break;
        }

        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static IEnumerable<T> FindLogicalDescendants<T>(System.Windows.DependencyObject root)
    {
        if (root == null)
        {
            yield break;
        }

        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root))
        {
            if (child is T match)
            {
                yield return match;
            }

            if (child is not System.Windows.DependencyObject childObject)
            {
                continue;
            }

            foreach (var descendant in FindLogicalDescendants<T>(childObject))
            {
                yield return descendant;
            }
        }
    }

}
