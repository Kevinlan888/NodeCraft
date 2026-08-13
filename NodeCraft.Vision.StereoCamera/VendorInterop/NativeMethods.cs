using System;
using System.Runtime.InteropServices;

namespace NodeCraft.Vision.StereoCamera.VendorInterop
{
    internal static class NativeMethods
    {
        internal const string LibraryName = "LibStereoCamera.dll";

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int scDiscovery(
            ScInterfaceType type,
            [Out] IntPtr[] cameras,
            ref int size);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern IntPtr scGetCamera(
            [MarshalAs(UnmanagedType.LPStr)] string data,
            ScCameraDataType type);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool scReleaseHandle(IntPtr handle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int scConnect(IntPtr camera);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int scDisconnect(IntPtr camera);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern uint scRegisterConnectEvent(
            IntPtr camera,
            ScFnConnectEvent callback,
            IntPtr data);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void scUnregisterConnectEvent(IntPtr camera, uint id);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int scStartGrabbing(IntPtr camera, int bufferCount);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void scStopGrabbing(IntPtr camera);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern IntPtr scGetFrame(IntPtr camera, uint timeout);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern ulong scGetFrameID(IntPtr frame);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern ulong scGetFrameTimestamp(IntPtr frame);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern IntPtr scGetFrameImage(IntPtr frame, ScImageType type);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern IntPtr scGetImageData(IntPtr image);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern uint scGetImageDataSize(IntPtr image);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int scGetImageWidth(IntPtr image);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int scGetImageHeight(IntPtr image);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern ScPixelFormat scGetImagePixelFormat(IntPtr image);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern IntPtr scCreateCalibDataManager(IntPtr camera);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int scDownloadCalibData(IntPtr handle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int scGetCameraCalibInfo(
            IntPtr handle,
            ScImageType type,
            out ScCameraCalibInfo data,
            [MarshalAs(UnmanagedType.I1)] bool isLeftReference);
    }
}
