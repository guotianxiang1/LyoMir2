using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using GameGate.Core;
using GameGate.Models;
using SystemModule;
using SystemModule.Packet;

namespace GameGate.Core;

/// <summary>
/// Transparent TCP proxy: Client ↔ GameGate ↔ shared DBSvr/GameSvr connections.
/// Parses 0xFF44FF44 frames for speed detection but forwards raw bytes.
/// Logical sessions are multiplexed by the native socket-handle route identifiers.
/// </summary>
public sealed class GateServer : IDisposable
{
    private const ushort SM_STARTPLAY = 525;
    private const ushort SM_LOGON = 50;
    private const ushort SM_SENDNOTICE = 658;
    private const ushort SM_NEWMAP = 51;
    private const ushort CM_QUERYBAGITEMS = 81;
    private const ushort CM_MERCHANTDLGSELECT = 1011;
    private const ushort SM_AREASTATE_SERVER = 766;
    private const ushort SM_AREASTATE_CLIENT = 708;
    private const ushort SM_SAFE_ZONE_INFO_SERVER = 795;
    private const ushort SM_SAFE_ZONE_INFO_CLIENT = 4230;
    private const ushort SM_MAPINFO_EX_SERVER = 796;
    private const ushort SM_MAPINFO_EX_CLIENT = 1281;
    private const ushort SM_MAPDESCRIPTION = 54;
    private const ushort SoftCloseQueryParam = 1;
    private const long QueryBagItemsMinIntervalMs = 30000;
    private const int SoftCloseQueryDelayMs = 600;
    private const int LoginPassionAreaStateDelayMs = 8000;
    private const int PostLoginExtraFrameDelayMs = 80;
    private const int BufferedFrameChunkSize = 24;
    private const int BufferedFrameChunkDelayMs = 15;
    private const int MaximumBufferedLoginFrames = 2048;
    private const int MaximumBufferedLoginBytes = 2 * 1024 * 1024;
    private const int InitialClientReceiveBuffer = 2048;
    private const int MaximumClientReceiveBuffer = 64 * 1024;

    // The action family used to be repacked here - Param folded into the high word of
    // Recog and Series copied over Tag - so that GameSvr could unpack it again. Native
    // has no such step: M2Server reads the three fields where they already are.
    // The shared action handler at 0x6D9EAF builds its call straight from the 12-byte
    // header:
    //   0x6D9EE9  0F B7 40 06  movzx eax, word [eax+6]   ; Param
    //   0x6D9EF1  8A 40 0A     mov   al,  byte [eax+0xA] ; Series low byte
    //   0x6D9EF4  24 07        and   al,  7              ; direction
    //   0x6D9EFA  8B 08        mov   ecx, [eax]          ; Recog, whole i32
    // and the callee 0x6EC078 compares them against the actor's own position:
    //   0x6EC0C3  3B B3 2C 01 00 00  cmp esi, [ebx+0x12C]   ; Recog  vs CurrX
    //   0x6EC0D2  3B 83 30 01 00 00  cmp eax, [ebx+0x130]   ; Param  vs CurrY
    // (+0x12C / +0x130 are CurrX / CurrY, confirmed by 0x770DF6 and 0x770E01 sending
    // them as the Tag / Series of SM_PHYSICAL_ATT.)
    //
    // The repack was self-consistent with GameSvr's matching unpack, so the pair worked,
    // but it made the gate-to-server link a C#-only dialect: an original gate could not
    // drive this server and this gate could not drive an original M2Server. Both sides
    // now use the native field assignment instead.
    private static ClientPacket CreateGameSvrClientPacket(MobileCodec.InnerHeader inner, ushort ident)
    {
        return new ClientPacket
        {
            Recog = inner.Recog,
            Ident = ident,
            Param = inner.Param,
            Tag = inner.Tag,
            Series = inner.Series
        };
    }

    internal static InternalPacket77 CreateGameDataPacket(uint connId, uint sequence,
        byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length > InternalPacket77.MAX_PAYLOAD_SIZE)
            throw new InvalidDataException(
                $"GameSvr payload {payload.Length} exceeds {InternalPacket77.MAX_PAYLOAD_SIZE} bytes");

        return new InternalPacket77
        {
            Magic = InternalPacket77.MAGIC,
            ConnID = connId,
            SeqID = sequence,
            FrameLen = checked((ushort)(InternalPacket77.HEADER_SIZE + payload.Length)),
            Cmd = Grobal2.GM_DATA,
            Field16 = unchecked((uint)Environment.TickCount),
            Field20 = checked((uint)payload.Length),
            Payload = payload
        };
    }

    private static ClientPacket CreateSoftCloseQueryPacket(int sessionId) => new()
    {
        Recog = sessionId,
        Ident = Grobal2.CM_QUERYCHR,
        Param = SoftCloseQueryParam,
        Tag = 0,
        Series = 0
    };

    private static bool IsActionCoordinateIdent(ushort ident)
    {
        switch (ident)
        {
            case Grobal2.CM_HORSERUN:
            case Grobal2.CM_TURN:
            case Grobal2.CM_WALK:
            case Grobal2.CM_SITDOWN:
            case Grobal2.CM_RUN:
            case Grobal2.CM_HIT:
            case Grobal2.CM_HEAVYHIT:
            case Grobal2.CM_BIGHIT:
            case Grobal2.CM_POWERHIT:
            case Grobal2.CM_LONGHIT:
            case Grobal2.CM_WIDEHIT:
            case Grobal2.CM_FIREHIT:
            case Grobal2.CM_CRSHIT:
            case Grobal2.CM_TWINHIT:
                return true;
            default:
                return false;
        }
    }

    private static void UpdateClientActionState(ClientSession session, ClientPacket packet)
    {
        if (!IsActionCoordinateIdent(packet.Ident)) return;

        session.X = (ushort)(packet.Recog & 0xFFFF);
        session.Y = packet.Param;
    }

    private static string ReadPlayerText(byte[] body)
    {
        if (body.Length == 0) return string.Empty;
        int length = Array.IndexOf(body, (byte)0);
        if (length < 0) length = body.Length;
        if (length == 0) return string.Empty;

        string value = HUtil32.GetString(body, 0, length).Trim();
        int controls = value.Count(ch => char.IsControl(ch) && ch != '\t');
        return value.Length > 0 && controls * 5 <= value.Length ? value : string.Empty;
    }

    private static void UpdatePlayerState(ClientSession session, ClientPacket packet, byte[] body)
    {
        switch (packet.Ident)
        {
            case SM_NEWMAP:
            case Grobal2.SM_CHANGEMAP:
            {
                string map = ReadPlayerText(body);
                if (map.Length > 0) session.MapName = map;
                session.X = packet.Param;
                session.Y = packet.Tag;
                break;
            }
            case Grobal2.SM_ABILITY:
                session.Gold = Math.Max(0, packet.Recog);
                session.Job = packet.Param & 0xFF;
                session.Ingot = (uint)packet.Tag | ((long)packet.Series << 16);
                if (body.Length >= sizeof(ushort))
                    session.Level = BinaryPrimitives.ReadUInt16LittleEndian(body);
                break;
            case Grobal2.SM_LEVELUP:
                if (packet.Param > 0) session.Level = packet.Param;
                break;
            case Grobal2.SM_GOLDCHANGED:
                session.Gold = Math.Max(0, packet.Recog);
                session.Ingot = (uint)packet.Param | ((long)packet.Tag << 16);
                break;
        }
    }

    private static bool IsLoginScriptCall(ushort ident, byte[] body)
    {
        if (ident != CM_MERCHANTDLGSELECT || body.Length == 0)
        {
            return false;
        }

        var text = HUtil32.GetString(body, 0, body.Length).TrimEnd('\0').Trim();
        return text.Equals("@onLogin", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("@onBzLogin~", StringComparison.OrdinalIgnoreCase);
    }

    private readonly GateConfig _cfg;
    private readonly SessionManager _sessions;
    private readonly SpeedDetector _speed;
    private readonly BanSystem _ban = new();
    private readonly DelayQueue _delayQueue;
    private readonly AbusiveFilter _abusiveFilter;
    private readonly SharedBackendHub _backend;
    private readonly ConcurrentDictionary<long, Task> _clientTasks = new();
    private readonly SemaphoreSlim _stopLock = new(1, 1);
    private volatile bool _running;
    private long _nextClientTaskId;
    private CancellationTokenSource? _lifetime;
    private Task? _acceptTask;
    private Task? _cleanupTask;

    private TcpListener? _listener;

    // Stats
    public long TotalPacketsUp, TotalPacketsDown, TotalBytesUp, TotalBytesDown;
    public long TotalDropped, TotalRejected, TotalClients, TotalDisconnects;
    public bool DBSConnected => _backend.DBConnected;
    public int Reconnects => _backend.Reconnects;
    public DateTime StartTime;
    public string LastError = "";

    // Events for GUI
    public event Action<string, object?>? OnLog;
    public event Action<ClientSession, string>? OnChat;
    public event Action? OnStatsChanged;

    public SessionManager Sessions => _sessions;
    public SpeedDetector Speed => _speed;
    public BanSystem Bans => _ban;
    public GateConfig Config => _cfg;
    public bool IsRunning => _running;
    public bool DBConnected => _backend.DBConnected;
    public bool GameConnected => _backend.GameConnected;

    public GateServer(GateConfig cfg)
    {
        _cfg = cfg;
        _sessions = new SessionManager(cfg.MaxSessions);
        _speed = new SpeedDetector(cfg);
        _delayQueue = new DelayQueue(1000); // 1-second delay for penalized packets
        _backend = new SharedBackendHub(cfg, Log);
        _speed.OnPenalty += (s, p, r) =>
        {
            Log("BAN", $"[{s.RemoteAddr}] {p} — {r}");
            if (p >= PenaltyLevel.BANNED && s.TcpClient is TcpClient client)
            {
                try { client.Dispose(); } catch { }
            }
        };

        // Delayed client actions remain upstream and are written to the original GameSvr stream.
        _delayQueue.OnDequeue += async pkt =>
        {
            if (pkt.IsUpstream) await ForwardDelayedUpstreamAsync(pkt);
        };

        _abusiveFilter = cfg.AbusiveFilter; // Fix 1: abusive filter from GateConfig

        _ban.LoadBlockIPs(cfg.BlockIPs);
        _ban.LoadBlockIPAreas(cfg.BlockIPAreas);      // Fix 2: IP area ranges
        _ban.LoadBlockHWIDs(cfg.BlockHWIDs);
        _ban.LoadBlockHWIDs(cfg.BlockHWIDList);        // Fix 4: BlockHWID.txt
        _ban.LoadBlockedNames(cfg.BlockNames);
        _ban.LoadMutedNames(cfg.MutedNames);
        _ban.LoadTemporaryBans(Path.Combine(cfg.ConfigDir, "TempBan.ini"));
        _ban.AutoBanDuration = Math.Max(60, cfg.BlackTime * 60);
        MobileTicketResolver.Install(); // wire up ticket→account MySQL resolution
    }

    private async Task ForwardDelayedUpstreamAsync(DelayedPacket packet)
    {
        var session = _sessions.Get(packet.SessionId, packet.Generation);
        if (session == null || !_backend.TryGetRoute(session.BackendRouteId,
                packet.Generation, out var route)) return;
        if (!await _backend.SendGameAsync(route, packet.Data))
            Log("DEBUG", "Delayed upstream send failed: shared GameSvr unavailable");
    }

    public Task StartAsync()
    {
        if (_running) return Task.CompletedTask;
        _running = true;
        StartTime = DateTime.Now;
        _lifetime = new CancellationTokenSource();
        _backend.Start();

        _cleanupTask = CleanupLoop(_lifetime.Token);

        _listener = new TcpListener(IPAddress.Parse(_cfg.GateAddr), _cfg.GatePort);
        _listener.Start(_sessions.Capacity);
        Log("INFO", $"Listening :{_cfg.GatePort} → DBSvr {_cfg.BackendIP}:{_cfg.BackendPort2}, " +
                    $"GameSvr {_cfg.GameBackendIP}:{_cfg.BackendPort}");
        _acceptTask = AcceptLoop();
        return Task.CompletedTask;
    }

    private async Task AcceptLoop()
    {
        var listener = _listener;
        if (listener == null) return;
        while (_running)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync();
                if (!_running)
                {
                    client.Dispose();
                    break;
                }
                var taskId = Interlocked.Increment(ref _nextClientTaskId);
                var task = HandleClient(client);
                _clientTasks.TryAdd(taskId, task);
                _ = task.ContinueWith(completed =>
                {
                    _clientTasks.TryRemove(taskId, out _);
                    if (completed.Exception != null)
                        Log("ERROR", $"client task failed: {completed.Exception.GetBaseException().Message}");
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { break; }
            catch (Exception ex) when (_running) { LastError = ex.Message; }
        }
    }

    private async Task HandleClient(TcpClient client)
    {
        if (!_running)
        {
            client.Dispose();
            return;
        }

        var ep = client.Client.RemoteEndPoint as IPEndPoint;
        string ip = ep?.Address.ToString() ?? "?";
        int port = ep?.Port ?? 0;

        // CC + IP check
        if (_ban.CheckCC(ip, _cfg.SpeedNum)) { Log("BAN", $"CC block: {ip}"); client.Dispose(); return; }
        if (_ban.IsIPBlocked(ip)) { Log("BAN", $"Blocked: {ip}"); client.Dispose(); return; }

        var session = _sessions.Acquire(ip, port);
        if (session == null) { Log("WARN", $"Slots full, reject {ip}"); client.Dispose(); return; }
        var generation = session.Generation;

        session.TcpClient = client;
        session.LastPacketTime = Environment.TickCount64;
        session.LastCleanTime = Environment.TickCount64; // Fix 7: track clean start
        Interlocked.Increment(ref TotalClients);
        Log("CONNECT", $"{ip}:{port} (ID:{session.SessionId})");

        // Fix 7: RecoveryAttempts — check if this IP exceeded recovery attempts and was permanently banned
        if (_ban.CheckRecoveryBan(ip))
        {
            Log("BAN", $"Permanent ban (recovery limit): {ip}");
            client.Dispose();
            _sessions.Release(session.SessionId, generation);
            return;
        }

        // Bidirectional relay using the original shared-backend topology:
        // Client↔GameGate: MobileCodec binary frames (0xFF3A3A44/0xFF44FF44)
        // GameGate↔DBSvr: 6bit AccountPacket (%A{handle}/{encoded}$)
        // GameGate↔GameSvr: one shared 77BBAA33 stream routed by ConnID=client handle.
        var cts = new CancellationTokenSource();
        var clientStream = client.GetStream();
        var clientWriteLock = session.ClientWriteLock;
        var deferredClientTasks = new List<Task>();
        var deferredClientTasksLock = new object();
        int sockHandle = (int)client.Client.Handle;
        SharedBackendRoute? route;
        try
        {
            route = await _backend.OpenRouteAsync(sockHandle, ip, generation,
                () =>
                {
                    try { cts.Cancel(); } catch { }
                    try { client.Dispose(); } catch { }
                }, cts.Token);
        }
        catch (Exception ex)
        {
            Log("ERROR", $"shared route open failed: {ex.Message}");
            cts.Dispose();
            await CleanupClientAsync(session, client, null);
            return;
        }
        if (route == null)
        {
            Log("ERROR", "DBSvr shared route open failed");
            cts.Dispose();
            await CleanupClientAsync(session, client, null);
            return;
        }
        session.BackendRouteId = route.ConnId;

        async Task WriteGameSvr(byte[] data, CancellationToken token = default)
        {
            if (session.Generation != generation || !await _backend.SendGameAsync(route, data, token))
                throw new IOException("shared GameSvr route is unavailable");
        }
        Trace("OPEN", $"shared backends route handle={sockHandle} conn=0x{route.ConnId:X8}");

        var bufferedGameFrames = new List<(ushort Ident, byte[] Frame, int BodyLen, int Plen, string Reason)>();
        var bufferedGameFrameBytes = 0;
        var bufferedGameFramesLock = new object();
        var waitClientMainReady = false;
        var clientMainReady = false;
        var delayedFirstPassionAreaState = false;
        var injectedNoticeOk = false;
        using var loginNoticeWriteLock = new SemaphoreSlim(1, 1);
        var sentLogonToClient = false;
        var sentLoginScriptToGameSvr = false;
        long lastQueryBagItemsForwardedAt = 0;
        var softCloseQueryPending = 0;

        async Task<bool> SendLoginNoticeOkOnce(byte[] frame, string source)
        {
            await loginNoticeWriteLock.WaitAsync(cts.Token);
            try
            {
                lock (bufferedGameFramesLock)
                {
                    if (injectedNoticeOk) return false;
                }

                await WriteGameSvr(frame, cts.Token);
                lock (bufferedGameFramesLock) injectedNoticeOk = true;
                Trace("GS", $"CM_LOGINNOTICEOK sent by {source}");
                return true;
            }
            finally
            {
                loginNoticeWriteLock.Release();
            }
        }

        async Task QueryCharactersAfterSoftClose()
        {
            if (Interlocked.Exchange(ref softCloseQueryPending, 1) != 0)
            {
                Trace("UP", "Duplicate CM_SOFTCLOSE character query suppressed");
                return;
            }

            try
            {
                await Task.Delay(SoftCloseQueryDelayMs, cts.Token);
                var account = session.Account;
                var sessionId = session.DBSessionId;
                if (string.IsNullOrEmpty(account) || sessionId <= 0)
                {
                    Trace("UP", $"CM_SOFTCLOSE character query skipped: account/session unavailable ({account ?? "<null>"}/{sessionId})");
                    return;
                }

                lock (bufferedGameFramesLock)
                {
                    bufferedGameFrames.Clear();
                    bufferedGameFrameBytes = 0;
                    waitClientMainReady = false;
                    clientMainReady = false;
                    delayedFirstPassionAreaState = false;
                    injectedNoticeOk = false;
                    sentLogonToClient = false;
                    sentLoginScriptToGameSvr = false;
                }
                lastQueryBagItemsForwardedAt = 0;

                var headerMsg = CreateSoftCloseQueryPacket(sessionId);
                var queryBody = HUtil32.GetBytes(EDcode.EncodeString($"{account}/{sessionId}"));
                var bodyEnc = Enc6Body(queryBody);
                var bodyStr = bodyEnc.Length > 0 ? HUtil32.GetString(bodyEnc, 0, bodyEnc.Length) : "";
                var apFrame = HUtil32.GetBytes($"%A{sockHandle}/#{EDcode.EncodeMessage(headerMsg)}{bodyStr}!$");
                if (!await _backend.SendDbAsync(route, apFrame, cts.Token))
                {
                    Log("UP", "CM_SOFTCLOSE character query failed: DBSvr unavailable");
                    return;
                }
                Trace("UP", $"CM_SOFTCLOSE → DBSvr CM_QUERYCHR account={account} session={sessionId} marker={SoftCloseQueryParam}");
            }
            finally
            {
                Volatile.Write(ref softCloseQueryPending, 0);
            }
        }

        async Task WriteClientMobileFrame(byte[] frame)
        {
            var wire = session.IsTiger
                ? Encoding.ASCII.GetBytes(TigerCodec.Encode(frame, session.TigerKeyOffset))
                : frame;
            await clientWriteLock.WaitAsync(cts.Token);
            try
            {
                await clientStream.WriteAsync(wire, 0, wire.Length, cts.Token);
            }
            finally
            {
                clientWriteLock.Release();
            }
        }

        async Task FlushBufferedGameFrames(string reason)
        {
            var throttleExtraFrames = false;
            var throttledFrameCount = 0;
            while (true)
            {
                List<(ushort Ident, byte[] Frame, int BodyLen, int Plen, string Reason)> frames;
                lock (bufferedGameFramesLock)
                {
                    if (bufferedGameFrames.Count == 0)
                    {
                        clientMainReady = true;
                        waitClientMainReady = false;
                        return;
                    }

                    frames = bufferedGameFrames.ToList();
                    bufferedGameFrames.Clear();
                    bufferedGameFrameBytes = 0;
                }

                foreach (var frameInfo in frames)
                {
                    if (!client.Connected) return;
                    if (throttleExtraFrames)
                    {
                        if (throttledFrameCount == 0)
                        {
                            await Task.Delay(PostLoginExtraFrameDelayMs, cts.Token);
                        }
                        else if (throttledFrameCount % BufferedFrameChunkSize == 0)
                        {
                            await Task.Delay(BufferedFrameChunkDelayMs, cts.Token);
                        }

                        throttledFrameCount++;
                    }

                    await WriteClientMobileFrame(frameInfo.Frame);
                    session.TotalSentBytes += frameInfo.Frame.Length;
                    Trace("GS", $"=>Client ident={frameInfo.Ident} body={frameInfo.BodyLen}B plen={frameInfo.Plen} flushed {reason} {frameInfo.Reason}".TrimEnd());

                    if (frameInfo.Ident == SM_MAPDESCRIPTION)
                        throttleExtraFrames = true;
                }
            }
        }

        async Task RelayUp()
        {
            // Helper: encode the standard outer DATA command with an inner ClientPacket.
            byte[] E(MobileCodec.InnerHeader inner, byte[] body, uint seq)
            {
                return MobileCodec.WriteFrame(inner, body ?? Array.Empty<byte>(), seq,
                    MobileCodec.MARKER_DATA);
            }

            var buf = new byte[8192];
            var accBuf = new byte[InitialClientReceiveBuffer];
            int accLen = 0;
            try
            {
                while (_running && client.Connected)
                {
                    int n = await clientStream.ReadAsync(buf, 0, buf.Length, cts.Token);
                    if (n <= 0) break;

                    session.TotalRecvBytes += n;
                    session.LastPacketTime = Environment.TickCount64;
                    Interlocked.Add(ref TotalBytesUp, n);

                    // Append to accumulation buffer
                    if (accLen + n > MaximumClientReceiveBuffer)
                        throw new InvalidDataException("client frame buffer overflow");
                    if (accLen + n > accBuf.Length)
                        Array.Resize(ref accBuf, Math.Min(MaximumClientReceiveBuffer,
                            Math.Max(accLen + n, accBuf.Length * 2)));
                    Buffer.BlockCopy(buf, 0, accBuf, accLen, n);
                    accLen += n;

                    // ── Tiger (BaiZhu) protocol detection ──
                    if (_cfg.OpenNewTigerGate)
                    {
                        int lhIdx;
                        while ((lhIdx = TigerCodec.FindLHSuffix(accBuf, 0, accLen)) >= 0)
                        {
                            try
                            {
                                string tigerStr = Encoding.ASCII.GetString(accBuf, 0, lhIdx);
                                byte[] decoded = TigerCodec.Decode(tigerStr, session.TigerKeyOffset);

                                if (!session.IsTiger)
                                {
                                    session.IsTiger = true;
                                    Trace("TIGER", $"Tiger protocol detected for {ip} (ID:{session.SessionId})");
                                }

                                // Update key offset: client uses SessionId (dataIndex) after first frame
                                session.TigerKeyOffset = (uint)(session.SessionId > 0 ? session.SessionId : 1);
                                Trace("TIGER", $"Decoded {tigerStr.Length}B tiger → {decoded.Length}B binary (keyOff={session.TigerKeyOffset})");

                                // Replace accBuf: decoded binary + any trailing data after |LH
                                int tigerConsumed = lhIdx + 3;
                                int remaining = accLen - tigerConsumed;
                                var decodedLength = decoded.Length + Math.Max(0, remaining);
                                if (decodedLength > MaximumClientReceiveBuffer)
                                    throw new InvalidDataException("decoded Tiger frame buffer overflow");
                                if (decodedLength > accBuf.Length)
                                    Array.Resize(ref accBuf, Math.Min(MaximumClientReceiveBuffer,
                                        Math.Max(decodedLength, accBuf.Length * 2)));
                                if (remaining > 0)
                                    Buffer.BlockCopy(accBuf, tigerConsumed, accBuf, decoded.Length, remaining);
                                Buffer.BlockCopy(decoded, 0, accBuf, 0, decoded.Length);
                                accLen = decodedLength;
                            }
                            catch (FormatException ex)
                            {
                                Log("TIGER", $"Invalid Tiger data, treating as binary: {ex.Message}");
                                break;
                            }
                        }
                    }

                    while (accLen > 0)
                    {
                        var parsed = MobileCodec.TryReadFrame(accBuf, 0, accLen,
                            out var mf, out int consumed);
                        if (!parsed)
                        {
                            if (consumed <= 0) break;
                            accLen -= consumed;
                            if (accLen > 0)
                                Buffer.BlockCopy(accBuf, consumed, accBuf, 0, accLen);
                            continue;
                        }

                        // Remove consumed bytes
                        accLen -= consumed;
                        if (accLen > 0) Buffer.BlockCopy(accBuf, consumed, accBuf, 0, accLen);

                        Interlocked.Increment(ref session.TotalPackets);
                        Interlocked.Increment(ref TotalPacketsUp);

                        Trace("FRAME", $"{mf.Header.Marker}/{mf.Inner.Ident} recog={mf.Inner.Recog} body={mf.Body?.Length ?? 0}B");

                        // === 控制帧处理 (44FF44FF, 对照战神文档 + 手游协议分析.md §5) ===
                        if (mf.Header.Marker == MobileCodec.MARKER_CONNECT) // 0x1802
                        {
                            // :7100 登录阶段: CONNECT → SERVER_INFO(0x2C, SM_LOGIN=4003)
                            // 客户端 processMsg 收到 SM_LOGIN 后发 CM_LOGIN_AUTH(0x59)
                            var siInner = new MobileCodec.InnerHeader { Recog = 0, Ident = 4003 }; // SM_LOGIN
                            var siFrame = E(siInner, new byte[44], mf.Header.Seq);
                            await WriteClientMobileFrame(siFrame);
                            Trace("SEND", $"→ SERVER_INFO(0x2C) SM_LOGIN");
                            continue;
                        }
                        if (mf.Header.Marker == MobileCodec.MARKER_PING)
                        {
                            Interlocked.Increment(ref session.HeartbeatCount);
                            var pongBytes = new byte[12];
                            // MARKER_PONG = 0x19FA
                            BitConverter.GetBytes(MobileCodec.SIGN).CopyTo(pongBytes, 0);
                            BitConverter.GetBytes((ushort)0x19FA).CopyTo(pongBytes, 4);
                            BitConverter.GetBytes(mf.Header.Seq).CopyTo(pongBytes, 8);
                            await WriteClientMobileFrame(pongBytes);
                            continue;
                        }
                        if (mf.Header.Marker == MobileCodec.MARKER_DISCONNECT)
                        {
                            Trace("DISCONNECT", $"Client disconnected: {ip}");
                            break;
                        }

                        // ── Encryption & dynamic encoding handlers ──
                        // GET_ENCRYPT: client requests encryption key → respond with key
                        if (mf.Header.Cmd == 24)
                        {
                            session.EncryptKey = (uint)Random.Shared.Next();
                            var keyResponse = MobileCodec.WriteSimpleFrame(24, 0,
                                BitConverter.GetBytes(session.EncryptKey), mf.Header.Seq);
                            await WriteClientMobileFrame(keyResponse);
                            Trace("CRYPT", $"Key exchange: key={session.EncryptKey:X8}");
                            continue;
                        }

                        if (mf.Header.Marker != MobileCodec.MARKER_DATA) continue;

                        // CMD 29: Tiger (BaiZhu) heartbeat — echo back as pong
                        if (mf.Header.Cmd == TigerCodec.CMD_HEARTBEAT)
                        {
                            var hbPong = MobileCodec.WriteSimpleFrame(TigerCodec.CMD_HEARTBEAT, 0,
                                Array.Empty<byte>(), mf.Header.Seq);
                            await WriteClientMobileFrame(hbPong);
                            Trace("HEARTBEAT", $"Tiger heartbeat pong seq={mf.Header.Seq}");
                            continue;
                        }

                        // ── Whitelist filtering (Fix 4) ──
                        if (!_cfg.IsMsgAllowed(mf.Inner.Ident, isUpstream: true))
                        {
                            Log("WHITELIST", $"Blocked upstream ident={mf.Inner.Ident}");
                            Interlocked.Increment(ref TotalRejected);
                            continue;
                        }

                        // ── Speed detection (Fix 5: DelayQueue integration) ──
                        // 角色管理/登录命令不加速检 (4012-4017, 4004, 4002, 100-107, 4039, 4041, 4031)
                        bool isCharCmd = mf.Inner.Ident is 4004 or 4002 or (>= 4012 and <= 4017)
                                         or (>= 100 and <= 107) or 4041 or 4039 or 4031;
                        var actionType = default(ActionType);
                        var hasActionType = !isCharCmd && ActionClassifier.TryClassify(mf.Inner.Ident, out actionType);
                        if (hasActionType && !_speed.Check(session, actionType))
                        {
                            // Fix 6: TurnPack penalty — set TurnPack flag on TURN violations
                            if (actionType == ActionType.TURN)
                            {
                                session.TurnPack = true;
                                Trace("TURNPACK", $"[{ip}] TurnPack penalty activated");
                            }

                            // Speed violation — delay the packet instead of dropping immediately
                            var delayedBody = mf.Body ?? Array.Empty<byte>();
                            var delayedClientPacket = CreateGameSvrClientPacket(mf.Inner, mf.Inner.Ident);
                            var delayedPayload = new byte[ClientPacket.PackSize + delayedBody.Length];
                            Buffer.BlockCopy(delayedClientPacket.GetBuffer(), 0, delayedPayload, 0, ClientPacket.PackSize);
                            if (delayedBody.Length > 0)
                                Buffer.BlockCopy(delayedBody, 0, delayedPayload, ClientPacket.PackSize, delayedBody.Length);
                            var delayedPacket = CreateGameDataPacket(route.ConnId,
                                route.NextSequence(), delayedPayload);
                            _delayQueue.Enqueue(new DelayedPacket
                            {
                                Data = delayedPacket.ToBytes(),
                                SessionId = session.SessionId,
                                Generation = generation,
                                IsUpstream = true
                            });
                            Trace("SPEED", $"[{ip}] {actionType} delayed — ident={mf.Inner.Ident}");
                            Interlocked.Increment(ref TotalDropped);
                            continue;
                        }

                        // Fix 6: TurnPack — skip forwarding TURN packets when penalty is active
                        if (hasActionType && actionType == ActionType.TURN && session.TurnPack)
                        {
                            Trace("TURNPACK", $"[{ip}] Turn packet dropped (TurnPack active)");
                            Interlocked.Increment(ref TotalDropped);
                            continue;
                        }

                        // Fix 6: Reset TurnPack after clean window (5 seconds with no TurnPack packets)
                        if (session.TurnPack &&
                            Environment.TickCount64 - session.LastPacketTime > 5000)
                        {
                            session.TurnPack = false;
                        }

                        // Fix 1: Abusive filter on chat messages (ident 0x0008 or 0x009A)
                        if (hasActionType && actionType == ActionType.CHAT && mf.Body is { Length: > 0 })
                        {
                            var chatText = HUtil32.GetString(mf.Body, 0, mf.Body.Length);
                            var (filtered, shouldDrop) = _abusiveFilter.Filter(chatText);
                            if (shouldDrop)
                            {
                                Log("ABUSIVE", $"DropConnect filter match from {session.RemoteAddr}");
                                session.BanFlag = true; // mark as server-kicked, skip recovery counting
                                try { client.Dispose(); } catch { }
                                cts.Cancel();
                                return;
                            }
                            OnChat?.Invoke(session, filtered.TrimEnd('\0'));
                            if (_ban.IsNameMuted(session.CharName ?? session.Account))
                            {
                                Log("CHAT", $"Muted message blocked: {session.CharName ?? session.Account ?? session.RemoteAddr}");
                                Interlocked.Increment(ref TotalDropped);
                                continue;
                            }
                            // Replace body with filtered text
                            var filteredBytes = HUtil32.GetBytes(filtered);
                            if (filteredBytes.Length != (mf.Body?.Length ?? 0))
                            {
                                // Body length changed; update mf.Body
                                mf.Body = filteredBytes;
                            }
                            else if (mf.Body != null)
                            {
                                Buffer.BlockCopy(filteredBytes, 0, mf.Body, 0, filteredBytes.Length);
                            }
                        }

                        // Fix 4: HWID collection — extract HWID from CMD_CLIENT_INFO packets
                        if ((mf.Inner.Ident == 0x005D || mf.Inner.Ident == 0x0065) && mf.Body is { Length: > 0 })
                        {
                            var hwidRaw = HUtil32.GetString(mf.Body, 0, mf.Body.Length).TrimEnd('\0');
                            if (!string.IsNullOrEmpty(hwidRaw) && hwidRaw.Length >= 8)
                            {
                                session.HWID = BanSystem.ComputeHWID(session.RemoteAddr,
                                    hwidRaw.Length >= 12 ? hwidRaw[..12] : hwidRaw,
                                    hwidRaw.Length >= 18 ? hwidRaw[12..18] : "");
                                Trace("HWID", $"[{ip}] HWID collected");

                                // Fix 4: Check HWID against block list
                                if (_ban.IsHWIDBlocked(session.HWID))
                                {
                                    Log("BAN", $"HWID blocked: {session.HWID} from {session.RemoteAddr}");
                                    session.BanFlag = true; // mark as server-kicked, skip recovery counting
                                    try { client.Dispose(); } catch { }
                                    cts.Cancel();
                                    return;
                                }
                            }
                        }

                        // 全部 DATA 帧转发 DBSvr :5100
                        // 战神架构: GameGate 纯转发, DBSvr/GameSvr 处理业务逻辑
                        byte[] bodyToSend = mf.Body ?? Array.Empty<byte>();
                        ushort fwdIdent = mf.Inner.Ident;
                        ushort serverIdent = MobileCmdMap.ToServer(fwdIdent);

                        if (fwdIdent == CM_QUERYBAGITEMS)
                        {
                            var now = Environment.TickCount64;
                            if (lastQueryBagItemsForwardedAt > 0 &&
                                now - lastQueryBagItemsForwardedAt < QueryBagItemsMinIntervalMs)
                            {
                                Trace("UP", $"Dropped duplicate CM_QUERYBAGITEMS within {QueryBagItemsMinIntervalMs / 1000}s");
                                continue;
                            }

                            lastQueryBagItemsForwardedAt = now;
                        }

                        if (IsLoginScriptCall(fwdIdent, bodyToSend))
                        {
                            if (sentLoginScriptToGameSvr)
                            {
                                Trace("UP", "Dropped duplicate login script call");
                                continue;
                            }

                            sentLoginScriptToGameSvr = true;
                        }

                        // 从认证请求 Recog 提取 sessionId (客户端生成的会话标识)
                        if (fwdIdent == 4004 && mf.Inner.Recog > 0) { session.DBSessionId = mf.Inner.Recog; Trace("UP", $"SessionId→session: {mf.Inner.Recog}"); }

                        // 从 SELECT_CHR 请求中提取角色名 (body=GBK_bytes+\0, 7B total)
                        if (fwdIdent == 4017 && bodyToSend.Length > 1)
                        {
                            var chrName = HUtil32.GetString(bodyToSend, 0, bodyToSend.Length).TrimEnd('\0');
                            if (!string.IsNullOrEmpty(chrName))
                            {
                                session.CharName = chrName;
                                Trace("UP", $"CharName→session: {chrName}");
                                if (_ban.IsNameBlocked(chrName))
                                {
                                    Log("BAN", $"Blocked player name: {chrName} from {session.RemoteAddr}");
                                    session.BanFlag = true;
                                    try { client.Dispose(); } catch { }
                                    cts.Cancel();
                                    return;
                                }
                            }
                        }

                        if (isCharCmd)
                        {
                            Trace("UP", $"→DBSvr {fwdIdent}→{serverIdent} body={bodyToSend.Length}B");
                            // EDcode header(16 chars) + 6-bit body, 与 DBSvr SendUserSocket 格式一致
                            var headerMsg = new ClientPacket { Recog = mf.Inner.Recog, Ident = serverIdent,
                                Param = mf.Inner.Param, Tag = mf.Inner.Tag, Series = mf.Inner.Series };
                            var headerStr = EDcode.EncodeMessage(headerMsg);
                            var bodyEnc = Enc6Body(bodyToSend);
                            var bodyStr = bodyEnc.Length > 0 ? HUtil32.GetString(bodyEnc, 0, bodyEnc.Length) : "";
                            var apFrame = HUtil32.GetBytes($"%A{sockHandle}/#{headerStr}{bodyStr}!$");
                            if (!await _backend.SendDbAsync(route, apFrame, cts.Token))
                                throw new IOException("shared DBSvr route is unavailable");
                        }
                        else
                        {
                            Trace("UP", $"→GameSvr ident={fwdIdent} body={bodyToSend.Length}B");
                            byte[] gsBody;
                            if (fwdIdent == 1018)
                            {
                                // CM_LOGINNOTICEOK: 构造 EDcode 认证字符串
                                // sessionId 从客户端认证请求(4004)的 Recog 提取, 与原版一致
                                var acct = session.Account ?? "guest";
                                var chr = session.CharName ?? acct;
                                var sid = session.DBSessionId > 0 ? session.DBSessionId : 2;
                                var certPlain = $"**{acct}/{chr}/{sid}/1/0/0123456789ABCDEF";
                                var certBody = HUtil32.GetBytes(EDcode.EncodeString(certPlain));
                                var cp = CreateGameSvrClientPacket(mf.Inner, fwdIdent);
                                cp.Recog = 0;
                                cp.Ident = 1018;
                                UpdateClientActionState(session, cp);
                                gsBody = new byte[ClientPacket.PackSize + certBody.Length];
                                Buffer.BlockCopy(cp.GetBuffer(), 0, gsBody, 0, ClientPacket.PackSize);
                                Buffer.BlockCopy(certBody, 0, gsBody, ClientPacket.PackSize, certBody.Length);
                            }
                            else
                            {
                                 // 其他游戏数据: ClientPacket(12B) + body
                                 var cp = CreateGameSvrClientPacket(mf.Inner, fwdIdent);
                                 UpdateClientActionState(session, cp);
                                 gsBody = new byte[ClientPacket.PackSize + bodyToSend.Length];
                                Buffer.BlockCopy(cp.GetBuffer(), 0, gsBody, 0, ClientPacket.PackSize);
                                if (bodyToSend.Length > 0) Buffer.BlockCopy(bodyToSend, 0, gsBody, ClientPacket.PackSize, bodyToSend.Length);
                            }
                            // 77BBAA33: Cmd=GM_DATA(5), Payload=gsBody
                            var pkt = CreateGameDataPacket(route.ConnId,
                                route.NextSequence(), gsBody);
                            if (fwdIdent == 1018)
                            {
                                var sent = await SendLoginNoticeOkOnce(pkt.ToBytes(), "client 1018");
                                await FlushBufferedGameFrames("after client 1018");
                                if (!sent) Trace("UP", "Duplicate CM_LOGINNOTICEOK suppressed");
                            }
                            else
                            {
                                await WriteGameSvr(pkt.ToBytes(), cts.Token);
                            }
                            if (fwdIdent == Grobal2.CM_SOFTCLOSE)
                            {
                                await QueryCharactersAfterSoftClose();
                            }
                        }
                    }

                    if (accLen == 0 && accBuf.Length > InitialClientReceiveBuffer)
                        Array.Resize(ref accBuf, InitialClientReceiveBuffer);
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (Exception ex) { Log("DEBUG", $"Up relay error [{ip}]: {ex.Message}"); }
            finally { cts.Cancel(); }
        }

        // 6-bit encode raw body bytes (no header — EDcode.EncodeMessage handles that)
        static byte[] Enc6Body(byte[]? body)
        {
            int bodyLen = body?.Length ?? 0;
            if (bodyLen == 0) body = Array.Empty<byte>(); // 确保始终有ClientPacket字节
            var enc = new byte[(body?.Length ?? 0) * 2 + 4];
            int encLen = Misc.Encode6BitBufDirect(body, body!.Length, enc);
            return encLen > 0 ? enc.Take(encLen).ToArray() : Array.Empty<byte>();
        }

        async Task RelayDown()
        {
            // DBSvr response format: %{connId}/#{EDcode_header}{6bit_body}!$
            // EDcode header is 16 chars (Grobal2.DEFBLOCKSIZE), body is 6bit-encoded
            try
            {
                while (_running)
                {
                    var downBuf = await route.DbResponses.Reader.ReadAsync(cts.Token);
                    int n = downBuf.Length;
                    if (n <= 0) break;
                    var accBuf = downBuf;
                    var accLen = n;
                    Interlocked.Add(ref TotalBytesDown, n);
                    Interlocked.Increment(ref TotalPacketsDown);

                    // Parse DBSvr response: %{connId}/#{edCode}{6bit}!$
                    while (accLen >= 10)
                    {
                        int dollar = Array.IndexOf(accBuf, (byte)'$', 0, accLen);
                        if (dollar < 0) break;
                        int percent = Array.IndexOf(accBuf, (byte)'%', 0, dollar);
                        if (percent < 0) { accLen = 0; break; }
                        int slash = Array.IndexOf(accBuf, (byte)'/', percent + 1, dollar - percent - 1);
                        if (slash < 0) { accLen = dollar + 1; break; }

                        // Extract data between / and $, e.g. #{EDcode_header}{6bit_body}!
                        int dataStart = slash + 1;
                        int dataLen = dollar - dataStart;
                        if (dataLen <= 2) { accLen = 0; break; }

                        var dataText = HUtil32.GetString(accBuf, dataStart, dataLen); // GBK, 与DBSvr一致
                        // Strip optional # prefix and ! suffix
                        if (dataText.StartsWith("#")) dataText = dataText.Substring(1);
                        if (dataText.EndsWith("!")) dataText = dataText.Substring(0, dataText.Length - 1);

                        Trace("DOWN", $"DBSvr response: connId={System.Text.Encoding.UTF8.GetString(accBuf, percent+1, slash-percent-1)} dataLen={dataText.Length}");

                        // First 16 chars = EDcode header, rest = 6bit body
                        if (dataText.Length >= Grobal2.DEFBLOCKSIZE)
                        {
                            var headerText = dataText.Substring(0, Grobal2.DEFBLOCKSIZE);
                            var bodyText = dataText.Length > Grobal2.DEFBLOCKSIZE
                                ? dataText.Substring(Grobal2.DEFBLOCKSIZE) : "";

                            var decodedHeader = EDcode.DecodePacket(headerText);
                            if (decodedHeader != null)
                            {
                                byte[] body = Array.Empty<byte>();
                                if (!string.IsNullOrEmpty(bodyText))
                                {
                                    var bodyBytes = HUtil32.GetBytes(bodyText);
                                    body = Misc.Decode6BitBufDirect(bodyBytes, bodyBytes.Length);
                                }

                                // 从 DBSvr 响应中提取 account 存入 session
                                if (decodedHeader.Ident == 4004 && body.Length > 0) // SM_LOGIN (mobile auth)
                                {
                                    var acctStr = HUtil32.GetString(body, 0, body.Length);
                                    int ni = acctStr.IndexOf('\0');
                                    if (ni > 0) acctStr = acctStr.Substring(0, ni);
                                    if (!string.IsNullOrEmpty(acctStr)) { session.Account = acctStr; Trace("DOWN", $"Account→session: {acctStr}"); }
                                }
                                else if (body.Length > 0)
                                {
                                    if (decodedHeader.Ident == SM_STARTPLAY) // SM_STARTPLAY - 选角完成, 注入 CM_LOGINNOTICEOK 给 GameSvr
                                    {
                                        var chrStr = HUtil32.GetString(body, 0, body.Length);
                                        int ni = chrStr.IndexOf('\0');
                                        if (ni > 0) chrStr = chrStr.Substring(0, ni);
                                        if (!string.IsNullOrEmpty(chrStr))
                                        {
                                            session.CharName = chrStr;
                                            Trace("DOWN", $"CharName→session: {chrStr}");
                                            if (_ban.IsNameBlocked(chrStr))
                                            {
                                                Log("BAN", $"Blocked player name: {chrStr} from {session.RemoteAddr}");
                                                session.BanFlag = true;
                                                try { client.Dispose(); } catch { }
                                                cts.Cancel();
                                                return;
                                            }
                                        }
                                    }
                                }

                                UpdatePlayerState(session, decodedHeader, body);

                                if (decodedHeader.Ident == SM_STARTPLAY)
                                {
                                    lock (bufferedGameFramesLock)
                                    {
                                        bufferedGameFrames.Clear();
                                        bufferedGameFrameBytes = 0;
                                        waitClientMainReady = true;
                                        clientMainReady = false;
                                        injectedNoticeOk = false;
                                    }
                                }

                                var inner = new MobileCodec.InnerHeader {
                                    Recog = decodedHeader.Recog, Ident = MobileCmdMap.ToClient(decodedHeader.Ident),
                                    Param = decodedHeader.Param, Tag = decodedHeader.Tag, Series = decodedHeader.Series
                                };
                                var frame = MobileCodec.WriteFrame(inner, body, 0,
                                    MobileCodec.MARKER_DATA);
                                session.TotalSentBytes += frame.Length;
                                if (client.Connected)
                                {
                                    await WriteClientMobileFrame(frame);
                                }
                                Trace("DOWN", $"Sent to client: ident={decodedHeader.Ident} body={body.Length}B hex={BitConverter.ToString(body).Replace("-"," ")}");

                                if (decodedHeader.Ident == SM_STARTPLAY)
                                {
                                    var acct = session.Account ?? "guest";
                                    var chr = session.CharName ?? acct;
                                    var sid = session.DBSessionId > 0 ? session.DBSessionId : 2;
                                    var certPlain = $"**{acct}/{chr}/{sid}/1/0/0123456789ABCDEF";
                                    var gsBody = HUtil32.GetBytes(EDcode.EncodeString(certPlain));
                                    var cp = new ClientPacket { Recog = 0, Ident = 1018, Param = 0, Tag = 0, Series = 0 };
                                    var pktBody = new byte[ClientPacket.PackSize + gsBody.Length];
                                    Buffer.BlockCopy(cp.GetBuffer(), 0, pktBody, 0, ClientPacket.PackSize);
                                    Buffer.BlockCopy(gsBody, 0, pktBody, ClientPacket.PackSize, gsBody.Length);
                                    var pkt = CreateGameDataPacket(route.ConnId,
                                        route.NextSequence(), pktBody);
                                    try
                                    {
                                        if (await SendLoginNoticeOkOnce(pkt.ToBytes(), "SM_STARTPLAY injection"))
                                            Trace("GS", "Buffering game packets until client 1018");
                                        else
                                            Trace("GS", "SM_STARTPLAY injection skipped; client 1018 won the race");
                                    }
                                    catch (Exception ex)
                                    {
                                        Log("ERROR", $"Failed to inject CM_LOGINNOTICEOK: {ex.Message}");
                                        throw new IOException("GameSvr login certification failed", ex);
                                    }
                                }
                            }
                        }

                        // Consume processed frame
                        int consumed = dollar + 1;
                        if (consumed < accLen)
                            Buffer.BlockCopy(accBuf, consumed, accBuf, 0, accLen - consumed);
                        accLen -= consumed;
                        if (accLen < 0) accLen = 0;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (Exception ex) { Log("DEBUG", $"Down relay error [{ip}]: {ex.Message}"); }
            finally { cts.Cancel(); }
        }

        // GameSvr relay: 77BBAA33 frames → 44FF44FF frames → client
        async Task RelayGameSvr()
        {
            Trace("GS", $"RelayGameSvr START sharedConnected={_backend.GameConnected}");

            async Task SendClientGameFrame(ushort ident, int recog, ushort param, ushort tag, ushort series, byte[] frameBody, string reason)
            {
                byte[]? delayedAreaStateFrame = null;
                var delayedAreaStateReason = reason;
                var delayedAreaStatePlen = MobileCodec.INNER_SIZE + frameBody.Length;

                if (ident == SM_AREASTATE_CLIENT && recog != 2 && !delayedFirstPassionAreaState)
                {
                    var originalAreaState = recog;
                    delayedFirstPassionAreaState = true;
                    var delayedInner = new MobileCodec.InnerHeader
                    {
                        Recog = recog,
                        Ident = ident,
                        Param = param,
                        Tag = tag,
                        Series = series
                    };
                    delayedAreaStateFrame = MobileCodec.WriteFrame(delayedInner, frameBody, 0,
                        MobileCodec.MARKER_DATA);
                    delayedAreaStateReason = string.IsNullOrEmpty(reason)
                        ? $"login areaState {originalAreaState} delayed"
                        : $"{reason} login areaState {originalAreaState} delayed";

                    recog = 2;
                    reason = string.IsNullOrEmpty(reason)
                        ? $"temporary safe before delayed areaState {originalAreaState}"
                        : $"{reason} temporary safe before delayed areaState {originalAreaState}";
                }

                if (ident == SM_LOGON)
                {
                    lock (bufferedGameFramesLock)
                    {
                        if (sentLogonToClient)
                        {
                            Trace("GS", "Dropped duplicate SM_LOGON to client");
                            return;
                        }

                        sentLogonToClient = true;
                    }
                }

                var inner = new MobileCodec.InnerHeader
                {
                    Recog = recog,
                    Ident = ident,
                    Param = param,
                    Tag = tag,
                    Series = series
                };
                int plen = MobileCodec.INNER_SIZE + frameBody.Length;
                var frame = MobileCodec.WriteFrame(inner, frameBody, 0, MobileCodec.MARKER_DATA);

                bool shouldBuffer;
                bool bufferOverflow = false;
                lock (bufferedGameFramesLock)
                {
                    shouldBuffer = waitClientMainReady && !clientMainReady && ident != SM_SENDNOTICE;
                    if (shouldBuffer)
                    {
                        if (bufferedGameFrames.Count >= MaximumBufferedLoginFrames
                            || bufferedGameFrameBytes > MaximumBufferedLoginBytes - frame.Length)
                            bufferOverflow = true;
                        else
                        {
                            bufferedGameFrames.Add((ident, frame, frameBody.Length, plen, reason));
                            bufferedGameFrameBytes += frame.Length;
                        }
                    }
                }

                if (bufferOverflow)
                    throw new IOException("client login frame buffer exceeded the bounded limit");

                if (shouldBuffer)
                {
                    Trace("GS", $"Buffered client ident={ident} body={frameBody.Length}B plen={plen} {reason}".TrimEnd());
                    return;
                }

                await WriteClientMobileFrame(frame);
                session.TotalSentBytes += frame.Length;
                if (ident == 31 && frameBody.Length >= 32)
                {
                    Trace("GS", $"SM_STRUCK recog={recog} headerHP={param} headerMaxHP={tag} damage={series} " +
                        $"state={BitConverter.ToInt32(frameBody, 4)} attacker={BitConverter.ToInt32(frameBody, 8)} " +
                        $"flag={BitConverter.ToInt32(frameBody, 12)} HP={BitConverter.ToInt32(frameBody, 16)} " +
                        $"MaxHP={BitConverter.ToInt32(frameBody, 20)} MP={BitConverter.ToInt32(frameBody, 24)} " +
                        $"MaxMP={BitConverter.ToInt32(frameBody, 28)}");
                }
                Trace("GS", $"=>Client ident={ident} body={frameBody.Length}B plen={plen} {reason}".TrimEnd());

                if (delayedAreaStateFrame != null)
                {
                    var delayedFrame = delayedAreaStateFrame;
                    var delayedBodyLen = frameBody.Length;
                    var deferredSend = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(LoginPassionAreaStateDelayMs, cts.Token);
                            if (!_running || !client.Connected || cts.IsCancellationRequested)
                            {
                                return;
                            }

                            await WriteClientMobileFrame(delayedFrame);
                            session.TotalSentBytes += delayedFrame.Length;
                            Trace("GS", $"=>Client ident={ident} body={delayedBodyLen}B plen={delayedAreaStatePlen} delayed {LoginPassionAreaStateDelayMs}ms {delayedAreaStateReason}".TrimEnd());
                        }
                        catch (OperationCanceledException)
                        {
                        }
                        catch (Exception ex)
                        {
                            Log("GS", $"Delayed areaState send failed: {ex.Message}");
                        }
                    });
                    lock (deferredClientTasksLock) deferredClientTasks.Add(deferredSend);
                }
            }

            static ushort ToCurrentClientIdent(ushort ident) => ident switch
            {
                SM_AREASTATE_SERVER => SM_AREASTATE_CLIENT,
                SM_SAFE_ZONE_INFO_SERVER => SM_SAFE_ZONE_INFO_CLIENT,
                SM_MAPINFO_EX_SERVER => SM_MAPINFO_EX_CLIENT,
                _ => ident
            };

            try
            {
                while (_running)
                {
                    var pkt = await route.GameResponses.Reader.ReadAsync(cts.Token);
                    if (pkt.FrameLen < InternalPacket77.HEADER_SIZE) continue;
                    Trace("GS", $"77BBAA33 cmd={pkt.Cmd} conn=0x{pkt.ConnID:X8} " +
                        $"frameLen={pkt.FrameLen} payload={pkt.Payload?.Length ?? 0}B");

                    // ── ACK (0x0C) dispatch (Fix 7) — 38% of all packets on the wire ──
                    if (pkt.Cmd == 0x0C)
                    {
                        Trace("GS", $"ACK seq={pkt.SeqID} conn=0x{pkt.ConnID:X8} frameLen={pkt.FrameLen}");
                        session.LastPacketTime = Environment.TickCount64;
                        continue;
                    }

                    if (pkt.Cmd == Grobal2.GM_DATA && pkt.Payload != null && pkt.Payload.Length >= ClientPacket.PackSize)
                    {
                        // 从 Payload 提取 ClientPacket + body
                        var cpBytes = new byte[ClientPacket.PackSize];
                        Buffer.BlockCopy(pkt.Payload, 0, cpBytes, 0, ClientPacket.PackSize);
                        var cp = Packets.ToPacket<ClientPacket>(cpBytes);
                        byte[] body = pkt.Payload.Length > ClientPacket.PackSize
                            ? new byte[pkt.Payload.Length - ClientPacket.PackSize]
                            : Array.Empty<byte>();
                        if (body.Length > 0)
                            Buffer.BlockCopy(pkt.Payload, ClientPacket.PackSize, body, 0, body.Length);

                        if (cp != null)
                        {
                            UpdatePlayerState(session, cp, body);
                            if (client.Connected)
                            {
                                var clientIdent = ToCurrentClientIdent(cp.Ident);
                                var mappedReason = clientIdent == cp.Ident ? string.Empty : $"mapped serverIdent={cp.Ident}";
                                await SendClientGameFrame(clientIdent, cp.Recog, cp.Param, cp.Tag, cp.Series, body, mappedReason);

                                if (cp.Ident == SM_NEWMAP)
                                    await SendClientGameFrame(SM_MAPINFO_EX_CLIENT, 0, 0, 0, 0,
                                        Array.Empty<byte>(), "injected empty mapinfo");
                            }
                        }
                    }
                    else if (pkt.Cmd == Grobal2.GM_SERVERUSERINDEX)
                    {
                        if (pkt.Payload != null && pkt.Payload.Length >= 4)
                            Trace("GS", $"GM_SERVERUSERINDEX userIdx={BitConverter.ToInt32(pkt.Payload, 0)}");
                    }
                    else if (pkt.Cmd == Grobal2.GM_RECEIVE_OK)
                        Trace("GS", "GM_RECEIVE_OK flow-ack");

                    session.LastPacketTime = Environment.TickCount64;
                    Interlocked.Add(ref TotalBytesDown, pkt.FrameLen);
                    Interlocked.Increment(ref TotalPacketsDown);
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (Exception ex) { Log("DEBUG", $"GS relay error: {ex.Message}"); }
        }

        var relayTasks = new[] { RelayUp(), RelayDown(), RelayGameSvr() };
        await Task.WhenAny(relayTasks);
        cts.Cancel();
        try { await Task.WhenAll(relayTasks); } catch { }
        Task[] deferred;
        lock (deferredClientTasksLock) deferred = deferredClientTasks.ToArray();
        if (deferred.Length > 0)
        {
            try { await Task.WhenAll(deferred); } catch { }
        }
        await CleanupClientAsync(session, client, route);
        cts.Dispose();
    }

    private async Task CleanupClientAsync(ClientSession s, TcpClient c, SharedBackendRoute? route)
    {
        var sessionId = s.SessionId;
        var generation = s.Generation;
        var remoteAddr = s.RemoteAddr;
        var wasBanned = s.State == SessionState.BANNED || s.BanFlag;

        await _backend.CloseRouteAsync(route);
        s.BackendRouteId = 0;
        Interlocked.Increment(ref TotalDisconnects);

        // Fix 7: Record recovery attempt on disconnect
        var ip = remoteAddr;
        if (!string.IsNullOrEmpty(ip))
        {
            // Only count as recovery attempt if the session wasn't already banned
            if (!wasBanned)
            {
                bool permBanned = _ban.RecordRecoveryAttempt(ip);
                if (permBanned)
                    Log("BAN", $"Permanent ban (20 recovery attempts): {ip}");
            }
            // Reset recovery if session was clean for 5+ minutes
            if (Environment.TickCount64 - s.LastCleanTime > 300000)
            {
                _ban.ResetRecoveryAttempts(ip);
            }
        }

        try { c.Dispose(); } catch { }
        _sessions.Release(sessionId, generation);
        Log("CONNECT", $"Disconnected: {remoteAddr} (ID:{sessionId})");
        OnStatsChanged?.Invoke();
    }

    private async Task CleanupLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (_running)
            {
                await Task.Delay(10000, cancellationToken);
                long now = Environment.TickCount64;
                foreach (var s in _sessions.GetAllActive())
                {
                    if (now - s.LastPacketTime > 300000) // 5min idle
                    {
                        s.State = SessionState.CLOSING;
                        if (s.TcpClient is TcpClient c) try { c.Dispose(); } catch { }
                    }

                    // Fix 7: Reset recovery attempts after 5 minutes of clean behavior
                    if (s.LastCleanTime > 0 && now - s.LastCleanTime > 300000)
                    {
                        _ban.ResetRecoveryAttempts(s.RemoteAddr);
                        s.LastCleanTime = now;
                        Trace("RECOVERY", $"Recovery attempts reset for {s.RemoteAddr}");
                    }

                    // Fix 6: Reset TurnPack after 10s if no packets
                    if (s.TurnPack && now - s.LastPacketTime > 10000)
                        s.TurnPack = false;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    public void DisconnectClient(int id)
    {
        var s = _sessions.Get(id);
        if (s != null) DisconnectClient(id, s.Generation);
    }

    public bool DisconnectClient(int id, long generation)
    {
        if (!_sessions.TryBeginClose(id, generation, out var client) || client == null)
            return false;
        try { client.Dispose(); } catch { }
        return true;
    }

    public void BanClient(int id) { var s = _sessions.Get(id); if (s != null) { _ban.BlockIP(s.RemoteAddr); DisconnectClient(id); } }

    public void ReloadAbusiveFilter() =>
        _abusiveFilter.ReloadRules(Path.Combine(_cfg.ConfigDir, "AbusiveFilter.txt"));

    public void ReloadRuntimeSettings()
    {
        _speed.ReloadLimits();
        _ban.AutoBanDuration = Math.Max(60, _cfg.BlackTime * 60);
    }

    public async Task<bool> SendSystemMessageAsync(int id, string message,
        CancellationToken cancellationToken = default)
    {
        var session = _sessions.Get(id);
        if (session == null) return false;
        return await SendSystemMessageAsync(id, session.Generation, message, cancellationToken);
    }

    public async Task<bool> SendSystemMessageAsync(int id, long generation, string message,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;
        var session = _sessions.Get(id, generation);
        if (session?.TcpClient is not TcpClient client || !client.Connected) return false;
        var inner = new MobileCodec.InnerHeader
        {
            Recog = 0,
            Ident = Grobal2.SM_SYSMESSAGE,
            Param = 255,
            Tag = 0,
            Series = 1
        };
        var frame = MobileCodec.WriteFrame(inner, HUtil32.GetBytes(message + '\0'),
            unchecked((uint)Environment.TickCount), MobileCodec.MARKER_DATA);
        var wire = session.IsTiger
            ? Encoding.ASCII.GetBytes(TigerCodec.Encode(frame, session.TigerKeyOffset))
            : frame;

        await session.ClientWriteLock.WaitAsync(cancellationToken);
        try
        {
            if (_sessions.Get(id, generation) != session || !client.Connected) return false;
            await client.GetStream().WriteAsync(wire, cancellationToken);
            Interlocked.Add(ref session.TotalSentBytes, wire.Length);
            Interlocked.Add(ref TotalBytesDown, wire.Length);
            Interlocked.Increment(ref TotalPacketsDown);
            return true;
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            Log("WARN", $"Send message to client {id} failed: {ex.Message}");
            return false;
        }
        finally
        {
            session.ClientWriteLock.Release();
        }
    }

    public async Task<int> BroadcastSystemMessageAsync(string message,
        CancellationToken cancellationToken = default)
    {
        int sent = 0;
        foreach (var session in _sessions.GetAllActive())
            if (await SendSystemMessageAsync(session.SessionId, session.Generation,
                    message, cancellationToken)) sent++;
        return sent;
    }

    public async Task StopAsync()
    {
        await _stopLock.WaitAsync();
        try
        {
            _running = false;
            _lifetime?.Cancel();
            _delayQueue.Dispose();
            _listener?.Stop();
            _listener?.Dispose();
            _listener = null;
            if (_acceptTask != null)
            {
                try { await _acceptTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
                _acceptTask = null;
            }
            foreach (var s in _sessions.GetAllActive())
                if (s.TcpClient is TcpClient c) try { c.Dispose(); } catch { }

            if (_cleanupTask != null)
            {
                try { await _cleanupTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
                _cleanupTask = null;
            }
            _lifetime?.Dispose();
            _lifetime = null;

            var clients = _clientTasks.Values.ToArray();
            if (clients.Length > 0)
            {
                try { await Task.WhenAll(clients).WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
            }
            await _backend.StopAsync();
        }
        finally { _stopLock.Release(); }
    }

    [System.Diagnostics.Conditional("GAMEGATE_PACKET_TRACE")]
    private void Trace(string level, string msg)
    {
#if GAMEGATE_PACKET_TRACE
        Log(level, msg);
#endif
    }

    private void Log(string level, string msg)
    {
        OnLog?.Invoke(level, msg);
    }

    public object GetStats()
    {
        var ss = _sessions.GetStats();
        return new
        {
            Uptime = (DateTime.Now - StartTime).TotalSeconds,
            Sessions = new { Active = ss.active, Banned = ss.banned, Muted = ss.muted,
                TotalConnected = ss.totalConn, TotalDisconnected = ss.totalDisc },
            Network = new { M2Connected = _backend.DBConnected && _backend.GameConnected, M2Reconnects = Reconnects,
                TotalPacketsUp, TotalPacketsDown, TotalBytesUp, TotalBytesDown,
                TotalPacketsDropped = TotalDropped, TotalPacketsRejected = TotalRejected,
                TotalClientConnections = TotalClients, TotalClientDisconnections = TotalDisconnects },
            Speed = new { TotalChecks = _speed.TotalChecks, TotalViolations = _speed.TotalViolations,
                TotalPenalties = _speed.TotalPenalties, ViolationsByType = _speed.ViolationsByType },
            Ban = _ban.GetStats(),
        };
    }

    public void Dispose()
    {
        _running = false;
        try { _lifetime?.Cancel(); } catch { }
        _delayQueue.Dispose();
        try { _listener?.Stop(); } catch { }
        _listener?.Dispose();
        _listener = null;
        foreach (var session in _sessions.GetAllActive())
            if (session.TcpClient is TcpClient client)
                try { client.Dispose(); } catch { }
        _backend.Dispose();
        _lifetime?.Dispose();
        _lifetime = null;
    }

}
