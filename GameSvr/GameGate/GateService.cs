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

        private readonly int _gateIdx;
        private readonly TGateInfo _gateInfo;
        private static long _nextUserGeneration;
        private readonly SendQueue _sendQueue;
        private readonly object _sendStateLock = new();
        private readonly Queue<byte[]> _pendingSendBuffers = new();
        private readonly Queue<TGateUserInfo> _deferredUserCleanup = new();
        private readonly object runSocketSection;
        private bool _closing;

        private readonly InternalPacket77FrameParser _frameParser = new(maximumFrameLength: 0x8000);
        private Task _queueTask;

        public GateService(int gateIdx, TGateInfo gateInfo)
        {
            _gateIdx = gateIdx;
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
            }
            lock (_sendStateLock) _pendingSendBuffers.Clear();
            _sendQueue.Stop();
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

                // 封装为 77BBAA33 帧发送
                var pkt = new InternalPacket77
                {
                    Magic = InternalPacket77.MAGIC,
                    ConnID = connId,
                    SeqID = (uint)Environment.TickCount,
                    FrameLen = (ushort)(InternalPacket77.HEADER_SIZE + payload.Length),
                    Cmd = cmd,
                    Field16 = (uint)Environment.TickCount,
                    Field20 = (uint)payload.Length,
                    Payload = payload
                };
                var pktBytes = pkt.ToBytes();

                if (checkBlockBytes > 0 && GateInfo.nSendBlockCount > 0
                    && GateInfo.nSendBlockCount + (long)pktBytes.Length >= checkBlockBytes)
                {
                    QueueControlPacketLocked(Grobal2.GM_RECEIVE_OK, compactAck: false);
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
                    QueueControlPacketLocked(Grobal2.GM_RECEIVE_OK, compactAck: false);
                    GateInfo.nSendChecked = 1;
                    GateInfo.dwSendCheckTick = HUtil32.GetTickCount();
                    return;
                }
            }
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
                        QueueControlPacketLocked(nIdent, compactAck: false);
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

        public void SendCompactAck()
        {
            Socket socketToClose = null;
            lock (runSocketSection)
            {
                if (_closing || GateInfo.Socket?.Connected != true) return;
                lock (_sendStateLock)
                {
                    try
                    {
                        QueueControlPacketLocked(0x0C, compactAck: true);
                    }
                    catch (Exception exception)
                    {
                        socketToClose = MarkSendFailureLocked(exception,
                            "TRunSocket::SendCompactAck");
                    }
                }
            }
            if (socketToClose != null) CloseSocket(socketToClose);
        }

        private void QueueControlPacketLocked(ushort nIdent, bool compactAck)
        {
            var pkt = new InternalPacket77
            {
                Magic = InternalPacket77.MAGIC,
                ConnID = 0,
                SeqID = (uint)Environment.TickCount,
                FrameLen = compactAck
                    ? InternalPacket77.ACK_FRAME_LEN
                    : (ushort)InternalPacket77.HEADER_SIZE,
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
                for (var i = 0; i < GateInfo.UserList.Count; i++)
                    GateInfo.UserList[i] = null;
                GateInfo.nUserCount = 0;
                GateInfo.boUsed = false;
                GateInfo.Socket = null;
                _frameParser.Reset();
            }

            foreach (var gateUser in users)
            {
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
                    case Grobal2.GM_CHECKCLIENT:
                        Gate.boSendKeepAlive = true;
                        break;
                    case Grobal2.GM_RECEIVE_OK:
                        CompleteFlowControl();
                        break;
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
                                if (GateUser.boCertification && nMsgLen >= 12)
                                {
                                    var defMsg = Packets.ToPacket<ClientPacket>(MsgBuff);
                                    PacketTrace($"[Pkt] ident={defMsg.Ident}(0x{defMsg.Ident:X4}) recog={defMsg.Recog} len={nMsgLen}");
                                    byte[] body = null;
                                    string sMsg = null;
                                    // `nMsgLen - 12` is 战神's CM dispatcher fourth parameter
                                    // (0x6B1B11 `movzx esi,word [node+8]` / 0x6B1B15 `sub esi,0x0C`),
                                    // so this body array's Length is what the length gates in
                                    // NativeClientBodyLengthGate compare against. It has to reach
                                    // ProcessUserMessage intact — do not shorten or re-encode it here.
                                    if (nMsgLen > ClientPacket.PackSize)
                                    {
                                        var bodyLength = Math.Min(nMsgLen, MsgBuff.Length) - ClientPacket.PackSize;
                                        body = new byte[bodyLength];
                                        Buffer.BlockCopy(MsgBuff, ClientPacket.PackSize, body, 0, bodyLength);

                                        // Keep the exact bytes for binary client commands while preserving the
                                        // legacy text view used by existing message handlers.
                                        sMsg = DecodeClientMessageBody(defMsg.Ident, body);
                                    }
                                    M2Share.UserEngine.ProcessUserMessage(GateUser.PlayObject, defMsg, sMsg, body);
                                }
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
                        gateUserInfo.FrontEngine = null;
                        gateUserInfo.UserEngine = M2Share.UserEngine;
                        gateUserInfo.PlayObject = PlayObject;
                        bound = true;
                        break;
                    }
                }
            }
            if (!bound && PlayObject != null)
            {
                PlayObject.m_boEmergencyClose = true;
                PlayObject.m_boSoftClose = true;
            }
            return bound;
        }
    }
}
