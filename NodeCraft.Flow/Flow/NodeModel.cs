using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NodeCraft.Localization;

namespace NodeCraft.Flow
{
    public class NodeModel
    {
        public NodeModel()
        {
            Id = Guid.NewGuid().ToString();
            Name = LanguageManager.GetString("NodeModel_DefaultName");
            InputParameters = new List<PortParameter>();
            OutputParameters = new List<PortParameter>();
            ExecutorType = "";
        }

        public string Id { get; set; }

        public string Name { get; set; }

        public double X { get; set; }

        public double Y { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        public string ExecutorType { get; set; }

        public List<PortParameter> InputParameters { get; set; }

        public List<PortParameter> OutputParameters { get; set; }

        // Tracks whether a dynamic input list came from a node instance or persisted graph state.
        // It is intentionally internal and non-serialized so new nodes can receive InitialCount
        // ports while loaded nodes preserve an explicitly saved zero-port list.
        internal bool DynamicInputPortsInitialized { get; set; }
    }
}
