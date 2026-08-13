using System;
using System.Collections.Generic;
using System.Linq;

namespace NodeCraft.Plugins
{
    public sealed class PluginLoadReport
    {
        public PluginLoadReport(IEnumerable<PluginLoadResult> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            Results = results.ToList();
            if (Results.Any(result => result == null))
            {
                throw new ArgumentException("Plugin load results cannot contain null entries.", nameof(results));
            }

            Failures = Results
                .Where(result => !result.IsSuccess)
                .ToList();
        }

        public IReadOnlyList<PluginLoadResult> Results { get; }

        public IReadOnlyList<PluginLoadResult> Failures { get; }
    }
}
