using SystemModule;
using SystemModule.Common;

namespace DBSvr
{
    /// <summary>
    /// DBServer 配置管理器。
    /// 从战神原版 Delphi DBService.ini 加载全部配置项。
    ///
    /// 注意: 战神原作中 [GameGates] 节在文件中出现两次:
    ///   第一次仅有 ListenPort=5100,
    ///   第二次包含 ListenPort=5100 + GameGate1=127.0.0.1:7100 (完整数据)。
    /// IniFile 使用 Dictionary (后写入覆盖前写入), 因此最终使用的是第二个 [GameGates] 的完整数据。
    /// </summary>
    public class ConfigManager : IniFile
    {
        public ConfigManager(string fileName) : base(fileName)
        {
            Load();
        }

        public void LoadConfig()
        {
            // ===================== [Setup] =====================
            // 优先取 ServerName, 为空时回退到 ZoneName
            DBShare.sServerName = ReadString("Setup", "ServerName", DBShare.sServerName);
            if (string.IsNullOrEmpty(DBShare.sServerName) || DBShare.sServerName == "热血传奇")
                DBShare.sServerName = ReadString("Setup", "ZoneName", DBShare.sServerName);

            DBShare.nZoneIdx = ReadInteger("Setup", "ZoneIdx", DBShare.nZoneIdx);
            DBShare.nGroupIdx = ReadInteger("Setup", "GroupIdx", DBShare.nGroupIdx);
            DBShare.sMapFile = ReadString("Setup", "MapInfoFile", DBShare.sMapFile);
            DBShare.MaxSingleIpHumanCount = ReadInteger("Setup", "MaxSingleIpHumanCount", DBShare.MaxSingleIpHumanCount);
            DBShare.VipYBConsume = ReadInteger("Setup", "VipYBConsume",
                DBShare.VipYBConsume);
            DBShare.DBBackupPassword = ReadString("Setup", "DBBackupPassword", DBShare.DBBackupPassword);

            // latin1 keeps the latin1_bin name columns byte-transparent; names are decoded as GBK in LegacyGbkText.
            // Delphi 使用 GameServer 用户（非 root），密码从 Delphi 二进制提取
            var dbUser = ReadString("Setup", "DBUser", "root");
            var dbPassword = ReadString("Setup", "DBPassword", "");
            DBShare.DBConnection = $"server=127.0.0.1;uid={dbUser};pwd={dbPassword};database=mir3;charset=latin1;Pooling=true;Min Pool Size=10;Max Pool Size=200;Connection Lifetime=300;";
            DBShare.DBConnectionRoot = $"server=127.0.0.1;uid=root;pwd={ReadString("Setup", "RootPassword", "dsdfffsadsd")};database=mir3;charset=latin1;";

            // 初始化数据库用户 + 会话参数（对应 Delphi 启动时的 GRANT + SET SESSION）
            DatabaseInitService.Initialize();

            // ===================== [Server] =====================
            DBShare.boIsNewZone = ReadBool("Server", "IsNewZone", DBShare.boIsNewZone);
            DBShare.boAutoRepair = ReadBool("Server", "AutoRepair", DBShare.boAutoRepair);
            DBShare.boAutoClear = ReadBool("Server", "AutoClear", DBShare.boAutoClear);
            DBShare.nLoginType = ReadInteger("Server", "LoginType", DBShare.nLoginType);

            // ===================== [LoginGate] =====================
            DBShare.sIDServerAddr = ReadString("LoginGate", "IP", DBShare.sIDServerAddr);
            DBShare.nIDServerPort = ReadInteger("LoginGate", "Port", DBShare.nIDServerPort);

            // ===================== [GameGates] =====================
            // 第二次出现覆盖第一次, 拥有 ListenPort + GameGate1 的完整数据
            DBShare.g_nGatePort = ReadInteger("GameGates", "ListenPort", DBShare.g_nGatePort);
            var gameGate1 = ReadString("GameGates", "GameGate1", "127.0.0.1:7100");
            ParseGameGate(gameGate1);
            ResolvePublicGameGate();

            // ===================== [GameServer] =====================
            DBShare.nServerPort = ReadInteger("GameServer", "ListenPort", DBShare.nServerPort);
            DBShare.sServerAddr = ReadString("GameServer", "GameServer1", DBShare.sServerAddr);

            // ===================== [LogViewer] =====================
            DBShare.boSendLog = ReadBool("LogViewer", "SendLog", DBShare.boSendLog);
        }

        public void SetVipYbConsume(int value)
        {
            DBShare.VipYBConsume = value;
            if (System.IO.File.Exists(FileName))
                WriteInteger("Setup", "VipYBConsume", value);
        }

        /// <summary>
        /// 解析 "IP:Port" 格式的网关地址。
        /// 例如 "127.0.0.1:7100" → g_sGateAddr = "127.0.0.1"
        /// </summary>
        private void ParseGameGate(string gateSpec)
        {
            var colonIdx = gateSpec.LastIndexOf(':');
            if (colonIdx > 0 && colonIdx < gateSpec.Length - 1)
            {
                DBShare.g_sGateAddr = gateSpec.Substring(0, colonIdx);
                DBShare.g_nPublicGatePort = HUtil32.Str_ToInt(
                    gateSpec.Substring(colonIdx + 1), 7100);
            }
            else
            {
                DBShare.g_sGateAddr = gateSpec;
            }
            DBShare.g_sPublicGateAddr = DBShare.g_sGateAddr;
        }

        private void ResolvePublicGameGate()
        {
            if (!ReadBool("GameGatePublicAddressMaps", "OpenMaps", false)) return;

            for (var i = 1; i <= 20; i++)
            {
                var privateIp = ReadString("GameGatePublicAddressMaps",
                    $"PrivateIp{i}", string.Empty).Trim();
                if (!string.Equals(privateIp, DBShare.g_sGateAddr,
                        System.StringComparison.OrdinalIgnoreCase)) continue;

                var publicIp = ReadString("GameGatePublicAddressMaps",
                    $"PublicIp{i}", string.Empty).Trim();
                if (!string.IsNullOrEmpty(publicIp))
                    DBShare.g_sPublicGateAddr = publicIp;
                return;
            }
        }
    }
}
