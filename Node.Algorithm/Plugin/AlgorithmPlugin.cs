using System;
using Microsoft.Extensions.Logging;
using Node.Algorithm.Interop;
using Node.Algorithm.Nodes;
using NodeCraft.Flow;

namespace Node.Algorithm.Plugin
{
    public sealed class AlgorithmPlugin : IFlowPlugin
    {
        private readonly IWaybillInferenceSessionFactory _sessionFactory;
        private readonly string _assemblyPath;

        public AlgorithmPlugin()
            : this(
                new WaybillNativeSessionFactory(),
                typeof(AlgorithmPlugin).Assembly.Location)
        {
        }

        internal AlgorithmPlugin(
            IWaybillInferenceSessionFactory sessionFactory,
            string assemblyPath)
        {
            _sessionFactory = sessionFactory
                ?? throw new ArgumentNullException(nameof(sessionFactory));
            if (string.IsNullOrWhiteSpace(assemblyPath))
            {
                throw new ArgumentException(
                    "A plugin assembly path is required.",
                    nameof(assemblyPath));
            }

            _assemblyPath = assemblyPath;
        }

        public PluginMetadata Metadata { get; } = new PluginMetadata
        {
            Id = "nodecraft.algorithm",
            DisplayName = "Algorithm",
            Version = new Version(1, 0, 0),
        };

        public void Register(IPluginContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            context.Nodes.Register(new FlowNodeRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = WaybillRecognizerNodeModel.FlowNodeTypeKey,
                    DisplayName = "Waybill Recognizer",
                    Category = "Algorithm",
                    InputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = "image",
                            DisplayName = "Image",
                            IOType = EIOType.Input,
                            DataType = FlowDataType.Image,
                            IsRequired = true,
                            Availability = FlowPortAvailability.Iteration,
                            PreferredDirection = EPortDirection.Left,
                        },
                    },
                    OutputPorts =
                    {
                        CreateOutput("count", "Count", FlowDataType.Number),
                        CreateOutput("detections", "Detections", FlowDataType.Object),
                        CreateOutput("annotatedImage", "Annotated Image", FlowDataType.Image),
                    },
                },
                () => new WaybillRecognizerExecutor(
                    _sessionFactory,
                    _assemblyPath,
                    context.Logger))
            {
                NodeModelType = typeof(WaybillRecognizerNodeModel),
                NodeFactory = () => new WaybillRecognizerNodeModel(),
                PaletteDisplayName = "Waybill Recognizer",
                PaletteDescription =
                    "Recognizes waybills from FlowImage values and returns split detections with an annotated FlowImage.",
            });

            context.Logger.LogInformation("Registered the Waybill Recognizer algorithm node.");
        }

        internal static AlgorithmPlugin CreateForTesting(
            IWaybillInferenceSessionFactory sessionFactory,
            string assemblyPath)
        {
            return new AlgorithmPlugin(sessionFactory, assemblyPath);
        }

        private static FlowPortDefinition CreateOutput(
            string id,
            string displayName,
            FlowDataType dataType)
        {
            return new FlowPortDefinition
            {
                Id = id,
                DisplayName = displayName,
                IOType = EIOType.Output,
                DataType = dataType,
                Availability = FlowPortAvailability.Iteration,
                PreferredDirection = EPortDirection.Right,
            };
        }
    }
}
