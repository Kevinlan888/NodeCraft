using System;
using NodeCraft.Flow;

namespace NodeCraft.Vision.StereoCamera.Camera
{
    internal sealed class FrameBundle
    {
        internal FrameBundle(
            long sequence,
            FlowImage colorImage,
            FlowImage depthImage)
        {
            if (colorImage == null)
            {
                throw new ArgumentNullException(nameof(colorImage));
            }

            if (depthImage == null)
            {
                throw new ArgumentNullException(nameof(depthImage));
            }

            if (colorImage.FrameId != depthImage.FrameId)
            {
                throw new ArgumentException("Color and depth images must belong to the same frame.");
            }

            Sequence = sequence;
            ColorImage = colorImage;
            DepthImage = depthImage;
        }

        internal long Sequence { get; }

        internal FlowImage ColorImage { get; }

        internal FlowImage DepthImage { get; }
    }
}
