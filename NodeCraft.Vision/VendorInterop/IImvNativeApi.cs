using System;

namespace NodeCraft.Vision.VendorInterop
{
    internal interface IImvNativeApi
    {
        int EnumDevices(out ImvDeviceList deviceList, ImvInterfaceType interfaceType);

        int CreateHandle(out IntPtr handle, ImvCreateHandleMode mode, IntPtr identifier);

        int DestroyHandle(IntPtr handle);

        int Open(IntPtr handle);

        int Close(IntPtr handle);

        int SetEnumFeatureSymbol(IntPtr handle, string featureName, string enumSymbol);

        int StartGrabbing(IntPtr handle);

        int StopGrabbing(IntPtr handle);

        int GetFrame(IntPtr handle, out ImvFrame frame, uint timeoutMilliseconds);

        int ReleaseFrame(IntPtr handle, ref ImvFrame frame);

        int PixelConvert(IntPtr handle, ref ImvPixelConvertParam parameter);
    }

    internal sealed class ImvNativeApi : IImvNativeApi
    {
        internal static readonly ImvNativeApi Instance = new ImvNativeApi();

        private ImvNativeApi()
        {
        }

        public int EnumDevices(out ImvDeviceList deviceList, ImvInterfaceType interfaceType)
        {
            return ImvNativeMethods.IMV_EnumDevices(out deviceList, interfaceType);
        }

        public int CreateHandle(out IntPtr handle, ImvCreateHandleMode mode, IntPtr identifier)
        {
            return ImvNativeMethods.IMV_CreateHandle(out handle, mode, identifier);
        }

        public int DestroyHandle(IntPtr handle)
        {
            return ImvNativeMethods.IMV_DestroyHandle(handle);
        }

        public int Open(IntPtr handle)
        {
            return ImvNativeMethods.IMV_Open(handle);
        }

        public int Close(IntPtr handle)
        {
            return ImvNativeMethods.IMV_Close(handle);
        }

        public int SetEnumFeatureSymbol(IntPtr handle, string featureName, string enumSymbol)
        {
            return ImvNativeMethods.IMV_SetEnumFeatureSymbol(handle, featureName, enumSymbol);
        }

        public int StartGrabbing(IntPtr handle)
        {
            return ImvNativeMethods.IMV_StartGrabbing(handle);
        }

        public int StopGrabbing(IntPtr handle)
        {
            return ImvNativeMethods.IMV_StopGrabbing(handle);
        }

        public int GetFrame(IntPtr handle, out ImvFrame frame, uint timeoutMilliseconds)
        {
            return ImvNativeMethods.IMV_GetFrame(handle, out frame, timeoutMilliseconds);
        }

        public int ReleaseFrame(IntPtr handle, ref ImvFrame frame)
        {
            return ImvNativeMethods.IMV_ReleaseFrame(handle, ref frame);
        }

        public int PixelConvert(IntPtr handle, ref ImvPixelConvertParam parameter)
        {
            return ImvNativeMethods.IMV_PixelConvert(handle, ref parameter);
        }
    }
}
