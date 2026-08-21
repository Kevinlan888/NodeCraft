using System;
using NodeCraft.Communication.Nodes;
using NodeCraft.Communication.Transport;
using NodeCraft.Communication.Views;
using NodeCraft.Flow;

namespace NodeCraft.Communication.Plugin
{
    public sealed class CommunicationPlugin : IFlowPlugin
    {
        public PluginMetadata Metadata { get; } = new PluginMetadata
        {
            Id = "nodecraft.communication",
            DisplayName = "Communication",
            Version = new Version(1, 0, 0),
        };

        public void Register(IPluginContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            context.Nodes.Register(new FlowNodeRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = TcpClientSendNodeModel.FlowNodeTypeKey,
                    DisplayName = "TCP Client Send",
                    Category = "Communication",
                    DynamicInputTemplate = new FlowDynamicInputTemplate
                    {
                        PortIdPrefix = "message",
                        DisplayNamePrefix = "Message",
                        DataType = FlowDataType.Object,
                        PreferredDirection = EPortDirection.Left,
                        IsRequired = true,
                        Availability = FlowPortAvailability.Iteration,
                        MinCount = 1,
                        InitialCount = 1,
                        MaxCount = null,
                    },
                },
                () => new TcpClientSendExecutor(
                    new TcpClientConnectionFactory(),
                    context.Logger))
            {
                NodeModelType = typeof(TcpClientSendNodeModel),
                NodeFactory = () => new TcpClientSendNodeModel(),
                PaletteDisplayName = "TCP Client Send",
                PaletteDescription = "Sends each message input over one TCP client session.",
                PaletteCategoryIconKind = "LanConnect",
                PaletteIconKind = "LanConnect",
                ContentFactory = TcpClientSendEditor.CreateContent,
            });
        }
    }
}
