using System;
using Microsoft.Extensions.Logging;
using NodeCraft.Flow;
using NodeCraft.Vision.StereoCamera.Camera;
using NodeCraft.Vision.StereoCamera.Nodes;
using NodeCraft.Vision.StereoCamera.Views;

namespace NodeCraft.Vision.Plugin
{
    internal static class StereoCameraRegistration
    {
        internal static FlowNodeRegistration Create(string pluginAssemblyPath, ILogger logger)
        {
            if (string.IsNullOrWhiteSpace(pluginAssemblyPath))
            {
                throw new ArgumentException("A plugin assembly path is required.", nameof(pluginAssemblyPath));
            }

            return new FlowNodeRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = StereoCameraNodeModel.FlowNodeTypeKey,
                    DisplayName = "Stereo Camera",
                    Category = "Vision",
                    OutputPorts =
                    {
                        CreateOutputPort(
                            "colorImage",
                            "Color Image",
                            FlowDataType.Image,
                            FlowPortAvailability.Iteration),
                        CreateOutputPort(
                            "depthImage",
                            "Depth Image",
                            FlowDataType.Image,
                            FlowPortAvailability.Iteration),
                        CreateOutputPort(
                            "colorCalibration",
                            "Color Calibration",
                            FlowDataType.CameraCalibration,
                            FlowPortAvailability.Session),
                        CreateOutputPort(
                            "depthCalibration",
                            "Depth Calibration",
                            FlowDataType.CameraCalibration,
                            FlowPortAvailability.Session),
                    },
                },
                () => new StereoCameraExecutor(
                    new VendorStereoCameraDeviceFactory(),
                    new ProductionCameraRuntimeScopeFactory(pluginAssemblyPath),
                    new SystemMonotonicClock(),
                    new StereoCameraCaptureOptions(),
                    logger))
            {
                NodeModelType = typeof(StereoCameraNodeModel),
                NodeFactory = () => new StereoCameraNodeModel(),
                PaletteDisplayName = "Stereo Camera",
                PaletteDescription = "Streams synchronized color, depth, and independent calibration data.",
                PaletteCategoryIconKind = "CameraOutline",
                PaletteIconKind = "CameraOutline",
                ContentFactory = StereoCameraEditor.CreateContent,
            };
        }

        private static FlowPortDefinition CreateOutputPort(
            string id,
            string displayName,
            FlowDataType dataType,
            FlowPortAvailability availability)
        {
            return new FlowPortDefinition
            {
                Id = id,
                DisplayName = displayName,
                IOType = EIOType.Output,
                DataType = dataType,
                Availability = availability,
                PreferredDirection = EPortDirection.Right,
            };
        }
    }
}
