using System.Collections.Generic;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Nodes
{
    internal static class BooleanNodePorts
    {
        internal static List<PortParameter> CreateBinaryBooleanInputs()
        {
            return new List<PortParameter>
            {
                new PortParameter
                {
                    PortId = BuiltInPortIds.InputA,
                    Parameter = new Parameter { ParameterType = FlowDataType.Boolean.Key },
                    PortDirection = EPortDirection.None,
                },
                new PortParameter
                {
                    PortId = BuiltInPortIds.InputB,
                    Parameter = new Parameter { ParameterType = FlowDataType.Boolean.Key },
                    PortDirection = EPortDirection.None,
                },
            };
        }

        internal static List<PortParameter> CreateBooleanOutput()
        {
            return new List<PortParameter>
            {
                new PortParameter
                {
                    PortId = BuiltInPortIds.Output,
                    Parameter = new Parameter { ParameterType = FlowDataType.Boolean.Key },
                    PortDirection = EPortDirection.None,
                },
            };
        }
    }
}
