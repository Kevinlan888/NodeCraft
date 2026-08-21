using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NodeCraft.Communication.Nodes;
using NodeCraft.Communication.Plugin;
using NodeCraft.Communication.Transport;
using NodeCraft.Flow;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Plugins;
using System.Windows;
using System.Windows.Controls;

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
            var registration = context.Registrations.Single(item =>
                item.Definition.TypeKey == TcpClientSendNodeModel.FlowNodeTypeKey);
            var registry = new FlowNodeRegistry();
            registry.RegisterPlugin(plugin.Metadata.Id, context.Registrations);
            var category = registry.CreatePaletteCategories().Single(item => item.Title == "Communication");
            var paletteItem = category.Items.Single(item => item.TypeKey == TcpClientSendNodeModel.FlowNodeTypeKey);

            return plugin.Metadata.Id == "nodecraft.communication"
                && plugin.Metadata.DisplayName == "Communication"
                && plugin.Metadata.Version.Equals(new Version(1, 0, 0))
                && registration.PaletteCategoryIconKind == "LanConnect"
                && registration.PaletteIconKind == "LanConnect"
                && category.IconKind == "LanConnect"
                && paletteItem.IconKind == "LanConnect";
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
            Exception? error = null;
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

        await RunAsync("TCP executor connects once and sends values in dynamic port order", async () =>
        {
            var model = new TcpClientSendNodeModel { Host = "fake", Port = 43123 };
            var definition = CreateEffectiveTcpDefinition(model, 3);
            var workflowNode = CreateWorkflowNode(model);
            var raw = new byte[] { 7, 8, 9 };
            var inputs = new Dictionary<string, object>
            {
                ["message_1"] = "alpha",
                ["message_2"] = raw,
                ["message_3"] = 99,
            };
            var factory = new RecordingTcpConnectionFactory();
            var executor = new TcpClientSendExecutor(factory);
            var context = new FlowNodeSessionContext(
                workflowNode,
                definition,
                NullLogger.Instance);

            await executor.StartSessionAsync(context, CancellationToken.None);
            try
            {
                await executor.ExecuteAsync(
                    new FlowExecutionContext(),
                    workflowNode,
                    definition,
                    inputs,
                    CancellationToken.None);
            }
            finally
            {
                await executor.StopSessionAsync(context, CancellationToken.None);
            }

            var connection = factory.Connections.Single();
            return connection.ConnectCount == 1
                && connection.Payloads.Count == 3
                && connection.Payloads[0].SequenceEqual(Encoding.UTF8.GetBytes("alpha"))
                && ReferenceEquals(connection.Payloads[1], raw)
                && connection.Payloads[2].SequenceEqual(Encoding.UTF8.GetBytes("99"))
                && connection.Disposed;
        });

        await RunAsync("TCP executor sends each dynamic input once without concatenating", async () =>
        {
            var model = new TcpClientSendNodeModel { Host = "fake", Port = 43123 };
            var definition = CreateEffectiveTcpDefinition(model, 3);
            var workflowNode = CreateWorkflowNode(model);
            var factory = new RecordingTcpConnectionFactory();
            var executor = new TcpClientSendExecutor(factory);
            var context = new FlowNodeSessionContext(
                workflowNode,
                definition,
                NullLogger.Instance);

            await executor.StartSessionAsync(context, CancellationToken.None);
            try
            {
                await executor.ExecuteAsync(
                    new FlowExecutionContext(),
                    workflowNode,
                    definition,
                    new Dictionary<string, object>
                    {
                        ["message_1"] = "one",
                        ["message_2"] = "two",
                        ["message_3"] = "three",
                    },
                    CancellationToken.None);
            }
            finally
            {
                await executor.StopSessionAsync(context, CancellationToken.None);
            }

            var connection = factory.Connections.Single();
            return connection.Payloads.Count == 3
                && connection.Payloads[0].SequenceEqual(Encoding.UTF8.GetBytes("one"))
                && connection.Payloads[1].SequenceEqual(Encoding.UTF8.GetBytes("two"))
                && connection.Payloads[2].SequenceEqual(Encoding.UTF8.GetBytes("three"));
        });

        await RunAsync("TCP executor stops after a failed send when configured", async () =>
        {
            var model = new TcpClientSendNodeModel
            {
                Host = "fake",
                Port = 43123,
                StopOnSendFailure = true,
            };
            var definition = CreateEffectiveTcpDefinition(model, 3);
            var workflowNode = CreateWorkflowNode(model);
            var factory = new RecordingTcpConnectionFactory
            {
                FailOnSendNumber = 2,
            };
            var executor = new TcpClientSendExecutor(factory);
            var context = new FlowNodeSessionContext(
                workflowNode,
                definition,
                NullLogger.Instance);
            var threw = false;

            await executor.StartSessionAsync(context, CancellationToken.None);
            try
            {
                try
                {
                    await executor.ExecuteAsync(
                        new FlowExecutionContext(),
                        workflowNode,
                        definition,
                        CreateThreeMessageInputs(),
                        CancellationToken.None);
                }
                catch (IOException)
                {
                    threw = true;
                }
            }
            finally
            {
                await executor.StopSessionAsync(context, CancellationToken.None);
            }

            var connection = factory.Connections.Single();
            return threw
                && connection.SendAttempts == 2
                && connection.Payloads.Count == 2
                && connection.Disposed;
        });

        await RunAsync("TCP executor logs and continues after a failed send when configured", async () =>
        {
            var model = new TcpClientSendNodeModel
            {
                Host = "fake",
                Port = 43123,
                StopOnSendFailure = false,
            };
            var definition = CreateEffectiveTcpDefinition(model, 3);
            var workflowNode = CreateWorkflowNode(model);
            var factory = new RecordingTcpConnectionFactory
            {
                FailOnSendNumber = 2,
            };
            var logger = new RecordingLogger();
            var executor = new TcpClientSendExecutor(factory, logger);
            var context = new FlowNodeSessionContext(workflowNode, definition, logger);

            await executor.StartSessionAsync(context, CancellationToken.None);
            try
            {
                await executor.ExecuteAsync(
                    new FlowExecutionContext(),
                    workflowNode,
                    definition,
                    CreateThreeMessageInputs(),
                    CancellationToken.None);
            }
            finally
            {
                await executor.StopSessionAsync(context, CancellationToken.None);
            }

            var connection = factory.Connections.Single();
            return connection.SendAttempts == 3
                && connection.Payloads.Count == 3
                && connection.Payloads[2].SequenceEqual(Encoding.UTF8.GetBytes("three"))
                && logger.Messages.Any(message =>
                    message.Contains("message_2", StringComparison.Ordinal)
                    && message.Contains("discarded", StringComparison.OrdinalIgnoreCase));
        });

        await RunAsync("TCP executor rejects null payloads regardless of failure policy", async () =>
        {
            var model = new TcpClientSendNodeModel
            {
                Host = "fake",
                Port = 43123,
                StopOnSendFailure = false,
            };
            var definition = CreateEffectiveTcpDefinition(model, 1);
            var workflowNode = CreateWorkflowNode(model);
            var factory = new RecordingTcpConnectionFactory();
            var executor = new TcpClientSendExecutor(factory);
            var context = new FlowNodeSessionContext(
                workflowNode,
                definition,
                NullLogger.Instance);
            var rejected = false;

            await executor.StartSessionAsync(context, CancellationToken.None);
            try
            {
                try
                {
                    await executor.ExecuteAsync(
                        new FlowExecutionContext(),
                        workflowNode,
                        definition,
                        new Dictionary<string, object> { ["message_1"] = null! },
                        CancellationToken.None);
                }
                catch (InvalidOperationException exception)
                {
                    rejected = exception.Message.Contains(
                        "message_1",
                        StringComparison.Ordinal);
                }
            }
            finally
            {
                await executor.StopSessionAsync(context, CancellationToken.None);
            }

            return rejected && factory.Connections.Single().SendAttempts == 0;
        });

        await RunAsync("TCP executor disposes a connection after startup failure", async () =>
        {
            var model = new TcpClientSendNodeModel { Host = "fake", Port = 43123 };
            var definition = CreateEffectiveTcpDefinition(model, 1);
            var workflowNode = CreateWorkflowNode(model);
            var factory = new RecordingTcpConnectionFactory
            {
                ConnectException = new IOException("connect failed"),
            };
            var executor = new TcpClientSendExecutor(factory);
            var context = new FlowNodeSessionContext(
                workflowNode,
                definition,
                NullLogger.Instance);
            var threw = false;

            try
            {
                await executor.StartSessionAsync(context, CancellationToken.None);
            }
            catch (IOException)
            {
                threw = true;
            }

            return threw
                && factory.Connections.Single().ConnectCount == 1
                && factory.Connections.Single().Disposed;
        });

        Run("TCP Client Send registration exposes an editor and network palette metadata", () =>
        {
            var plugin = new CommunicationPlugin();
            var context = new PluginRegistrationContext(
                NullLogger.Instance,
                new Version(1, 0));
            plugin.Register(context);
            var registration = context.Registrations.Single(item =>
                item.Definition.TypeKey == TcpClientSendNodeModel.FlowNodeTypeKey);
            var registry = EnsureCommunicationRegistered();
            var communicationCategory = registry.CreatePaletteCategories().Single(category =>
                category.Title == "Communication");
            var item = communicationCategory.Items.Single(paletteItem =>
                paletteItem.TypeKey == TcpClientSendNodeModel.FlowNodeTypeKey);

            return registration.ContentFactory != null
                && item.IconKind == "LanConnect"
                && communicationCategory.IconKind == "LanConnect";
        });

        Run("TCP Client Send editor XAML is compiled as a Page with all settings controls", () =>
        {
            var assembly = typeof(CommunicationPlugin).Assembly;
            using var stream = assembly.GetManifestResourceStream(
                "NodeCraft.Communication.g.resources");
            if (stream == null)
            {
                return false;
            }

            var hasBaml = false;
            using (var reader = new System.Resources.ResourceReader(stream))
            {
                foreach (var entry in reader.Cast<System.Collections.DictionaryEntry>())
                {
                    if (string.Equals(
                        (string)entry.Key,
                        "views/tcpclientsendeditor.baml",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        hasBaml = true;
                        break;
                    }
                }
            }

            var xaml = File.ReadAllText(FindRepositoryFile(
                "NodeCraft.Communication",
                "Views",
                "TcpClientSendEditor.xaml"));
            return hasBaml
                && xaml.Contains("HostEditor", StringComparison.Ordinal)
                && xaml.Contains("PortEditor", StringComparison.Ordinal)
                && xaml.Contains("ConnectTimeoutEditor", StringComparison.Ordinal)
                && xaml.Contains("StopOnSendFailureEditor", StringComparison.Ordinal);
        });

        await RunAsync("TCP Client Send editor updates settings and ignores invalid numbers", () =>
            Task.FromResult(RunOnSta(() =>
            {
                var registry = EnsureCommunicationRegistered();
                var registration = registry.Resolve(TcpClientSendNodeModel.FlowNodeTypeKey);
                var canvas = new FlowCanvas();
                var node = new TcpClientSendNodeModel();
                var graphChanges = 0;
                canvas.GraphChanged += (_, __) => graphChanges++;
                var content = registration.ContentFactory?.Invoke(canvas, node);
                var host = GetPrivateField<TextBox>(content!, "_hostEditor");
                var port = GetPrivateField<TextBox>(content!, "_portEditor");
                var timeout = GetPrivateField<TextBox>(content!, "_connectTimeoutEditor");
                var stopOnFailure = GetPrivateField<CheckBox>(
                    content!,
                    "_stopOnSendFailureEditor");
                var initialChanges = graphChanges;

                host.Text = "localhost";
                port.Text = "43210";
                timeout.Text = "1800";
                stopOnFailure.IsChecked = false;
                var validChanges = graphChanges;
                port.Text = "not-an-int";
                timeout.Text = "0";

                return content is FrameworkElement
                    && initialChanges == 0
                    && node.Host == "localhost"
                    && node.Port == 43210
                    && node.ConnectTimeoutMilliseconds == 1800
                    && !node.StopOnSendFailure
                    && validChanges == 4
                    && graphChanges == validChanges;
            })));

        Run("TCP Client Send configuration and dynamic port survive graph XML round-trip", () =>
        {
            var registry = EnsureCommunicationRegistered();
            var original = new TcpClientSendNodeModel
            {
                Host = "localhost",
                Port = 43210,
                ConnectTimeoutMilliseconds = 1800,
                StopOnSendFailure = false,
            };
            FlowDynamicInputResolver.MaterializeNodePorts(
                original,
                registry.Resolve(TcpClientSendNodeModel.FlowNodeTypeKey).Definition);
            var path = Path.Combine(
                Path.GetTempPath(),
                "nodecraft-communication-" + Guid.NewGuid().ToString("N") + ".flow.xml");

            try
            {
                GraphModelXmlSerializer.Save(
                    new GraphModel
                    {
                        Nodes = new List<NodeModel> { original },
                        Links = new List<GraphLink>(),
                    },
                    path);
                var loaded = GraphModelXmlSerializer.Load(path).Nodes.Single();
                var node = (TcpClientSendNodeModel)loaded;
                return node.Host == "localhost"
                    && node.Port == 43210
                    && node.ConnectTimeoutMilliseconds == 1800
                    && !node.StopOnSendFailure
                    && node.InputParameters.Count(port => port.IsDynamic) == 1
                    && node.InputParameters.Single(port => port.IsDynamic).PortId == "message_1";
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        });

        var communicationLoaderRoot = CreateTemporaryPluginDirectory("nodecraft-communication-loader-");
        try
        {
            Run("PluginLoader loads the Communication manifest and TCP registration", () =>
            {
                var pluginsDirectory = Path.Combine(communicationLoaderRoot, "Plugins");
                var packageDirectory = Path.Combine(pluginsDirectory, "NodeCraft.Communication");
                Directory.CreateDirectory(packageDirectory);
                FlowNodeRegistry registry = null!;
                PluginLoader loader = null!;
                PluginLoadReport report = null!;

                try
                {
                    CopyFileToDirectory(FindBuiltCommunicationAssembly(), packageDirectory);
                    CopyFileToDirectory(FindBuiltCommunicationManifest(), packageDirectory);
                    registry = new FlowNodeRegistry();
                    loader = new PluginLoader(
                        registry,
                        new Version(1, 0),
                        NullLoggerFactory.Instance);
                    report = loader.LoadAll(pluginsDirectory);

                    return report.Failures.Count == 0
                        && report.Results.Count == 1
                        && report.Results[0].PluginId == "nodecraft.communication"
                        && report.Results[0].IsSuccess
                        && registry.Contains(TcpClientSendNodeModel.FlowNodeTypeKey)
                        && registry.Resolve(TcpClientSendNodeModel.FlowNodeTypeKey).ContentFactory != null;
                }
                finally
                {
                    loader = null!;
                    registry = null!;
                    UnloadPluginLoadContexts(ref report);
                }
            });

            Run("isolated-ALC editor content loads through InitializeComponent pack URI", () =>
            {
                var pilotRoot = CreateTemporaryPluginDirectory("nodecraft-communication-pilot-");
                FlowNodeRegistry registry = null!;
                PluginLoader loader = null!;
                PluginLoadReport report = null!;
                object content = null!;
                object node = null!;
                FlowNodeRegistration registration = null!;

                try
                {
                    var pluginsDirectory = Path.Combine(pilotRoot, "Plugins");
                    var packageDirectory = Path.Combine(pluginsDirectory, "NodeCraft.Communication");
                    Directory.CreateDirectory(packageDirectory);
                    CopyFileToDirectory(FindBuiltCommunicationAssembly(), packageDirectory);
                    CopyFileToDirectory(FindBuiltCommunicationManifest(), packageDirectory);
                    registry = new FlowNodeRegistry();
                    loader = new PluginLoader(
                        registry,
                        new Version(1, 0),
                        NullLoggerFactory.Instance);
                    report = loader.LoadAll(pluginsDirectory);
                    if (report.Failures.Count != 0 || !report.Results[0].IsSuccess)
                    {
                        return false;
                    }

                    registration = registry.Resolve(
                        TcpClientSendNodeModel.FlowNodeTypeKey);
                    return RunOnSta(() =>
                    {
                        var canvas = new FlowCanvas();
                        node = registration.NodeFactory();
                        content = registration.ContentFactory.Invoke(canvas, (NodeModel)node);
                        var host = GetPrivateField<TextBox>(content!, "_hostEditor");
                        var port = GetPrivateField<TextBox>(content!, "_portEditor");
                        return content is FrameworkElement
                            && host != null
                            && port != null
                            && host.Text == string.Empty
                            && int.TryParse(
                                port.Text,
                                System.Globalization.NumberStyles.Integer,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out _);
                    });
                }
                finally
                {
                    content = null!;
                    node = null!;
                    registration = null!;
                    registry = null!;
                    loader = null!;
                    UnloadPluginLoadContexts(ref report);
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    RegisterDeferredCleanup(pilotRoot);
                }
            });
        }
        finally
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            DeleteDirectoryIfExists(communicationLoaderRoot);
        }

        Run("Graph adapter preserves ordered Communication dynamic input links", () =>
        {
            var registry = EnsureCommunicationRegistered();
            var sourceOne = new StringValueNodeModel { Id = "source-one" };
            var sourceTwo = new StringValueNodeModel { Id = "source-two" };
            var target = new TcpClientSendNodeModel { Id = "tcp-target" };
            var targetDefinition = registry.Resolve(TcpClientSendNodeModel.FlowNodeTypeKey).Definition;
            FlowDynamicInputResolver.MaterializeNodePorts(target, targetDefinition);
            if (!FlowDynamicInputResolver.TryAddDynamicPort(
                target,
                targetDefinition,
                out _,
                out _))
            {
                return false;
            }

            var graph = new GraphModel
            {
                Nodes = new List<NodeModel> { sourceOne, sourceTwo, target },
                Links = new List<GraphLink>
                {
                    new GraphLink
                    {
                        Id = "link-one",
                        OriginNodeId = sourceOne.Id,
                        OriginSlot = 0,
                        TargetNodeId = target.Id,
                        TargetSlot = 1,
                    },
                    new GraphLink
                    {
                        Id = "link-two",
                        OriginNodeId = sourceTwo.Id,
                        OriginSlot = 0,
                        TargetNodeId = target.Id,
                        TargetSlot = 2,
                    },
                },
            };

            var workflow = GraphModelWorkflowAdapter.Convert(graph);
            var workflowTarget = workflow.Nodes.Single(node => node.Id == target.Id);
            var first = workflowTarget.Inputs["message_1"] as LinkRef;
            var second = workflowTarget.Inputs["message_2"] as LinkRef;
            return workflowTarget.DynamicInputPortIds.SequenceEqual(
                    new[] { "message_1", "message_2" })
                && first != null
                && first.SourceNodeId == sourceOne.Id
                && first.SourceSlot == 0
                && second != null
                && second.SourceNodeId == sourceTwo.Id
                && second.SourceSlot == 0;
        });

        await RunAsync("TCP Client Send delivers ordered bytes to a loopback server", async () =>
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var model = new TcpClientSendNodeModel
                {
                    Host = IPAddress.Loopback.ToString(),
                    Port = port,
                };
                var definition = CreateEffectiveTcpDefinition(model, 2);
                var workflowNode = CreateWorkflowNode(model);
                var registration = EnsureCommunicationRegistered()
                    .Resolve(TcpClientSendNodeModel.FlowNodeTypeKey);
                var executor = registration.ExecutorFactory();
                var lifecycle = executor as IFlowNodeSessionLifecycle;
                if (lifecycle == null)
                {
                    return false;
                }

                var sessionContext = new FlowNodeSessionContext(
                    workflowNode,
                    definition,
                    NullLogger.Instance);
                var acceptTask = listener.AcceptTcpClientAsync();
                var started = false;
                try
                {
                    await lifecycle.StartSessionAsync(sessionContext, CancellationToken.None);
                    started = true;
                    using var server = await acceptTask;
                    using var stream = server.GetStream();
                    await executor.ExecuteAsync(
                        new FlowExecutionContext(),
                        workflowNode,
                        definition,
                        new Dictionary<string, object>
                        {
                            ["message_1"] = "first",
                            ["message_2"] = "second",
                        },
                        CancellationToken.None);
                    var received = await ReadExactlyAsync(stream, "firstsecond".Length);
                    return Encoding.UTF8.GetString(received) == "firstsecond";
                }
                finally
                {
                    if (started)
                    {
                        await lifecycle.StopSessionAsync(sessionContext, CancellationToken.None);
                    }
                }
            }
            finally
            {
                listener.Stop();
            }
        });
    }

    private static FlowNodeDefinition CreateEffectiveTcpDefinition(
        TcpClientSendNodeModel node,
        int dynamicCount)
    {
        var plugin = new CommunicationPlugin();
        var context = new PluginRegistrationContext(
            NullLogger.Instance,
            new Version(1, 0));
        plugin.Register(context);
        var registration = context.Registrations.Single(item =>
            item.Definition.TypeKey == TcpClientSendNodeModel.FlowNodeTypeKey);
        FlowDynamicInputResolver.MaterializeNodePorts(node, registration.Definition);
        while (node.InputParameters.Count(port => port.IsDynamic) < dynamicCount)
        {
            if (!FlowDynamicInputResolver.TryAddDynamicPort(
                node,
                registration.Definition,
                out _,
                out var error))
            {
                throw new InvalidOperationException(error);
            }
        }

        return FlowDynamicInputResolver.ResolveDefinition(
            registration.Definition,
            FlowDynamicInputResolver.GetDynamicPortIds(node));
    }

    private static WorkflowNode CreateWorkflowNode(TcpClientSendNodeModel node)
    {
        var workflowNode = new WorkflowNode
        {
            Id = node.Id,
            TypeKey = node.ExecutorType,
        };
        node.WriteWorkflowInputs(workflowNode);
        return workflowNode;
    }

    private static Dictionary<string, object> CreateThreeMessageInputs()
    {
        return new Dictionary<string, object>
        {
            ["message_1"] = "one",
            ["message_2"] = "two",
            ["message_3"] = "three",
        };
    }

    private static FlowNodeRegistry EnsureCommunicationRegistered()
    {
        var registry = NodeExecutorFactory.Registry;
        if (registry.TryResolve(TcpClientSendNodeModel.FlowNodeTypeKey, out _))
        {
            return registry;
        }

        var plugin = new CommunicationPlugin();
        var context = new PluginRegistrationContext(
            NullLogger.Instance,
            new Version(1, 0));
        plugin.Register(context);
        registry.RegisterPlugin(plugin.Metadata.Id, context.Registrations);
        return registry;
    }

    private static string FindBuiltCommunicationAssembly()
    {
        return FindRepositoryFile(
            "NodeCraft.Communication",
            "bin",
            GetBuildMetadata("BuildConfiguration"),
            GetBuildMetadata("BuildTargetFramework"),
            "NodeCraft.Communication.dll");
    }

    private static string FindBuiltCommunicationManifest()
    {
        return Path.Combine(
            Path.GetDirectoryName(FindBuiltCommunicationAssembly())
                ?? throw new InvalidOperationException(
                    "Communication plugin output directory was not found."),
            "plugin.json");
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

    private sealed class RecordingTcpConnectionFactory : ITcpClientConnectionFactory
    {
        public List<RecordingTcpConnection> Connections { get; }
            = new List<RecordingTcpConnection>();

        public int FailOnSendNumber { get; set; }

        public Exception? ConnectException { get; set; }

        public ITcpClientConnection Create()
        {
            var connection = new RecordingTcpConnection
            {
                FailOnSendNumber = FailOnSendNumber,
                ConnectException = ConnectException,
            };
            Connections.Add(connection);
            return connection;
        }
    }

    private sealed class RecordingTcpConnection : ITcpClientConnection
    {
        public List<byte[]> Payloads { get; } = new List<byte[]>();

        public int ConnectCount { get; private set; }

        public int SendAttempts { get; private set; }

        public bool Disposed { get; private set; }

        public int FailOnSendNumber { get; set; }

        public Exception? ConnectException { get; set; }

        public Task ConnectAsync(
            string host,
            int port,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            ConnectCount++;
            if (ConnectException != null)
            {
                return Task.FromException(ConnectException);
            }

            return Task.CompletedTask;
        }

        public Task SendAsync(byte[] payload, CancellationToken cancellationToken)
        {
            SendAttempts++;
            Payloads.Add(payload);
            if (FailOnSendNumber > 0 && SendAttempts == FailOnSendNumber)
            {
                return Task.FromException(new IOException("send failed"));
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Messages { get; } = new List<string>();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new NullScope();

            public void Dispose()
            {
            }
        }
    }
}
