using System;
using System.IO;
using System.Linq;
using NodeCraft.Cli;

namespace NodeCraft.Cli.Tests
{
    internal static class QuestionnaireTests
    {
        private static Questionnaire Create(string input)
        {
            return new Questionnaire(new StringReader(input), new StringWriter());
        }

        public static void RunAll()
        {
            Program.Run("ask string accepts default on empty line", () =>
            {
                var q = Create("\n");
                return q.AskString("Plugin ID", "company.default.nodes", _ => "bad") == "company.default.nodes";
            });

            Program.Run("ask string re-prompts on validation error", () =>
            {
                var q = Create("\ncompany.ok.nodes\n");
                return q.AskString("Plugin ID", null, value =>
                    value == "company.ok.nodes" ? null : "invalid") == "company.ok.nodes";
            });

            Program.Run("ask string trims input", () =>
            {
                var q = Create("  spaced name  \n");
                return q.AskString("Display name", null, _ => null) == "spaced name";
            });

            Program.Run("ask string returns null on end of input", () =>
            {
                var q = Create("");
                return q.AskString("Plugin ID", null, _ => null) == null;
            });

            Program.Run("ask confirm defaults to no", () =>
            {
                var q = Create("\n");
                return q.AskConfirm("Overwrite?", false) == false;
            });

            Program.Run("ask confirm accepts y", () =>
            {
                var q = Create("y\n");
                return q.AskConfirm("Overwrite?", false) == true;
            });

            Program.Run("ask confirm re-prompts on garbage", () =>
            {
                var q = Create("maybe\nn\n");
                return q.AskConfirm("Overwrite?", false) == false;
            });

            Program.Run("ask confirm accepts uppercase y", () =>
            {
                var q = Create("Y\n");
                return q.AskConfirm("Overwrite?", false) == true;
            });

            Program.Run("ask confirm returns default on end of input", () =>
            {
                var q = Create("");
                return q.AskConfirm("Overwrite?", false) == false;
            });

            Program.Run("select features returns empty selection", () =>
            {
                var q = Create("\n");
                var features = q.SelectFeatures(new[] { "Custom UI", "Private dependency" });
                return features.Count == 0;
            });

            Program.Run("select features toggles by index", () =>
            {
                var q = Create("1\n2\n\n");
                var features = q.SelectFeatures(new[] { "Custom UI", "Private dependency" });
                return features.Count == 2
                    && features.Contains(0)
                    && features.Contains(1);
            });

            Program.Run("select features supports select all", () =>
            {
                var q = Create("a\n\n");
                var features = q.SelectFeatures(new[] { "Custom UI", "Private dependency" });
                return features.Count == 2;
            });

            Program.Run("select features toggles multiple indices in one line", () =>
            {
                var q = Create("1 2\n\n");
                var features = q.SelectFeatures(new[] { "Custom UI", "Private dependency" });
                return features.Count == 2 && features.Contains(0) && features.Contains(1);
            });

            Program.Run("select features supports all minus one and clear", () =>
            {
                var q = Create("a\n1\n\n");
                var features = q.SelectFeatures(new[] { "Custom UI", "Private dependency" });
                return features.Count == 1 && features.Contains(1);
            });

            Program.Run("select features clears with n", () =>
            {
                var q = Create("1\nn\n\n");
                var features = q.SelectFeatures(new[] { "Custom UI", "Private dependency" });
                return features.Count == 0;
            });

            Program.Run("select features reports unknown selection", () =>
            {
                var output = new StringWriter();
                var q = new Questionnaire(new StringReader("9\n\n"), output);
                q.SelectFeatures(new[] { "Custom UI", "Private dependency" });
                return output.ToString().Contains("Error: unknown selection '9'.");
            });
        }
    }
}
