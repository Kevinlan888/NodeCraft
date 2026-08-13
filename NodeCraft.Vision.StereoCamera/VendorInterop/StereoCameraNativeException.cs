using System;

namespace NodeCraft.Vision.StereoCamera.VendorInterop
{
    internal sealed class StereoCameraNativeException : Exception
    {
        internal StereoCameraNativeException(string operation, int errorCode)
            : base($"StereoCamera native operation '{operation}' failed with error code {errorCode}.")
        {
            Operation = operation;
            ErrorCode = errorCode;
        }

        internal string Operation { get; }

        internal int ErrorCode { get; }

        internal static void ThrowIfError(string operation, int errorCode)
        {
            if (errorCode != (int)ScError.OK)
            {
                throw new StereoCameraNativeException(operation, errorCode);
            }
        }
    }
}
