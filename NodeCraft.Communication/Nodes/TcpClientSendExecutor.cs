using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NodeCraft.Communication.Transport;
using NodeCraft.Flow;

namespace NodeCraft.Communication.Nodes
{
    internal sealed class TcpClientSendExecutor : IFlowNodeExecutor, IFlowNodeSessionLifecycle
    {
        private readonly ITcpClientConnectionFactory _connectionFactory;
        private readonly ILogger _logger;
        private ITcpClientConnection _connection;
        private bool _stopOnSendFailure = true;

        internal TcpClientSendExecutor(
            ITcpClientConnectionFactory connectionFactory,
            ILogger logger = null)
        {
            _connectionFactory = connectionFactory
                ?? throw new ArgumentNullException(nameof(connectionFactory));
            _logger = logger ?? NullLogger.Instance;
        }

        public async Task StartSessionAsync(
            FlowNodeSessionContext context,
            CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (_connection != null)
            {
                throw new InvalidOperationException("TCP client session has already started.");
            }

            var settings = ReadSettings(context.Node.Inputs);
            var connection = _connectionFactory.Create()
                ?? throw new InvalidOperationException("TCP connection factory returned null.");

            try
            {
                await connection.ConnectAsync(
                        settings.Host,
                        settings.Port,
                        TimeSpan.FromMilliseconds(settings.ConnectTimeoutMilliseconds),
                        cancellationToken)
                    .ConfigureAwait(false);
                _stopOnSendFailure = settings.StopOnSendFailure;
                _connection = connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        public Task StopSessionAsync(
            FlowNodeSessionContext context,
            CancellationToken cancellationToken)
        {
            var connection = _connection;
            _connection = null;
            _stopOnSendFailure = true;
            connection?.Dispose();
            return Task.CompletedTask;
        }

        public async Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
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

            var connection = _connection
                ?? throw new InvalidOperationException("TCP client session has not started.");

            foreach (var inputPort in definition.InputPorts.Where(port => port != null && port.IsDynamic))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!inputs.TryGetValue(inputPort.Id, out var value))
                {
                    throw new InvalidOperationException(
                        $"Required TCP input '{inputPort.Id}' was not provided for node '{node.Id}'.");
                }

                var payload = TcpPayloadEncoder.Encode(value, inputPort.Id);
                try
                {
                    await connection.SendAsync(payload, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        exception,
                        "TCP send failed for node '{NodeId}' input '{InputId}'; "
                            + "{Outcome}.",
                        node.Id,
                        inputPort.Id,
                        _stopOnSendFailure
                            ? "execution terminated"
                            : "payload discarded and execution continued");

                    if (_stopOnSendFailure)
                    {
                        throw;
                    }
                }
            }

            return new Dictionary<string, object>();
        }

        private static TcpClientSendSettings ReadSettings(
            IReadOnlyDictionary<string, object> inputs)
        {
            if (inputs == null)
            {
                throw new ArgumentNullException(nameof(inputs));
            }

            var host = ReadRequiredString(inputs, "host");
            var port = ReadInteger(inputs, "port", required: true, defaultValue: 0);
            var timeout = ReadInteger(
                inputs,
                "connectTimeoutMilliseconds",
                required: false,
                defaultValue: 5000);
            var stopOnSendFailure = ReadBoolean(
                inputs,
                "stopOnSendFailure",
                defaultValue: true);

            if (port < 1 || port > 65535)
            {
                throw new InvalidOperationException("TCP port must be between 1 and 65535.");
            }

            if (timeout <= 0)
            {
                throw new InvalidOperationException(
                    "TCP connect timeout must be greater than zero milliseconds.");
            }

            return new TcpClientSendSettings(host, port, timeout, stopOnSendFailure);
        }

        private static string ReadRequiredString(
            IReadOnlyDictionary<string, object> inputs,
            string key)
        {
            if (!inputs.TryGetValue(key, out var value) || value == null)
            {
                throw new InvalidOperationException($"TCP setting '{key}' is required.");
            }

            var text = value as string ?? value.ToString();
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException($"TCP setting '{key}' is required.");
            }

            return text.Trim();
        }

        private static int ReadInteger(
            IReadOnlyDictionary<string, object> inputs,
            string key,
            bool required,
            int defaultValue)
        {
            if (!inputs.TryGetValue(key, out var value) || value == null)
            {
                if (!required)
                {
                    return defaultValue;
                }

                throw new InvalidOperationException($"TCP setting '{key}' is required.");
            }

            if (value is int integer)
            {
                return integer;
            }

            if (value is long longValue && longValue >= int.MinValue && longValue <= int.MaxValue)
            {
                return (int)longValue;
            }

            if (value is string text
                && int.TryParse(
                    text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsed))
            {
                return parsed;
            }

            throw new InvalidOperationException(
                $"TCP setting '{key}' must be an integer.");
        }

        private static bool ReadBoolean(
            IReadOnlyDictionary<string, object> inputs,
            string key,
            bool defaultValue)
        {
            if (!inputs.TryGetValue(key, out var value) || value == null)
            {
                return defaultValue;
            }

            if (value is bool boolean)
            {
                return boolean;
            }

            if (value is string text
                && bool.TryParse(text, out var parsed))
            {
                return parsed;
            }

            throw new InvalidOperationException(
                $"TCP setting '{key}' must be a boolean.");
        }

        private sealed class TcpClientSendSettings
        {
            public TcpClientSendSettings(
                string host,
                int port,
                int connectTimeoutMilliseconds,
                bool stopOnSendFailure)
            {
                Host = host;
                Port = port;
                ConnectTimeoutMilliseconds = connectTimeoutMilliseconds;
                StopOnSendFailure = stopOnSendFailure;
            }

            public string Host { get; }

            public int Port { get; }

            public int ConnectTimeoutMilliseconds { get; }

            public bool StopOnSendFailure { get; }
        }
    }
}
