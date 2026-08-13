using System;
using Microsoft.Extensions.Logging;
using NodeCraft.Flow;
using NodeCraft.Flow.Nodes;
using NodeCraft.PluginSample.Nodes;
using NodeCraft.PluginSample.Views;

namespace NodeCraft.PluginSample.Plugin
{
    public sealed class SamplePlugin : IFlowPlugin
    {
        public PluginMetadata Metadata { get; } = new PluginMetadata
        {
            Id = "company.sample.nodes",
            DisplayName = "Sample Nodes",
            Version = new Version(1, 0, 0),
        };

        public void Register(IPluginContext context)
        {
            context.Nodes.Register(CreateValueRegistration(context));
            context.Nodes.Register(CreatePreviewRegistration());
            context.Logger.LogInformation("Registered sample nodes.");
        }

        private static FlowNodeRegistration CreateValueRegistration(IPluginContext context)
        {
            return new FlowNodeRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = SampleValueExecutor.FlowNodeTypeKey,
                    DisplayName = "Sample Value",
                    Category = "Value",
                    OutputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.Output,
                            DisplayName = "Value",
                            IOType = EIOType.Output,
                            DataType = FlowDataType.String,
                            PreferredDirection = EPortDirection.Right,
                        },
                    },
                },
                () => new SampleValueExecutor())
            {
                NodeModelType = typeof(SampleValueNodeModel),
                NodeFactory = () => new SampleValueNodeModel(),
                PaletteDisplayName = "Sample Value",
                PaletteDescription = "Formats a string through a plugin-private dependency.",
                ContentFactory = SampleValueEditor.CreateContent,
            };
        }

        private static FlowNodeRegistration CreatePreviewRegistration()
        {
            return new FlowNodeRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = SamplePreviewExecutor.FlowNodeTypeKey,
                    DisplayName = "Sample Preview",
                    Category = "Preview",
                    InputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.Input,
                            DisplayName = "Input",
                            IOType = EIOType.Input,
                            DataType = FlowDataType.Object,
                            PreferredDirection = EPortDirection.Left,
                            IsRequired = true,
                        },
                    },
                    OutputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.Output,
                            DisplayName = "Output",
                            IOType = EIOType.Output,
                            DataType = FlowDataType.String,
                            PreferredDirection = EPortDirection.Right,
                        },
                    },
                },
                () => new SamplePreviewExecutor())
            {
                NodeModelType = typeof(SamplePreviewNodeModel),
                NodeFactory = () => new SamplePreviewNodeModel(),
                PaletteDisplayName = "Sample Preview",
                PaletteDescription = "Shows the most recent formatted output from the sample plugin.",
                ExecutionResultHandler = (node, executionContext) =>
                {
                    if (node is SamplePreviewNodeModel previewNode)
                    {
                        if (executionContext != null
                            && executionContext.TryGetPortValue(previewNode.Id, 0, out var value))
                        {
                            previewNode.LastPreviewText = value as string ?? value?.ToString() ?? string.Empty;
                        }
                        else
                        {
                            previewNode.LastPreviewText = string.Empty;
                        }
                    }
                },
            };
        }
    }
}
