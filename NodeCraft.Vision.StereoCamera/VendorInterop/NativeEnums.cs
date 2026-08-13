namespace NodeCraft.Vision.StereoCamera.VendorInterop
{
    internal enum ScError
    {
        ScErrorOK = 0,
        ScErrorUnknown,
        ScErrorInternalError,
        ScErrorInvalidParameter,
        ScErrorNotConnected,
        ScErrorNotFound,
        ScErrorTimeout,
        ScErrorNotImplemented,
        ScErrorRepeatOperation,
        ScErrorNullPtr,
        ScErrorReadDataFail,
        ScErrorWriteDataFail,
        ScErrorDataCheckFail,
        ScErrorImageSizeError,
        ScErrorImageTypeError,
        ScErrorImageDataTypeError,
        ScErrorSerializeFail,
        ScErrorDeserializeFail,
        ScErrorOpenFileFail,
        ScErrorWriteFileFail,
        ScErrorInvalidHandle,

        OK = ScErrorOK,
        Unknown = ScErrorUnknown,
        InternalError = ScErrorInternalError,
        InvalidParameter = ScErrorInvalidParameter,
        NotConnected = ScErrorNotConnected,
        NotFound = ScErrorNotFound,
        Timeout = ScErrorTimeout,
        NotImplemented = ScErrorNotImplemented,
        RepeatOperation = ScErrorRepeatOperation,
        NullPtr = ScErrorNullPtr,
        ReadDataFail = ScErrorReadDataFail,
        WriteDataFail = ScErrorWriteDataFail,
        DataCheckFail = ScErrorDataCheckFail,
        ImageSizeError = ScErrorImageSizeError,
        ImageTypeError = ScErrorImageTypeError,
        ImageDataTypeError = ScErrorImageDataTypeError,
        SerializeFail = ScErrorSerializeFail,
        DeserializeFail = ScErrorDeserializeFail,
        OpenFileFail = ScErrorOpenFileFail,
        WriteFileFail = ScErrorWriteFileFail,
        InvalidHandle = ScErrorInvalidHandle,
    }

    internal enum ScInterfaceType : uint
    {
        ScInterfaceTypeUnknown = 0,
        ScInterfaceTypeNIC = 0x00000001,
        ScInterfaceTypeUSB = 0x00000002,
        ScInterfaceTypeAll = 0xffffffff,

        Unknown = ScInterfaceTypeUnknown,
        NIC = ScInterfaceTypeNIC,
        USB = ScInterfaceTypeUSB,
        All = ScInterfaceTypeAll,
    }

    internal enum ScCameraDataType
    {
        ScCameraDataTypeKey = 0,
        ScCameraDataTypeSN,
        ScCameraDataTypeIP,
        ScCameraDataTypeMAC,

        Key = ScCameraDataTypeKey,
        SN = ScCameraDataTypeSN,
        IP = ScCameraDataTypeIP,
        MAC = ScCameraDataTypeMAC,
    }

    internal enum ScImageType
    {
        ScImageTypeUnknown = 0,
        ScImageTypeLeftIR,
        ScImageTypeRightIR,
        ScImageTypeLeftRectify,
        ScImageTypeRightRectify,
        ScImageTypeDepth,
        ScImageTypeColor,
        ScImageTypeMask = 10,

        Unknown = ScImageTypeUnknown,
        LeftIR = ScImageTypeLeftIR,
        RightIR = ScImageTypeRightIR,
        LeftRectify = ScImageTypeLeftRectify,
        RightRectify = ScImageTypeRightRectify,
        Depth = ScImageTypeDepth,
        Color = ScImageTypeColor,
        Mask = ScImageTypeMask,
    }

    internal enum ScPixelFormat
    {
        ScPixelFormatUnknown = 0,
        ScPixelFormatMono8,
        ScPixelFormatDepth16,
        ScPixelFormatBGR,
        ScPixelFormatRGB,

        Unknown = ScPixelFormatUnknown,
        Mono8 = ScPixelFormatMono8,
        Depth16 = ScPixelFormatDepth16,
        BGR = ScPixelFormatBGR,
        RGB = ScPixelFormatRGB,
    }

    internal enum ScConnectEventState
    {
        Offline = 0,
    }
}
