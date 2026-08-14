using GameSvr.Configs;
using MySql.Data.MySqlClient;
using SystemModule;

namespace GameSvr.Services
{
    /// <summary>
    /// B-tier startup configuration / schema validation paths reversed from the
    /// original M2Server boot chain (Hero.ini, 卧龙山庄.ini, gamedata, Encryptor.dll,
    /// system-parameter init log, etc.).
    /// </summary>
    internal static class NativeStartupConfigValidation
    {
        internal const uint HeroIniMissingEa = 0x00643350;
        internal const uint WoLongConfigMissingEa = 0x00605320;
        internal const uint StallGamedataMissingEa = 0x0061A9D0;
        internal const uint MailGamedataMissingEa = 0x00709914;
        internal const uint EncryptorDllEa = 0x006A433C;
        internal const uint MagicTowerMonsterFileErrorEa = 0x0064E974;
        internal const uint MagicTowerBoxPrizeMissingEa = 0x0074E968;
        internal const uint BufferConfLoaderEa = 0x00749BE4;
        internal const uint GemConfigTlsEa = 0x0078C1DC;
        internal const uint MonExploreConfigEa = 0x0067BA0C;
        internal const uint MonPkEquivalentEa = 0x0067B83C;

        internal const string HeroIniMissingMessage =
            "[Error]: Hero.ini 文件不存在";
        internal const string WoLongConfigMissingMessage =
            "[Error]：卧龙配置文件不存在 ";
        internal const string StallGamedataMissingMessage =
            "[Error] 摆摊数据库gamedata未创建，请先使用转换工具转换！";
        internal const string MailGamedataMissingMessage =
            "[Error] 邮包数据库gamedata未创建，请先使用转换工具转换！";
        internal const string SystemParametersInitializedMessage =
            "系统参数初始化完毕";

        internal static string ResolveShareConfigPath(string fileName)
        {
            return Path.GetFullPath(Path.Combine(
                M2Share.sRootPath,
                M2Share.g_Config?.sBaseDir ?? string.Empty,
                "Config",
                fileName));
        }

        internal static void ReportHeroIniMissing(string heroFilePath)
        {
            M2Share.ErrorMessage(HeroIniMissingMessage);
        }

        internal static void ValidateWoLongConfigAtStartup()
        {
            var path = ResolveShareConfigPath("卧龙山庄.ini");
            if (File.Exists(path) && new FileInfo(path).Length > 0)
                return;
            M2Share.ErrorMessage(WoLongConfigMissingMessage);
        }

        internal static void ValidateEncryptorDllAtStartup()
        {
            var serverInfoPath = Path.GetFullPath(Path.Combine(
                M2Share.sRootPath, "Share", "ServerInfo.ini"));
            if (!File.Exists(serverInfoPath))
            {
                M2Share.ErrorMessage("[Error]:" + serverInfoPath + " 不存在！");
                return;
            }

            var serverInfo = new ServerInfoLoader(serverInfoPath);
            var encryptorPath = serverInfo.EncKey;
            if (string.IsNullOrWhiteSpace(encryptorPath))
                encryptorPath = "Encryptor.dll";
            if (!Path.IsPathRooted(encryptorPath))
            {
                encryptorPath = Path.GetFullPath(Path.Combine(
                    M2Share.sRootPath, encryptorPath));
            }
            if (File.Exists(encryptorPath))
                return;
            M2Share.ErrorMessage("[Error]:" + encryptorPath + " 不存在！");
        }

        internal static bool TryEnsureGamedataSchema(out string error)
        {
            error = string.Empty;
            var connectionString = M2Share.g_Config?.sConnctionString;
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                error = "database connection string is empty";
                return false;
            }

            try
            {
                using var connection = new MySqlConnection(connectionString);
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE DATABASE IF NOT EXISTS gamedata";
                command.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        internal static void ReportStallGamedataMissing()
        {
            M2Share.ErrorMessage(StallGamedataMissingMessage);
        }

        internal static void ReportMailGamedataMissing()
        {
            M2Share.ErrorMessage(MailGamedataMissingMessage);
        }

        internal static void ReportMonBasePkMissing()
        {
            M2Share.ErrorMessage(
                "[Error]: 未发现怪物PK当量配置文件 ");
        }

        internal static void ReportButchTypeMissing()
        {
            M2Share.ErrorMessage(
                "[Error]: 未发现怪物可探索类型配置文件 ");
        }

        internal static void ValidateMagicTowerBoxPrizeConfigs()
        {
            var configDirectory = Path.GetFullPath(Path.Combine(
                M2Share.sRootPath, "Share", "config"));
            var copperPath = Path.Combine(configDirectory, "CopperBoxPrize.ini");
            if (!File.Exists(copperPath))
            {
                M2Share.ErrorMessage("[Error]: 缺少闯天关配置文件：");
                return;
            }
            if (!File.Exists(Path.Combine(configDirectory, "SillerBoxPrize.ini")))
            {
                M2Share.ErrorMessage("[Error]: 缺少闯天关配置文件：");
            }
        }

        internal static void LogSystemParametersInitialized()
        {
            M2Share.MainOutMessage(SystemParametersInitializedMessage);
        }
    }
}
