using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Node.Algorithm.Models;
using NodeCraft.Flow;

namespace Node.Algorithm.Interop
{
    internal sealed class WaybillNativeSessionFactory : IWaybillInferenceSessionFactory
    {
        public IWaybillInferenceSession Create(
            string pluginAssemblyPath,
            string modelPath,
            WaybillInferenceOptions options)
        {
            return WaybillNativeSession.Create(pluginAssemblyPath, modelPath, options);
        }
    }

    internal sealed class WaybillNativeSession : IWaybillInferenceSession
    {
        private readonly object _gate = new object();
        private readonly WaybillRuntimeScope _runtimeScope;
        private readonly WaybillInferenceOptions _options;
        private NativeWaybillConfig _config;
        private IntPtr _handle;
        private int _inputFormat = -1;
        private bool _disposed;

        private WaybillNativeSession(
            IntPtr handle,
            WaybillRuntimeScope runtimeScope,
            WaybillInferenceOptions options,
            NativeWaybillConfig config)
        {
            _handle = handle;
            _runtimeScope = runtimeScope;
            _options = options;
            _config = config;
        }

        internal static WaybillNativeSession Create(
            string pluginAssemblyPath,
            string modelPath,
            WaybillInferenceOptions options)
        {
            ValidateCreateArguments(pluginAssemblyPath, modelPath, options);
            var runtimeScope = WaybillRuntimeScope.Acquire(pluginAssemblyPath);
            var handle = IntPtr.Zero;
            try
            {
                WaybillNativeException.ThrowIfFailed(
                    "waybill_create",
                    WaybillNativeMethods.Create(modelPath, out handle));
                if (handle == IntPtr.Zero)
                {
                    throw new InvalidDataException("waybill_create returned a null handle.");
                }

                var config = new NativeWaybillConfig
                {
                    Confidence = options.Confidence,
                    Iou = options.Iou,
                    MinMaskAreaRatio = options.MinMaskAreaRatio,
                    MaxDetections = options.MaxDetections,
                    NumThreads = options.NumThreads,
                    InputFormat = 0,
                };
                WaybillNativeException.ThrowIfFailed(
                    "waybill_set_cfg",
                    WaybillNativeMethods.SetConfig(handle, ref config));

                return new WaybillNativeSession(handle, runtimeScope, options, config);
            }
            catch
            {
                if (handle != IntPtr.Zero)
                {
                    WaybillNativeMethods.Release(handle);
                }

                runtimeScope.Dispose();
                throw;
            }
        }

        public WaybillRecognitionResult Process(FlowImage image, CancellationToken cancellationToken)
        {
            if (image == null)
            {
                throw new ArgumentNullException(nameof(image));
            }

            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                ThrowIfDisposed();
                cancellationToken.ThrowIfCancellationRequested();

                using var imageBuffer = WaybillImageBuffer.Create(image);
                if (_inputFormat != imageBuffer.InputFormat)
                {
                    _config.InputFormat = imageBuffer.InputFormat;
                    WaybillNativeException.ThrowIfFailed(
                        "waybill_set_cfg",
                        WaybillNativeMethods.SetConfig(_handle, ref _config));
                    _inputFormat = imageBuffer.InputFormat;
                }

                NativeWaybillResult nativeResult;
                WaybillNativeException.ThrowIfFailed(
                    "waybill_process",
                    WaybillNativeMethods.Process(
                        _handle,
                        imageBuffer.Pointer,
                        imageBuffer.Width,
                        imageBuffer.Height,
                        out nativeResult));

                try
                {
                    return CopyResult(nativeResult, image.Width, image.Height);
                }
                finally
                {
                    WaybillNativeMethods.ReleaseDetections(ref nativeResult);
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                var handle = _handle;
                _handle = IntPtr.Zero;
                try
                {
                    if (handle != IntPtr.Zero)
                    {
                        WaybillNativeMethods.Release(handle);
                    }
                }
                finally
                {
                    _runtimeScope.Dispose();
                }
            }
        }

        private WaybillRecognitionResult CopyResult(
            NativeWaybillResult nativeResult,
            int expectedWidth,
            int expectedHeight)
        {
            if (nativeResult.Width != expectedWidth || nativeResult.Height != expectedHeight)
            {
                throw new InvalidDataException(
                    $"waybill_process returned {nativeResult.Width}x{nativeResult.Height} for an {expectedWidth}x{expectedHeight} image.");
            }

            if (nativeResult.Count < 0 || nativeResult.Count > _options.MaxDetections)
            {
                throw new InvalidDataException(
                    $"waybill_process returned an invalid detection count: {nativeResult.Count}.");
            }

            if (nativeResult.Count > 0 && nativeResult.Detections == IntPtr.Zero)
            {
                throw new InvalidDataException("waybill_process returned detections without a buffer.");
            }

            var detections = new List<WaybillDetection>(nativeResult.Count);
            var nativeSize = Marshal.SizeOf<NativeWaybillDetection>();
            for (var index = 0; index < nativeResult.Count; index++)
            {
                var nativePointer = IntPtr.Add(
                    nativeResult.Detections,
                    checked(index * nativeSize));
                var nativeDetection = Marshal.PtrToStructure<NativeWaybillDetection>(nativePointer);
                if (nativeDetection.GeometryMethod != 0 && nativeDetection.GeometryMethod != 1)
                {
                    throw new InvalidDataException(
                        $"waybill_process returned an unknown geometry method: {nativeDetection.GeometryMethod}.");
                }

                detections.Add(new WaybillDetection(
                    nativeDetection.Score,
                    new[]
                    {
                        new WaybillPoint(nativeDetection.Point0X, nativeDetection.Point0Y),
                        new WaybillPoint(nativeDetection.Point1X, nativeDetection.Point1Y),
                        new WaybillPoint(nativeDetection.Point2X, nativeDetection.Point2Y),
                        new WaybillPoint(nativeDetection.Point3X, nativeDetection.Point3Y),
                    },
                    (WaybillGeometryMethod)nativeDetection.GeometryMethod,
                    nativeDetection.MaskIou));
            }

            return new WaybillRecognitionResult(nativeResult.Width, nativeResult.Height, detections);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(WaybillNativeSession));
            }
        }

        private static void ValidateCreateArguments(
            string pluginAssemblyPath,
            string modelPath,
            WaybillInferenceOptions options)
        {
            if (string.IsNullOrWhiteSpace(pluginAssemblyPath))
            {
                throw new ArgumentException("A plugin assembly path is required.", nameof(pluginAssemblyPath));
            }

            if (string.IsNullOrWhiteSpace(modelPath))
            {
                throw new ArgumentException("A model path is required.", nameof(modelPath));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (float.IsNaN(options.Confidence)
                || float.IsInfinity(options.Confidence)
                || options.Confidence < 0
                || options.Confidence > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(options.Confidence));
            }

            if (float.IsNaN(options.Iou)
                || float.IsInfinity(options.Iou)
                || options.Iou < 0
                || options.Iou > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(options.Iou));
            }

            if (float.IsNaN(options.MinMaskAreaRatio)
                || float.IsInfinity(options.MinMaskAreaRatio)
                || options.MinMaskAreaRatio < 0
                || options.MinMaskAreaRatio > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(options.MinMaskAreaRatio));
            }

            if (options.MaxDetections <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options.MaxDetections));
            }

            if (options.NumThreads < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options.NumThreads));
            }
        }
    }
}
