using System;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.Logging;
using NodeCraft.Flow;
using NodeCraft.Vision.StereoCamera.Camera;
using NodeCraft.Vision.StereoCamera.Nodes;
using NodeCraft.Vision.StereoCamera.Runtime;
using NodeCraft.Vision.StereoCamera.Views;

namespace NodeCraft.Vision.StereoCamera.Plugin
{
    public sealed class StereoCameraPlugin : IFlowPlugin
    {
        private readonly IStereoCameraDeviceFactory _deviceFactory;
        private readonly ICameraRuntimeScopeFactory _runtimeScopeFactory;
        private readonly IMonotonicClock _clock;
        private readonly StereoCameraCaptureOptions _captureOptions;

        public StereoCameraPlugin()
            : this(
                new VendorStereoCameraDeviceFactory(),
                new ProductionCameraRuntimeScopeFactory(typeof(StereoCameraPlugin).Assembly.Location),
                new SystemMonotonicClock(),
                new StereoCameraCaptureOptions())
        {
        }

        internal StereoCameraPlugin(
            IStereoCameraDeviceFactory deviceFactory,
            ICameraRuntimeScopeFactory runtimeScopeFactory,
            IMonotonicClock clock,
            StereoCameraCaptureOptions captureOptions)
        {
            _deviceFactory = deviceFactory ?? throw new ArgumentNullException(nameof(deviceFactory));
            _runtimeScopeFactory = runtimeScopeFactory ?? throw new ArgumentNullException(nameof(runtimeScopeFactory));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _captureOptions = captureOptions ?? throw new ArgumentNullException(nameof(captureOptions));
        }

        public PluginMetadata Metadata { get; } = new PluginMetadata
        {
            Id = "nodecraft.vision.stereo-camera",
            DisplayName = "Stereo Camera",
            Version = new Version(1, 0, 0),
        };

        public void Register(IPluginContext context)
        {
            context.Nodes.Register(CreateCameraRegistration(context.Logger));
            context.Nodes.Register(CreatePreviewRegistration());
            context.Logger.LogInformation("Registered StereoCamera visual nodes.");
        }

        internal static StereoCameraPlugin CreateForTesting(
            IStereoCameraDeviceFactory deviceFactory,
            ICameraRuntimeScopeFactory runtimeScopeFactory,
            IMonotonicClock clock,
            StereoCameraCaptureOptions captureOptions)
        {
            return new StereoCameraPlugin(deviceFactory, runtimeScopeFactory, clock, captureOptions);
        }

        private FlowNodeRegistration CreateCameraRegistration(ILogger logger)
        {
            return new FlowNodeRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = StereoCameraNodeModel.FlowNodeTypeKey,
                    DisplayName = "Stereo Camera",
                    Category = "Vision",
                    InputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = "ipAddress",
                            DisplayName = "IPv4 address",
                            IOType = EIOType.Input,
                            DataType = FlowDataType.String,
                            IsRequired = true,
                            PreferredDirection = EPortDirection.Left,
                        },
                    },
                    OutputPorts =
                    {
                        CreateOutputPort("colorImage", "Color Image", FlowDataType.Image),
                        CreateOutputPort("depthImage", "Depth Image", FlowDataType.Image),
                        CreateOutputPort("colorCalibration", "Color Calibration", FlowDataType.CameraCalibration),
                        CreateOutputPort("depthCalibration", "Depth Calibration", FlowDataType.CameraCalibration),
                    },
                },
                () => new StereoCameraExecutor(
                    _deviceFactory,
                    _runtimeScopeFactory,
                    _clock,
                    _captureOptions,
                    logger))
            {
                NodeModelType = typeof(StereoCameraNodeModel),
                NodeFactory = () => new StereoCameraNodeModel(),
                PaletteDisplayName = "Stereo Camera",
                PaletteDescription = "Streams synchronized color, depth, and independent calibration data.",
                ContentFactory = StereoCameraEditor.CreateContent,
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
