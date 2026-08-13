using System;
using System.Collections.Generic;

namespace NodeCraft.Flow
{
    public sealed class CameraCalibration
    {
        public CameraCalibration(
            int imageWidth,
            int imageHeight,
            IReadOnlyList<double> intrinsic,
            IReadOnlyList<double> distortion,
            IReadOnlyList<double> extrinsic,
            bool isLeftReference)
        {
            if (imageWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(imageWidth), "Image width must be positive.");
            }

            if (imageHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(imageHeight), "Image height must be positive.");
            }

            Intrinsic = CopyExact(intrinsic, 9, nameof(intrinsic));
            Distortion = CopyExact(distortion, 12, nameof(distortion));
            Extrinsic = CopyExact(extrinsic, 16, nameof(extrinsic));
            ImageWidth = imageWidth;
            ImageHeight = imageHeight;
            IsLeftReference = isLeftReference;
        }

        public int ImageWidth { get; }

        public int ImageHeight { get; }

        public ReadOnlyMemory<double> Intrinsic { get; }

        public ReadOnlyMemory<double> Distortion { get; }

        public ReadOnlyMemory<double> Extrinsic { get; }

        public bool IsLeftReference { get; }

        private static ReadOnlyMemory<double> CopyExact(
            IReadOnlyList<double> values,
            int expectedLength,
            string parameterName)
        {
            if (values == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            if (values.Count != expectedLength)
            {
                throw new ArgumentException(
                    $"{parameterName} must contain exactly {expectedLength} values.",
                    parameterName);
            }

            var copy = new double[expectedLength];
            for (var index = 0; index < expectedLength; index++)
            {
                copy[index] = values[index];
            }

            return copy;
        }
    }
}
