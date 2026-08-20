using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Node.Algorithm.Interop;
using Node.Algorithm.Models;
using Node.Algorithm.Nodes;
using NodeCraft.Flow;

internal static partial class Program
{
    private static async Task RunAlgorithmExecutorTestsAsync()
    {
        Run("Waybill node persists algorithm settings", () =>
        {
            var node = new WaybillRecognizerNodeModel
            {
                ModelPath = "models/custom.onnx",
                Confidence = 0.42f,
                Iou = 0.61f,
                MinMaskAreaRatio = 0.002f,
                MaxDetections = 12,
                NumThreads = 4,
            };
            var workflowNode = new WorkflowNode();
            node.WriteWorkflowInputs(workflowNode);
            var config = WaybillRecognizerConfiguration.Read(workflowNode);

            return Equals(workflowNode.Inputs["modelPath"], "models/custom.onnx")
                && Equals(workflowNode.Inputs["confidence"], 0.42f)
                && Equals(workflowNode.Inputs["iou"], 0.61f)
                && Equals(workflowNode.Inputs["minMaskAreaRatio"], 0.002f)
                && Equals(workflowNode.Inputs["maxDetections"], 12)
                && Equals(workflowNode.Inputs["numThreads"], 4)
                && config.ModelPath == "models/custom.onnx"
                && config.Options.Confidence == 0.42f
                && config.Options.Iou == 0.61f
                && config.Options.MinMaskAreaRatio == 0.002f
                && config.Options.MaxDetections == 12
                && config.Options.NumThreads == 4;
        });

        Run("Waybill configuration applies stable defaults", () =>
        {
            var config = WaybillRecognizerConfiguration.Read(new WorkflowNode());
            return config.ModelPath == "models/baseline-2-960.onnx"
                && config.Options.Confidence == 0.35f
                && config.Options.Iou == 0.50f
                && config.Options.MinMaskAreaRatio == 0.0001f
                && config.Options.MaxDetections == 100
                && config.Options.NumThreads == 0;
        });

        Run("Waybill configuration rejects invalid numeric settings", () =>
        {
            return ThrowsAlgorithm<ArgumentOutOfRangeException>(() =>
                WaybillRecognizerConfiguration.Read(new WorkflowNode
                {
                    Inputs = { ["confidence"] = double.NaN },
                }))
                && ThrowsAlgorithm<ArgumentOutOfRangeException>(() =>
                    WaybillRecognizerConfiguration.Read(new WorkflowNode
                    {
                        Inputs = { ["iou"] = 1.1d },
                    }))
                && ThrowsAlgorithm<ArgumentOutOfRangeException>(() =>
                    WaybillRecognizerConfiguration.Read(new WorkflowNode
                    {
                        Inputs = { ["maxDetections"] = 0 },
                    }))
                && ThrowsAlgorithm<ArgumentOutOfRangeException>(() =>
                    WaybillRecognizerConfiguration.Read(new WorkflowNode
                    {
                        Inputs = { ["numThreads"] = -1 },
                    }));
        });

        await RunAsync("Waybill executor creates one session and returns split FlowImage outputs", async () =>
        {
            using var root = new TemporaryDirectory("nodecraft-waybill-executor-");
            var modelDirectory = Path.Combine(root.Path, "models");
            Directory.CreateDirectory(modelDirectory);
            File.WriteAllText(Path.Combine(modelDirectory, "baseline-2-960.onnx"), "test-model");

            var detection = CreateDetection(1, 1, 6, 4)[0];
            var session = new RecordingWaybillSession(
                new WaybillRecognitionResult(8, 6, new[] { detection }));
            var factory = new RecordingWaybillSessionFactory(session);
            var node = new WaybillRecognizerNodeModel
            {
                Id = "waybill",
                ModelPath = "models/baseline-2-960.onnx",
            };
            var workflowNode = new WorkflowNode
            {
                Id = node.Id,
                TypeKey = node.ExecutorType,
            };
            node.WriteWorkflowInputs(workflowNode);
            var definition = new FlowNodeDefinition { TypeKey = node.ExecutorType };
            var context = new FlowNodeSessionContext(
                workflowNode,
                definition,
                NullLogger.Instance);
            var executor = new WaybillRecognizerExecutor(
                factory,
                Path.Combine(root.Path, "Node.Algorithm.dll"),
                NullLogger.Instance);
            var image = CreateBgrImage(8, 6);

            await executor.StartSessionAsync(context, CancellationToken.None);
            var outputs = await executor.ExecuteAsync(
                new FlowExecutionContext(),
                workflowNode,
                definition,
                new Dictionary<string, object> { ["image"] = image },
                CancellationToken.None);
            await executor.StopSessionAsync(context, CancellationToken.None);

            return factory.CreateCount == 1
                && factory.ModelPath == Path.Combine(root.Path, "models", "baseline-2-960.onnx")
                && session.ProcessCount == 1
                && session.DisposeCount == 1
                && Equals(outputs["count"], 1)
                && outputs["detections"] is IReadOnlyList<WaybillDetection> detections
                && detections.Count == 1
                && outputs["annotatedImage"] is FlowImage annotated
                && annotated.Buffer.Span[1 * annotated.Stride + 1 * 3 + 2] == 255;
        });

        await RunAsync("Waybill executor rejects missing and unsupported images", async () =>
        {
            using var root = new TemporaryDirectory("nodecraft-waybill-input-");
            Directory.CreateDirectory(Path.Combine(root.Path, "models"));
            File.WriteAllText(Path.Combine(root.Path, "models", "baseline-2-960.onnx"), "test-model");
            var session = new RecordingWaybillSession(
                new WaybillRecognitionResult(2, 1, Array.Empty<WaybillDetection>()));
            var executor = new WaybillRecognizerExecutor(
                new RecordingWaybillSessionFactory(session),
                Path.Combine(root.Path, "Node.Algorithm.dll"),
                NullLogger.Instance);
            var workflowNode = new WorkflowNode
            {
                Id = "waybill",
                TypeKey = WaybillRecognizerNodeModel.FlowNodeTypeKey,
            };
            var definition = new FlowNodeDefinition
            {
                TypeKey = WaybillRecognizerNodeModel.FlowNodeTypeKey,
            };
            var context = new FlowNodeSessionContext(
                workflowNode,
                definition,
                NullLogger.Instance);
            await executor.StartSessionAsync(context, CancellationToken.None);

            var missing = ThrowsAlgorithm<InvalidOperationException>(
                () => executor.ExecuteAsync(
                    new FlowExecutionContext(),
                    workflowNode,
                    definition,
                    new Dictionary<string, object>(),
                    CancellationToken.None).GetAwaiter().GetResult());
            var depth16 = FlowImage.CopyFrom(
                2,
                1,
                4,
                FlowPixelFormat.Depth16,
                FlowImageKind.Depth,
                new byte[4],
                1,
                2,
                DateTimeOffset.UtcNow);
            var unsupported = ThrowsAlgorithm<InvalidDataException>(
                () => executor.ExecuteAsync(
                    new FlowExecutionContext(),
                    workflowNode,
                    definition,
                    new Dictionary<string, object> { ["image"] = depth16 },
                    CancellationToken.None).GetAwaiter().GetResult());
            await executor.StopSessionAsync(context, CancellationToken.None);
            return missing && unsupported && session.DisposeCount == 1;
        });

        await RunAsync("Waybill executor does not retain a failed start session", async () =>
        {
            using var root = new TemporaryDirectory("nodecraft-waybill-start-");
            Directory.CreateDirectory(Path.Combine(root.Path, "models"));
            File.WriteAllText(Path.Combine(root.Path, "models", "baseline-2-960.onnx"), "test-model");
            var factory = new RecordingWaybillSessionFactory(null!)
            {
                CreateException = new InvalidOperationException("native failure"),
            };
            var executor = new WaybillRecognizerExecutor(
                factory,
                Path.Combine(root.Path, "Node.Algorithm.dll"),
                NullLogger.Instance);
            var workflowNode = new WorkflowNode
            {
                Id = "waybill",
                TypeKey = WaybillRecognizerNodeModel.FlowNodeTypeKey,
            };
            new WaybillRecognizerNodeModel().WriteWorkflowInputs(workflowNode);
            var context = new FlowNodeSessionContext(
                workflowNode,
                new FlowNodeDefinition { TypeKey = workflowNode.TypeKey },
                NullLogger.Instance);

            var failed = ThrowsAlgorithm<InvalidOperationException>(
                () => executor.StartSessionAsync(context, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult());
            await executor.StopSessionAsync(context, CancellationToken.None);
            return failed && factory.CreateCount == 1;
        });
    }

    private static FlowImage CreateBgrImage(int width, int height)
    {
        return FlowImage.CopyFrom(
            width,
            height,
            width * 3,
            FlowPixelFormat.Bgr24,
            FlowImageKind.Color,
            new byte[width * height * 3],
            1,
            2,
            DateTimeOffset.UtcNow);
    }

    private sealed class RecordingWaybillSessionFactory : IWaybillInferenceSessionFactory
    {
        private readonly IWaybillInferenceSession _session;

        public RecordingWaybillSessionFactory(IWaybillInferenceSession session)
        {
            _session = session;
        }

        public int CreateCount { get; private set; }

        public string ModelPath { get; private set; } = null!;

        public Exception CreateException { get; set; } = null!;

        public IWaybillInferenceSession Create(
            string pluginAssemblyPath,
            string modelPath,
            WaybillInferenceOptions options)
        {
            CreateCount++;
            ModelPath = modelPath;
            if (CreateException != null)
            {
                throw CreateException;
            }

            return _session;
        }
    }

    private sealed class RecordingWaybillSession : IWaybillInferenceSession
    {
        private readonly WaybillRecognitionResult _result;

        public RecordingWaybillSession(WaybillRecognitionResult result)
        {
            _result = result;
        }

        public int ProcessCount { get; private set; }

        public int DisposeCount { get; private set; }

        public WaybillRecognitionResult Process(FlowImage image, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProcessCount++;
            return _result;
        }

        public void Dispose()
        {
            DisposeCount++;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory(string prefix)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                prefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
