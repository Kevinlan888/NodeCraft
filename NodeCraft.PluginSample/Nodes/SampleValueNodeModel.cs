using System.Collections.Generic;
using NodeCraft.Flow;

namespace NodeCraft.PluginSample.Nodes
{
    public sealed class SampleValueNodeModel : NodeModel, IWorkflowNodeValueProvider
    {
        public string ValueText { get; set; } = "sample";

        public bool UseAccentStyle { get; set; }

        public SampleValueNodeModel()
        {
            ExecutorType = SampleValueExecutor.FlowNodeTypeKey;
            Name = "Sample Value";
            InputParameters = new List<PortParameter>();
            OutputParameters = new List<PortParameter>
            {
                new PortParameter
                {
                    PortId = SamplePortIds.Output,
                    Parameter = new Parameter
                    {
                        ParameterType = FlowDataType.String.Key,
                    },
                    PortDirection = EPortDirection.None,
                },
            };
        }

        public void WriteWorkflowInputs(WorkflowNode node)
        {
            node.Inputs[SamplePortIds.Value] = ValueText ?? string.Empty;
        }
    }
}
