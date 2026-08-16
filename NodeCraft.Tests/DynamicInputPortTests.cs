using System;
using System.Collections.Generic;
using System.Linq;
using NodeCraft.Flow;

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
