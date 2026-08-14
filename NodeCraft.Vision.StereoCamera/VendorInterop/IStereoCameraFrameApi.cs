using System;

namespace NodeCraft.Vision.StereoCamera.VendorInterop
{
    internal interface IStereoCameraFrameApi
    {
        IntPtr GetFrame(IntPtr camera, uint timeoutMilliseconds);

        ulong GetFrameId(IntPtr frame);

        ulong GetFrameTimestamp(IntPtr frame);

        IntPtr GetFrameImage(IntPtr frame, ScImageType type);

        int GetImageWidth(IntPtr image);

        int GetImageHeight(IntPtr image);

        ScPixelFormat GetImagePixelFormat(IntPtr image);

        uint GetImageDataSize(IntPtr image);

        IntPtr GetImageData(IntPtr image);

        bool ReleaseHandle(IntPtr handle);
    }
}
