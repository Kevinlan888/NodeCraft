using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace NodeCraft.Communication.Transport
{
    internal sealed class TcpClientConnection : ITcpClientConnection
    {
        private TcpClient _client = new TcpClient();
        private NetworkStream _stream;
        private bool _disposed;

        public async Task ConnectAsync(
            string host,
            int port,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(host))
            {
                throw new ArgumentException("Host is required.", nameof(host));
            }

            if (port < 1 || port > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(port));
            }

            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeoutSource.CancelAfter(timeout);

            try
            {
                await _client.ConnectAsync(host, port, timeoutSource.Token)
                    .ConfigureAwait(false);
                _stream = _client.GetStream();
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested
                    && timeoutSource.IsCancellationRequested)
            {
                Dispose();
                throw new TimeoutException(
                    $"TCP connection to '{host}:{port}' timed out after {timeout}.");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public async Task SendAsync(byte[] payload, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            if (_stream == null)
            {
                throw new InvalidOperationException("TCP connection has not been established.");
            }

            await _stream.WriteAsync(payload.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            var stream = _stream;
            _stream = null;
            var client = _client;
            _client = null;

            try
            {
                stream?.Dispose();
            }
            finally
            {
                client?.Dispose();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(TcpClientConnection));
            }
        }
    }

    internal sealed class TcpClientConnectionFactory : ITcpClientConnectionFactory
    {
        public ITcpClientConnection Create()
        {
            return new TcpClientConnection();
        }
    }
}
