using System.Collections.Generic;

namespace NodeCraft.Flow.Nodes
{
    internal static class BooleanNodePorts
    {
        public static List<PortParameter> CreateBinaryBooleanInputs()
        {
            return new List<PortParameter>
            {
                new PortParameter
                {
                    PortId = BuiltInNodePorts.InputA,
                    Parameter = new Parameter { ParameterType = FlowDataType.Boolean.Key },
                    PortDirection = EPortDirection.None,
                },
                new PortParameter
                {
                    PortId = BuiltInNodePorts.InputB,
                    Parameter = new Parameter { ParameterType = FlowDataType.Boolean.Key },
                    PortDirection = EPortDirection.None,
                }
            };
        }

        public static List<PortParameter> CreateBooleanOutput()
        {
            return new List<PortParameter>
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