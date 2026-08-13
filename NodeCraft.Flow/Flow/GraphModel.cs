using System.Collections.Generic;

namespace NodeCraft.Flow
{
    public class GraphModel
    {
        public List<NodeModel> Nodes { get; set; }

        public List<GraphLink> Links { get; set; }
    }
}
