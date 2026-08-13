using System.Linq;

namespace NodeCraft.Cli
{
    public static class PluginIdValidator
    {
        /// <summary>
        /// Returns an error message, or null when the id is valid.
        /// Rules mirror the host PluginMetadata.ValidateId: non-empty, no whitespace,
        /// plus a dotted-segment shape (alphanumeric segments separated by dots).
        /// </summary>
        public static string ValidatePluginId(string pluginId)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                return "Plugin ID must be non-empty.";
            }

            if (pluginId.Any(char.IsWhiteSpace))
            {
                return "Plugin ID must not contain whitespace.";
            }

            var segments = pluginId.Split('.');
            if (segments.Any(segment => string.IsNullOrEmpty(segment)
                || segment.Any(character => !char.IsLetterOrDigit(character))))
            {
                return "Plugin ID segments must be non-empty and contain only letters and digits.";
            }

            return null;
        }

        /// <summary>
        /// Returns an error message, or null when the project name is a valid
        /// single-directory, C# identifier-style name (letters, digits,
        /// underscores; letter-first).
        /// </summary>
        public static string ValidateProjectName(string projectName)
        {
            if (string.IsNullOrWhiteSpace(projectName))
            {
                return "Project name must be non-empty.";
            }

            if (projectName.IndexOfAny(new[] { '/', '\\' }) >= 0)
            {
                return "Project name must not contain path separators.";
            }

            if (!projectName.All(character => char.IsLetterOrDigit(character) || character == '_'))
            {
                return "Project name may contain only letters, digits and underscores.";
            }

            if (!char.IsLetter(projectName[0]))
            {
                return "Project name must start with a letter.";
            }

            return null;
        }
    }
}
