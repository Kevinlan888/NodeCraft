using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NodeCraft.Flow;

namespace NodeCraft.Vision.StereoCamera.Nodes
{
    internal sealed class FlowImagePreviewExecutor : IFlowNodeExecutor
    {
        internal const string FlowNodeTypeKey = FlowImagePreviewNodeModel.FlowNodeTypeKey;

        public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (inputs == null
                || !inputs.TryGetValue("image", out var value)
                || !(value is FlowImage image))
            {
                throw new InvalidOperationException("Image Preview requires a FlowImage input.");
            }

            IReadOnlyDictionary<string, object> outputs = new Dictionary<string, object>
            {
                ["image"] = image,
            };
            return Task.FromResult(outputs);
        }
    }
}
