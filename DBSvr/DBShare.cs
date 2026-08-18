using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using DBSvr.Core;
using SystemModule;
using SystemModule.Common;
using SystemModule.Packet;

namespace DBSvr
{
    /// <summary>
    /// DBServer 全局共享状态。
    /// 从 Delphi DBServer 二进制逆向工程完整还原配置项和数据结构。
    /// </summary>
    public class DBShare
    {
        // ===================== 端口与地址 =====================
        /// <summary>GameSvr 连接端口 (6000)</summary>
        public static int nServerPort = 6000;
        /// <summary>GameSvr 绑定地址</summary>
        public static string sServerAddr = "*";
        /// <summary>网关连接端口 (5100)</summary>
        public static int g_nGatePort = 5100;
        /// <summary>网关绑定地址</summary>
        public static string g_sGateAddr = "*";
        /// <summary>LoginGate 返回给客户端的 GameGate 地址</summary>
        public static string g_sPublicGateAddr = "127.0.0.1";
        /// <summary>LoginGate 返回给客户端的 GameGate 端口</summary>
        public static int g_nPublicGatePort = 7100;
        /// <summary>LoginSvr 端口 (对应原版 DBServerListen=5600)</summary>
        public static int nIDServerPort = 5600;
        /// <summary>LoginSvr 地址</summary>
        public static string sIDServerAddr = "127.0.0.1";

        // ===================== 数据库 =====================
        /// <summary>MySQL 连接字符串；latin1 保持 latin1_bin 名称列中的原始 GBK 字节。</summary>
        public static string DBConnection = "server=127.0.0.1;uid=root;pwd=;database=mir3;charset=latin1;Pooling=true;Min Pool Size=10;Max Pool Size=200;Connection Lifetime=300;";
        /// <summary>root 连接字符串 (仅用于初始化: 建用户/授权)</summary>
        public static string DBConnectionRoot = "server=127.0.0.1;uid=root;pwd=dsdfffsadsd;database=mir3;charset=latin1;";
        /// <summary>数据库备份密码</summary>
        public static string DBBackupPassword = "xiaoxin567";

        // ===================== 服务器基础 =====================
        public static string sServerName = "热血传奇";
        public static bool g_boEnglishNames = false;
        public static bool g_boDynamicIPMode = false;
        public static bool boDenyChrName = true;

        // ===================== 战神 [Setup] 扩展 =====================
        /// <summary>区号 (对应 serverlist 的 Area 字段)</summary>
        public static int nZoneIdx = 0;
        /// <summary>组号</summary>
        public static int nGroupIdx = 0;
        public static int VipYBConsume = 0;

        // ===================== 战神 [Server] 扩展 =====================
        /// <summary>是否新区</summary>
        public static bool boIsNewZone = false;
        /// <summary>自动修复表结构</summary>
        public static bool boAutoRepair = true;
        /// <summary>自动清理旧数据</summary>
        public static bool boAutoClear = true;
        /// <summary>登录类型</summary>
        public static int nLoginType = 0;

        // ===================== 战神 [LogViewer] =====================
        /// <summary>是否发送日志到 LogViewer</summary>
        public static bool boSendLog = false;

        // ===================== 文件路径 =====================
        public static string sMapFile = string.Empty;
        // 配置文件名常量 (由 UserSocService 和 DBShare 引用)
        public const string sGateConfFileName = "ServerInfo.txt";
        private static string sServerIPConfFileName = "AddrTable.txt";
        private static string sGateIDConfFileName = "SelectID.txt";

        // ===================== 数据结构 =====================
        public static StringList DenyChrNameList = null;
        public static StringList g_ClearMakeIndex = null;
        public static TRouteInfo[] g_RouteInfo = new TRouteInfo[20];
        private static Hashtable ServerIPList = null;
        private static Dictionary<string, short> GateIDList = null;

        // ===================== 定时与阈值 =====================
        /// <summary>清理间隔 (ms)</summary>
        public static int dwInterval = 1000;
        /// <summary>单 IP 同时在线上限</summary>
        public static int MaxSingleIpHumanCount = 100;
        public static bool NativeQueueEnabled = false;
        /// <summary>排行榜活跃窗口 (30天)</summary>
        public const int RankingActiveDays = 30;
        /// <summary>跨服记录保留天数</summary>
        public const int TransferRecordDays = 7;
        /// <summary>分页查询每批大小</summary>
        public const int BatchLimit = 5000;
        /// <summary>排行榜返回上限</summary>
        public const int RankLimit = 100;

        // ===================== 初始化 =====================
        public static void Initialization()
        {
            DenyChrNameList = new StringList();
            ServerIPList = new Hashtable();
            GateIDList = new Dictionary<string, short>();
            g_ClearMakeIndex = new StringList();
        }

        // ===================== 配置加载 =====================
        public static void LoadConfig()
        {
            LoadIPTable();
            LoadGateID();
        }

        private static void LoadIPTable()
        {
            ServerIPList.Clear();
            try
            {
                var list = new StringList();
                list.LoadFromFile(sServerIPConfFileName);
                for (var i = 0; i < list.Count; i++)
                {
                    if (!ServerIPList.ContainsKey(list[i]))
                        ServerIPList.Add(list[i], list[i]);
                }
            }
            catch
            {
                MainOutMessage("加载IP列表文件 " + sServerIPConfFileName + " 出错!!!");
            }
        }

        private static void LoadGateID()
        {
            GateIDList.Clear();
            if (!File.Exists(sGateIDConfFileName)) return;

            var list = new StringList();
            list.LoadFromFile(sGateIDConfFileName);
            for (var i = 0; i < list.Count; i++)
            {
                var line = list[i];
                if (string.IsNullOrEmpty(line) || line[0] == ';') continue;

                string sID = string.Empty;
                string sIPaddr = string.Empty;
                line = HUtil32.GetValidStr3(line, ref sID, new[] { " ", "\09" });
                line = HUtil32.GetValidStr3(line, ref sIPaddr, new[] { " ", "\09" });
                int nID = HUtil32.Str_ToInt(sID, -1);
                if (nID < 0) continue;
                GateIDList[sIPaddr] = (short)nID;
            }
        }

        // ===================== 工具方法 =====================
        public static short GetGateID(string sIPaddr)
        {
            if (GateIDList.TryGetValue(sIPaddr, out short id))
                return id;
            return 0;
        }

        /// <summary>
        /// IP校验: 自动放行本地回环/局域网，然后检查 IP 表。
        /// </summary>
        public static bool CheckServerIP(string sIP)
        {
            if (sIP == "127.0.0.1" || sIP == "::1" || sIP.StartsWith("192.168.") || sIP == "localhost")
                return true;
            return ServerIPList.ContainsKey(sIP);
        }

        public static void MainOutMessage(string sMsg)
        {
            Console.WriteLine(sMsg);
        }
    }

    // ===================== 数据类型定义 =====================

    /// <summary>
    /// 路由信息 (多网关负载均衡)。
    /// </summary>
    public class TRouteInfo
    {
        public int nGateCount;
        public string sSelGateIP;
        public string[] sGameGateIP;
        public int[] nGameGatePort;

        public TRouteInfo()
        {
            sGameGateIP = new string[8];
            nGameGatePort = new int[8];
        }
    }

    /// <summary>
    /// 游戏服务器连接信息。
    /// </summary>
    public class TServerInfo
    {
        public int nSckHandle;
        public readonly object SyncRoot = new object();
        public readonly DbServerWireModeDetector WireModeDetector = new DbServerWireModeDetector();
        public readonly RequestServerFrameParser FrameParser = new RequestServerFrameParser();
        public readonly LegacyDbServerStreamParser NativeFrameParser =
            new LegacyDbServerStreamParser(NativeDbServerProtocol.MaximumFrameLength,
                LegacyDbServerStreamParser.NativeMaximumBufferedLength);
        public Socket Socket;
        public ushort NativeHeartbeatUninitializedWord;
        public int NativeHeartbeatState;
        public int NativeUserCount;
        public long NativeHeartbeatTick;
        public int NativeServerType;
        public int NativeRegistrationInitialized;
        public long NativeRankingGenerationSent = -1;
    }

    /// <summary>
    /// 角色索引记录 (对应 user_index 表)。
    /// </summary>
    public class HumRecordData
    {
        public int Id;
        public TRecordHeader Header;
        public string sChrName;
        public string sAccount;   // PTID
        public bool boDeleted;
        public byte boSelected;
    }

    /// <summary>
    /// 全局会话信息 (账号级)。
    /// </summary>
    public class TGlobaSessionInfo
    {
        public string sAccount;
        public string sIPaddr;
        public int nSessionID;
        public long dwAddTick;
        public DateTime dAddDate;
        public bool boLoadRcd;      // 是否已加载角色
        public bool boStartPlay;    // 是否已开始游戏
    }

    /// <summary>
    /// 网关信息。
    /// </summary>
    public class TGateInfo
    {
        public Socket Socket;
        public string sGateaddr;
        public IList<TUserInfo> UserList;
        public long dwTick10;
        public short nGateID;
        public TGateWireMode WireMode;
        public ushort NativeRoutePort;
        public byte NativeRouteID;
        public PercentDollarFrameParser FrameParser = new PercentDollarFrameParser();
        public YbDbLegacy77StreamParser NativeFrameParser = new YbDbLegacy77StreamParser();
    }

    public enum TGateWireMode
    {
        Unknown,
        PrivatePercentDollar,
        Native77
    }

    /// <summary>
    /// 用户信息 (客户端连接)。
    /// </summary>
    public class TUserInfo
    {
        public string sAccount;
        public string sUserIPaddr;
        public string sGateIPaddr;
        public string sConnID;
        public int nSessionID;
        public Socket Socket;
        public string sText;
        public bool boChrSelected;
        public bool boChrQueryed;
        public long dwTick34;
        public long dwChrTick;
        public short nSelGateID;
        public TGateWireMode WireMode;
        public int NativeQueryId;
        public ushort NativeConnectionId;
        public int NativeAuthTick;
        public NativeLoginGateAuthResponse NativeAuthResponse;
        public string NativeText102;
        public long NativeLoginDateTimeBits;
        public string sReconnectID;
        public NativeSwitchHandoffSlot NativeSwitchHandoff { get; } = new();

        /// <summary>
        /// 登录排队位次，复刻原版账号/会话对象的 <c>word[Self+0x9C]</c>。
        /// 0 = 不在排队中（原版 <c>0x5CE30A cmp word [eax+0x9c],0 / jbe</c> 放行）。
        ///
        /// 唯一 word 写者是 <c>0x5CFC90 mov word [edx+0x9c], ax</c>；紧随其后
        /// <c>0x5CFC97 cmp word [ebp-6],0xa / ja</c> ⇒ 位次 &lt;= 10 时才
        /// <c>call 0x5CFCAC</c> 发位次通知（opcode 0x10EC = 4332）。
        ///
        /// ⚠️ C# 目前**没有**排队的生产者：<c>DrainNativeAdmissionQueue</c> 是空实现，
        /// 注释自承「阈值仍在闭合中」。故本字段实际恒为 0，
        /// 即那道门恒放行 —— 与「未启用排队」的原版行为一致，不是伪造。
        /// 位次通知（4332）与准入阈值仍是已知缺口。
        /// </summary>
        public ushort NativeQueuePosition;
    }

    /// <summary>
    /// 角色简略查询结果。
    /// </summary>
    public class TQueryChr
    {
        public byte btJob;
        public byte btHair;
        public byte btSex;
        public ushort wLevel;
        public string sName;
    }
}
