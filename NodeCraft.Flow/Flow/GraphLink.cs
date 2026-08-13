namespace NodeCraft.Flow
{
    public class GraphLink
    {
        public string Id { get; set; }

        public string OriginNodeId { get; set; }

        public int OriginSlot { get; set; }

        public string TargetNodeId { get; set; }

        public int TargetSlot { get; set; }
    }
}
