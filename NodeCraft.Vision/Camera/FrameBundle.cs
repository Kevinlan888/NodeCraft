using System;
using NodeCraft.Flow;

namespace NodeCraft.Vision.StereoCamera.Camera
{
    internal sealed class FrameBundle
    {
        internal FrameBundle(
            long sequence,
            FlowImage colorImage,
            FlowImage depthImage,
            CameraCalibration colorCalibration,
            CameraCalibration depthCalibration)
        {
            if (colorImage == null)
            {
                throw new ArgumentNullException(nameof(colorImage));
            }

            if (depthImage == null)
            {
                throw new ArgumentNullException(nameof(depthImage));
            }

            if (colorCalibration == null)
            {
                throw new ArgumentNullException(nameof(colorCalibration));
            }

            if (depthCalibration == null)
            {
                throw new ArgumentNullException(nameof(depthCalibration));
            }

            if (colorImage.FrameId != depthImage.FrameId)
            {
                throw new ArgumentException("Color and depth images must belong to the same frame.");
            }

            Sequence = sequence;
            ColorImage = colorImage;
            DepthImage = depthImage;
            ColorCalibration = colorCalibration;
            DepthCalibration = depthCalibration;
        }

        internal long Sequence { get; }

        internal FlowImage ColorImage { get; }

        internal FlowImage DepthImage { get; }

        internal CameraCalibration ColorCalibration { get; }

        internal CameraCalibration DepthCalibration { get; }
    }
}
