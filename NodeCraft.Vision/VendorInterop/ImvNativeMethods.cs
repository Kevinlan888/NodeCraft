using System;
using System.Runtime.InteropServices;

namespace NodeCraft.Vision.VendorInterop
{
    internal static class ImvNativeMethods
    {
        internal const string LibraryName = "MVSDKmd.dll";

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        internal static extern int IMV_EnumDevices(
            out ImvDeviceList deviceList,
            ImvInterfaceType interfaceType);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        internal static extern int IMV_CreateHandle(
            out IntPtr handle,
            ImvCreateHandleMode mode,
            IntPtr identifier);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        internal static extern int IMV_DestroyHandle(IntPtr handle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        internal static extern int IMV_Open(IntPtr handle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        internal static extern int IMV_Close(IntPtr handle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        internal static extern int IMV_SetEnumFeatureSymbol(
            IntPtr handle,
            [MarshalAs(UnmanagedType.LPStr)] string featureName,
            [MarshalAs(UnmanagedType.LPStr)] string enumSymbol);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        internal static extern int IMV_StartGrabbing(IntPtr handle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        internal static extern int IMV_StopGrabbing(IntPtr handle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        internal static extern int IMV_GetFrame(
            IntPtr handle,
            out ImvFrame frame,
            uint timeoutMilliseconds);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        internal static extern int IMV_ReleaseFrame(
            IntPtr handle,
            ref ImvFrame frame);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        internal static extern int IMV_PixelConvert(
            IntPtr handle,
            ref ImvPixelConvertParam parameter);
    }
}
