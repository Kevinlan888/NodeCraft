using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using NodeCraft.Flow;

namespace NodeCraft.Vision.Nodes
{
    internal sealed class VirtualCameraEntry
    {
        internal VirtualCameraEntry(
            int ordinal,
            string path,
            VirtualCameraImageTemplate preloadedTemplate)
        {
            Ordinal = ordinal;
            Path = path;
            PreloadedTemplate = preloadedTemplate;
        }

        public int Ordinal { get; }

        public string Path { get; }

        public VirtualCameraImageTemplate PreloadedTemplate { get; }
    }

    internal sealed class VirtualCameraSource
    {
        internal VirtualCameraSource(
            string imageDirectory,
            bool isBuiltin,
            IReadOnlyList<VirtualCameraEntry> entries)
        {
            ImageDirectory = imageDirectory;
            IsBuiltin = isBuiltin;
            Entries = entries;
        }

        public string ImageDirectory { get; }

        public bool IsBuiltin { get; }

        public IReadOnlyList<VirtualCameraEntry> Entries { get; }
    }

    internal static class VirtualCameraSourceResolver
    {
        internal const string BuiltinPrefix = "builtin://vision/";

        internal static bool IsBuiltinUri(string sourcePath)
        {
            return !string.IsNullOrWhiteSpace(sourcePath)
                && sourcePath.StartsWith(BuiltinPrefix, StringComparison.OrdinalIgnoreCase);
        }

        internal static VirtualCameraSource Resolve(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                throw new InvalidOperationException(
                    "VirtualCamera source '<empty>' is required.");
            }

            if (IsBuiltinUri(sourcePath))
            {
                return ResolveBuiltin(sourcePath);
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(sourcePath);
            }
            catch (Exception exception) when (IsExpectedSourceResolutionFailure(exception))
            {
                throw WrapSourceFailure(sourcePath, exception);
            }

            if (File.Exists(fullPath))
            {
                if (!IsSupportedImagePath(fullPath))
                {
                    throw new InvalidOperationException(
                        $"VirtualCamera source '{fullPath}' is not a supported image file.");
                }

                return new VirtualCameraSource(
                    Path.GetDirectoryName(fullPath) ?? fullPath,
                    isBuiltin: false,
                    new[] { new VirtualCameraEntry(0, fullPath, null) });
            }

            if (Directory.Exists(fullPath))
            {
                string[] imagePaths;
                try
                {
                    imagePaths = Directory
                        .EnumerateFiles(fullPath)
                        .Where(IsSupportedImagePath)
                        .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                        .ThenBy(path => Path.GetFileName(path), StringComparer.Ordinal)
                        .ToArray();
                }
                catch (Exception exception) when (IsExpectedSourceResolutionFailure(exception))
                {
                    throw WrapSourceFailure(fullPath, exception);
                }

                if (imagePaths.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"VirtualCamera source '{fullPath}' contains no supported images.");
                }

                return new VirtualCameraSource(
                    fullPath,
                    isBuiltin: false,
                    imagePaths
                        .Select((path, ordinal) => new VirtualCameraEntry(ordinal, path, null))
                        .ToArray());
            }

            throw new InvalidOperationException(
                $"VirtualCamera source '{fullPath}' does not exist.");
        }

        private static VirtualCameraSource ResolveBuiltin(string sourcePath)
        {
            var asset = sourcePath.Substring(BuiltinPrefix.Length);
            if (string.Equals(asset, "sample-set", StringComparison.OrdinalIgnoreCase))
            {
                return CreateBuiltinCollection();
            }

            if (string.Equals(asset, "sample-set/checkerboard", StringComparison.OrdinalIgnoreCase))
            {
                return CreateBuiltinSingle(
                    "builtin://vision/sample-set/checkerboard",
                    CreateCheckerboardImage);
            }

            if (string.Equals(asset, "sample-set/color-bars", StringComparison.OrdinalIgnoreCase))
            {
                return CreateBuiltinSingle(
                    "builtin://vision/sample-set/color-bars",
                    CreateColorBarsImage);
            }

            throw new InvalidOperationException(
                $"VirtualCamera builtin source '{sourcePath}' is unknown.");
        }

        private static VirtualCameraSource CreateBuiltinCollection()
        {
            return new VirtualCameraSource(
                "builtin://vision/sample-set",
                isBuiltin: true,
                new[]
                {
                    new VirtualCameraEntry(
                        0,
                        "builtin://vision/sample-set/checkerboard",
                        CreateCheckerboardImage()),
                    new VirtualCameraEntry(
                        1,
                        "builtin://vision/sample-set/color-bars",
                        CreateColorBarsImage()),
                });
        }

        private static VirtualCameraSource CreateBuiltinSingle(
            string path,
            Func<VirtualCameraImageTemplate> imageFactory)
        {
            return new VirtualCameraSource(
                "builtin://vision/sample-set",
                isBuiltin: true,
                new[] { new VirtualCameraEntry(0, path, imageFactory()) });
        }

        private static VirtualCameraImageTemplate CreateCheckerboardImage()
        {
            return new VirtualCameraImageTemplate(
                2,
                2,
                6,
                FlowPixelFormat.Bgr24,
                FlowImageKind.Color,
                new byte[]
                {
                    255, 255, 255, 0, 0, 0,
                    0, 0, 0, 255, 255, 255,
                });
        }

        private static VirtualCameraImageTemplate CreateColorBarsImage()
        {
            return new VirtualCameraImageTemplate(
                3,
                1,
                9,
                FlowPixelFormat.Bgr24,
                FlowImageKind.Color,
                new byte[]
                {
                    255, 0, 0, 0, 255, 0, 0, 0, 255,
                });
        }

        private static bool IsSupportedImagePath(string path)
        {
            var extension = Path.GetExtension(path);
            return string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".bmp", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsExpectedSourceResolutionFailure(Exception exception)
        {
            return exception is ArgumentException
                || exception is NotSupportedException
                || exception is PathTooLongException
                || exception is UnauthorizedAccessException
                || exception is SecurityException
                || exception is IOException;
        }

        private static InvalidOperationException WrapSourceFailure(
            string sourceLabel,
            Exception exception)
        {
            return new InvalidOperationException(
                $"VirtualCamera source '{sourceLabel}' could not be resolved.",
                exception);
        }
    }
}
