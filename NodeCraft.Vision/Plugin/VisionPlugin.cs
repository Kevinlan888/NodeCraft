using System;
using Microsoft.Extensions.Logging;
using NodeCraft.Flow;
using NodeCraft.Vision.Camera;
using NodeCraft.Vision.Nodes;
using NodeCraft.Vision.Preview;
using NodeCraft.Vision.Runtime;
using NodeCraft.Vision.Views;

namespace NodeCraft.Vision.Plugin
{
    public sealed class VisionPlugin : IFlowPlugin
    {
        private readonly IVisionCameraDeviceFactory _deviceFactory;
        private readonly ICameraRuntimeScopeFactory _runtimeScopeFactory;
        private readonly IMonotonicClock _clock;
        private readonly VisionCameraCaptureOptions _captureOptions;

        public VisionPlugin()
            : this(
                new VisionCameraDeviceFactory(),
                new ProductionVisionCameraRuntimeScopeFactory(typeof(VisionPlugin).Assembly.Location),
                new SystemMonotonicClock(),
                new VisionCameraCaptureOptions())
        {
        }

        internal VisionPlugin(
            IVisionCameraDeviceFactory deviceFactory,
            ICameraRuntimeScopeFactory runtimeScopeFactory,
            IMonotonicClock clock,
            VisionCameraCaptureOptions captureOptions)
        {
            _deviceFactory = deviceFactory ?? throw new ArgumentNullException(nameof(deviceFactory));
            _runtimeScopeFactory = runtimeScopeFactory ?? throw new ArgumentNullException(nameof(runtimeScopeFactory));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _captureOptions = captureOptions ?? throw new ArgumentNullException(nameof(captureOptions));
        }

        public PluginMetadata Metadata { get; } = new PluginMetadata
        {
            Id = "nodecraft.vision",
            DisplayName = "Vision",
            Version = new Version(1, 0, 0),
        };

        public void Register(IPluginContext context)
        {
            context.Nodes.Register(CreateCameraRegistration(context.Logger));
            context.Nodes.Register(StereoCameraRegistration.Create(
                typeof(VisionPlugin).Assembly.Location,
                context.Logger));
            context.Nodes.Register(CreatePreviewRegistration());
            context.Logger.LogInformation("Registered Vision 2D and technical MVSDK 3D visual nodes.");
        }

        internal static VisionPlugin CreateForTesting(
            IVisionCameraDeviceFactory deviceFactory,
            ICameraRuntimeScopeFactory runtimeScopeFactory,
            IMonotonicClock clock,
            VisionCameraCaptureOptions captureOptions)
        {
            return new VisionPlugin(deviceFactory, runtimeScopeFactory, clock, captureOptions);
        }

        private FlowNodeRegistration CreateCameraRegistration(ILogger logger)
        {
            return new FlowNodeRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = VisionCameraNodeModel.FlowNodeTypeKey,
                    DisplayName = "Vision Camera",
                    Category = "Vision",
                    OutputPorts =
                    {
                        CreateOutputPort("image", "Image", FlowDataType.Image),
                    },
                },
                () => new VisionCameraExecutor(
                    _deviceFactory,
                    _runtimeScopeFactory,
                    _clock,
                    _captureOptions,
                    logger))
            {
                NodeModelType = typeof(VisionCameraNodeModel),
                NodeFactory = () => new VisionCameraNodeModel(),
                PaletteDisplayName = "Vision Camera",
                PaletteDescription = "Streams complete images from an IMV camera.",
                ContentFactory = VisionCameraEditor.CreateContent,
            };
        }

        private static FlowNodeRegistration CreatePreviewRegistration()
        {
            return new FlowNodeRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = FlowImagePreviewNodeModel.FlowNodeTypeKey,
                    DisplayName = "Image Preview (FlowImage)",
                    Category = "Vision",
                    InputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = "image",
                            DisplayName = "Image",
                            IOType = EIOType.Input,
                            DataType = FlowDataType.Image,
                            IsRequired = true,
                            PreferredDirection = EPortDirection.Left,
                        },
                    },
                    OutputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = "image",
                            DisplayName = "Image",
                            IOType = EIOType.Output,
                            DataType = FlowDataType.Image,
                            PreferredDirection = EPortDirection.Right,
                        },
                    },
                },
                () => new FlowImagePreviewExecutor())
            {
                NodeModelType = typeof(FlowImagePreviewNodeModel),
                NodeFactory = () => new FlowImagePreviewNodeModel(),
                PaletteDisplayName = "Image Preview (FlowImage)",
                PaletteDescription = "Displays a FlowImage without copying or changing the value.",
                ContentFactory = FlowImagePreviewView.CreateContent,
                RefreshContentAfterExecution = false,
                ExecutionResultHandler = (node, executionContext) =>
                {
                    if (!(node is FlowImagePreviewNodeModel previewNode))
                    {
                        return;
                    }

                    if (executionContext != null
                        && executionContext.TryGetPortValue(previewNode.Id, 0, out var value)
                        && value is FlowImage image)
                    {
                        previewNode.SetCurrentImage(image);
                        return;
                    }

                    previewNode.SetCurrentImage(null);
                },
            };
        }

        private static FlowPortDefinition CreateOutputPort(
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
                PreferredDirection = EPortDirection.Right,
            };
        }
    }
}
