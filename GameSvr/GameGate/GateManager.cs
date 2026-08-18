using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using SystemModule;
using SystemModule.Common;
using SystemModule.Packages;
using SystemModule.Packet;
using SystemModule.Sockets;

namespace GameSvr
{
    public class GateManager
    {
        private const int MaxGateConnections = 5000;
        [Conditional("GAMESVR_PACKET_TRACE")]
        private static void PacketTrace(string message)
        {
#if GAMESVR_PACKET_TRACE
            Debug.WriteLine(message);
#endif
        }

        private static readonly GateManager instance = new GateManager();
        public static GateManager Instance => instance;
        private readonly ISocketServer _gateSocket = null;
        private readonly ConcurrentDictionary<int, GateService> _gateDataService;

        private GateManager()
        {
            LoadRunAddr();
            _gateDataService = new ConcurrentDictionary<int, GateService>();
            _gateSocket = new ISocketServer(MaxGateConnections, 1024);
            _gateSocket.OnClientConnect += GateSocketClientConnect;
            _gateSocket.OnClientDisconnect += GateSocketClientDisconnect;
            _gateSocket.OnClientRead += GateSocketClientRead;
            _gateSocket.OnClientError += GateSocketClientError;
            _gateSocket.Init();
        }

        public void Start()
        {
            _gateSocket.Start(M2Share.g_Config.sGateAddr, M2Share.g_Config.nGatePort);
            M2Share.MainOutMessage($"游戏网关[{ M2Share.g_Config.sGateAddr}:{M2Share.g_Config.nGatePort}]已启动...");
        }

        public void Stop()
        {
            _gateSocket.Shutdown();
        }

        private void LoadRunAddr()
        {
            var sFileName = ".\\RunAddr.txt";
            if (File.Exists(sFileName))
            {
                var runAddrList = new StringList();
                runAddrList.LoadFromFile(sFileName);
                M2Share.TrimStringList(runAddrList);
            }
        }

        private void AddGate(AsyncUserToken e)
        {
            const string sGateOpen = "游戏网关({0})已打开...";
            const string sKickGate = "服务器未就绪: {0}";
            if (M2Share.boStartReady)
            {
                if (_gateDataService.Count >= MaxGateConnections)
                {
                    e.Socket.Close();
                    return;
                }
                var gateInfo = new TGateInfo();
                gateInfo.nSendMsgCount = 0;
                gateInfo.nSendRemainCount = 0;
                gateInfo.dwSendTick = HUtil32.GetTickCount();
                gateInfo.nSendMsgBytes = 0;
                gateInfo.nSendedMsgCount = 0;
                gateInfo.boUsed = true;
                gateInfo.SocketId = e.ConnectionId;
                gateInfo.Socket = e.Socket;
                gateInfo.UserList = new List<TGateUserInfo>();
                gateInfo.nUserCount = 0;
                gateInfo.boSendKeepAlive = false;
                gateInfo.nSendChecked = 0;
                gateInfo.nSendBlockCount = 0;
                var gateService = new GateService(e.ConnectionId, gateInfo);
                if (!_gateDataService.TryAdd(e.ConnectionId, gateService))
                {
                    gateService.Stop();
                    e.Socket?.Close();
                    return;
                }
                M2Share.MainOutMessage(string.Format(sGateOpen, e.EndPoint));
                gateService.StartQueueService();
            }
            else
            {
                M2Share.ErrorMessage(string.Format(sKickGate, e.EndPoint));
                e.Socket.Close();
            }
        }

        public void CloseUser(int gateIdx, int nSocket)
        {
            if (_gateDataService.TryGetValue(gateIdx, out var dataService))
            {
                dataService.CloseUser(nSocket);
            }
            else
            {
                Console.WriteLine("未找到用户对应Socket服务.");
            }
        }

        public bool CloseUser(int gateIdx, int nSocket,
            long expectedGeneration)
        {
            return _gateDataService.TryGetValue(gateIdx, out var dataService) &&
                   dataService.CloseUser(nSocket, expectedGeneration);
        }

        public bool IsCurrentUser(int gateIdx, int nSocket, long generation)
        {
            return _gateDataService.TryGetValue(gateIdx, out var dataService) &&
                   dataService.IsCurrentUser(nSocket, generation);
        }

        public void KickUser(string sAccount, int nSessionID, int payMode)
        {
            const string sExceptionMsg = "[Exception] TRunSocket::KickUser";
            try
            {
                foreach (var gateService in _gateDataService.Values.ToList())
                    gateService.KickUser(sAccount, nSessionID, payMode);
            }
            catch (Exception e)
            {
                M2Share.ErrorMessage(sExceptionMsg);
                M2Share.ErrorMessage(e.Message, MessageType.Error);
            }
        }

        public void CloseAllGate()
        {
            foreach (var gateService in _gateDataService.Values.ToArray())
            {
                if (gateService.TryGetCurrentSocket(out var socket))
                    socket.Close();
            }
        }

        public void CloseErrGate(Socket Socket)
        {
            if (Socket.Connected)
            {
                Socket.Close();
            }
        }

        private void CloseGate(AsyncUserToken e)
        {
            const string sGateClose = "游戏网关({0}:{1})已关闭...";
            if (!_gateDataService.TryGetValue(e.ConnectionId,
                    out var dataService) ||
                !dataService.TryCloseConnection(e.Socket))
            {
                return;
            }
            var removed = ((ICollection<KeyValuePair<int, GateService>>)
                _gateDataService).Remove(new KeyValuePair<int, GateService>(
                e.ConnectionId, dataService));
            dataService.Stop();
            if (!removed) return;
            M2Share.ErrorMessage(string.Format(sGateClose,
                e.EndPoint?.Address, e.EndPoint?.Port ?? 0));
        }

        public void SendOutConnectMsg(int nGateIdx, int nSocket, int nGsIdx)
        {
            if (!TrySendOutConnectMsg(nGateIdx, nSocket, nGsIdx, 0, false))
                Console.WriteLine("发送玩家退出消息失败.");
        }

        public bool SendOutConnectMsg(int nGateIdx, int nSocket, int nGsIdx,
            long expectedGeneration)
        {
            var result = TrySendOutConnectMsg(nGateIdx, nSocket, nGsIdx,
                expectedGeneration, true);
            if (!result) Console.WriteLine("发送玩家退出消息失败.");
            return result;
        }

        private bool TrySendOutConnectMsg(int nGateIdx, int nSocket,
            int nGsIdx, long expectedGeneration, bool requireCurrentUser)
        {
            var defMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_OUTOFCONNECTION, 0, 0, 0, 0);
            var msgHeader = new PacketHeader();
            msgHeader.PacketCode = Grobal2.RUNGATECODE;
            msgHeader.Socket = nSocket;
            msgHeader.SocketIdx = (ushort)nGsIdx;
            msgHeader.Ident = Grobal2.GM_DATA;
            msgHeader.PackLength = ClientPacket.PackSize;
            ClientOutMessage outMessage = new ClientOutMessage(msgHeader, defMsg);
            if (!_gateDataService.TryGetValue(nGateIdx, out var dataService))
                return false;
            var buffer = outMessage.GetBuffer();
            return requireCurrentUser
                ? dataService.HandleCurrentUserSendBuffer(buffer, nSocket,
                    (ushort)nGsIdx, expectedGeneration)
                : dataService.HandleSendBuffer(buffer);
        }

        
        
        
        public bool SetGateUserList(int nGateIdx, int nSocket,
            TPlayObject PlayObject)
        {
            if (_gateDataService.TryGetValue(nGateIdx, out var dataService))
                return dataService.SetGateUserList(nSocket, PlayObject);
            if (PlayObject != null)
            {
                PlayObject.m_boEmergencyClose = true;
                PlayObject.m_boSoftClose = true;
            }
            return false;
        }

        public void Run()
        {
            var dwRunTick = HUtil32.GetTickCount();
            if (M2Share.boStartReady)
            {
                if (_gateDataService.Count > 0)
                {
                    var gateServiceList = _gateDataService.Values.ToList();
                    foreach (var gateService in gateServiceList)
                    {
                        var gateInfo = gateService.GateInfo;
                        if (gateInfo.Socket != null)
                        {
                            if (HUtil32.GetTickCount() - gateInfo.dwSendTick >= 1000)
                            {
                                gateInfo.dwSendTick = HUtil32.GetTickCount();
                                gateInfo.nSendMsgBytes = gateInfo.nSendBytesCount;
                                gateInfo.nSendedMsgCount = gateInfo.nSendCount;
                                gateInfo.nSendBytesCount = 0;
                                gateInfo.nSendCount = 0;
                            }
                            if (gateInfo.boSendKeepAlive)
                            {
                                gateInfo.boSendKeepAlive = false;
                                gateService.SendCheck(Grobal2.GM_CHECKSERVER);
                            }
                            gateService.ResumeFlowControlIfTimedOut();
                            // 每 5 秒发送心跳 ACK (CMD=0x0C), 防止 GameGate 超时断连
                            if ((HUtil32.GetTickCount() - gateInfo.dwCompactAckTick) > 5000)
                            {
                                gateInfo.dwCompactAckTick = HUtil32.GetTickCount();
                                gateService.SendCompactAck();
                            }
                        }
                    }
                }
            }
            M2Share.g_nSockCountMin = HUtil32.GetTickCount() - dwRunTick;
            if (M2Share.g_nSockCountMin > M2Share.g_nSockCountMax)
            {
                M2Share.g_nSockCountMax = M2Share.g_nSockCountMin;
            }
        }

        private void SendGateTestMsg(int nIndex)
        {
            var defMsg = new ClientPacket();
            var msgHdr = new PacketHeader
            {
                PacketCode = Grobal2.RUNGATECODE,
                Socket = 0,
                Ident = Grobal2.GM_TEST,
                PackLength = 100
            };
            var nLen = msgHdr.PackLength + PacketHeader.PacketSize;
            using var memoryStream = new MemoryStream();
            var backingStream = new BinaryWriter(memoryStream);
            backingStream.Write(nLen);
            backingStream.Write(msgHdr.GetBuffer());
            backingStream.Write(defMsg.GetBuffer());
            memoryStream.Seek(0, SeekOrigin.Begin);
            var data = new byte[memoryStream.Length];
            memoryStream.Read(data, 0, data.Length);
            if (!M2Share.GateManager.AddGateBuffer(nIndex, data))
            {
                data = null;
            }
        }

        public bool AddGateBuffer(int gateIdx, byte[] buffer)
        {
            if (_gateDataService.TryGetValue(gateIdx, out var dataService))
            {
                return dataService.HandleSendBuffer(buffer);
            }
            else
            {
                PacketTrace($"[GateMiss] gateIdx={gateIdx} not in _gateDataService (count={_gateDataService.Count})");
            }
            return false;
        }

        public int BroadcastLegacyType18(LegacyGateType18 packet)
        {
            if (packet == null) return 0;

            var frame = packet.ToBytes();
            var sentCount = 0;
            foreach (var gateService in _gateDataService.Values.ToList())
            {
                if (gateService.HandleLegacyType18(frame))
                    sentCount++;
            }
            return sentCount;
        }

        public int BroadcastInternalPacket77(InternalPacket77 packet)
        {
            if (packet == null
                || (packet.Payload?.Length ?? 0) > InternalPacket77.MAX_PAYLOAD_SIZE)
                return 0;

            var frame = packet.ToBytes();
            var sentCount = 0;
            foreach (var gateService in _gateDataService.Values.ToList())
            {
                if (gateService.HandleInternalPacket77(frame))
                    sentCount++;
            }
            return sentCount;
        }

        
        
        
        #region Socket Events

        private void GateSocketClientError(object sender, AsyncSocketErrorEventArgs e)
        {
            
            Console.WriteLine(e.Exception);
        }

        private void GateSocketClientDisconnect(object sender, AsyncUserToken e)
        {
            M2Share.GateManager.CloseGate(e);
        }

        private void GateSocketClientConnect(object sender, AsyncUserToken e)
        {
            M2Share.GateManager.AddGate(e);
        }

        private void GateSocketClientRead(object sender, AsyncUserToken e)
        {
            if (_gateDataService.TryGetValue(e.ConnectionId,
                    out var dataService))
            {
                var nMsgLen = e.BytesReceived;
                var data = new byte[e.BytesReceived];
                Buffer.BlockCopy(e.ReceiveBuffer, e.Offset, data, 0, nMsgLen);
                dataService.HandleReceiveBuffer(e.Socket, nMsgLen, data);
            }
            else
            {
                Console.WriteLine("错误的网关数据");
            }
        }

        #endregion
    }

}
