using System;

namespace NodeCraft.Vision.VendorInterop
{
    internal sealed class VisionNativeException : Exception
    {
        internal VisionNativeException(string operation, int errorCode)
            : base($"Vision native operation '{operation}' failed with error code {errorCode}.")
        {
            Operation = operation;
            ErrorCode = errorCode;
        }

        internal string Operation { get; }

        internal int ErrorCode { get; }

        internal static void ThrowIfError(string operation, int errorCode)
        {
            if (errorCode != (int)ImvError.Ok)
            {
                throw new VisionNativeException(operation, errorCode);
            }
        }
    }
}
