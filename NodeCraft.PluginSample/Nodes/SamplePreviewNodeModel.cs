using System.Collections.Generic;
using NodeCraft.Flow;

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
                    PortId = SamplePortIds.Input,
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
                    PortId = SamplePortIds.Output,
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
