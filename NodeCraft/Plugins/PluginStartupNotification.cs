using System;
using System.Collections.Generic;
using System.Linq;

namespace NodeCraft.Plugins
{
    public static class PluginStartupNotification
    {
        public static string BuildMessage(IReadOnlyList<PluginLoadResult> failures)
        {
            if (failures == null)
            {
                throw new ArgumentNullException(nameof(failures));
            }

            if (failures.Count == 0)
            {
                return string.Empty;
            }

            var summary = string.Join(
                "; ",
                failures.Select(failure => $"{failure.PluginId} ({failure.Phase})"));
            var noun = failures.Count == 1 ? "plugin" : "plugins";
            return $"{failures.Count} {noun} failed to load: {summary}. See the NodeCraft log for details.";
        }
    }
}
