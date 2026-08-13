using System.Collections.Generic;

namespace NodeCraft.Flow.Nodes
{
    public class AddNumberNodeModel : NodeModel
    {
        public const string FlowNodeTypeKey = AddNumberExecutor.FlowNodeTypeKey;

        public AddNumberNodeModel()
        {
            ExecutorType = FlowNodeTypeKey;
            Name = "Add";
            InputParameters = CreateBinaryNumberInputs();
            OutputParameters = CreateNumberOutput();
        }

        internal static List<PortParameter> CreateBinaryNumberInputs()
        {
            return new List<PortParameter>
            {
                new PortParameter
                {
                    PortId = BuiltInNodePorts.InputA,
                    Parameter = new Parameter { ParameterType = FlowDataType.Number.Key },
                    PortDirection = EPortDirection.None,
                },
                new PortParameter
                {
                    PortId = BuiltInNodePorts.InputB,
                    Parameter = new Parameter { ParameterType = FlowDataType.Number.Key },
                    PortDirection = EPortDirection.None,
                }
            };
        }

        internal static List<PortParameter> CreateNumberOutput()
        {
            return new List<PortParameter>
            {
                new PortParameter
                {
                    PortId = BuiltInNodePorts.Output,
                    Parameter = new Parameter { ParameterType = FlowDataType.Number.Key },
                    PortDirection = EPortDirection.None,
                }
            };
        }
    }
}