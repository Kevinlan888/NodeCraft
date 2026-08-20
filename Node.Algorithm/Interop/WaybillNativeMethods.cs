using System;
using System.Runtime.InteropServices;

namespace Node.Algorithm.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeWaybillConfig
    {
        internal float Confidence;
        internal float Iou;
        internal float MinMaskAreaRatio;
        internal int MaxDetections;
        internal int NumThreads;
        internal int InputFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeWaybillDetection
    {
        internal float Score;
        internal int Point0X;
        internal int Point0Y;
        internal int Point1X;
        internal int Point1Y;
        internal int Point2X;
        internal int Point2Y;
        internal int Point3X;
        internal int Point3Y;
        internal int GeometryMethod;
        internal float MaskIou;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeWaybillResult
    {
        internal int Width;
        internal int Height;
        internal int Count;
        internal IntPtr Detections;
    }

    internal static class WaybillNativeMethods
    {
        internal const string LibraryName = "waybill_infer.dll";

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int Create(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string modelPath,
            out IntPtr handle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int GetConfig(
            IntPtr handle,
            out NativeWaybillConfig config);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int SetConfig(
            IntPtr handle,
            ref NativeWaybillConfig config);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int Process(
            IntPtr handle,
            IntPtr pixels,
            int width,
            int height,
            out NativeWaybillResult result);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void ReleaseDetections(ref NativeWaybillResult result);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void Release(IntPtr handle);
    }
}
