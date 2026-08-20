using System;
using System.Threading;
using Node.Algorithm.Models;
using NodeCraft.Flow;

namespace Node.Algorithm.Interop
{
    internal sealed class WaybillInferenceOptions
    {
        public float Confidence { get; set; }

        public float Iou { get; set; }

        public float MinMaskAreaRatio { get; set; }

        public int MaxDetections { get; set; }

        public int NumThreads { get; set; }
    }

    internal interface IWaybillInferenceSession : IDisposable
    {
        WaybillRecognitionResult Process(FlowImage image, CancellationToken cancellationToken);
    }

    internal interface IWaybillInferenceSessionFactory
    {
        IWaybillInferenceSession Create(
            string pluginAssemblyPath,
            string modelPath,
            WaybillInferenceOptions options);
    }
}
