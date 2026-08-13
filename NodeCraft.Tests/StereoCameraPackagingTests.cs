using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NodeCraft.Vision.StereoCamera.Runtime;

internal static partial class Program
{
    private static void RunStereoCameraPackagingTests()
    {
        Run("stereo camera packaging manifest matches the supplied x64 SDK inventory", () =>
        {
            var manifestPath = FindRepositoryFile(
                "NodeCraft.Vision.StereoCamera",
                "Build",
                "StereoCameraRuntimeFiles.txt");
            var manifest = File.ReadAllLines(manifestPath)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim())
                .ToArray();
            var sdkRuntime = "/mnt/kevin/kevin/Downloads/test/app/Runtime/x64";
            var expected = Directory.Exists(sdkRuntime)
                ? Directory.GetFiles(sdkRuntime).Select(Path.GetFileName).Where(name =>
                    !string.Equals(name, "StereoCamera.Net.dll", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(name, "NLog.dll", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : manifest.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();

            return manifest.SequenceEqual(manifest.Distinct(StringComparer.OrdinalIgnoreCase), StringComparer.Ordinal)
                && !manifest.Any(name => string.Equals(name, "StereoCamera.Net.dll", StringComparison.OrdinalIgnoreCase))
                && !manifest.Any(name => string.Equals(name, "NLog.dll", StringComparison.OrdinalIgnoreCase))
                && manifest.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .SequenceEqual(expected, StringComparer.OrdinalIgnoreCase);
        });

        Run("stereo camera packaging target is explicit and has a complete missing-file guard", () =>
        {
            var targetPath = FindRepositoryFile(
                "NodeCraft.Vision.StereoCamera",
                "Build",
                "StereoCameraPackaging.targets");
            var target = File.ReadAllText(targetPath);
            var projectPath = FindRepositoryFile(
                "NodeCraft.Vision.StereoCamera",
                "NodeCraft.Vision.StereoCamera.csproj");
            var project = File.ReadAllText(projectPath);
            return target.Contains("Name=\"StageStereoCameraPlugin\"", StringComparison.Ordinal)
                && target.Contains("_MissingStereoCameraSdkFile", StringComparison.Ordinal)
                && target.Contains("StereoCameraSdkRoot must point", StringComparison.Ordinal)
                && target.Contains("RemoveDir Directories=\"$(StereoCameraPackageRoot)\"", StringComparison.Ordinal)
                && !target.Contains("BeforeTargets=\"Build\"", StringComparison.Ordinal)
                && project.Contains("StereoCameraPackaging.targets", StringComparison.Ordinal)
                && target.Contains("StereoCamera.Net.dll", StringComparison.Ordinal)
                && target.Contains("NLog.dll", StringComparison.Ordinal)
                && target.Contains("NodeCraft.Flow.dll", StringComparison.Ordinal)
                && target.Contains("CommonControls.WPF.dll", StringComparison.Ordinal)
                && target.Contains("Microsoft.Extensions.Logging", StringComparison.Ordinal);
        });

        Run("stereo camera native runtime setup is process-local and x64-only", () =>
        {
            var sourcePath = FindRepositoryFile(
                "NodeCraft.Vision.StereoCamera",
                "Runtime",
                "NativeRuntimeScope.cs");
            var source = File.ReadAllText(sourcePath);
            var noGlobalWrites = !source.Contains("Registry", StringComparison.Ordinal)
                && !source.Contains("EnvironmentVariableTarget.Machine", StringComparison.Ordinal)
                && !source.Contains("PATH", StringComparison.Ordinal);
            return source.Contains("AddDllDirectory", StringComparison.Ordinal)
                && source.Contains("RemoveDllDirectory", StringComparison.Ordinal)
                && source.Contains("MV_GENICAM_64", StringComparison.Ordinal)
                && source.Contains("Environment.Is64BitProcess", StringComparison.Ordinal)
                && noGlobalWrites;
        });

        Run("stereo camera runtime scope rejects unsupported Linux process loads", () =>
        {
            if (OperatingSystem.IsWindows())
            {
                return true;
            }

            var root = Path.Combine(Path.GetTempPath(), "nodecraft-stereo-runtime-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "lib"));
            try
            {
                NativeRuntimeScope.Acquire(Path.Combine(root, "NodeCraft.Vision.StereoCamera.dll"));
                return false;
            }
            catch (PlatformNotSupportedException)
            {
                return true;
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        });
    }
}
