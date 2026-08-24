using System.Diagnostics;
using System.Net.Sockets;
using SystemModule;
using SystemModule.Packages;
using SystemModule.Packet;

namespace GameSvr
{
    public class GateService
    {


        [Conditional("GAMESVR_PACKET_TRACE")]
        private static void PacketTrace(string msg)
        {
#if GAMESVR_PACKET_TRACE
            Debug.WriteLine(msg);
            PacketTraceWriter.Write($"{DateTime.Now:O} {msg}");
#endif
        }

        private static bool IsRawClientTextMessage(int ident)
        {
            return ident == Grobal2.CM_SAY
                   || ident == Grobal2.CM_DROPITEM
                   || ident == Grobal2.CM_TAKEONITEM
                   || ident == Grobal2.CM_TAKEOFFITEM
                   || ident == Grobal2.CM_EAT
                   || ident == Grobal2.CM_MERCHANTDLGSELECT
                   || ident == Grobal2.CM_MERCHANT_QUERY
                   || ident == Grobal2.CM_USERBUYITEM
                   || ident == Grobal2.CM_USERGETDETAILITEM
                   || ident == Grobal2.CM_3295;
        }

        private static string DecodeClientMessageBody(int ident, byte[] body)
        {
            if (body == null || body.Length == 0) return string.Empty;

            var textLength = body.Length;
            if (body[textLength - 1] == 0) textLength--;
            if (textLength == 0) return string.Empty;

            var rawText = HUtil32.GetString(body, 0, textLength);
            return IsRawClientTextMessage(ident) ? rawText : EDcode.DeCodeString(rawText);
        }

        private const int MaxPendingClientMessages = 16;
        private const int MaxPendingClientBytes = 8192;

        private static bool TryParseClientMessage(byte[] msgBuff, int nMsgLen,
            out ClientPacket defMsg, out string sMsg, out byte[] body)
        {
            defMsg = null;
            sMsg = null;
            body = null;
            if (msgBuff == null || nMsgLen < ClientPacket.PackSize ||
                nMsgLen > msgBuff.Length)
                return false;

            defMsg = Packets.ToPacket<ClientPacket>(msgBuff);
            if (defMsg == null) return false;
            var bodyLength = nMsgLen - ClientPacket.PackSize;
            if (bodyLength == 0) return true;
            body = new byte[bodyLength];
            Buffer.BlockCopy(msgBuff, ClientPacket.PackSize, body, 0,
                bodyLength);
            sMsg = DecodeClientMessageBody(defMsg.Ident, body);
            return true;
        }

        private readonly int _gateIdx;
        // _gateIdx is the managed connection/dictionary key.  Native M2
        // keeps a separate 1..32 GateIndex learned from the type-5 handshake.
        // Keep the two identities distinct so a large socket ConnectionId does
        // not leak into the native route field.
        private int _nativeGateIndex;
        private readonly Func<GateService, int, bool> _claimNativeGateIndex;
        private readonly Action<GateService, int> _releaseNativeGateIndex;
        private int _nativeGateSlotReleased;
        private readonly bool _requireNativeRegistration;
        private readonly TGateInfo _gateInfo;
        private static long _nextUserGeneration;
        private readonly SendQueue _sendQueue;
        private readonly object _sendStateLock = new();
        private readonly Queue<byte[]> _pendingSendBuffers = new();
        private readonly Queue<TGateUserInfo> _deferredUserCleanup = new();
        private readonly object runSocketSection;
        private bool _closing;

        // This parser is on the Gate -> M2 receive direction.  The native M2
        // validator accepts BodyLen <= 0x3000; the larger 0x8000 send-buffer
        // ceiling belongs to the opposite M2 -> Gate accumulation path.
        private readonly InternalPacket77FrameParser _frameParser =
            new(maximumFrameLength: InternalPacket77FrameParser.NativeMaximumFrameLength);
        private Task _queueTask;

        public GateService(int gateIdx, TGateInfo gateInfo)
            : this(gateIdx, gateInfo, false, null, null)
        {
        }

        internal GateService(int gateIdx, TGateInfo gateInfo,
            bool requireNativeRegistration)
            : this(gateIdx, gateInfo, requireNativeRegistration, null, null)
        {
        }

        internal GateService(int gateIdx, TGateInfo gateInfo,
            bool requireNativeRegistration,
            Func<GateService, int, bool> claimNativeGateIndex,
            Action<GateService, int> releaseNativeGateIndex)
        {
            _gateIdx = gateIdx;
            _requireNativeRegistration = requireNativeRegistration;
            _claimNativeGateIndex = claimNativeGateIndex;
            _releaseNativeGateIndex = releaseNativeGateIndex;
            _gateInfo = gateInfo ?? throw new ArgumentNullException(nameof(gateInfo));
            _gateInfo.UserList ??= new List<TGateUserInfo>();
            runSocketSection = new object();
            _sendQueue = new SendQueue(gateInfo.Socket);
        }

        public TGateInfo GateInfo => _gateInfo;

        public void StartQueueService()
        {
            lock (runSocketSection)
            {
                if (_closing || _queueTask != null) return;
                _queueTask = Task.Run(async () =>
                {
                    await _sendQueue.ProcessSendQueue();
                    var terminalError = _sendQueue.TerminalError;
                    if (terminalError == null) return;

                    var socketToClose = MarkSendFailureLocked(
                        terminalError, "TRunSocket::ProcessSendQueue");
                    if (socketToClose != null)
                        _ = Task.Run(() => CloseSocket(socketToClose));
                });
            }
        }

        public void Stop()
        {
            lock (runSocketSection)
            {
                _closing = true;
                GateInfo.boUsed = false;
                ClearPendingClientMessagesLocked("stop");
            }
            lock (_sendStateLock) _pendingSendBuffers.Clear();
            _sendQueue.Stop();
            ReleaseNativeGateSlot();
            try
            {
                _queueTask?.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // Task was already canceled — ignore
            }
        }

        
        
        
        
        public void HandleReceiveBuffer(int nMsgLen, byte[] data)
        {
            HandleReceiveBuffer(null, nMsgLen, data);
        }

        internal void HandleReceiveBuffer(Socket sourceSocket, int nMsgLen,
            byte[] data)
        {
            const string exceptionMessage = "[Exception] TRunSocket::ExecGateBuffers";
            if (nMsgLen <= 0 || data == null || nMsgLen > data.Length)
            {
                return;
            }
            lock (runSocketSection)
            {
                if (_closing || (sourceSocket != null &&
                    !ReferenceEquals(GateInfo.Socket, sourceSocket)))
                {
                    return;
                }
                try
                {
                    if (!_frameParser.TryAppend(data, 0, nMsgLen, out var packets, out var error))
                    {
                        M2Share.ErrorMessage($"{exceptionMessage}: {error}");
                        return;
                    }

                    PacketTrace($"[GateSvc] recv={nMsgLen}B buffered={_frameParser.BufferedLength} frames={packets.Count}");
                    foreach (var internalPacket in packets)
                    {
                        if (internalPacket.Cmd ==
                            NativeGameGateCommands.GateRegistrationRequest)
                        {
                            HandleNativeRegistrationPacketLocked(internalPacket);
                            continue;
                        }

                        var body = internalPacket.Payload ?? Array.Empty<byte>();
                        var messageHeader = new PacketHeader
                        {
                            PacketCode = Grobal2.RUNGATECODE,
                            Ident = internalPacket.Cmd,
                            UserIndex = 0,
                            Socket = (int)internalPacket.ConnID,
                            SocketIdx = 0,
                            PackLength = body.Length
                        };

                        PacketTrace($"[77Recv] cmd={internalPacket.Cmd} conn=0x{internalPacket.ConnID:X8} seq=0x{internalPacket.SeqID:X8} frameLen={internalPacket.FrameLen} payload={body.Length}B gateUsers={GateInfo.UserList?.Count ?? -1}");
                        ExecGateBuffers(messageHeader, body);
                    }
                }
                catch (Exception ex)
                {
                    _frameParser.Reset();
                    M2Share.ErrorMessage($"{exceptionMessage}: {ex.Message}");
                }
            } // lock (runSocketSection)
            DrainDeferredUserCleanup();
        }

        
        
        
        
        
        
        public bool HandleSendBuffer(byte[] buffer)
        {
            return HandleSendBuffer(buffer, 0, 0, 0, false);
        }

        public bool HandleCurrentUserSendBuffer(byte[] buffer, int nSocket,
            ushort nGSocketIdx, long expectedGeneration)
        {
            if (expectedGeneration == 0) return false;
            return HandleSendBuffer(buffer, nSocket, nGSocketIdx,
                expectedGeneration, true);
        }

        private bool HandleSendBuffer(byte[] buffer, int expectedSocket,
            ushort expectedGSocketIdx, long expectedGeneration,
            bool requireCurrentUser)
        {
            if (buffer == null || buffer.Length < 24)
            {
                PacketTrace("[SendBuf] SKIP: invalid buffer or disconnected gate");
                return false;
            }
            Socket socketToClose = null;
            lock (runSocketSection)
            {
                PacketTrace($"[SendBuf] boUsed={GateInfo.boUsed} socket={GateInfo.Socket != null} connected={GateInfo.Socket?.Connected} nSendChecked={GateInfo.nSendChecked}");
                if (_closing || !GateInfo.boUsed || GateInfo.Socket == null)
                {
                    PacketTrace("[SendBuf] SKIP: invalid buffer or disconnected gate");
                    return false;
                }
                if (requireCurrentUser && !GateInfo.UserList.Any(user =>
                        user != null && user.nSocket == expectedSocket &&
                        user.nGSocketIdx == expectedGSocketIdx &&
                        user.UserGeneration == expectedGeneration))
                    return false;
                lock (_sendStateLock)
                {
                    _pendingSendBuffers.Enqueue(buffer);
                    try
                    {
                        DrainPendingSendBuffersLocked();
                        return true;
                    }
                    catch (Exception e)
                    {
                        M2Share.ErrorMessage("[Exception] TRunSocket::SendGateBuffers -> SendBuff");
                        M2Share.ErrorMessage(e.StackTrace, MessageType.Error);
                        socketToClose = MarkSendFailureLocked(e,
                            "TRunSocket::HandleSendBuffer");
                    }
                }
            }
            if (socketToClose != null)
                CloseSocket(socketToClose);
            return false;
        }

        internal bool HandleLegacyType18(byte[] frame)
        {
            if (frame == null) return false;
            var packet = LegacyGateType18.FromBytes(frame, 0, frame.Length);
            if (packet == null
                || LegacyGateType18.HeaderSize + packet.PayloadLength
                != frame.Length)
                return false;

            return QueueInternalBroadcastFrame(frame,
                "TRunSocket::HandleLegacyType18");
        }

        internal bool HandleInternalPacket77(byte[] frame)
        {
            if (frame == null
                || frame.Length < InternalPacket77.HEADER_SIZE
                || frame.Length > InternalPacket77.MAX_FRAME_SIZE)
                return false;

            var bodyLength = BitConverter.ToUInt16(frame, 14);
            if (InternalPacket77.HEADER_SIZE + bodyLength != frame.Length)
                return false;

            var packet = InternalPacket77.FromBytes(frame, 0, frame.Length);
            if (packet == null || packet.Magic != InternalPacket77.MAGIC)
                return false;

            return QueueInternalBroadcastFrame(frame,
                "TRunSocket::HandleInternalPacket77");
        }

        private bool QueueInternalBroadcastFrame(byte[] frame,
            string operation)
        {

            Socket socketToClose = null;
            lock (runSocketSection)
            {
                if (_closing || !GateInfo.boUsed || GateInfo.Socket == null)
                    return false;
                lock (_sendStateLock)
                {
                    try
                    {
                        _sendQueue.AddToQueue(frame);
                        GateInfo.nSendCount++;
                        GateInfo.nSendBytesCount += frame.Length;
                        GateInfo.nSendBlockCount += frame.Length;
                    }
                    catch (Exception exception)
                    {
                        socketToClose = MarkSendFailureLocked(exception, operation);
                    }
                }
            }
            if (socketToClose != null)
            {
                CloseSocket(socketToClose);
                return false;
            }
            return true;
        }

        private void DrainPendingSendBuffersLocked()
        {
            try
            {
                DrainPendingSendBuffersCoreLocked();
            }
            catch (Exception exception)
            {
                var socketToClose = MarkSendFailureLocked(exception,
                    "TRunSocket::DrainPendingSendBuffersLocked");
                if (socketToClose != null)
                    _ = Task.Run(() => CloseSocket(socketToClose));
                throw;
            }
        }

        private void DrainPendingSendBuffersCoreLocked()
        {
            if (_requireNativeRegistration
                && !IsValidNativeGateIndex(Volatile.Read(ref _nativeGateIndex)))
                return;

            if (GateInfo.nSendChecked > 0)
            {
                if (unchecked((uint)(HUtil32.GetTickCount() - GateInfo.dwSendCheckTick))
                    <= (uint)M2Share.g_dwSocCheckTimeOut)
                    return;

                GateInfo.nSendChecked = 0;
                GateInfo.nSendBlockCount = 0;
            }

            var checkBlockBytes = (long)M2Share.g_Config.nCheckBlock * 10L;
            while (_pendingSendBuffers.Count > 0)
            {
                var buffer = _pendingSendBuffers.Peek();
                var bodyLen = buffer.Length - 24;
                if (bodyLen > InternalPacket77.MAX_PAYLOAD_SIZE)
                {
                    _pendingSendBuffers.Dequeue();
                    throw new InvalidDataException(
                        $"GameGate frame payload {bodyLen} exceeds {InternalPacket77.MAX_PAYLOAD_SIZE} bytes");
                }

                // 从 PacketHeader 提取路由信息 (77BBAA33 协议)
                // buffer格式: [4B len][4B PktCode][4B Socket][2B SockIdx][2B Ident][4B UserIdx][4B PktLen][body]
                uint connId = BitConverter.ToUInt32(buffer, 8);     // buffer[8..11] = Socket = ConnID
                ushort cmd = BitConverter.ToUInt16(buffer, 14);     // buffer[14..15] = Ident = CMD
                byte[] payload;
                if (bodyLen > 0)
                {
                    payload = new byte[bodyLen];
                    Buffer.BlockCopy(buffer, 24, payload, 0, bodyLen);
                }
                else payload = Array.Empty<byte>();

                // 封装为 77BBAA33 帧发送。  GM_DATA=5 is the historical
                // GameSvr dialect; native M2 -> GameGate DATA is type 14.
                // Keep the legacy value in PacketHeader and translate only at
                // this wire boundary so game logic remains source-compatible.
                var wireCmd = cmd == Grobal2.GM_DATA
                    ? NativeGameGateCommands.M2ClientData
                    : cmd;
                var pkt = new InternalPacket77
                {
                    Magic = InternalPacket77.MAGIC,
                    ConnID = connId,
                    // Native M2 emits the stable routed session key at +0x08,
                    // not a per-frame tick/sequence.  The socket/session word
                    // is the low WORD of the +0x04 connection field.
                    SeqID = ComposeWireRouteContext(unchecked((ushort)connId)),
                    FrameLen = (ushort)(InternalPacket77.HEADER_SIZE + payload.Length),
                    Cmd = wireCmd,
                    Field16 = (uint)Environment.TickCount,
                    Field20 = (uint)payload.Length,
                    Payload = payload
                };
                var pktBytes = pkt.ToBytes();

                if (checkBlockBytes > 0 && GateInfo.nSendBlockCount > 0
                    && GateInfo.nSendBlockCount + (long)pktBytes.Length >= checkBlockBytes)
                {
                    QueueControlPacketLocked(Grobal2.GM_RECEIVE_OK);
                    GateInfo.nSendChecked = 1;
                    GateInfo.dwSendCheckTick = HUtil32.GetTickCount();
                    return;
                }

                _pendingSendBuffers.Dequeue();

                PacketTrace($"[Send77] connId=0x{connId:X8} cmd={cmd} frameLen={pkt.FrameLen} payload={payload.Length}B qCount={_sendQueue.GetQueueCount}");

                _sendQueue.AddToQueue(pktBytes);
                GateInfo.nSendCount++;
                GateInfo.nSendBytesCount += pktBytes.Length;
                GateInfo.nSendBlockCount += pktBytes.Length;

                if (checkBlockBytes > 0 && pktBytes.Length >= checkBlockBytes)
                {
                    QueueControlPacketLocked(Grobal2.GM_RECEIVE_OK);
                    GateInfo.nSendChecked = 1;
                    GateInfo.dwSendCheckTick = HUtil32.GetTickCount();
                    return;
                }
            }
        }

        private uint ComposeWireRouteContext(ushort sessionWord)
        {
            var nativeGateIndex = Volatile.Read(ref _nativeGateIndex);
            if (nativeGateIndex is < NativeGameGateCommands.MinGateIndex
                or > NativeGameGateCommands.MaxGateIndex)
                nativeGateIndex = _gateIdx;

            if (nativeGateIndex is >= NativeGameGateCommands.MinGateIndex
                and <= NativeGameGateCommands.MaxGateIndex)
            {
                return NativeGameGateCommands.ComposeRouteId(nativeGateIndex,
                    sessionWord);
            }

            // GateManager's dictionary key is an internal ConnectionId and is
            // not the native 1..32 gate number.  The native route can only be
            // composed after a registration identity is available.  Keep the
            // historical managed context for synthetic/legacy GateService
            // instances instead of throwing from the send path and closing a
            // valid socket.  Real native deployments must provide a valid
            // registration index before relying on this field for routing.
            return _requireNativeRegistration
                ? 0u
                : unchecked((uint)Environment.TickCount);
        }

        private static bool IsValidNativeGateIndex(int gateIndex) =>
            gateIndex is >= NativeGameGateCommands.MinGateIndex
                and <= NativeGameGateCommands.MaxGateIndex;

        private void HandleNativeRegistrationPacketLocked(
            InternalPacket77 packet)
        {
            // Native M2 reads only byte[frame+0x08] and ignores the body.  The
            // managed sender places the requested GateIndex in SeqID, so the
            // low byte is the complete registration value here as well.
            var requested = (int)(packet.SeqID & 0xFF);
            // The native handler registers only while its slot is empty.  A
            // duplicate request on an already registered connection is a
            // no-op, matching that one-shot state transition.
            if (Interlocked.CompareExchange(ref _nativeGateIndex,
                    requested, 0) != 0)
                return;

            // Native stores the byte before the manager's 1..32 validation;
            // preserve that one-shot behavior for malformed requests too.
            if (!IsValidNativeGateIndex(requested))
            {
                PacketTrace($"[GateRegister] rejected request gate={requested} " +
                            $"body={packet.Payload?.Length ?? 0}");
                return;
            }

            if (_claimNativeGateIndex != null
                && !_claimNativeGateIndex(this, requested))
            {
                // GateManager normally replaces the previous owner, matching
                // the native 32-slot table.  A custom owner callback may
                // reject the claim; leave this connection unregistered.
                Interlocked.Exchange(ref _nativeGateIndex, 0);
                PacketTrace($"[GateRegister] slot claim failed gate={requested}");
                return;
            }

            lock (_sendStateLock)
            {
                QueueControlPacketLocked(
                    NativeGameGateCommands.M2RegistrationReply,
                    (uint)requested, 0);
                if (_requireNativeRegistration)
                    DrainPendingSendBuffersLocked();
            }
            PacketTrace($"[GateRegister] accepted gate={requested}");
        }

        private void ReleaseNativeGateSlot()
        {
            if (Interlocked.Exchange(ref _nativeGateSlotReleased, 1) != 0)
                return;
            var gateIndex = Volatile.Read(ref _nativeGateIndex);
            if (IsValidNativeGateIndex(gateIndex))
                _releaseNativeGateIndex?.Invoke(this, gateIndex);
        }

        public void ResumeFlowControlIfTimedOut()
        {
            Socket socketToClose = null;
            lock (runSocketSection)
            {
                if (_closing) return;
                lock (_sendStateLock)
                {
                    if (GateInfo.nSendChecked <= 0
                        || unchecked((uint)(HUtil32.GetTickCount() - GateInfo.dwSendCheckTick))
                        <= (uint)M2Share.g_dwSocCheckTimeOut)
                        return;
                    GateInfo.nSendChecked = 0;
                    GateInfo.nSendBlockCount = 0;
                    try
                    {
                        DrainPendingSendBuffersLocked();
                    }
                    catch (Exception exception)
                    {
                        socketToClose = MarkSendFailureLocked(exception,
                            "TRunSocket::ResumeFlowControlIfTimedOut");
                    }
                }
            }
            if (socketToClose != null) CloseSocket(socketToClose);
        }

        public void SendCheck(ushort nIdent)
        {
            Socket socketToClose = null;
            lock (runSocketSection)
            {
                if (_closing || GateInfo.Socket?.Connected != true) return;
                lock (_sendStateLock)
                {
                    try
                    {
                        QueueControlPacketLocked(nIdent);
                    }
                    catch (Exception exception)
                    {
                        socketToClose = MarkSendFailureLocked(exception,
                            "TRunSocket::SendCheck");
                    }
                }
            }
            if (socketToClose != null) CloseSocket(socketToClose);
        }

        private void QueueControlPacketLocked(ushort nIdent)
        {
            QueueControlPacketLocked(nIdent, 0,
                unchecked((uint)Environment.TickCount));
        }

        private void QueueControlPacketLocked(ushort nIdent, uint connId,
            uint seqId)
        {
            var pkt = new InternalPacket77
            {
                Magic = InternalPacket77.MAGIC,
                ConnID = connId,
                SeqID = seqId,
                FrameLen = (ushort)InternalPacket77.HEADER_SIZE,
                Cmd = nIdent,
                Field16 = (uint)Environment.TickCount,
                Field20 = 0,
                Payload = Array.Empty<byte>()
            };
            _sendQueue.AddToQueue(pkt.ToBytes());
        }

        private void CompleteFlowControl()
        {
            lock (_sendStateLock)
            {
                GateInfo.nSendChecked = 0;
                GateInfo.nSendBlockCount = 0;
                DrainPendingSendBuffersLocked();
            }
        }

        
        
        
        private void ExecGateBuffers(PacketHeader packet, byte[] data)
        {
            if (packet.PackLength == 0)
            {
                ExecGateMsg(_gateIdx, GateInfo, packet, Array.Empty<byte>(), 0);
            }
            else
            {
                ExecGateMsg(_gateIdx, GateInfo, packet, data, packet.PackLength);
            }
        }

        private bool DoClientCertification_GetCertification(string sMsg, ref string sAccount, ref string sChrName, ref int nSessionID, ref int nClientVersion, ref bool boFlag, ref byte[] tHWID)
        {
            var result = false;
            var sCodeStr = string.Empty;
            var sClientVersion = string.Empty;
            var sHWID = string.Empty;
            var sIdx = string.Empty;
            const string sExceptionMsg = "[Exception] TRunSocket::DoClientCertification -> GetCertification";
            try
            {
                var sData = EDcode.DeCodeString(sMsg);
                if (sData.Length > 2 && sData[0] == '*' && sData[1] == '*')
                {
                    sData = sData.Substring(2, sData.Length - 2);
                    sData = HUtil32.GetValidStr3(sData, ref sAccount, HUtil32.Backslash);
                    sData = HUtil32.GetValidStr3(sData, ref sChrName, HUtil32.Backslash);
                    sData = HUtil32.GetValidStr3(sData, ref sCodeStr, HUtil32.Backslash);
                    sData = HUtil32.GetValidStr3(sData, ref sClientVersion, HUtil32.Backslash);
                    sData = HUtil32.GetValidStr3(sData, ref sIdx, HUtil32.Backslash);
                    sData = HUtil32.GetValidStr3(sData, ref sHWID, HUtil32.Backslash);
                    nSessionID = HUtil32.Str_ToInt(sCodeStr, 0);
                    if (sIdx == "0")
                    {
                        boFlag = true;
                    }
                    else
                    {
                        boFlag = false;
                    }
                    if (!string.IsNullOrEmpty(sAccount) && !string.IsNullOrEmpty(sChrName) && nSessionID >= 2)
                    {
                        nClientVersion = HUtil32.Str_ToInt(sClientVersion, 0);
                        if (!string.IsNullOrEmpty(sHWID))
                        {
                            tHWID = MD5.MD5UnPrInt(sHWID);
                        }
                        result = true;
                    }
                }
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage(sExceptionMsg + " " + ex.Message, MessageType.Error);
            }
            return result;
        }

        private void DoClientCertification(int GateIdx, TGateUserInfo GateUser, int nSocket, string sMsg)
        {
            var sData = string.Empty;
            var sAccount = string.Empty;
            var sChrName = string.Empty;
            var nSessionID = 0;
            var boFlag = false;
            var nClientVersion = 0;
            var nPayMent = 0;
            var nPayMode = 0;
            var certPayload = string.Empty;
            byte[] HWID = MD5.g_MD5EmptyDigest;
            const string sExceptionMsg = "[Exception] TRunSocket::DoClientCertification";
            const string sDisable = "*disable*";
            try
            {
                if (string.IsNullOrEmpty(GateUser.sAccount))
                {
                    certPayload = sMsg;
                    if (HUtil32.TagCount(sMsg, '!') > 0)
                    {
                        sData = HUtil32.ArrestStringEx(sMsg, "#", "!", ref sMsg);
                        certPayload = sMsg;
                        if (!string.IsNullOrEmpty(certPayload) && char.IsDigit(certPayload[0]))
                        {
                            certPayload = certPayload.Substring(1, certPayload.Length - 1);
                        }
                    }
                    else if (!string.IsNullOrEmpty(certPayload) && char.IsDigit(certPayload[0]))
                    {
                        certPayload = certPayload.Substring(1, certPayload.Length - 1);
                    }

                    if (DoClientCertification_GetCertification(certPayload,
                            ref sAccount, ref sChrName, ref nSessionID,
                            ref nClientVersion, ref boFlag, ref HWID))
                    {
                        var sessInfo = IdSrvClient.Instance.GetAdmission(sAccount,
                            GateUser.sIPaddr, nSessionID, ref nPayMode, ref nPayMent);
                        if (sessInfo != null && nPayMent > 0)
                        {
                            GateUser.boCertification = true;
                            GateUser.sAccount = sAccount.Trim();
                            GateUser.sCharName = sChrName.Trim();
                            GateUser.nSessionID = nSessionID;
                            GateUser.nClientVersion = nClientVersion;
                            GateUser.SessInfo = sessInfo;
                            try
                            {
                                M2Share.FrontEngine.AddToLoadRcdList(sAccount,
                                    sChrName, GateUser.sIPaddr, boFlag, nSessionID,
                                    nPayMent, nPayMode, nClientVersion, nSocket,
                                    GateUser.nGSocketIdx, GateIdx,
                                    GateUser.UserGeneration);
                            }
                            catch (Exception ex)
                            {
                                M2Share.ErrorMessage(sExceptionMsg + " " + ex.Message);
                            }
                        }
                        else
                        {
                            GateUser.sAccount = sDisable;
                            GateUser.boCertification = false;
                            CloseUser(nSocket);
                        }
                    }
                    else
                    {
                        M2Share.MainOutMessage(
                            $"[CertFail] GetCertification returned false payloadLen={(certPayload == null ? 0 : certPayload.Length)}");
                        GateUser.sAccount = sDisable;
                        GateUser.boCertification = false;
                        CloseUser(nSocket);
                    }
                }
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage(sExceptionMsg + " " + ex.Message);
            }
        }
        public void CloseUser(int nSocket)
        {
            CloseUser(nSocket, 0);
        }

        public bool CloseUser(int nSocket, long expectedGeneration)
        {
            var deferCleanup = Monitor.IsEntered(runSocketSection);
            TGateUserInfo gateUser = null;
            lock (runSocketSection)
            {
                if (_closing) return false;
                for (var i = 0; i < GateInfo.UserList.Count; i++)
                {
                    var candidate = GateInfo.UserList[i];
                    if (candidate == null || candidate.nSocket != nSocket ||
                        (expectedGeneration != 0 &&
                         candidate.UserGeneration != expectedGeneration))
                        continue;
                    gateUser = candidate;
                    PacketTrace($"[GateClose] reason=CloseUser gate={_gateIdx} " +
                                $"socket={candidate.nSocket} generation={candidate.UserGeneration} " +
                                $"playObj={candidate.PlayObject != null} cert={candidate.boCertification}");
                    ClearPendingClientMessagesLocked(gateUser, "close");
                    GateInfo.UserList[i] = null;
                    GateInfo.nUserCount = CountActiveUsersLocked();
                    break;
                }
            }
            if (gateUser == null) return false;
            if (deferCleanup)
            {
                lock (runSocketSection) _deferredUserCleanup.Enqueue(gateUser);
            }
            else
            {
                CleanupClosedUser(gateUser);
            }
            return true;
        }

        public bool KickUser(string account, int sessionId, int payMode)
        {
            const string duplicateLoginMessage =
                "当前登录帐号正在其它位置登录，本机已被强行离线!!!";
            TGateUserInfo gateUser = null;
            lock (runSocketSection)
            {
                if (_closing) return false;
                for (var i = 0; i < GateInfo.UserList.Count; i++)
                {
                    var candidate = GateInfo.UserList[i];
                    if (candidate == null
                        || !string.Equals(candidate.sAccount, account,
                            StringComparison.OrdinalIgnoreCase)
                        || candidate.nSessionID != sessionId)
                        continue;
                    gateUser = candidate;
                    PacketTrace($"[GateClose] reason=KickUser gate={_gateIdx} " +
                                $"socket={candidate.nSocket} generation={candidate.UserGeneration} " +
                                $"playObj={candidate.PlayObject != null} cert={candidate.boCertification}");
                    ClearPendingClientMessagesLocked(gateUser, "kick");
                    GateInfo.UserList[i] = null;
                    GateInfo.nUserCount = CountActiveUsersLocked();
                    break;
                }
            }
            if (gateUser == null) return false;
            TryCancelLoad(gateUser);
            if (gateUser.PlayObject != null)
            {
                gateUser.PlayObject.SysMsg(
                    payMode == 0
                        ? duplicateLoginMessage
                        : "账号付费时间已到,本机已被强行离线,请充值后再继续进行游戏!",
                    MsgColor.Red, MsgType.Hint);
                gateUser.PlayObject.m_boEmergencyClose = true;
                gateUser.PlayObject.m_boSoftClose = true;
            }
            return true;
        }

        public bool IsCurrentUser(int nSocket, long generation)
        {
            lock (runSocketSection)
            {
                if (_closing || generation == 0) return false;
                return GateInfo.UserList.Any(user => user != null &&
                    user.nSocket == nSocket &&
                    user.UserGeneration == generation);
            }
        }

        public bool TryGetCurrentSocket(out Socket socket)
        {
            lock (runSocketSection)
            {
                socket = _closing ? null : GateInfo.Socket;
                return socket != null;
            }
        }

        public bool TryCloseConnection(Socket socket)
        {
            List<TGateUserInfo> users;
            lock (runSocketSection)
            {
                if (!ReferenceEquals(GateInfo.Socket, socket) ||
                    (_closing && GateInfo.Socket == null))
                    return false;
                _closing = true;
                users = GateInfo.UserList.Where(user => user != null).ToList();
                ClearPendingClientMessagesLocked("connection-close");
                for (var i = 0; i < GateInfo.UserList.Count; i++)
                    GateInfo.UserList[i] = null;
                GateInfo.nUserCount = 0;
                GateInfo.boUsed = false;
                GateInfo.Socket = null;
                _frameParser.Reset();
            }

            ReleaseNativeGateSlot();

            foreach (var gateUser in users)
            {
                PacketTrace($"[GateClose] reason=connection-close gate={_gateIdx} " +
                            $"socket={gateUser.nSocket} generation={gateUser.UserGeneration} " +
                            $"playObj={gateUser.PlayObject != null} cert={gateUser.boCertification}");
                TryCancelLoad(gateUser);
                if (gateUser.PlayObject == null) continue;
                gateUser.PlayObject.m_boEmergencyClose = true;
                gateUser.PlayObject.m_boSoftClose = true;
            }
            return true;
        }

        private void CleanupClosedUser(TGateUserInfo gateUser)
        {
            TryCancelLoad(gateUser);
            var playObject = gateUser.PlayObject;
            if (playObject == null) return;
            if (!playObject.m_boOffLineFlag) playObject.m_boSoftClose = true;
            if (playObject.m_boGhost && !playObject.m_boReconnection)
                IdSrvClient.Instance.SendHumanLogOutMsg(gateUser.sAccount,
                    gateUser.nSessionID);
            if (playObject.m_boSoftClose && playObject.m_boReconnection &&
                playObject.m_boEmergencyClose)
                IdSrvClient.Instance.SendHumanLogOutMsg(gateUser.sAccount,
                    gateUser.nSessionID);
        }

        private void DrainDeferredUserCleanup()
        {
            List<TGateUserInfo> users;
            lock (runSocketSection)
            {
                if (_deferredUserCleanup.Count == 0) return;
                users = new List<TGateUserInfo>(_deferredUserCleanup.Count);
                while (_deferredUserCleanup.Count > 0)
                    users.Add(_deferredUserCleanup.Dequeue());
            }
            foreach (var gateUser in users) CleanupClosedUser(gateUser);
        }

        private void TryCancelLoad(TGateUserInfo gateUser)
        {
            try
            {
                gateUser.FrontEngine?.DeleteHuman(_gateIdx, gateUser.nSocket,
                    gateUser.UserGeneration);
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage("[Exception] TRunSocket::CancelLoad " +
                                     ex.Message);
            }
        }

        private int CountActiveUsersLocked() =>
            GateInfo.UserList.Count(user => user != null);

        private static void ClearPendingClientMessagesLocked(
            TGateUserInfo gateUser, string reason)
        {
            if (gateUser == null)
                return;

            var count = gateUser.PendingClientMessages.Count;
            if (count > 0)
            {
                PacketTrace($"[PendingDrop] reason={reason} socket={gateUser.nSocket} " +
                            $"generation={gateUser.UserGeneration} count={count} " +
                            $"bytes={gateUser.PendingClientBytes}");
                gateUser.PendingClientMessages.Clear();
            }
            gateUser.PendingClientBytes = 0;
            // The queue can be empty while a replay batch is executing: the
            // batch is detached before dispatch. Always clear this state so a
            // replay exception/flush cannot strand later client packets.
            gateUser.PendingClientReplayInProgress = false;
        }

        private void ClearPendingClientMessagesLocked(string reason)
        {
            foreach (var gateUser in GateInfo.UserList)
                ClearPendingClientMessagesLocked(gateUser, reason);
        }

        private static List<PendingClientPacket> TakePendingClientMessagesLocked(
            TGateUserInfo gateUser)
        {
            if (gateUser == null || gateUser.PendingClientMessages.Count == 0)
                return null;
            var result = new List<PendingClientPacket>(
                gateUser.PendingClientMessages.Count);
            while (gateUser.PendingClientMessages.Count > 0)
                result.Add(gateUser.PendingClientMessages.Dequeue());
            gateUser.PendingClientBytes = 0;
            return result;
        }

        private static bool QueuePendingClientMessageLocked(
            TGateUserInfo gateUser, byte[] msgBuff, int nMsgLen)
        {
            if (gateUser == null || !TryParseClientMessage(msgBuff, nMsgLen,
                    out var defMsg, out _, out _))
            {
                PacketTrace($"[PendingDrop] reason=malformed socket=" +
                            $"{gateUser?.nSocket} len={nMsgLen}");
                return false;
            }

            if (nMsgLen > MaxPendingClientBytes ||
                gateUser.PendingClientMessages.Count >= MaxPendingClientMessages ||
                gateUser.PendingClientBytes > MaxPendingClientBytes - nMsgLen)
            {
                PacketTrace($"[PendingDrop] reason=limit socket={gateUser.nSocket} " +
                            $"generation={gateUser.UserGeneration} ident={defMsg.Ident} " +
                            $"len={nMsgLen} count={gateUser.PendingClientMessages.Count} " +
                            $"bytes={gateUser.PendingClientBytes}");
                return false;
            }

            var copy = new byte[nMsgLen];
            Buffer.BlockCopy(msgBuff, 0, copy, 0, nMsgLen);
            gateUser.PendingClientMessages.Enqueue(new PendingClientPacket(
                copy, nMsgLen, defMsg.Ident, gateUser.UserGeneration));
            gateUser.PendingClientBytes += nMsgLen;
            PacketTrace($"[PendingQueue] socket={gateUser.nSocket} " +
                        $"generation={gateUser.UserGeneration} ident={defMsg.Ident} " +
                        $"len={nMsgLen} count={gateUser.PendingClientMessages.Count} " +
                        $"bytes={gateUser.PendingClientBytes}");
            return true;
        }

        private void ReplayPendingClientMessages(TGateUserInfo expectedUser,
            TPlayObject expectedPlayObject, long expectedGeneration,
            List<PendingClientPacket> pending)
        {
            if (pending == null || pending.Count == 0)
            {
                lock (runSocketSection)
                {
                    expectedUser.PendingClientReplayInProgress = false;
                }
                return;
            }
            var batch = pending;
            while (batch != null && batch.Count > 0)
            {
                foreach (var pendingMessage in batch)
                {
                    ClientPacket defMsg;
                    string sMsg;
                    byte[] body;
                    UserEngine userEngine;
                    lock (runSocketSection)
                    {
                        if (_closing || pendingMessage.Generation != expectedGeneration)
                        {
                            PacketTrace($"[PendingDrop] reason=generation socket=" +
                                        $"{expectedUser?.nSocket} ident={pendingMessage.Ident}");
                            continue;
                        }
                        var current = GateInfo.UserList.FirstOrDefault(user =>
                            user != null && user.nSocket == expectedUser.nSocket &&
                            user.UserGeneration == expectedGeneration);
                        if (current == null || !ReferenceEquals(current, expectedUser) ||
                            !ReferenceEquals(current.PlayObject, expectedPlayObject) ||
                            current.UserEngine == null || !current.boCertification)
                        {
                            PacketTrace($"[PendingDrop] reason=stale-bind socket=" +
                                        $"{expectedUser?.nSocket} generation={expectedGeneration} " +
                                        $"ident={pendingMessage.Ident}");
                            ClearPendingClientMessagesLocked(expectedUser, "stale-bind");
                            continue;
                        }
                        if (!TryParseClientMessage(pendingMessage.Data,
                                pendingMessage.Length, out defMsg, out sMsg, out body))
                        {
                            PacketTrace($"[PendingDrop] reason=malformed-replay socket=" +
                                        $"{current.nSocket} ident={pendingMessage.Ident}");
                            continue;
                        }
                        userEngine = current.UserEngine;
                    }

                    // Do not hold the gate lock while invoking game logic. The same
                    // path can close/rebind a user and must be free to acquire it.
                    PacketTrace($"[PendingFlush] socket={expectedUser.nSocket} " +
                                $"generation={expectedGeneration} ident={defMsg.Ident} " +
                                $"len={pendingMessage.Length}");
                    try
                    {
                        userEngine.ProcessUserMessage(expectedPlayObject,
                            defMsg, sMsg, body);
                    }
                    catch (Exception exception)
                    {
                        PacketTrace($"[PendingDrop] reason=replay-exception " +
                                    $"socket={expectedUser.nSocket} ident={defMsg.Ident} " +
                                    $"error={exception.GetType().Name}");
                        lock (runSocketSection)
                            ClearPendingClientMessagesLocked(expectedUser,
                                "replay-exception");
                        return;
                    }
                }

                lock (runSocketSection)
                {
                    var current = GateInfo.UserList.FirstOrDefault(user =>
                        user != null && user.nSocket == expectedUser.nSocket &&
                        user.UserGeneration == expectedGeneration);
                    if (current == null || !ReferenceEquals(current, expectedUser) ||
                        !ReferenceEquals(current.PlayObject, expectedPlayObject) ||
                        !current.boCertification)
                    {
                        ClearPendingClientMessagesLocked(expectedUser, "flush-end-stale");
                        batch = null;
                        continue;
                    }
                    batch = TakePendingClientMessagesLocked(current);
                    if (batch == null || batch.Count == 0)
                    {
                        current.PendingClientReplayInProgress = false;
                        batch = null;
                    }
                }
            }
            DrainDeferredUserCleanup();
        }

        private int OpenNewUser(int nSocket, ushort nGSocketIdx, string sIPaddr, IList<TGateUserInfo> UserList)
        {
            for (var i = 0; i < UserList.Count; i++)
            {
                if (UserList[i]?.nSocket == nSocket) return i;
            }
            int result;
            var GateUser = new TGateUserInfo
            {
                sAccount = string.Empty,
                sCharName = String.Empty,
                sIPaddr = sIPaddr,
                nSocket = nSocket,
                UserGeneration = Interlocked.Increment(ref _nextUserGeneration),
                nGSocketIdx = nGSocketIdx,
                nSessionID = 0,
                UserEngine = null,
                FrontEngine = M2Share.FrontEngine,
                PlayObject = null,
                dwNewUserTick = HUtil32.GetTickCount(),
                boCertification = false
            };
            for (var i = 0; i < UserList.Count; i++)
            {
                if (UserList[i] == null)
                {
                    UserList[i] = GateUser;
                    result = i;
                    return result;
                }
            }
            UserList.Add(GateUser);
            result = UserList.Count - 1;
            return result;
        }

        private void SendNewUserMsg(int nSocket, int nUserIdex)
        {
            if (GateInfo.Socket?.Connected != true) return;
            // Payload: 4B UserIndex (目标玩家在 UserList 中的槽位+1)
            var payload = BitConverter.GetBytes(nUserIdex);
            var pkt = new InternalPacket77
            {
                Magic = InternalPacket77.MAGIC,
                ConnID = (uint)nSocket,
                SeqID = (uint)Environment.TickCount,
                FrameLen = (ushort)(InternalPacket77.HEADER_SIZE + payload.Length),
                Cmd = Grobal2.GM_SERVERUSERINDEX,
                Field16 = (uint)Environment.TickCount,
                Field20 = (uint)payload.Length,
                Payload = payload
            };
            try
            {
                _sendQueue.AddToQueue(pkt.ToBytes());
            }
            catch (Exception exception)
            {
                var socketToClose = MarkSendFailureLocked(exception,
                    "TRunSocket::SendNewUserMsg");
                if (socketToClose != null)
                    _ = Task.Run(() => CloseSocket(socketToClose));
                throw;
            }
        }

        private Socket MarkSendFailureLocked(Exception exception,
            string operation)
        {
            lock (runSocketSection)
            {
                if (_closing) return GateInfo.Socket;
                _closing = true;
                GateInfo.boUsed = false;
                ClearPendingClientMessagesLocked("send-failure");
                _sendQueue.Stop();
                M2Share.ErrorMessage($"[Exception] {operation}: " +
                                     exception.Message);
                return GateInfo.Socket;
            }
        }

        private static void CloseSocket(Socket socket)
        {
            try { socket?.Close(); }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        }

        private void ExecGateMsg(int GateIdx, TGateInfo Gate, PacketHeader MsgHeader, byte[] MsgBuff, int nMsgLen)
        {
            int nUserIdx;
            const string sExceptionMsg = "[Exception] TRunSocket::ExecGateMsg";
            try
            {
                switch (MsgHeader.Ident)
                {
                    case Grobal2.GM_OPEN:
                        var sIPaddr = HUtil32.GetString(MsgBuff, 0, nMsgLen);
                        nUserIdx = OpenNewUser(MsgHeader.Socket, MsgHeader.SocketIdx, sIPaddr, Gate.UserList);
                        SendNewUserMsg(MsgHeader.Socket, nUserIdx + 1);
                        Gate.nUserCount = CountActiveUsersLocked();
                        break;
                    case Grobal2.GM_CLOSE:
                        CloseUser(MsgHeader.Socket);
                        break;
                    case NativeGameGateCommands.GateKeepAliveRequest:
                        // Native RunGate sends type 3 and expects the bare
                        // M2->gate type 13 reply on the same connection.
                        SendCheck(NativeGameGateCommands.M2KeepAliveReply);
                        break;
                    case NativeGameGateCommands.GateClientData:
                    case Grobal2.GM_DATA:
                        TGateUserInfo GateUser = null;
                        if (MsgHeader.UserIndex >= 1)
                        {
                            nUserIdx = MsgHeader.UserIndex - 1;
                            if (Gate.UserList.Count > nUserIdx)
                            {
                                GateUser = Gate.UserList[nUserIdx];
                                if (GateUser != null && GateUser.nSocket != MsgHeader.Socket)
                                {
                                    GateUser = null;
                                }
                            }
                        }
                        if (GateUser == null)
                        {
                            for (var i = 0; i < Gate.UserList.Count; i++)
                            {
                                if (Gate.UserList[i] == null)
                                {
                                    continue;
                                }
                                if (Gate.UserList[i].nSocket == MsgHeader.Socket)
                                {
                                    GateUser = Gate.UserList[i];
                                    PacketTrace($"[GateDataFound] socket={MsgHeader.Socket} idx={i} playObj={GateUser.PlayObject!=null} cert={GateUser.boCertification}");
                                    break;
                                }
                            }
                            if (GateUser == null)
                            {
#if GAMESVR_PACKET_TRACE
                                for (var i = 0; i < Gate.UserList.Count; i++)
                                {
                                    if (Gate.UserList[i] != null)
                                        PacketTrace($"[GateUserSlot] i={i} nSocket={Gate.UserList[i].nSocket} vs Msg={MsgHeader.Socket}");
                                }
#endif
                            }
                        }
                        if (GateUser != null)
                        {
                            if (GateUser.PlayObject != null && GateUser.UserEngine != null)
                            {
                                if (GateUser.boCertification &&
                                    GateUser.PendingClientReplayInProgress)
                                {
                                    QueuePendingClientMessageLocked(GateUser,
                                        MsgBuff, nMsgLen);
                                }
                                else if (GateUser.boCertification &&
                                         TryParseClientMessage(MsgBuff, nMsgLen,
                                             out var defMsg, out var sMsg,
                                             out var body))
                                {
                                    PacketTrace($"[Pkt] ident={defMsg.Ident}(0x{defMsg.Ident:X4}) " +
                                                $"recog={defMsg.Recog} param={defMsg.Param} " +
                                                $"tag={defMsg.Tag} series={defMsg.Series} len={nMsgLen}");
                                    M2Share.UserEngine.ProcessUserMessage(GateUser.PlayObject, defMsg, sMsg, body);
                                }
                            }
                            else if (GateUser.boCertification)
                            {
                                // Certification is acknowledged before the async DB load
                                // creates PlayObject. Preserve the exact client packet until
                                // SetGateUserList binds this same socket/generation.
                                QueuePendingClientMessageLocked(GateUser, MsgBuff,
                                    nMsgLen);
                            }
                            else if (!GateUser.boCertification)
                            {
                                // GameGate 注入格式: [ClientPacket 12B][cert body], 跳过二进制头
                                string sMsg;
                                if (MsgBuff.Length > 12 && MsgBuff[0] == 0)
                                    sMsg = HUtil32.GetString(MsgBuff, 12, MsgBuff.Length - 12);
                                else
                                    sMsg = HUtil32.StrPas(MsgBuff);
                                DoClientCertification(GateIdx, GateUser, MsgHeader.Socket, sMsg);
                            }
                        }
                        else
                        {
#if GAMESVR_PACKET_TRACE
                            // Log the actual nSocket values in the user list for debugging
                            string users = "";
                            for (int ui = 0; ui < Gate.UserList.Count; ui++)
                            {
                                var gu = Gate.UserList[ui];
                                if (gu != null) users += $"[{ui}:sock={gu.nSocket} playObj={gu.PlayObject!=null}]";
                                else users += $"[{ui}:null]";
                            }
                            PacketTrace($"[GateDataMiss] socket={MsgHeader.Socket} userIdx={MsgHeader.UserIndex} gateUsers={Gate.UserList.Count} list={users}");
#endif
                         }
                         break;
                    case Grobal2.GM_RECEIVE_OK:
                        CompleteFlowControl();
                        break;
                }
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage(sExceptionMsg + " " + ex.Message);
            }
        }

        
        
        
        public bool SetGateUserList(int nSocket, TPlayObject PlayObject)
        {
            var bound = false;
            TGateUserInfo boundUser = null;
            List<PendingClientPacket> pending = null;
            long boundGeneration = 0;
            var startReplay = false;
            lock (runSocketSection)
            {
                if (!_closing && PlayObject != null &&
                    PlayObject.m_nGateIdx == _gateIdx &&
                    PlayObject.m_nSocket == nSocket &&
                    PlayObject.m_UserGeneration != 0)
                {
                    for (var i = 0; i < GateInfo.UserList.Count; i++)
                    {
                        var gateUserInfo = GateInfo.UserList[i];
                        if (gateUserInfo == null ||
                            gateUserInfo.nSocket != nSocket ||
                            gateUserInfo.UserGeneration !=
                            PlayObject.m_UserGeneration ||
                            gateUserInfo.nGSocketIdx !=
                            PlayObject.m_nGSocketIdx ||
                            gateUserInfo.nSessionID !=
                            PlayObject.m_nSessionID ||
                            (!string.IsNullOrEmpty(gateUserInfo.sAccount) &&
                             !string.Equals(gateUserInfo.sAccount,
                                 PlayObject.m_sLoginAccount,
                                 StringComparison.OrdinalIgnoreCase)) ||
                            (!string.IsNullOrEmpty(gateUserInfo.sCharName) &&
                             !string.Equals(gateUserInfo.sCharName,
                                  PlayObject.m_sCharName,
                                  StringComparison.OrdinalIgnoreCase)) ||
                            (gateUserInfo.PlayObject != null &&
                             !ReferenceEquals(gateUserInfo.PlayObject,
                                  PlayObject)))
                            continue;

                        // The load pipeline can publish the same player twice.
                        // Once replay owns this generation, a duplicate bind
                        // must not detach a second batch or clear the owner's
                        // in-progress marker while the first batch is running.
                        if (gateUserInfo.PendingClientReplayInProgress &&
                            ReferenceEquals(gateUserInfo.PlayObject, PlayObject))
                        {
                            boundUser = gateUserInfo;
                            boundGeneration = gateUserInfo.UserGeneration;
                            bound = true;
                            PacketTrace($"[GateBind] gate={_gateIdx} socket={nSocket} " +
                                        $"generation={boundGeneration} duplicate=replay-owner");
                            break;
                        }
                        gateUserInfo.FrontEngine = null;
                        gateUserInfo.UserEngine = M2Share.UserEngine;
                        gateUserInfo.PlayObject = PlayObject;
                        boundUser = gateUserInfo;
                        boundGeneration = gateUserInfo.UserGeneration;
                        pending = TakePendingClientMessagesLocked(gateUserInfo);
                        gateUserInfo.PendingClientReplayInProgress =
                            pending != null && pending.Count > 0;
                        startReplay = pending != null && pending.Count > 0;
                        PacketTrace($"[GateBind] gate={_gateIdx} socket={nSocket} " +
                                    $"generation={boundGeneration} pending={pending?.Count ?? 0}");
                        bound = true;
                        break;
                    }
                }
                if (!bound && PlayObject != null)
                {
                    var failedGeneration = PlayObject.m_UserGeneration;
                    var failedUser = GateInfo.UserList.FirstOrDefault(user =>
                        user != null && user.nSocket == nSocket &&
                        user.UserGeneration == failedGeneration);
                    ClearPendingClientMessagesLocked(failedUser, "bind-failed");
                    PacketTrace($"[GateBindFail] gate={_gateIdx} socket={nSocket} " +
                                $"generation={failedGeneration} userFound={failedUser != null}");
                }
            }
            if (!bound && PlayObject != null)
            {
                PlayObject.m_boEmergencyClose = true;
                PlayObject.m_boSoftClose = true;
            }
            if (bound && startReplay)
                ReplayPendingClientMessages(boundUser, PlayObject,
                    boundGeneration, pending);
            return bound;
        }
    }
}
