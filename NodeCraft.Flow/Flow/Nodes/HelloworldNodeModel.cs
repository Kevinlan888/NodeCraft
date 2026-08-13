using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeCraft.Flow.Nodes
{
    public class HelloworldNodeModel : NodeModel
    {
        public const string FlowNodeTypeKey = "node.hello-world";

        public HelloworldNodeModel()
        {
            ExecutorType = FlowNodeTypeKey;
            InputParameters = new List<PortParameter>
            {
                new PortParameter
                {
                    PortId = BuiltInNodePorts.Input,
                    Parameter = new Parameter { ParameterType = "String" },
                    PortDirection = EPortDirection.None
                }
            };

            OutputParameters = new List<PortParameter>
            {
                new PortParameter
                {
                     PortId = BuiltInNodePorts.Output,
                     Parameter = new Parameter { ParameterType = "String" },
                     PortDirection = EPortDirection.None,
                }
            };
        }
    }
}
