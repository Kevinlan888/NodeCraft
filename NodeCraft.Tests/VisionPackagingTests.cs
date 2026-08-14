using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NodeCraft.Vision.Runtime;

internal static partial class Program
{
    private static void RunVisionPackagingTests()
    {
        Run("Vision packaging manifest matches the supplied x64 SDK inventory", () =>
        {
            var manifestPath = FindRepositoryFile(
                "NodeCraft.Vision",
                "Build",
                "VisionRuntimeFiles.txt");
            var manifest = File.ReadAllLines(manifestPath)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim())
                .ToArray();
            var expected = new[]
            {
                "CamUpgradeModule.dll",
                "CLAllSerial_MD_VC120_v3_0.dll",
                "CLProtocol_MD_VC120_v3_0.dll",
                "CLSerCOM.dll",
                "LibStereoCamera.dll",
                "clserVsp.dll",
                "compress_decode.dll",
                "DeCompressFile.dll",
                "GCBase_MD_VC120_v3_0.dll",
                "GenApi_MD_VC120_v3_0.dll",
                "GenCP_MD_VC120_v3_0.dll",
                "iImageProcessing64.dll",
                "ImageConvert.dll",
                "ImageSave.dll",
                "Log_MD_VC120_v3_0.dll",
                "log4cpp_MD_VC120_v3_0.dll",
                "MathParser_MD_VC120_v3_0.dll",
                "MVlog4cppmd.dll",
                "MVProducerGEV.cti",
                "MVProducerCXP.cti",
                "MVProducerU3V.cti",
                "MVSDKmd.dll",
                "MvsShowAerailView64.dll",
                "NodeMapData_MD_VC120_v3_0.dll",
                "SDKLOG_default.properties",
                "TinyXmlmd.dll",
                "VideoRender.dll",
                "XmlParser_MD_VC120_v3_0.dll",
                "oxylog.dll",
            };

            return manifest.Length == expected.Length
                && manifest.SequenceEqual(manifest.Distinct(StringComparer.OrdinalIgnoreCase), StringComparer.Ordinal)
                && !manifest.Any(name => string.Equals(name, "NLog.dll", StringComparison.OrdinalIgnoreCase))
                && manifest.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .SequenceEqual(expected.OrderBy(name => name, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        });

        Run("Vision packaging target is explicit and has a complete missing-file guard", () =>
        {
            var targetPath = FindRepositoryFile(
                "NodeCraft.Vision",
                "Build",
                "VisionPackaging.targets");
            var target = File.ReadAllText(targetPath);
            var projectPath = FindRepositoryFile(
                "NodeCraft.Vision",
                "NodeCraft.Vision.csproj");
            var project = File.ReadAllText(projectPath);
            return target.Contains("Name=\"StageVisionPlugin\"", StringComparison.Ordinal)
                && target.Contains("_MissingVisionSdkFile", StringComparison.Ordinal)
                && target.Contains("VisionSdkRoot must point", StringComparison.Ordinal)
                && target.Contains("StereoCameraSdkRoot must point", StringComparison.Ordinal)
                && target.Contains("_StereoRuntimeSource", StringComparison.Ordinal)
                && target.Contains("_StereoRootRuntimeSource", StringComparison.Ordinal)
                && target.Contains("RemoveDir Directories=\"$(VisionPackageRoot)\"", StringComparison.Ordinal)
                && !target.Contains("BeforeTargets=\"Build\"", StringComparison.Ordinal)
                && project.Contains("VisionPackaging.targets", StringComparison.Ordinal)
                && target.Contains("StereoCamera.Net.dll", StringComparison.Ordinal)
                && target.Contains("VisionRuntimeFiles.txt", StringComparison.Ordinal)
                && target.Contains("NLog.dll", StringComparison.Ordinal)
                && target.Contains("NodeCraft.Flow.dll", StringComparison.Ordinal)
                && target.Contains("CommonControls.WPF.dll", StringComparison.Ordinal)
                && target.Contains("Microsoft.Extensions.Logging", StringComparison.Ordinal);
        });

        Run("Vision native runtime setup is process-local and x64-only", () =>
        {
            var sourcePath = FindRepositoryFile(
                "NodeCraft.Vision",
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

        Run("Vision runtime scope rejects unsupported Linux process loads", () =>
        {
            if (OperatingSystem.IsWindows())
            {
                return true;
            }

            var root = Path.Combine(Path.GetTempPath(), "nodecraft-vision-runtime-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "lib"));
            try
            {
                NativeRuntimeScope.Acquire(Path.Combine(root, "NodeCraft.Vision.dll"));
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
