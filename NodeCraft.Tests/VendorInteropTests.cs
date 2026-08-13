using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using NodeCraft.Vision.StereoCamera.VendorInterop;

internal static partial class Program
{
    private static void RunVendorInteropTests()
    {
        Run("vendor calibration layout matches CAPI.h", () =>
        {
            var fields = typeof(ScCameraCalibInfo)
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return Marshal.SizeOf<ScCameraCalibInfo>() == 416
                && ReadByValArraySize(fields, "Intrinsic") == 9
                && ReadByValArraySize(fields, "Distortion") == 12
                && ReadByValArraySize(fields, "Extrinsic") == 16
                && ReadByValArraySize(fields, "Reserved") == 28;
        });

        Run("vendor connect event layout and callbacks use Cdecl", () =>
        {
            var fields = typeof(ScConnectEventArg)
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var connectAttribute = typeof(ScFnConnectEvent)
                .GetCustomAttribute<UnmanagedFunctionPointerAttribute>();
            var frameAttribute = typeof(ScFnFrameCallback)
                .GetCustomAttribute<UnmanagedFunctionPointerAttribute>();
            return Marshal.SizeOf<ScConnectEventArg>() == 68
                && ReadByValArraySize(fields, "Reserved") == 64
                && connectAttribute?.CallingConvention == CallingConvention.Cdecl
                && frameAttribute?.CallingConvention == CallingConvention.Cdecl;
        });

        Run("vendor native entry points use exact Cdecl imports and I1 bool marshaling", () =>
        {
            var methods = typeof(NativeMethods)
                .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .Where(method => method.GetCustomAttribute<DllImportAttribute>() != null)
                .ToArray();
            var importsAreExactCdecl = methods.Length > 0 && methods.All(method =>
            {
                var attribute = method.GetCustomAttribute<DllImportAttribute>();
                return attribute.CallingConvention == CallingConvention.Cdecl
                    && attribute.ExactSpelling
                    && attribute.Value == NativeMethods.LibraryName;
            });
            var boolMarshalingIsExplicit = methods.All(method =>
            {
                var returnType = method.ReturnType;
                var returnIsSafe = returnType != typeof(bool)
                    || method.ReturnParameter.GetCustomAttribute<MarshalAsAttribute>()?.Value == UnmanagedType.I1;
                var parametersAreSafe = method.GetParameters()
                    .Where(parameter => parameter.ParameterType == typeof(bool))
                    .All(parameter => parameter.GetCustomAttribute<MarshalAsAttribute>()?.Value == UnmanagedType.I1);
                return returnIsSafe && parametersAreSafe;
            });
            return importsAreExactCdecl && boolMarshalingIsExplicit;
        });

        Run("vendor handles release through the common native handle API", () =>
        {
            var handleTypes = new[]
            {
                typeof(StereoCameraCameraHandle),
                typeof(StereoCameraFrameHandle),
                typeof(StereoCameraImageHandle),
                typeof(StereoCameraCalibrationManagerHandle),
            };
            return handleTypes.All(type =>
                typeof(Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid).IsAssignableFrom(type)
                && type.BaseType == typeof(StereoCameraSafeHandleBase));
        });

        Run("vendor error wrapper preserves operation and numeric code", () =>
        {
            try
            {
                StereoCameraNativeException.ThrowIfError("scConnect", (int)ScError.NotConnected);
                return false;
            }
            catch (StereoCameraNativeException exception)
            {
                return exception.Operation == "scConnect"
                    && exception.ErrorCode == (int)ScError.NotConnected
                    && exception.Message.Contains("scConnect", StringComparison.Ordinal)
                    && exception.Message.Contains(((int)ScError.NotConnected).ToString(), StringComparison.Ordinal);
            }
        });
    }

    private static int ReadByValArraySize(FieldInfo[] fields, string fieldName)
    {
        return fields
            .Single(field => field.Name == fieldName)
            .GetCustomAttribute<MarshalAsAttribute>()?.SizeConst ?? -1;
    }
}
