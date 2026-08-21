using System.Collections.Generic;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Nodes
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
                    PortId = BuiltInPortIds.InputA,
                    Parameter = new Parameter { ParameterType = FlowDataType.Number.Key },
                    PortDirection = EPortDirection.None,
                },
                new PortParameter
                {
                    PortId = BuiltInPortIds.InputB,
                    Parameter = new Parameter { ParameterType = FlowDataType.Number.Key },
                    PortDirection = EPortDirection.None,
                },
            };
        }

        internal static List<PortParameter> CreateNumberOutput()
        {
            return new List<PortParameter>
            {
                new PortParameter
                {
                    PortId = BuiltInPortIds.Output,
                    Parameter = new Parameter { ParameterType = FlowDataType.Number.Key },
                    PortDirection = EPortDirection.None,
                },
            };
        }
    }
}
