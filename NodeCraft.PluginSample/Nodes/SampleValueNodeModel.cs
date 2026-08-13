using System.Collections.Generic;
using NodeCraft.Flow;
using NodeCraft.Flow.Nodes;

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
                    PortId = BuiltInNodePorts.Output,
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
            node.Inputs[BuiltInNodePorts.Value] = ValueText ?? string.Empty;
        }
    }
}
