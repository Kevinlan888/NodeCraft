using System.Collections.Generic;
using NodeCraft.Flow;
using NodeCraft.Flow.Nodes;

namespace NodeCraft.PluginSample.Nodes
{
    public sealed class SamplePreviewNodeModel : NodeModel
    {
        public string LastPreviewText { get; set; } = string.Empty;

        public SamplePreviewNodeModel()
        {
            ExecutorType = SamplePreviewExecutor.FlowNodeTypeKey;
            Name = "Sample Preview";
            InputParameters = new List<PortParameter>
            {
                new PortParameter
                {
                    PortId = BuiltInNodePorts.Input,
                    Parameter = new Parameter
                    {
                        ParameterType = FlowDataType.Object.Key,
                    },
                    PortDirection = EPortDirection.None,
                },
            };
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
    }
}
