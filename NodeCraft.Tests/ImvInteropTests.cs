using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using NodeCraft.Vision.VendorInterop;

internal static partial class Program
{
    private static void RunImvInteropTests()
    {
        Run("IMV structs match the x64 C layout", () =>
            IntPtr.Size != 8
                || Marshal.SizeOf<ImvDeviceList>() == 16
                && Marshal.SizeOf<ImvFrameInfo>() == 136
                && Marshal.SizeOf<ImvFrame>() == 192
                && Marshal.SizeOf<ImvPixelConvertParam>() == 96);

        Run("IMV constants match IMVDefines.h", () =>
            (uint)ImvInterfaceType.All == 0
                && (uint)ImvInterfaceType.Invalid == 0xffffffff
                && (int)ImvCreateHandleMode.ByIpAddress == 3
                && (int)ImvBayerDemosaic.Bilinear == 1
                && (int)ImvError.Timeout == -119
                && (int)ImvPixelType.Mono8 == 0x01080001
                && (int)ImvPixelType.Bgr8 == 0x02180015
                && (int)ImvPixelType.Rgb8 == 0x02180014
                && (int)ImvPixelType.BayerRg8 == 0x01080009);

        Run("IMV native entry points use exact StdCall imports", () =>
        {
            var methods = typeof(ImvNativeMethods)
                .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .Where(method => method.GetCustomAttribute<DllImportAttribute>() != null)
                .ToArray();
            return methods.Length == 11
                && methods.All(method =>
                {
                    var attribute = method.GetCustomAttribute<DllImportAttribute>();
                    return attribute.CallingConvention == CallingConvention.StdCall
                        && attribute.ExactSpelling
                        && attribute.Value == ImvNativeMethods.LibraryName;
                })
                && ImvNativeMethods.LibraryName == "MVSDKmd.dll";
        });

        Run("IMV native error preserves operation and code", () =>
        {
            try
            {
                VisionNativeException.ThrowIfError("IMV_GetFrame", (int)ImvError.Timeout);
                return false;
            }
            catch (VisionNativeException exception)
            {
                return exception.Operation == "IMV_GetFrame"
                    && exception.ErrorCode == -119
                    && exception.Message.Contains("IMV_GetFrame", StringComparison.Ordinal)
                    && exception.Message.Contains("-119", StringComparison.Ordinal);
            }
        });

        Run("IMV safe handle releases through the injected destroy delegate", () =>
        {
            var releaseCount = 0;
            using (var handle = new VisionCameraSafeHandle(
                new IntPtr(42),
                _ =>
                {
                    releaseCount++;
                    return (int)ImvError.Ok;
                }))
            {
                if (handle.IsInvalid)
                {
                    return false;
                }
            }

            return releaseCount == 1;
        });
    }
}
