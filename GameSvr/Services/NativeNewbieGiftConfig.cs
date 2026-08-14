using SystemModule;
using SystemModule.Common;

namespace GameSvr
{
    /// <summary>
    /// Loader for config\新手礼包.ini (sub_7520E0 @0x007520E0).
    /// Stores per-job item indices/descriptors; logs [Error]: 新手奖励配置错误 on parse failure.
    /// </summary>
    public sealed class NativeNewbieGiftConfig
    {
        private readonly Dictionary<int, string> _prizesByJob;

        private NativeNewbieGiftConfig(Dictionary<int, string> prizesByJob)
        {
            _prizesByJob = prizesByJob;
        }

        public IReadOnlyDictionary<int, string> PrizesByJob => _prizesByJob;

        public bool TryGetPrize(int job, out string descriptor)
        {
            return _prizesByJob.TryGetValue(job, out descriptor);
        }

        public static bool TryLoad(string fileName,
            out NativeNewbieGiftConfig config, out string error)
        {
            config = new NativeNewbieGiftConfig(new Dictionary<int, string>());
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                error = "config\\新手礼包.ini path is empty";
                return false;
            }

            if (!File.Exists(fileName))
                return true;

            try
            {
                var ini = new NewbieGiftIni(fileName);
                var prizes = new Dictionary<int, string>();
                for (var job = 0; job <= 3; job++)
                {
                    var section = "配置" + (job + 1);
                    var value = ini.ReadString(section, "奖品1", null);
                    if (string.IsNullOrWhiteSpace(value))
                        continue;
                    if (!TryParseDescriptor(value, out var descriptor))
                    {
                        LogConfigError(value);
                        error = "invalid prize in " + section;
                        config = new NativeNewbieGiftConfig(prizes);
                        return false;
                    }

                    prizes[job] = descriptor;
                }

                config = new NativeNewbieGiftConfig(prizes);
                return true;
            }
            catch (Exception ex) when (ex is IOException ||
                                       ex is UnauthorizedAccessException)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool TryParseDescriptor(string value, out string descriptor)
        {
            descriptor = value.Trim();
            return descriptor.Length > 0;
        }

        private static void LogConfigError(string suffix)
        {
            if (M2Share.LogSystem != null)
                M2Share.ErrorMessage("[Error]: 新手奖励配置错误:[" + suffix + "]");
        }

        private sealed class NewbieGiftIni : IniFile
        {
            internal NewbieGiftIni(string fileName) : base(fileName)
            {
                try
                {
                    Load();
                }
                catch (Exception ex) when (ex.GetType() == typeof(Exception) &&
                    ConfigCount == 0 && string.Equals(ex.Message,
                        $"配置文件[{fileName}]不存在或配置文件内容为空。",
                        StringComparison.Ordinal))
                {
                }
            }
        }
    }
}
