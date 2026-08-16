using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NodeCraft.Communication.Nodes;
using NodeCraft.Communication.Plugin;
using NodeCraft.Communication.Transport;
using NodeCraft.Flow;

internal static partial class Program
{
    private static async Task RunCommunicationTestsAsync()
    {
        await RunAsync("Communication project exposes the plugin manifest", () =>
        {
            var projectPath = FindRepositoryFile(
                "NodeCraft.Communication",
                "NodeCraft.Communication.csproj");
            var manifestPath = FindRepositoryFile(
                "NodeCraft.Communication",
                "plugin.json");
            var manifest = File.ReadAllText(manifestPath);

            return Task.FromResult(
                File.Exists(projectPath)
                && manifest.Contains("nodecraft.communication", StringComparison.Ordinal)
                && manifest.Contains(
                    "NodeCraft.Communication.Plugin.CommunicationPlugin",
                    StringComparison.Ordinal));
        });

        Run("Communication plugin exposes stable metadata and TCP registration", () =>
        {
            var plugin = new CommunicationPlugin();
            var context = new PluginRegistrationContext(
                NullLogger.Instance,
                new Version(1, 0));
            plugin.Register(context);

            return plugin.Metadata.Id == "nodecraft.communication"
                && plugin.Metadata.DisplayName == "Communication"
                && plugin.Metadata.Version.Equals(new Version(1, 0, 0))
                && context.Registrations.Any(registration =>
                    registration.Definition.TypeKey
                        == "nodecraft.communication.tcp-client-send");
        });

        Run("TCP payload encoder preserves bytes and uses UTF-8 fallback", () =>
        {
            var raw = new byte[] { 0, 1, 255 };
            var text = TcpPayloadEncoder.Encode("你好", "message_1");
            var bytes = TcpPayloadEncoder.Encode(raw, "message_2");
            var number = TcpPayloadEncoder.Encode(42, "message_3");

            return text.SequenceEqual(Encoding.UTF8.GetBytes("你好"))
                && ReferenceEquals(raw, bytes)
                && number.SequenceEqual(Encoding.UTF8.GetBytes("42"));
        });

        Run("TCP payload encoder rejects null values with the input id", () =>
        {
            try
            {
                TcpPayloadEncoder.Encode(null, "message_2");
                return false;
            }
            catch (InvalidOperationException exception)
            {
                return exception.Message.Contains("message_2", StringComparison.Ordinal);
            }
        });

        await RunAsync("TCP connection sends a payload to a loopback listener", async () =>
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var expected = Encoding.UTF8.GetBytes("hello");
                var acceptTask = listener.AcceptTcpClientAsync();
                using var connection = new TcpClientConnection();
                await connection.ConnectAsync(
                    IPAddress.Loopback.ToString(),
                    ((IPEndPoint)listener.LocalEndpoint).Port,
                    TimeSpan.FromSeconds(1),
                    CancellationToken.None);

                using var server = await acceptTask;
                using var stream = server.GetStream();
                await connection.SendAsync(expected, CancellationToken.None);
                var actual = await ReadExactlyAsync(stream, expected.Length);
                return actual.SequenceEqual(expected);
            }
            finally
            {
                listener.Stop();
            }
        });

        await RunAsync("TCP connection observes a bounded connect timeout", async () =>
        {
            using var connection = new TcpClientConnection();
            var stopwatch = Stopwatch.StartNew();
            Exception error = null;
            try
            {
                await connection.ConnectAsync(
                    "192.0.2.1",
                    65000,
                    TimeSpan.FromMilliseconds(100),
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                error = exception;
            }

            stopwatch.Stop();
            return error != null && stopwatch.Elapsed < TimeSpan.FromSeconds(2);
        });

        Run("TCP Client Send exposes an unlimited required dynamic message template", () =>
        {
            var plugin = new CommunicationPlugin();
            var context = new PluginRegistrationContext(
                NullLogger.Instance,
                new Version(1, 0));
            plugin.Register(context);
            var registration = context.Registrations.Single(item =>
                item.Definition.TypeKey == TcpClientSendNodeModel.FlowNodeTypeKey);
            var template = registration.Definition.DynamicInputTemplate;
            var node = (TcpClientSendNodeModel)registration.NodeFactory();
            FlowDynamicInputResolver.MaterializeNodePorts(node, registration.Definition);

            return template != null
                && template.PortIdPrefix == "message"
                && template.DisplayNamePrefix == "Message"
                && template.DataType == FlowDataType.Object
                && template.PreferredDirection == EPortDirection.Left
                && template.IsRequired
                && template.Availability == FlowPortAvailability.Iteration
                && template.MinCount == 1
                && template.InitialCount == 1
                && template.MaxCount == null
                && node.InputParameters.Count(port => port.IsDynamic) == 1
                && node.InputParameters.Single(port => port.IsDynamic).PortId == "message_1";
        });

        Run("TCP Client Send node projects its persisted workflow settings", () =>
        {
            var node = new TcpClientSendNodeModel
            {
                Host = "127.0.0.1",
                Port = 43123,
                ConnectTimeoutMilliseconds = 2300,
                StopOnSendFailure = false,
            };
            var workflowNode = new WorkflowNode();
            node.WriteWorkflowInputs(workflowNode);

            return Equals(workflowNode.Inputs["host"], "127.0.0.1")
                && Equals(workflowNode.Inputs["port"], 43123)
                && Equals(workflowNode.Inputs["connectTimeoutMilliseconds"], 2300)
                && Equals(workflowNode.Inputs["stopOnSendFailure"], false);
        });
    }

    private static async Task<byte[]> ReadExactlyAsync(NetworkStream stream, int length)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset));
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        return offset == buffer.Length ? buffer : buffer.Take(offset).ToArray();
    }
}
