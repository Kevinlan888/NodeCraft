using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;

internal static partial class Program
{
    private static void RunVisionProjectTests()
    {
        Run("Vision project has the requested Windows x64 identity", () =>
        {
            var projectPath = FindRepositoryFile(
                "NodeCraft.Vision",
                "NodeCraft.Vision.csproj");
            var projectText = File.ReadAllText(projectPath);
            var project = XDocument.Load(projectPath);
            var propertyGroup = project.Root?.Elements("PropertyGroup").FirstOrDefault();
            var projectReference = project
                .Descendants("ProjectReference")
                .SingleOrDefault();

            return string.Equals((string?)propertyGroup?.Element("TargetFramework"), "net8.0-windows", StringComparison.OrdinalIgnoreCase)
                && string.Equals((string?)propertyGroup?.Element("UseWPF"), "true", StringComparison.OrdinalIgnoreCase)
                && string.Equals((string?)propertyGroup?.Element("LangVersion"), "9.0", StringComparison.Ordinal)
                && string.Equals((string?)propertyGroup?.Element("PlatformTarget"), "x64", StringComparison.OrdinalIgnoreCase)
                && string.Equals((string?)propertyGroup?.Element("Prefer32Bit"), "false", StringComparison.OrdinalIgnoreCase)
                && string.Equals((string?)projectReference?.Attribute("Private"), "false", StringComparison.OrdinalIgnoreCase)
                && projectReference?.Attribute("Include")?.Value.EndsWith("NodeCraft.Flow.csproj", StringComparison.OrdinalIgnoreCase) == true
                && string.Equals((string?)propertyGroup?.Element("RootNamespace"), "NodeCraft.Vision", StringComparison.Ordinal)
                && string.Equals((string?)propertyGroup?.Element("AssemblyName"), "NodeCraft.Vision", StringComparison.Ordinal)
                && !projectText.Contains("StereoCamera.Net", StringComparison.Ordinal)
                && !projectText.Contains("System.Drawing.Common", StringComparison.Ordinal);
        });

        Run("Vision manifest has the new plugin identity", () =>
        {
            var manifestPath = FindRepositoryFile(
                "NodeCraft.Vision",
                "plugin.json");
            var json = File.ReadAllText(manifestPath);
            using var manifest = JsonDocument.Parse(json);
            var root = manifest.RootElement;
            return json.Contains("\"id\": \"nodecraft.vision\"", StringComparison.Ordinal)
                && json.Contains("\"entryAssembly\": \"NodeCraft.Vision.dll\"", StringComparison.Ordinal)
                && json.Contains("\"entryType\": \"NodeCraft.Vision.Plugin.VisionPlugin\"", StringComparison.Ordinal)
                && json.Contains("\"apiVersion\": \"1.0\"", StringComparison.Ordinal)
                && json.Contains("\"privateLibraryPath\": \"lib\"", StringComparison.Ordinal)
                && root.GetProperty("id").GetString() == "nodecraft.vision";
        });
    }
}
