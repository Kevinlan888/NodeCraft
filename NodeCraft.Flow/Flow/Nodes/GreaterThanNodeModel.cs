using System.Collections.Generic;

namespace NodeCraft.Flow.Nodes
{
    public class GreaterThanNodeModel : NodeModel
    {
        public const string FlowNodeTypeKey = GreaterThanExecutor.FlowNodeTypeKey;

        public GreaterThanNodeModel()
        {
            ExecutorType = FlowNodeTypeKey;
            Name = "Greater Than";
            InputParameters = AddNumberNodeModel.CreateBinaryNumberInputs();
            OutputParameters = new List<PortParameter>
            {
                new PortParameter
                {
                    PortId = BuiltInNodePorts.Output,
                    Parameter = new Parameter { ParameterType = FlowDataType.Boolean.Key },
                    PortDirection = EPortDirection.None,
                }
            };
        }
    }
}