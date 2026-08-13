using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LoginGate.Core;

internal sealed class ClientSelectionService
{
    private const int MaximumClientConnections = 5000;
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);
    private readonly LoginGateConfig _config;
    private readonly NativeDbServerService _nativeDbServer;
    private readonly LoginGateCounters _counters;
    private readonly Action<string, string> _log;
    private readonly Action _stateChanged;
    private readonly ConcurrentDictionary<long, TcpClient> _clients = new();
    private readonly ConcurrentDictionary<long, Task> _sessionTasks = new();
    private readonly SemaphoreSlim _connectionSlots = new(
        MaximumClientConnections, MaximumClientConnections);
    private readonly object _lifecycle = new();
    private CancellationTokenSource? _stop;
    private TcpListener? _listener;
    private Task? _acceptTask;
    private long _nextConnectionId;
    private int _nextDataIndex = 3000;

    public ClientSelectionService(LoginGateConfig config,
        NativeDbServerService nativeDbServer, LoginGateCounters counters,
        Action<string, string> log, Action stateChanged)
    {
        _config = config;
        _nativeDbServer = nativeDbServer;
        _counters = counters;
        _log = log;
        _stateChanged = stateChanged;
    }

    public int BoundPort { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_lifecycle)
        {
            if (_listener != null) return Task.CompletedTask;
            _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _listener = new TcpListener(IPAddress.Any, _config.LoginGateListen);
            _listener.Server.NoDelay = true;
            _listener.Start(MaximumClientConnections);
            BoundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _acceptTask = AcceptLoopAsync(_listener, _stop.Token);
        }
        _log("INFO", $"客户端选服监听端口：{BoundPort}");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        Task? acceptTask;
        CancellationTokenSource? stop;
        lock (_lifecycle)
        {
            if (_listener == null) return;
            stop = _stop;
            acceptTask = _acceptTask;
            _stop = null;
            _acceptTask = null;
            try { stop?.Cancel(); } catch { }
            try { _listener.Stop(); } catch { }
            _listener = null;
            BoundPort = 0;
        }

        foreach (var client in _clients.Values)
        {
            try { client.Dispose(); } catch { }
        }
        if (acceptTask != null)
        {
            try { await acceptTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (SocketException) when (stop?.IsCancellationRequested == true) { }
        }
        var tasks = _sessionTasks.Values.ToArray();
        if (tasks.Length != 0)
        {
            try { await Task.WhenAll(tasks).ConfigureAwait(false); }
            catch { }
        }
        stop?.Dispose();
        _stateChanged();
    }

    private async Task AcceptLoopAsync(TcpListener listener,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await _connectionSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
            TcpClient? client = null;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken)
                    .ConfigureAwait(false);
                client.NoDelay = true;
                var connectionId = Interlocked.Increment(ref _nextConnectionId);
                _clients[connectionId] = client;
                _counters.ClientAccepted();
                _stateChanged();
                var task = HandleClientAsync(client, cancellationToken);
                _sessionTasks[connectionId] = task;
                _ = task.ContinueWith(completedTask =>
                {
                    _sessionTasks.TryRemove(connectionId, out _);
                    if (_clients.TryRemove(connectionId, out var completedClient))
                    {
                        try { completedClient.Dispose(); } catch { }
                    }
                    _counters.ClientClosed();
                    _connectionSlots.Release();
                    _stateChanged();
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                client = null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                client?.Dispose();
                _connectionSlots.Release();
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                client?.Dispose();
                _connectionSlots.Release();
                break;
            }
            catch
            {
                client?.Dispose();
                _connectionSlots.Release();
                throw;
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client,
        CancellationToken serverCancellation)
    {
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(serverCancellation);
        idle.CancelAfter(IdleTimeout);
        var stream = client.GetStream();
        var parser = new LoginGateClientStreamParser();
        var buffer = new byte[1024];
        var serverDataIndex = NextDataIndex();
        var session = new ClientSelectionSession(serverDataIndex);
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "?";

        try
        {
            while (!idle.IsCancellationRequested && session.State != ClientSelectionState.Complete)
            {
                var read = await stream.ReadAsync(buffer, idle.Token).ConfigureAwait(false);
                if (read == 0) break;
                idle.CancelAfter(IdleTimeout);
                var frames = new List<LoginGateClientFrame>();
                parser.Append(buffer.AsSpan(0, read), frames.Add);
                foreach (var frame in frames)
                {
                    await ProcessFrameAsync(stream, session, frame, idle.Token)
                        .ConfigureAwait(false);
                    if (session.State == ClientSelectionState.Complete) break;
                }
            }
        }
        catch (OperationCanceledException) when (idle.IsCancellationRequested) { }
        catch (IOException) { }
        catch (SocketException) { }
        catch (Exception ex)
        {
            _counters.FrameRejected();
            _log("WARN", $"客户端选服数据被拒绝：{ex.Message}");
        }
        finally
        {
            _log("INFO", $"客户端选服连接关闭：{remote}");
        }
    }

    private async Task ProcessFrameAsync(NetworkStream stream,
        ClientSelectionSession session,
        LoginGateClientFrame frame, CancellationToken cancellationToken)
    {
        if (session.State == ClientSelectionState.AwaitingConnect)
        {
            if (!LoginGateWireProtocol.TryParseConnectRequest(frame,
                    out var requestedArea, out var error))
                throw new InvalidDataException(error);
            var area = _config.FindArea(requestedArea)
                       ?? throw new InvalidDataException($"客户端请求了未配置区域 {requestedArea}");
            var groups = area.Groups.OrderBy(item => item.Slot).ToArray();
            if (groups.Length == 0)
                throw new InvalidOperationException($"LoginGate.ini 未配置 Area{area.Slot}/groupNname");
            if (!LoginGateWireProtocol.TryCreateServerListFrame(session.ServerDataIndex,
                    groups.Select(group => (group.Name, group.Description)).ToArray(),
                    out var response, out error))
                throw new InvalidDataException(error);
            await WriteFrameAsync(stream, response, cancellationToken).ConfigureAwait(false);
            session.Area = area;
            session.State = ClientSelectionState.AwaitingSelection;
            return;
        }

        if (session.State != ClientSelectionState.AwaitingSelection)
        {
            session.State = ClientSelectionState.Complete;
            return;
        }
        if (!LoginGateWireProtocol.TryParseSelectServerRequest(frame,
                out var selection, out var selectionError))
            throw new InvalidDataException(selectionError);

        var areaSelection = session.Area
                            ?? throw new InvalidOperationException("客户端区域状态丢失");
        var groupSelection = areaSelection.Groups.FirstOrDefault(group =>
            group.Name.Equals(selection.SelectedName, StringComparison.Ordinal));
        if (groupSelection == null)
        {
            await WriteSelectErrorAsync(stream, session.ServerDataIndex, 4,
                cancellationToken).ConfigureAwait(false);
            session.State = ClientSelectionState.Complete;
            return;
        }

        var select = await _nativeDbServer.RequestSelectServerAsync(
                areaSelection, groupSelection,
                LoginGateWireProtocol.NativeMobileEncodeIndex,
                unchecked((ushort)session.ServerDataIndex),
                cancellationToken)
            .ConfigureAwait(false);
        if (select.ErrorSeries != 0 || select.Route == null)
        {
            await WriteSelectErrorAsync(stream, session.ServerDataIndex,
                select.ErrorSeries == 0 ? (byte)3 : select.ErrorSeries,
                cancellationToken).ConfigureAwait(false);
            session.State = ClientSelectionState.Complete;
            return;
        }

        var route = select.Route;
        var port = route.Port;
        if (_config.SecondZone && route.EnCodeIndex % 100 == 0)
            port |= 0x8000;
        var suffix = ReadSuffix(route.Suffix);
        if (!LoginGateWireProtocol.TryCreateSelectServerJumpFrame(
                session.ServerDataIndex, unchecked((int)route.SessionId),
                new IPAddress(route.Ipv4AddressBytes).ToString(),
                port, route.AreaIndex, route.GroupIndex, suffix,
                out var jump, out var jumpError))
            throw new InvalidDataException(jumpError);
        await WriteFrameAsync(stream, jump, cancellationToken).ConfigureAwait(false);
        _log("INFO", $"选服完成：{groupSelection.Name} -> " +
                     $"{new IPAddress(route.Ipv4AddressBytes)}:{port}");
        session.State = ClientSelectionState.Complete;
    }

    private static async Task WriteSelectErrorAsync(NetworkStream stream,
        uint dataIndex, byte errorSeries, CancellationToken cancellationToken)
    {
        if (!LoginGateWireProtocol.TryCreateSelectServerErrorFrame(
                dataIndex, errorSeries, out var frame, out var error))
            throw new InvalidDataException(error);
        await WriteFrameAsync(stream, frame, cancellationToken).ConfigureAwait(false);
    }

    private static string ReadSuffix(byte[] suffix)
    {
        if (suffix == null || suffix.Length == 0) return string.Empty;
        var terminator = Array.IndexOf(suffix, (byte)0);
        var length = terminator < 0 ? suffix.Length : terminator;
        if (length == 0) return string.Empty;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(936).GetString(suffix, 0, length);
    }

    private static async Task WriteFrameAsync(NetworkStream stream,
        LoginGateClientFrame frame, CancellationToken cancellationToken)
    {
        if (!LoginGateWireProtocol.TryEncodeClientFrame(frame,
                out var wire, out var error))
            throw new InvalidDataException(error);
        await stream.WriteAsync(wire, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private uint NextDataIndex() => unchecked((uint)NextPositive(ref _nextDataIndex));

    private static int NextPositive(ref int value)
    {
        while (true)
        {
            var next = Interlocked.Increment(ref value);
            if (next > 0) return next;
            if (Interlocked.CompareExchange(ref value, 1, next) == next) return 1;
        }
    }

    private enum ClientSelectionState
    {
        AwaitingConnect,
        AwaitingSelection,
        Complete
    }

    private sealed class ClientSelectionSession(uint serverDataIndex)
    {
        public uint ServerDataIndex { get; } = serverDataIndex;
        public ClientSelectionState State { get; set; } = ClientSelectionState.AwaitingConnect;
        public LoginGateArea? Area { get; set; }
    }
}
