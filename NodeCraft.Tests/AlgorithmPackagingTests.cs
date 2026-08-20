using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

internal static partial class Program
{
    private static async Task RunAlgorithmPackagingTestsAsync()
    {
        Run("Algorithm runtime manifest lists only private native dependencies", () =>
        {
            var manifestPath = FindRepositoryFile(
                "Node.Algorithm",
                "Build",
                "WaybillRuntimeFiles.txt");
            var files = File.ReadAllLines(manifestPath)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim())
                .ToArray();
            var expected = new[]
            {
                "waybill_infer.dll",
                "onnxruntime.dll",
                "msvcp140.dll",
                "msvcp140_1.dll",
                "msvcp140_2.dll",
                "msvcp140_atomic_wait.dll",
                "msvcp140_codecvt_ids.dll",
                "vcruntime140.dll",
                "vcruntime140_1.dll",
            };

            return files.SequenceEqual(files.Distinct(StringComparer.OrdinalIgnoreCase), StringComparer.Ordinal)
                && files.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .SequenceEqual(expected.OrderBy(value => value, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase)
                && !files.Any(value => value.Contains("NodeCraft", StringComparison.OrdinalIgnoreCase))
                && !files.Any(value => value.Contains("CommonControls", StringComparison.OrdinalIgnoreCase));
        });

        Run("Algorithm packaging target declares explicit staging properties", () =>
        {
            var targetPath = FindRepositoryFile(
                "Node.Algorithm",
                "Build",
                "AlgorithmPackaging.targets");
            var projectPath = FindRepositoryFile(
                "Node.Algorithm",
                "Node.Algorithm.csproj");
            var target = File.ReadAllText(targetPath);
            var project = File.ReadAllText(projectPath);
            return target.Contains("Name=\"StageAlgorithmPlugin\"", StringComparison.Ordinal)
                && target.Contains("DependsOnTargets=\"Build\"", StringComparison.Ordinal)
                && target.Contains("AlgorithmPackageRoot", StringComparison.Ordinal)
                && target.Contains("WaybillSourceRoot", StringComparison.Ordinal)
                && target.Contains("WaybillRuntimeRoot", StringComparison.Ordinal)
                && target.Contains("WaybillOpenCvRuntimeRoot", StringComparison.Ordinal)
                && target.Contains("WaybillModelPath", StringComparison.Ordinal)
                && target.Contains("RemoveDir Directories=\"$(AlgorithmPackageRoot)\"", StringComparison.Ordinal)
                && target.Contains("opencv_world4110.dll", StringComparison.Ordinal)
                && target.Contains("_MissingWaybill", StringComparison.Ordinal)
                && project.Contains("AlgorithmPackaging.targets", StringComparison.Ordinal);
        });

        await RunAsync("Algorithm staging copies the complete package layout", async () =>
        {
            using var root = new TemporaryDirectory("nodecraft-algorithm-stage-");
            var runtimeRoot = Path.Combine(root.Path, "runtime");
            var openCvRoot = Path.Combine(root.Path, "opencv");
            var packageRoot = Path.Combine(root.Path, "package");
            var modelPath = Path.Combine(root.Path, "model.onnx");
            Directory.CreateDirectory(runtimeRoot);
            Directory.CreateDirectory(openCvRoot);
            File.WriteAllBytes(modelPath, new byte[] { 1, 2, 3 });
            File.WriteAllBytes(
                Path.Combine(openCvRoot, "opencv_world4110.dll"),
                new byte[] { 4 });

            var runtimeManifest = FindRepositoryFile(
                "Node.Algorithm",
                "Build",
                "WaybillRuntimeFiles.txt");
            var runtimeFiles = File.ReadAllLines(runtimeManifest)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim())
                .ToArray();
            foreach (var fileName in runtimeFiles)
            {
                File.WriteAllBytes(Path.Combine(runtimeRoot, fileName), new byte[] { 5 });
            }

            var projectPath = FindRepositoryFile(
                "Node.Algorithm",
                "Node.Algorithm.csproj");
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = FindRepositoryRoot(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("msbuild");
            startInfo.ArgumentList.Add(projectPath);
            startInfo.ArgumentList.Add("-t:StageAlgorithmPlugin");
            startInfo.ArgumentList.Add("-p:Configuration=Release");
            startInfo.ArgumentList.Add("-p:AlgorithmPackageRoot=" + packageRoot);
            startInfo.ArgumentList.Add("-p:WaybillRuntimeRoot=" + runtimeRoot);
            startInfo.ArgumentList.Add("-p:WaybillOpenCvRuntimeRoot=" + openCvRoot);
            startInfo.ArgumentList.Add("-p:WaybillModelPath=" + modelPath);

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return false;
            }

            await process.WaitForExitAsync().ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                return false;
            }

            return File.Exists(Path.Combine(packageRoot, "plugin.json"))
                && File.Exists(Path.Combine(packageRoot, "Node.Algorithm.dll"))
                && File.Exists(Path.Combine(packageRoot, "models", "baseline-2-960.onnx"))
                && File.Exists(Path.Combine(packageRoot, "lib", "opencv_world4110.dll"))
                && runtimeFiles.All(fileName =>
                    File.Exists(Path.Combine(packageRoot, "lib", fileName)));
        });
    }

    private static string FindRepositoryRoot()
    {
        var projectPath = FindRepositoryFile("Node.Algorithm", "Node.Algorithm.csproj");
        return Directory.GetParent(Path.GetDirectoryName(projectPath)!)!.FullName;
    }
}
