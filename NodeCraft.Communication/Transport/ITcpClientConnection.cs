using System;
using System.Threading;
using System.Threading.Tasks;

namespace NodeCraft.Communication.Transport
{
    internal interface ITcpClientConnection : IDisposable
    {
        Task ConnectAsync(
            string host,
            int port,
            TimeSpan timeout,
            CancellationToken cancellationToken);

        Task SendAsync(byte[] payload, CancellationToken cancellationToken);
    }

    internal interface ITcpClientConnectionFactory
    {
        ITcpClientConnection Create();
    }
}
