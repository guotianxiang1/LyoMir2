namespace LoginGate.Core;

public sealed class LoginGateServer : IAsyncDisposable, IDisposable
{
    private readonly LoginGateConfig _config;
    private readonly LoginGateCounters _counters = new();
    private readonly NativeDbServerService _nativeDbServer;
    private readonly ClientSelectionService _clientSelection;
    private readonly PigCompatibilityService _pigCompatibility;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private CancellationTokenSource? _run;
    private int _running;
    private int _maximumReportedOnline;
    private bool _disposed;

    public LoginGateServer(LoginGateConfig config,
        ILoginTicketAuthenticator? authenticator = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _config.ThrowIfInvalid();
        authenticator ??= new RejectingLoginTicketAuthenticator();
        _nativeDbServer = new NativeDbServerService(_config, authenticator,
            _counters, WriteLog, RaiseStateChanged);
        _clientSelection = new ClientSelectionService(_config, _nativeDbServer,
            _counters, WriteLog, RaiseStateChanged);
        _pigCompatibility = new PigCompatibilityService(_config, WriteLog);
    }

    public event Action<LoginGateLogEntry>? LogReceived;
    public event Action? StateChanged;

    public bool IsRunning => Volatile.Read(ref _running) != 0;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunning) return;
            _config.ThrowIfInvalid();
            _run = new CancellationTokenSource();
            try
            {
                await _nativeDbServer.StartAsync(_run.Token).ConfigureAwait(false);
                await _pigCompatibility.StartAsync(_run.Token).ConfigureAwait(false);
                await _clientSelection.StartAsync(_run.Token).ConfigureAwait(false);
                Volatile.Write(ref _running, 1);
                WriteLog("INFO", "LoginGate 服务已启动");
                RaiseStateChanged();
            }
            catch
            {
                try { _run.Cancel(); } catch { }
                await StopServicesAsync().ConfigureAwait(false);
                _run.Dispose();
                _run = null;
                throw;
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_run == null) return;
            try { _run.Cancel(); } catch { }
            await StopServicesAsync().ConfigureAwait(false);
            _run.Dispose();
            _run = null;
            Volatile.Write(ref _running, 0);
            WriteLog("INFO", "LoginGate 服务已停止");
            RaiseStateChanged();
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public LoginGateStatsSnapshot GetStats()
    {
        var backends = _nativeDbServer.GetBackends();
        var totalOnline = backends.Sum(backend => Math.Max(0, backend.OnlineCount));
        while (true)
        {
            var maximum = Volatile.Read(ref _maximumReportedOnline);
            if (maximum >= totalOnline || Interlocked.CompareExchange(
                    ref _maximumReportedOnline, totalOnline, maximum) == maximum)
                break;
        }
        return new LoginGateStatsSnapshot(
            IsRunning,
            _clientSelection.BoundPort,
            _nativeDbServer.BoundPort,
            _pigCompatibility.BoundPort,
            _counters.ActiveClients,
            _nativeDbServer.RegisteredCount,
            _counters.ActiveAuthentications,
            _counters.AcceptedClients,
            _counters.RejectedFrames,
            totalOnline,
            Volatile.Read(ref _maximumReportedOnline));
    }

    public IReadOnlyList<LoginGateBackendSnapshot> GetBackends() =>
        _nativeDbServer.GetBackends();

    private async Task StopServicesAsync()
    {
        await _clientSelection.StopAsync().ConfigureAwait(false);
        await _pigCompatibility.StopAsync().ConfigureAwait(false);
        await _nativeDbServer.StopAsync().ConfigureAwait(false);
    }

    private void WriteLog(string level, string message) =>
        LogReceived?.Invoke(new LoginGateLogEntry(DateTime.Now, level, message));

    private void RaiseStateChanged() => StateChanged?.Invoke();

    public void Dispose()
    {
        if (_disposed) return;
        StopAsync().GetAwaiter().GetResult();
        _lifecycle.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await StopAsync().ConfigureAwait(false);
        _lifecycle.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
