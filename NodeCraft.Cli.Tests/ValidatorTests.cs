using NodeCraft.Cli;

namespace NodeCraft.Cli.Tests
{
    internal static class ValidatorTests
    {
        public static void RunAll()
        {
            Program.Run("plugin id accepts company.namespaced ids", () =>
                PluginIdValidator.ValidatePluginId("company.sample.nodes") == null);

            Program.Run("plugin id rejects whitespace", () =>
                PluginIdValidator.ValidatePluginId("company sample nodes") != null);

            Program.Run("plugin id rejects empty", () =>
                PluginIdValidator.ValidatePluginId("") != null);

            Program.Run("plugin id rejects null", () =>
                PluginIdValidator.ValidatePluginId(null!) != null);

            Program.Run("plugin id rejects leading dot", () =>
                PluginIdValidator.ValidatePluginId(".company.nodes") != null);

            Program.Run("plugin id rejects trailing dot", () =>
                PluginIdValidator.ValidatePluginId("company.nodes.") != null);

            Program.Run("plugin id accepts digits and dots", () =>
                PluginIdValidator.ValidatePluginId("company2.nodes.v1") == null);

            Program.Run("plugin id rejects non-alphanumeric segment", () =>
                PluginIdValidator.ValidatePluginId("company-node") != null);

            Program.Run("project name rejects illegal path chars", () =>
                PluginIdValidator.ValidateProjectName("bad/name") != null);

            Program.Run("project name rejects backslash", () =>
                PluginIdValidator.ValidateProjectName("bad\\name") != null);

            Program.Run("project name rejects dots", () =>
                PluginIdValidator.ValidateProjectName("My.Plugin") != null);

            Program.Run("project name rejects spaces", () =>
                PluginIdValidator.ValidateProjectName("My Plugin") != null);

            Program.Run("project name rejects hyphen", () =>
                PluginIdValidator.ValidateProjectName("My-Plugin") != null);

            Program.Run("project name rejects digit start", () =>
                PluginIdValidator.ValidateProjectName("1MyPlugin") != null);

            Program.Run("project name accepts underscores", () =>
                PluginIdValidator.ValidateProjectName("My_Plugin") == null);

            Program.Run("project name accepts plain names", () =>
                PluginIdValidator.ValidateProjectName("MyPlugin") == null);

            Program.Run("project name rejects empty", () =>
                PluginIdValidator.ValidateProjectName("") != null);
        }
    }
}
