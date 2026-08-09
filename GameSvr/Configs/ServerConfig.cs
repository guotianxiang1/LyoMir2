using System.Buffers.Binary;
using SystemModule;
using SystemModule.Common;

namespace GameSvr.Configs
{
    /// <summary>
    /// Reads 战神 (Zhanshen) original Delphi !Setup.txt sections:
    ///   [Server], [Share], [Setup], [GameGate], [DataBase], [Authenticate]
    /// ([Names] section is handled by StringConfig.cs)
    /// Does NOT write-back to the config file. Missing keys use C# defaults from GameSvrConfig.
    /// </summary>
    public class ServerConfig : IniFile
    {
        private readonly string _itemNumberPath;
        private readonly string _formGmSetPath;
        private readonly object _itemNumberSaveLock = new();

        public ServerConfig(string fileName) : base(fileName)
        {
            var configDirectory = Path.GetDirectoryName(fileName) ?? string.Empty;
            _itemNumberPath = Path.Combine(configDirectory, "ItemNumber.Dat");
            _formGmSetPath = Path.Combine(configDirectory, "FormGMSet.ini");
            Load();
        }

        public void LoadConfig()
        {
            // ============ [Server] section ============
            M2Share.nServerIndex = ReadInteger("Server", "ServerIndex", M2Share.nServerIndex);
            var itemNumberSeed = unchecked((uint)M2Share.nServerIndex + 1000u);
            M2Share.g_Config.nItemNumberSeed = unchecked((int)itemNumberSeed);
            M2Share.g_Config.nItemNumber = LoadItemNumber(itemNumberSeed);
            M2Share.g_Config.sServerName = ReadString("Server", "ServerName", M2Share.g_Config.sServerName);
            M2Share.g_Config.boTestServer = ReadBool("Server", "testserver", M2Share.g_Config.boTestServer);
            M2Share.g_Config.sDBAddr = ReadString("Server", "DBAddr", M2Share.g_Config.sDBAddr);
            var ybDbAddr = ReadString("Server", "YBDBAddr", "");
            M2Share.g_Config.sYBDBAddr = string.IsNullOrWhiteSpace(ybDbAddr)
                ? M2Share.g_Config.sDBAddr
                : ybDbAddr;
            M2Share.g_Config.nDBPort = ReadInteger("Server", "DBPort", M2Share.g_Config.nDBPort);
            M2Share.g_dwHumLimit = ReadInteger("Server", "HumLimit", M2Share.g_dwHumLimit);
            M2Share.g_dwMonLimit = ReadInteger("Server", "MonLimit", M2Share.g_dwMonLimit);
            M2Share.g_dwZenLimit = ReadInteger("Server", "ZenLimit", M2Share.g_dwZenLimit);
            M2Share.g_dwNpcLimit = ReadInteger("Server", "NpcLimit", M2Share.g_dwNpcLimit);
            M2Share.g_dwSocLimit = ReadInteger("Server", "SocLimit", M2Share.g_dwSocLimit);
            M2Share.nDecLimit = ReadInteger("Server", "DecLimit", M2Share.nDecLimit);
            M2Share.g_Config.nUserFull = ReadInteger("Server", "UserFull", M2Share.g_Config.nUserFull);
            M2Share.g_Config.nZenFastStep = ReadInteger("Server", "ZenFastStep", M2Share.g_Config.nZenFastStep);
            M2Share.g_Config.nSendBlock = ReadInteger("Server", "SendBlock", M2Share.g_Config.nSendBlock);
            M2Share.g_Config.nCheckBlock = ReadInteger("Server", "CheckBlock", M2Share.g_Config.nCheckBlock);
            M2Share.g_Config.nAvailableBlock = ReadInteger("Server", "AvailableBlock", M2Share.g_Config.nAvailableBlock);
            M2Share.g_Config.nGateLoad = ReadInteger("Server", "GateLoad", M2Share.g_Config.nGateLoad);
            M2Share.g_Config.sLogServerAddr = ReadString("Server", "LogServerAddr", M2Share.g_Config.sLogServerAddr);
            M2Share.g_Config.nLogServerPort = ReadInteger("Server", "LogServerPort", M2Share.g_Config.nLogServerPort);
            // DiscountForNightTime: 0=false, 1=true in !Setup.txt
            M2Share.g_Config.boDiscountForNightTime = ReadInteger("Server", "DiscountForNightTime", 0) != 0;
            M2Share.g_Config.nHalfFeeStart = ReadInteger("Server", "HalfFeeStart", M2Share.g_Config.nHalfFeeStart);
            M2Share.g_Config.nHalfFeeEnd = ReadInteger("Server", "HalfFeeEnd", M2Share.g_Config.nHalfFeeEnd);
            // GMSuperCode is read but not used in C# — skip.

            // ============ [Share] section ============
            M2Share.g_Config.sBaseDir = ReadString("Share", "BaseDir", M2Share.g_Config.sBaseDir);
            M2Share.g_Config.sGuildDir = ReadString("Share", "GuildDir", M2Share.g_Config.sGuildDir);
            M2Share.g_Config.sGuildFile = ReadString("Share", "GuildFile", M2Share.g_Config.sGuildFile);
            M2Share.g_Config.sVentureDir = ReadString("Share", "VentureDir", M2Share.g_Config.sVentureDir);
            M2Share.g_Config.sConLogDir = ReadString("Share", "ConLogDir", M2Share.g_Config.sConLogDir);
            M2Share.g_Config.sCastleDir = ReadString("Share", "CastleDir", M2Share.g_Config.sCastleDir);
            M2Share.g_Config.sEnvirDir = ReadString("Share", "EnvirDir", M2Share.g_Config.sEnvirDir);
            M2Share.g_Config.sMapDir = ReadString("Share", "MapDir", M2Share.g_Config.sMapDir);
            M2Share.g_Config.sCastleFile = ReadString("Share", "CastleFile", M2Share.g_Config.sCastleFile);

            // ============ [Setup] section ============
            M2Share.g_Config.sHomeMap = ReadString("Setup", "HomeMap", M2Share.g_Config.sHomeMap);
            M2Share.g_Config.nHomeX = Read<short>("Setup", "HomeX", M2Share.g_Config.nHomeX);
            M2Share.g_Config.nHomeY = Read<short>("Setup", "HomeY", M2Share.g_Config.nHomeY);
            M2Share.g_Config.sClientFile1 = ReadString("Setup", "ClientFile1", M2Share.g_Config.sClientFile1);
            M2Share.g_Config.sClientFile2 = ReadString("Setup", "ClientFile2", M2Share.g_Config.sClientFile2);
            M2Share.g_Config.sClientFile3 = ReadString("Setup", "ClientFile3", M2Share.g_Config.sClientFile3);
            M2Share.g_Config.nPKAddPoint = ReadInteger("Setup", "PKAddPoint", M2Share.g_Config.nPKAddPoint);
            M2Share.g_Config.nPKPunishPoint = ReadInteger("Setup", "PKPunishPoint", M2Share.g_Config.nPKPunishPoint);
            M2Share.g_Config.nGSTaskVersion = ReadInteger("Setup", "GS_Task_Version", M2Share.g_Config.nGSTaskVersion);

            // ============ [Authenticate] section ============
            M2Share.g_Config.boAuthOpen = ReadInteger("Authenticate", "opened", 0) != 0;
            M2Share.g_Config.nAuthStartDay = ReadInteger("Authenticate", "AuthStartDay", M2Share.g_Config.nAuthStartDay);
            M2Share.g_Config.nSoftVersionDate = ReadInteger("Authenticate", "SoftVersionDate", M2Share.g_Config.nSoftVersionDate);
            M2Share.g_Config.boOldClientShowHiLevel = ReadBool("Authenticate", "OldClientShowHiLevel", M2Share.g_Config.boOldClientShowHiLevel);
            M2Share.g_Config.boCanOldClientLogon = ReadBool("Authenticate", "CanOldClientLogon", M2Share.g_Config.boCanOldClientLogon);
            M2Share.g_Config.boShowLoginAttackModeHint = ReadBool("Setup", "ShowLoginAttackModeHint", M2Share.g_Config.boShowLoginAttackModeHint);

            // ============ [GameGate] section ============
            M2Share.g_Config.sGateAddr = ReadString("GameGate", "GateAddr", M2Share.g_Config.sGateAddr);
            M2Share.g_Config.nGatePort = ReadInteger("GameGate", "GatePort", M2Share.g_Config.nGatePort);

            // ============ [DataBase] section ============
            M2Share.g_Config.sIDSAddr = ReadString("DataBase", "IDSAddr", M2Share.g_Config.sIDSAddr);
            M2Share.g_Config.nIDSPort = ReadInteger("DataBase", "IDSPort", M2Share.g_Config.nIDSPort);
            M2Share.g_Config.sMsgSrvAddr = ReadString("DataBase", "MsgSrvAddr", M2Share.g_Config.sMsgSrvAddr);
            M2Share.g_Config.nMsgSrvPort = ReadInteger("DataBase", "MsgSrvPort", M2Share.g_Config.nMsgSrvPort);
            M2Share.g_Config.sServerIPaddr = ReadString("DataBase", "ServerIPaddr", M2Share.g_Config.sServerIPaddr);
            M2Share.g_Config.nServerNumber = ReadInteger("DataBase", "ServerNumber", M2Share.g_Config.nServerNumber);
            M2Share.g_Config.sDBType = ReadString("DataBase", "DBType", M2Share.g_Config.sDBType);
            var connString = ReadString("DataBase", "ConnString", "");
            if (string.IsNullOrWhiteSpace(connString))
            {
                connString = ReadString("DataBase", "ConnctionString", "");
            }
            if (!string.IsNullOrWhiteSpace(connString))
            {
                M2Share.g_Config.sConnctionString = connString;
            }

            LoadFormGmSetConfig();

            // ============ Remaining fields: use C# defaults (no write-back) ============
            // All other config values already have defaults in GameSvrConfig constructor.
            // !Setup.txt does not contain these remaining extended C# settings — they stay at defaults.
        }

        private void LoadFormGmSetConfig()
        {
            if (!File.Exists(_formGmSetPath)) return;

            try
            {
                var formGmSet = new ReadOnlyFlatConfig(_formGmSetPath);
                M2Share.g_Config.nMinMasterLevel = formGmSet.ReadInteger(
                    string.Empty, "SETKEY_SHOUTU",
                    M2Share.g_Config.nMinMasterLevel);
                M2Share.g_Config.nMasterOKLevel = formGmSet.ReadInteger(
                    string.Empty, "SETKEY_CHUSHI",
                    M2Share.g_Config.nMasterOKLevel);
                M2Share.g_Config.nMaxApprenticeLevel = formGmSet.ReadInteger(
                    string.Empty, "SETKEY_BAISHI",
                    M2Share.g_Config.nMaxApprenticeLevel);
                M2Share.g_Config.nHeroUnionMaxEnergy = formGmSet.ReadInteger(
                    string.Empty, "SETKEY_MAXLQ",
                    M2Share.g_Config.nHeroUnionMaxEnergy);

                M2Share.g_Config.HeroUnionChargeOverrides.Clear();
                var chargeFile = formGmSet.ReadString(
                    string.Empty, "SETF_HERO_LV_NQ", string.Empty).Trim();
                if (chargeFile.Length > 0)
                {
                    var formDirectory = Path.GetDirectoryName(_formGmSetPath) ?? string.Empty;
                    var chargePath = Path.IsPathRooted(chargeFile)
                        ? chargeFile
                        : Path.Combine(formDirectory, chargeFile);
                    if (File.Exists(chargePath))
                    {
                        var chargeConfig = new ReadOnlyFlatConfig(chargePath);
                        foreach (var entry in chargeConfig.ReadIntegerEntries())
                            M2Share.g_Config.HeroUnionChargeOverrides[entry.Key] = entry.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage($"读取 FormGMSet.ini 失败: {ex.Message}");
            }
        }

        private sealed class ReadOnlyFlatConfig : IniFile
        {
            public ReadOnlyFlatConfig(string fileName) : base(fileName)
            {
                Load();
            }

            public IEnumerable<KeyValuePair<int, int>> ReadIntegerEntries()
            {
                foreach (var key in GetSectionItemName(string.Empty))
                {
                    if (int.TryParse(key, out var index))
                        yield return new KeyValuePair<int, int>(index,
                            ReadInteger(string.Empty, key, 0));
                }
            }
        }

        private int LoadItemNumber(uint seed)
        {
            try
            {
                if (File.Exists(_itemNumberPath))
                {
                    // The native loader reads up to four bytes over the seeded current.
                    Span<byte> currentBytes = stackalloc byte[sizeof(uint)];
                    BinaryPrimitives.WriteUInt32LittleEndian(currentBytes, seed);
                    using var stream = new FileStream(_itemNumberPath,
                        FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    _ = stream.Read(currentBytes);
                    var value = BinaryPrimitives.ReadUInt32LittleEndian(currentBytes);
                    return unchecked((int)(value > seed ? value : seed));
                }
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage($"读取 ItemNumber.Dat 失败: {ex.Message}");
            }
            return unchecked((int)seed);
        }

        public void SaveItemNumbers()
        {
            lock (_itemNumberSaveLock)
            {
                var itemNumber = unchecked((uint)Volatile.Read(
                    ref M2Share.g_Config.nItemNumber));
                var data = new byte[sizeof(uint)];
                BinaryPrimitives.WriteUInt32LittleEndian(data, itemNumber);
                AtomicFile.WriteAllBytes(_itemNumberPath, data);
            }
        }
    }
}
