using System.Collections.Generic;
using NodeCraft.Flow;

namespace NodeCraft.Communication.Nodes
{
    public sealed class TcpClientSendNodeModel : NodeModel, IWorkflowNodeValueProvider
    {
        public const string FlowNodeTypeKey = "nodecraft.communication.tcp-client-send";

        public TcpClientSendNodeModel()
        {
            ExecutorType = FlowNodeTypeKey;
            Name = "TCP Client Send";
            InputParameters = new List<PortParameter>();
            OutputParameters = new List<PortParameter>();
        }

        public string Host { get; set; } = string.Empty;

        public int Port { get; set; }

        public int ConnectTimeoutMilliseconds { get; set; } = 5000;

        public bool StopOnSendFailure { get; set; } = true;

        public void WriteWorkflowInputs(WorkflowNode node)
        {
            node.Inputs["host"] = Host ?? string.Empty;
            node.Inputs["port"] = Port;
            node.Inputs["connectTimeoutMilliseconds"] = ConnectTimeoutMilliseconds;
            node.Inputs["stopOnSendFailure"] = StopOnSendFailure;
        }
    }
}
