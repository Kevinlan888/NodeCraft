using System;

namespace Node.Algorithm.Interop
{
    internal sealed class WaybillNativeException : InvalidOperationException
    {
        internal WaybillNativeException(string operation, int errorCode)
            : base($"{operation} failed with {GetErrorName(errorCode)} ({errorCode}).")
        {
            Operation = operation ?? string.Empty;
            ErrorCode = errorCode;
            ErrorName = GetErrorName(errorCode);
        }

        internal string Operation { get; }

        internal int ErrorCode { get; }

        internal string ErrorName { get; }

        internal static string GetErrorName(int errorCode)
        {
            switch (errorCode)
            {
                case 0: return "WAYBILL_OK";
                case 1: return "WAYBILL_ERR_INVALID_ARG";
                case 2: return "WAYBILL_ERR_MODEL_LOAD";
                case 3: return "WAYBILL_ERR_MODEL_SHAPE";
                case 4: return "WAYBILL_ERR_INFERENCE";
                case 5: return "WAYBILL_ERR_CONFIG";
                case 6: return "WAYBILL_ERR_NOMEM";
                case 7: return "WAYBILL_ERR_INTERNAL";
                default: return "WAYBILL_ERR_UNKNOWN";
            }
        }

        internal static void ThrowIfFailed(string operation, int errorCode)
        {
            if (errorCode != 0)
            {
                throw new WaybillNativeException(operation, errorCode);
            }
        }
    }
}
