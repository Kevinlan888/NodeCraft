using System;
using System.Runtime.InteropServices;

namespace NodeCraft.Vision.VendorInterop
{
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct ImvDeviceList
    {
        internal uint DeviceCount;
        internal IntPtr DeviceInfo;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct ImvFrameInfo
    {
        internal ulong BlockId;
        internal uint Status;
        internal uint Width;
        internal uint Height;
        internal uint Size;
        internal ImvPixelType PixelFormat;
        internal ulong TimeStamp;
        internal uint ChunkCount;
        internal uint PaddingX;
        internal uint PaddingY;
        internal uint RecvFrameTime;
        internal uint TriggerCount;
        internal uint RotaryFrameEndCount;
        internal uint Reserved0;
        internal uint Reserved1;
        internal uint Reserved2;
        internal uint Reserved3;
        internal uint Reserved4;
        internal uint Reserved5;
        internal uint Reserved6;
        internal uint Reserved7;
        internal uint Reserved8;
        internal uint Reserved9;
        internal uint Reserved10;
        internal uint Reserved11;
        internal uint Reserved12;
        internal uint Reserved13;
        internal uint Reserved14;
        internal uint Reserved15;
        internal uint Reserved16;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct ImvFrame
    {
        internal IntPtr FrameHandle;
        internal IntPtr Data;
        internal ImvFrameInfo FrameInfo;
        internal uint Reserved0;
        internal uint Reserved1;
        internal uint Reserved2;
        internal uint Reserved3;
        internal uint Reserved4;
        internal uint Reserved5;
        internal uint Reserved6;
        internal uint Reserved7;
        internal uint Reserved8;
        internal uint Reserved9;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct ImvPixelConvertParam
    {
        internal uint Width;
        internal uint Height;
        internal ImvPixelType PixelFormat;
        internal IntPtr SourceData;
        internal uint SourceDataLength;
        internal uint PaddingX;
        internal uint PaddingY;
        internal ImvBayerDemosaic BayerDemosaic;
        internal ImvPixelType DestinationPixelFormat;
        internal IntPtr DestinationBuffer;
        internal uint DestinationBufferSize;
        internal uint DestinationDataLength;
        internal uint Reserved0;
        internal uint Reserved1;
        internal uint Reserved2;
        internal uint Reserved3;
        internal uint Reserved4;
        internal uint Reserved5;
        internal uint Reserved6;
        internal uint Reserved7;
    }
}
