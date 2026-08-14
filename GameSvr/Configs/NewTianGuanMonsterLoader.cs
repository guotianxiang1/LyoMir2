namespace GameSvr
{
    /// <summary>
    /// 新天关怪物配置加载器
    ///
    /// 从 config/新天关怪物.txt 加载新天关副本的怪物配置
    /// 解析失败时记录错误日志并返回默认值
    ///
    /// Status: Initial implementation
    /// </summary>
    public class NewTianGuanMonsterLoader
    {
        private const string ConfigFileName = "config/新天关怪物.txt";

        // 默认配置值
        public string MonsterName { get; set; } = "";
        public int MonsterLevel { get; set; } = 1;
        public int MonsterHp { get; set; } = 100;
        public int MonsterMp { get; set; } = 0;
        public int MonsterAc { get; set; } = 0;
        public int MonsterMac { get; set; } = 0;
        public int MonsterDc { get; set; } = 0;
        public int MonsterMc { get; set; } = 0;
        public int MonsterSc { get; set; } = 0;
        public int MonsterSpeed { get; set; } = 1000;
        public int MonsterHitRate { get; set; } = 5;
        public int MonsterExp { get; set; } = 0;

        /// <summary>
        /// 加载新天关怪物配置
        /// </summary>
        public static NewTianGuanMonsterLoader LoadConfig(string baseDir)
        {
            var config = new NewTianGuanMonsterLoader();
            var configPath = Path.Combine(baseDir, ConfigFileName);

            if (!File.Exists(configPath))
            {
                M2Share.MainOutMessage($"[配置] {ConfigFileName} 不存在，使用默认值");
                return config;
            }

            try
            {
                // 使用GBK编码读取配置文件
                var lines = File.ReadAllLines(configPath, System.Text.Encoding.GetEncoding("GBK"));

                foreach (var line in lines)
                {
                    // 跳过空行和注释行
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";") || line.StartsWith("#"))
                        continue;

                    // 解析键值对 (格式: key=value)
                    var parts = line.Split('=');
                    if (parts.Length != 2)
                        continue;

                    var key = parts[0].Trim();
                    var value = parts[1].Trim();

                    // 解析配置项
                    switch (key)
                    {
                        case "MonsterName":
                        case "怪物名称":
                            config.MonsterName = value;
                            break;
                        case "MonsterLevel":
                        case "怪物等级":
                            if (int.TryParse(value, out var level))
                                config.MonsterLevel = level;
                            else
                                M2Share.MainOutMessage($"[警告] {ConfigFileName} MonsterLevel 解析失败: {value}");
                            break;
                        case "MonsterHp":
                        case "怪物生命":
                            if (int.TryParse(value, out var hp))
                                config.MonsterHp = hp;
                            else
                                M2Share.MainOutMessage($"[警告] {ConfigFileName} MonsterHp 解析失败: {value}");
                            break;
                        case "MonsterMp":
                        case "怪物魔法":
                            if (int.TryParse(value, out var mp))
                                config.MonsterMp = mp;
                            else
                                M2Share.MainOutMessage($"[警告] {ConfigFileName} MonsterMp 解析失败: {value}");
                            break;
                        case "MonsterAc":
                        case "怪物防御":
                            if (int.TryParse(value, out var ac))
                                config.MonsterAc = ac;
                            else
                                M2Share.MainOutMessage($"[警告] {ConfigFileName} MonsterAc 解析失败: {value}");
                            break;
                        case "MonsterMac":
                        case "怪物魔防":
                            if (int.TryParse(value, out var mac))
                                config.MonsterMac = mac;
                            else
                                M2Share.MainOutMessage($"[警告] {ConfigFileName} MonsterMac 解析失败: {value}");
                            break;
                        case "MonsterDc":
                        case "怪物攻击":
                            if (int.TryParse(value, out var dc))
                                config.MonsterDc = dc;
                            else
                                M2Share.MainOutMessage($"[警告] {ConfigFileName} MonsterDc 解析失败: {value}");
                            break;
                        case "MonsterMc":
                        case "怪物魔法攻击":
                            if (int.TryParse(value, out var mc))
                                config.MonsterMc = mc;
                            else
                                M2Share.MainOutMessage($"[警告] {ConfigFileName} MonsterMc 解析失败: {value}");
                            break;
                        case "MonsterSc":
                        case "怪物道术":
                            if (int.TryParse(value, out var sc))
                                config.MonsterSc = sc;
                            else
                                M2Share.MainOutMessage($"[警告] {ConfigFileName} MonsterSc 解析失败: {value}");
                            break;
                        case "MonsterSpeed":
                        case "怪物速度":
                            if (int.TryParse(value, out var speed))
                                config.MonsterSpeed = speed;
                            else
                                M2Share.MainOutMessage($"[警告] {ConfigFileName} MonsterSpeed 解析失败: {value}");
                            break;
                        case "MonsterHitRate":
                        case "怪物命中":
                            if (int.TryParse(value, out var hitRate))
                                config.MonsterHitRate = hitRate;
                            else
                                M2Share.MainOutMessage($"[警告] {ConfigFileName} MonsterHitRate 解析失败: {value}");
                            break;
                        case "MonsterExp":
                        case "怪物经验":
                            if (int.TryParse(value, out var exp))
                                config.MonsterExp = exp;
                            else
                                M2Share.MainOutMessage($"[警告] {ConfigFileName} MonsterExp 解析失败: {value}");
                            break;
                    }
                }

                M2Share.MainOutMessage($"[配置] {ConfigFileName} 加载完成");
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[错误] 加载 {ConfigFileName} 失败: {ex.Message}");
                M2Share.MainOutMessage("[配置] 使用默认新天关怪物配置");
            }

            return config;
        }

        /// <summary>
        /// 验证加载的配置值
        /// </summary>
        public bool Validate()
        {
            bool isValid = true;

            if (MonsterLevel < 1 || MonsterLevel > 999)
            {
                M2Share.MainOutMessage($"[警告] 新天关怪物配置 MonsterLevel={MonsterLevel} 超出范围 [1-999]，重置为1");
                MonsterLevel = 1;
                isValid = false;
            }

            if (MonsterHp < 1)
            {
                M2Share.MainOutMessage($"[警告] 新天关怪物配置 MonsterHp={MonsterHp} 无效，重置为100");
                MonsterHp = 100;
                isValid = false;
            }

            if (MonsterSpeed < 100 || MonsterSpeed > 10000)
            {
                M2Share.MainOutMessage($"[警告] 新天关怪物配置 MonsterSpeed={MonsterSpeed} 超出范围 [100-10000]，重置为1000");
                MonsterSpeed = 1000;
                isValid = false;
            }

            return isValid;
        }
    }
}
