namespace NodeCraft.Flow
{
    /// <summary>
    /// Inline link in the ComfyUI API format: inputs[name] = [source_node_id, source_slot].
    /// This is the strong-typed equivalent of ComfyUI's is_link.
    /// </summary>
    public sealed class LinkRef
    {
        public string SourceNodeId { get; set; }

        public int SourceSlot { get; set; }

        public static bool IsLinkRef(object value)
        {
            return value is LinkRef;
        }
    }
}
