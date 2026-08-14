using System;
using NodeCraft.Vision.Camera;

namespace NodeCraft.Vision.Runtime
{
    internal sealed class ProductionVisionCameraRuntimeScopeFactory : ICameraRuntimeScopeFactory
    {
        private readonly string _pluginAssemblyPath;

        internal ProductionVisionCameraRuntimeScopeFactory(string pluginAssemblyPath)
        {
            _pluginAssemblyPath = pluginAssemblyPath ?? throw new ArgumentNullException(nameof(pluginAssemblyPath));
        }

        public IDisposable Acquire()
        {
            return NativeRuntimeScope.Acquire(_pluginAssemblyPath);
        }
    }
}
