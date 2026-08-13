using System;
using System.Runtime.Loader;

namespace NodeCraft.Plugins
{
    public sealed class PluginLoadResult
    {
        private PluginLoadResult(
            string pluginId,
            string phase,
            bool isSuccess,
            Exception exception,
            AssemblyLoadContext context)
        {
            PluginId = pluginId;
            Phase = phase;
            IsSuccess = isSuccess;
            Exception = exception;
            Context = context;
        }

        public string PluginId { get; }

        public string Phase { get; }

        public bool IsSuccess { get; }

        public Exception Exception { get; }

        public AssemblyLoadContext Context { get; }

        public static PluginLoadResult Succeeded(string pluginId, AssemblyLoadContext context)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                throw new ArgumentException("Plugin ID is required.", nameof(pluginId));
            }

            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            return new PluginLoadResult(pluginId, null, true, null, context);
        }

        public static PluginLoadResult Failed(string pluginId, string phase, Exception exception)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                throw new ArgumentException("Plugin ID is required.", nameof(pluginId));
            }

            if (string.IsNullOrWhiteSpace(phase))
            {
                throw new ArgumentException("Failure phase is required.", nameof(phase));
            }

            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            return new PluginLoadResult(pluginId, phase, false, exception, null);
        }
    }
}
