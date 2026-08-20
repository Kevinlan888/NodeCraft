using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Node.Algorithm.Interop;
using NodeCraft.Flow;

internal static partial class Program
{
    private static void RunAlgorithmInteropTests()
    {
        Run("Waybill native structs match the x64 C ABI", () =>
            Marshal.SizeOf<NativeWaybillConfig>() == 24
                && Marshal.SizeOf<NativeWaybillDetection>() == 44
                && Marshal.SizeOf<NativeWaybillResult>() == 24
                && Marshal.OffsetOf<NativeWaybillDetection>(nameof(NativeWaybillDetection.GeometryMethod)).ToInt32() == 36
                && Marshal.OffsetOf<NativeWaybillDetection>(nameof(NativeWaybillDetection.MaskIou)).ToInt32() == 40);

        Run("Waybill native entry points use exact C ABI names", () =>
        {
            var expected = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [nameof(WaybillNativeMethods.Create)] = "waybill_create",
                [nameof(WaybillNativeMethods.GetConfig)] = "waybill_get_cfg",
                [nameof(WaybillNativeMethods.SetConfig)] = "waybill_set_cfg",
                [nameof(WaybillNativeMethods.Process)] = "waybill_process",
                [nameof(WaybillNativeMethods.ReleaseDetections)] = "waybill_release_detections",
                [nameof(WaybillNativeMethods.Release)] = "waybill_release",
            };
            return expected.All(pair =>
            {
                var method = typeof(WaybillNativeMethods).GetMethod(
                    pair.Key,
                    BindingFlags.NonPublic | BindingFlags.Static);
                var import = method?.GetCustomAttribute<DllImportAttribute>();
                return import != null
                    && import.CallingConvention == CallingConvention.Cdecl
                    && import.EntryPoint == pair.Value;
            });
        });

        Run("Waybill native errors expose stable names", () =>
            WaybillNativeException.GetErrorName(2) == "WAYBILL_ERR_MODEL_LOAD"
                && WaybillNativeException.GetErrorName(7) == "WAYBILL_ERR_INTERNAL"
                && WaybillNativeException.GetErrorName(999) == "WAYBILL_ERR_UNKNOWN");

        Run("Waybill image buffer packs padded BGR rows", () =>
        {
            var image = FlowImage.CopyFrom(
                3,
                2,
                12,
                FlowPixelFormat.Bgr24,
                FlowImageKind.Color,
                new byte[]
                {
                    1, 2, 3, 4, 5, 6, 7, 8, 9, 90, 91, 92,
                    10, 11, 12, 13, 14, 15, 16, 17, 18, 80, 81, 82,
                },
                1,
                2,
                DateTimeOffset.UtcNow);
            using var buffer = WaybillImageBuffer.Create(image);
            var packed = new byte[18];
            Marshal.Copy(buffer.Pointer, packed, 0, packed.Length);

            return buffer.Width == 3
                && buffer.Height == 2
                && buffer.InputFormat == 0
                && packed.SequenceEqual(new byte[]
                {
                    1, 2, 3, 4, 5, 6, 7, 8, 9,
                    10, 11, 12, 13, 14, 15, 16, 17, 18,
                });
        });

        Run("Waybill image buffer retains packed RGB rows", () =>
        {
            var image = FlowImage.CopyFrom(
                2,
                1,
                6,
                FlowPixelFormat.Rgb24,
                FlowImageKind.Color,
                new byte[] { 1, 2, 3, 4, 5, 6 },
                1,
                2,
                DateTimeOffset.UtcNow);
            using var buffer = WaybillImageBuffer.Create(image);
            var packed = new byte[6];
            Marshal.Copy(buffer.Pointer, packed, 0, packed.Length);
            return buffer.InputFormat == 1 && packed.SequenceEqual(image.Buffer.ToArray());
        });

        Run("Waybill image buffer packs padded Mono8 rows", () =>
        {
            var image = FlowImage.CopyFrom(
                3,
                2,
                4,
                FlowPixelFormat.Mono8,
                FlowImageKind.Color,
                new byte[] { 1, 2, 3, 99, 4, 5, 6, 98 },
                1,
                2,
                DateTimeOffset.UtcNow);
            using var buffer = WaybillImageBuffer.Create(image);
            var packed = new byte[6];
            Marshal.Copy(buffer.Pointer, packed, 0, packed.Length);
            return buffer.InputFormat == 2
                && packed.SequenceEqual(new byte[] { 1, 2, 3, 4, 5, 6 });
        });

        Run("Waybill image buffer rejects Depth16", () =>
        {
            var image = FlowImage.CopyFrom(
                2,
                1,
                4,
                FlowPixelFormat.Depth16,
                FlowImageKind.Depth,
                new byte[] { 1, 2, 3, 4 },
                1,
                2,
                DateTimeOffset.UtcNow);
            return ThrowsAlgorithm<InvalidDataException>(() => WaybillImageBuffer.Create(image));
        });
    }
}
