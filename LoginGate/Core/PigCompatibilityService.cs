using System.Net;
using System.Net.Sockets;

namespace LoginGate.Core;

/// <summary>
/// Preserves the original PIG listener topology. Its wire contract is not yet
/// evidenced, so unrecognized peers are closed instead of receiving invented data.
/// </summary>
internal sealed class PigCompatibilityService
{
    private readonly LoginGateConfig _config;
    private readonly Action<string, string> _log;
    private readonly object _lifecycle = new();
    private CancellationTokenSource? _stop;
    private TcpListener? _listener;
    private Task? _acceptTask;

    public PigCompatibilityService(LoginGateConfig config, Action<string, string> log)
    {
        _config = config;
        _log = log;
    }

    public int BoundPort { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_lifecycle)
        {
            if (_listener != null) return Task.CompletedTask;
            _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _listener = new TcpListener(IPAddress.Any, _config.PIGServerListen);
            _listener.Start(16);
            BoundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _acceptTask = AcceptLoopAsync(_listener, _stop.Token);
        }
        _log("INFO", $"PIG 兼容监听端口：{BoundPort}");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        Task? task;
        CancellationTokenSource? stop;
        lock (_lifecycle)
        {
            if (_listener == null) return;
            stop = _stop;
            task = _acceptTask;
            _stop = null;
            _acceptTask = null;
            try { stop?.Cancel(); } catch { }
            try { _listener.Stop(); } catch { }
            _listener = null;
            BoundPort = 0;
        }
        if (task != null)
        {
            try { await task.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (SocketException) when (stop?.IsCancellationRequested == true) { }
        }
        stop?.Dispose();
    }

    private async Task AcceptLoopAsync(TcpListener listener,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (!IsAllowedPeer(client))
                {
                    _log("WARN", "拒绝与 Setup/PIGServerIP 不一致的 PIG 连接");
                }
                else
                {
                    _log("WARN", "PIG 连接已关闭：该私有业务协议尚无可验证报文");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            finally
            {
                client?.Dispose();
            }
        }
    }

    private bool IsAllowedPeer(TcpClient client)
    {
        if (!LoginGateConfig.TryParseIpv4(_config.PIGServerIP, out var allowed))
            return false;
        if (allowed.Equals(IPAddress.Any)) return true;
        if (client.Client.RemoteEndPoint is not IPEndPoint remote) return false;
        var address = remote.Address.IsIPv4MappedToIPv6
            ? remote.Address.MapToIPv4()
            : remote.Address;
        return address.Equals(allowed);
    }
}
