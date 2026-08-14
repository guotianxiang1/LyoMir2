namespace GameSvr
{
    /// <summary>
    /// Native festival configuration loader (EA 0x00624A1C)
    ///
    /// Evidence: sub_624A1C in M2Server unpacked image
    /// - Loads festival/event configuration files
    /// - Configures time-limited events, special drops, bonus rates
    /// - Supports multiple festival configurations
    ///
    /// Status: Placeholder implementation (core body deferred)
    /// Config structure needs reverse engineering from binary
    /// </summary>
    public class NativeFestivalConfig
    {
        public const uint LoaderFunctionEa = 0x00624A1C;
        private const string ConfigFileName = "Festival.ini";

        public class FestivalEvent
        {
            public string Name { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public double ExpBonus { get; set; } = 1.0;
            public double DropBonus { get; set; } = 1.0;
            public bool Enabled { get; set; } = false;
        }

        // Placeholder default values (need verification from sub_624A1C)
        public bool EnableFestivalMode { get; set; } = false;
        public List<FestivalEvent> Events { get; set; } = new List<FestivalEvent>();
        public double GlobalExpMultiplier { get; set; } = 1.0;
        public double GlobalDropMultiplier { get; set; } = 1.0;
        public bool EnableFestivalAnnouncement { get; set; } = true;
        public int AnnouncementInterval { get; set; } = 3600; // seconds

        /// <summary>
        /// Loads Festival.ini configuration from EnvirDir
        /// </summary>
        public static NativeFestivalConfig Load(string envirDir)
        {
            var config = new NativeFestivalConfig();
            var configPath = Path.Combine(envirDir, ConfigFileName);

            if (!File.Exists(configPath))
            {
                M2Share.MainOutMessage($"[配置] {ConfigFileName} 不存在，节日模式关闭");
                return config;
            }

            try
            {
                // TODO: Parse Festival.ini format (INI file structure)
                // Native loader sub_624A1C reads specific sections/keys
                // Need to reverse engineer exact field names and sections

                // Placeholder: Expected ini structure
                // [Festival]
                // Enabled=1
                // GlobalExpMultiplier=2.0
                // GlobalDropMultiplier=1.5
                // Announcement=1
                // AnnouncementInterval=3600
                //
                // [Event1]
                // Name=春节活动
                // StartTime=2026-01-25 00:00:00
                // EndTime=2026-02-10 23:59:59
                // ExpBonus=2.0
                // DropBonus=1.5
                // Enabled=1

                var lines = File.ReadAllLines(configPath, System.Text.Encoding.GetEncoding("GBK"));
                string currentSection = "";
                FestivalEvent currentEvent = null;

                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith(";") || trimmed.StartsWith("#"))
                        continue;

                    // Section header
                    if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                    {
                        if (currentEvent != null)
                        {
                            config.Events.Add(currentEvent);
                            currentEvent = null;
                        }

                        currentSection = trimmed.Substring(1, trimmed.Length - 2).Trim();
                        if (currentSection.StartsWith("Event"))
                        {
                            currentEvent = new FestivalEvent();
                        }
                        continue;
                    }

                    var parts = trimmed.Split('=');
                    if (parts.Length != 2)
                        continue;

                    var key = parts[0].Trim();
                    var value = parts[1].Trim();

                    if (currentSection == "Festival")
                    {
                        // Global festival settings
                        switch (key)
                        {
                            case "Enabled":
                                config.EnableFestivalMode = value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
                                break;
                            case "GlobalExpMultiplier":
                                if (double.TryParse(value, out var expMult))
                                    config.GlobalExpMultiplier = expMult;
                                break;
                            case "GlobalDropMultiplier":
                                if (double.TryParse(value, out var dropMult))
                                    config.GlobalDropMultiplier = dropMult;
                                break;
                            case "Announcement":
                                config.EnableFestivalAnnouncement = value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
                                break;
                            case "AnnouncementInterval":
                                if (int.TryParse(value, out var interval))
                                    config.AnnouncementInterval = interval;
                                break;
                        }
                    }
                    else if (currentEvent != null)
                    {
                        // Individual event settings
                        switch (key)
                        {
                            case "Name":
                                currentEvent.Name = value;
                                break;
                            case "StartTime":
                                if (DateTime.TryParse(value, out var startTime))
                                    currentEvent.StartTime = startTime;
                                break;
                            case "EndTime":
                                if (DateTime.TryParse(value, out var endTime))
                                    currentEvent.EndTime = endTime;
                                break;
                            case "ExpBonus":
                                if (double.TryParse(value, out var expBonus))
                                    currentEvent.ExpBonus = expBonus;
                                break;
                            case "DropBonus":
                                if (double.TryParse(value, out var dropBonus))
                                    currentEvent.DropBonus = dropBonus;
                                break;
                            case "Enabled":
                                currentEvent.Enabled = value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
                                break;
                        }
                    }
                }

                // Add last event if exists
                if (currentEvent != null)
                {
                    config.Events.Add(currentEvent);
                }

                M2Share.MainOutMessage($"[配置] {ConfigFileName} 加载完成，节日事件数: {config.Events.Count}");
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[错误] 加载 {ConfigFileName} 失败: {ex.Message}");
                M2Share.MainOutMessage("[配置] 节日模式关闭");
                config.EnableFestivalMode = false;
            }

            return config;
        }

        /// <summary>
        /// Validates loaded configuration values
        /// </summary>
        public bool Validate()
        {
            // TODO: Apply native validation rules from sub_624A1C
            // - ExpMultiplier: 0.1-100.0
            // - DropMultiplier: 0.1-100.0
            // - StartTime must be before EndTime

            if (GlobalExpMultiplier < 0.1 || GlobalExpMultiplier > 100.0)
            {
                M2Share.MainOutMessage($"[警告] Festival.ini GlobalExpMultiplier={GlobalExpMultiplier} 超出范围，重置为1.0");
                GlobalExpMultiplier = 1.0;
            }

            if (GlobalDropMultiplier < 0.1 || GlobalDropMultiplier > 100.0)
            {
                M2Share.MainOutMessage($"[警告] Festival.ini GlobalDropMultiplier={GlobalDropMultiplier} 超出范围，重置为1.0");
                GlobalDropMultiplier = 1.0;
            }

            // Validate each event
            foreach (var evt in Events)
            {
                if (evt.StartTime >= evt.EndTime)
                {
                    M2Share.MainOutMessage($"[警告] 节日事件 '{evt.Name}' 时间配置错误，已禁用");
                    evt.Enabled = false;
                }

                if (evt.ExpBonus < 0.1 || evt.ExpBonus > 100.0)
                {
                    M2Share.MainOutMessage($"[警告] 节日事件 '{evt.Name}' ExpBonus={evt.ExpBonus} 超出范围，重置为1.0");
                    evt.ExpBonus = 1.0;
                }

                if (evt.DropBonus < 0.1 || evt.DropBonus > 100.0)
                {
                    M2Share.MainOutMessage($"[警告] 节日事件 '{evt.Name}' DropBonus={evt.DropBonus} 超出范围，重置为1.0");
                    evt.DropBonus = 1.0;
                }
            }

            return true;
        }

        /// <summary>
        /// Gets active event at current time
        /// </summary>
        public FestivalEvent GetActiveEvent()
        {
            if (!EnableFestivalMode)
                return null;

            var now = DateTime.Now;
            foreach (var evt in Events)
            {
                if (evt.Enabled && evt.StartTime <= now && now <= evt.EndTime)
                    return evt;
            }

            return null;
        }

        /// <summary>
        /// Gets effective exp multiplier (global * event)
        /// </summary>
        public double GetEffectiveExpMultiplier()
        {
            if (!EnableFestivalMode)
                return 1.0;

            var activeEvent = GetActiveEvent();
            if (activeEvent != null)
                return GlobalExpMultiplier * activeEvent.ExpBonus;

            return GlobalExpMultiplier;
        }

        /// <summary>
        /// Gets effective drop multiplier (global * event)
        /// </summary>
        public double GetEffectiveDropMultiplier()
        {
            if (!EnableFestivalMode)
                return 1.0;

            var activeEvent = GetActiveEvent();
            if (activeEvent != null)
                return GlobalDropMultiplier * activeEvent.DropBonus;

            return GlobalDropMultiplier;
        }
    }
}
