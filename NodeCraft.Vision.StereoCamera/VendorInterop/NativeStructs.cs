using System;
using System.Runtime.InteropServices;

namespace NodeCraft.Vision.StereoCamera.VendorInterop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct ScConnectEventArg
    {
        internal ScConnectEventState State;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64, ArraySubType = UnmanagedType.I1)]
        internal byte[] Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ScCameraCalibInfo
    {
        internal int IntrinsicImgWidth;
        internal int IntrinsicImgHeight;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9, ArraySubType = UnmanagedType.R8)]
        internal double[] Intrinsic;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12, ArraySubType = UnmanagedType.R8)]
        internal double[] Distortion;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16, ArraySubType = UnmanagedType.R8)]
        internal double[] Extrinsic;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 28, ArraySubType = UnmanagedType.I4)]
        internal int[] Reserved;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void ScFnConnectEvent(IntPtr arg, IntPtr data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void ScFnFrameCallback(IntPtr frame, IntPtr data);
}
