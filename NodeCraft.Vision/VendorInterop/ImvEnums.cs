namespace NodeCraft.Vision.VendorInterop
{
    internal enum ImvError : int
    {
        Ok = 0,
        Timeout = -119,
    }

    internal enum ImvInterfaceType : uint
    {
        Gige = 0x00000001,
        Usb3 = 0x00000002,
        CameraLink = 0x00000004,
        All = 0x00000000,
        Invalid = 0xffffffff,
    }

    internal enum ImvCreateHandleMode : int
    {
        ByIndex = 0,
        ByCameraKey = 1,
        ByDeviceUserId = 2,
        ByIpAddress = 3,
    }

    internal enum ImvBayerDemosaic : int
    {
        NearestNeighbor = 0,
        Bilinear = 1,
        EdgeSensing = 2,
        NotSupported = 255,
    }

    internal enum ImvPixelType : int
    {
        Undefined = -1,
        Mono8 = 0x01080001,
        BayerGr8 = 0x01080008,
        BayerRg8 = 0x01080009,
        BayerGb8 = 0x0108000a,
        BayerBg8 = 0x0108000b,
        Rgb8 = 0x02180014,
        Bgr8 = 0x02180015,
    }
}
