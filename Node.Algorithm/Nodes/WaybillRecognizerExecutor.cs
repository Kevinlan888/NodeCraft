using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Node.Algorithm.Imaging;
using Node.Algorithm.Interop;
using NodeCraft.Flow;

namespace Node.Algorithm.Nodes
{
    internal sealed class WaybillRecognizerExecutor : IFlowNodeExecutor, IFlowNodeSessionLifecycle
    {
        private readonly IWaybillInferenceSessionFactory _sessionFactory;
        private readonly string _pluginAssemblyPath;
        private readonly ILogger _logger;
        private IWaybillInferenceSession _session;

        internal WaybillRecognizerExecutor(
            IWaybillInferenceSessionFactory sessionFactory,
            string pluginAssemblyPath,
            ILogger logger = null)
        {
            _sessionFactory = sessionFactory
                ?? throw new ArgumentNullException(nameof(sessionFactory));
            if (string.IsNullOrWhiteSpace(pluginAssemblyPath))
            {
                throw new ArgumentException(
                    "A plugin assembly path is required.",
                    nameof(pluginAssemblyPath));
            }

            _pluginAssemblyPath = Path.GetFullPath(pluginAssemblyPath);
            _logger = logger ?? NullLogger.Instance;
        }

        public Task StartSessionAsync(
            FlowNodeSessionContext context,
            CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (_session != null)
            {
                return Task.CompletedTask;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var configuration = WaybillRecognizerConfiguration.Read(context.Node);
            if (string.IsNullOrWhiteSpace(configuration.ModelPath))
            {
                throw new InvalidOperationException("Waybill recognizer requires a model path.");
            }

            var pluginDirectory = Path.GetDirectoryName(_pluginAssemblyPath)
                ?? throw new InvalidOperationException("Waybill plugin assembly has no containing directory.");
            var modelPath = Path.IsPathRooted(configuration.ModelPath)
                ? Path.GetFullPath(configuration.ModelPath)
                : Path.GetFullPath(Path.Combine(pluginDirectory, configuration.ModelPath));
            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException(
                    $"Waybill model file was not found: {modelPath}",
                    modelPath);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var session = _sessionFactory.Create(
                _pluginAssemblyPath,
                modelPath,
                configuration.Options);
            if (session == null)
            {
                throw new InvalidOperationException(
                    "Waybill inference session factory returned null.");
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _session = session;
            }
            catch
            {
                session.Dispose();
                throw;
            }

            return Task.CompletedTask;
        }

        public Task StopSessionAsync(
            FlowNodeSessionContext context,
            CancellationToken cancellationToken)
        {
            var session = _session;
            _session = null;
            session?.Dispose();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (inputs == null)
            {
                throw new ArgumentNullException(nameof(inputs));
            }

            cancellationToken.ThrowIfCancellationRequested();
            var session = _session
                ?? throw new InvalidOperationException(
                    "Waybill recognizer session has not started.");
            if (!inputs.TryGetValue("image", out var value) || !(value is FlowImage image))
            {
                throw new InvalidOperationException(
                    "Waybill recognizer requires a FlowImage input named 'image'.");
            }

            var result = session.Process(image, cancellationToken)
                ?? throw new InvalidOperationException(
                    "Waybill inference session returned no result.");
            IReadOnlyDictionary<string, object> outputs = new Dictionary<string, object>
            {
                ["count"] = result.Detections.Count,
                ["detections"] = result.Detections,
                ["annotatedImage"] = WaybillOverlayRenderer.Render(image, result.Detections),
            };
            return Task.FromResult(outputs);
        }
    }
}
