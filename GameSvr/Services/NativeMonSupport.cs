using System.Globalization;
using SystemModule;
using SystemModule.Common;

namespace GameSvr
{
    /// <summary>
    /// 战神 TMonSupport (sub_67CC74..sub_67D680): Thousand_mon.ini loader and
    /// the daily boss/wave state machine.
    /// </summary>
    internal sealed class NativeMonSupport
    {
        internal const string ConfigFileName = "Thousand_mon.ini";
        internal const string ReloadSucceeded = "怪物攻城配置文件重载成功！";
        internal const string ReloadFailed = "怪物攻城配置文件重载失败！";
        internal const string NotReady = "[Error]：怪物攻城未准备好。";
        internal const string Started = "怪物攻城已经启动！";
        internal const string Stopped = "怪物攻城已经停止！";
        internal const string EventException =
            "[Exception]:TMonSupport.EventExecute";

        private readonly Func<string, bool> _isLocalMap;
        private readonly Func<string, string, int, int, int, TBaseObject> _spawn;
        private readonly Action<string> _broadcast;
        private readonly Action<string> _log;
        private readonly Func<int> _tick;
        private readonly Func<DateTime> _now;
        private readonly List<Entry> _waves = new();

        internal NativeMonSupport(
            Func<string, bool> isLocalMap,
            Func<string, string, int, int, int, TBaseObject> spawn,
            Action<string> broadcast,
            Action<string> log,
            Func<int> tick,
            Func<DateTime> now)
        {
            _isLocalMap = isLocalMap ?? throw new ArgumentNullException(nameof(isLocalMap));
            _spawn = spawn ?? throw new ArgumentNullException(nameof(spawn));
            _broadcast = broadcast ?? throw new ArgumentNullException(nameof(broadcast));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _tick = tick ?? throw new ArgumentNullException(nameof(tick));
            _now = now ?? throw new ArgumentNullException(nameof(now));
            Boss = new Entry();
        }

        internal sealed class Entry
        {
            internal string MapName = string.Empty;
            internal int X;
            internal int Y;
            internal string MonsterName = string.Empty;
            internal int Range;
            internal int Number;
            internal int Current;
            internal TBaseObject Actor;
            internal Entry Next;
        }

        internal bool Loaded { get; private set; }
        internal bool Enabled { get; private set; }
        internal byte State { get; private set; }
        internal int OuterTick { get; private set; }
        internal int StateTick { get; private set; }
        internal int DelaySeconds { get; private set; }
        internal int StartSeconds1 { get; private set; }
        internal int StartSeconds2 { get; private set; }
        internal string StartNotice { get; private set; } = string.Empty;
        internal string AttackNotice { get; private set; } = string.Empty;
        internal string FailNotice { get; private set; } = string.Empty;
        internal Entry Boss { get; }
        internal Entry Cursor { get; private set; }
        internal IReadOnlyList<Entry> Waves => _waves;
        internal int WaveCount => _waves.Count;

        internal static string ResolveConfigPath(string rootPath, string baseDir)
        {
            return Path.GetFullPath(Path.Combine(rootPath ?? string.Empty,
                baseDir ?? string.Empty, "Config", ConfigFileName));
        }

        internal static void ResolveSpawnCoordinates(int x, int y, int range,
            Func<int, int> random, out int spawnX, out int spawnY)
        {
            spawnX = x;
            spawnY = y;
            if (range <= 0)
                return;

            var bound = unchecked(range * 2 + 1);
            spawnX = unchecked(x + random(bound) - range);
            spawnY = unchecked(y + random(bound) - range);
        }

        /// <summary>
        /// sub_67CC74. Reload deliberately preserves the boss actor, notices,
        /// boss fields and timers until the corresponding new fields are read.
        /// </summary>
        internal void Load(string fileName)
        {
            State = 0;
            Loaded = false;
            Enabled = false;
            _waves.Clear();
            Cursor = null;

            if (!File.Exists(fileName))
                return;

            var ini = new ConfFile(fileName);
            if (!ini.Load())
                return;

            StartSeconds1 = ReadStartSeconds(ini, "StartTime1");
            StartSeconds2 = ReadStartSeconds(ini, "StartTime2");
            StartNotice = ini.ReadString("Setup", "StartNotice", string.Empty);
            AttackNotice = ini.ReadString("Setup", "AttackNotice", string.Empty);
            FailNotice = ini.ReadString("Setup", "FailNotice", string.Empty);
            DelaySeconds = ini.ReadInteger("Setup", "DelayTime", 1800);

            if (string.IsNullOrEmpty(StartNotice)
                || string.IsNullOrEmpty(AttackNotice)
                || string.IsNullOrEmpty(FailNotice)
                || DelaySeconds < 120)
                return;

            Boss.MapName = ToNativeShortString(
                ini.ReadString("boss", "Map", string.Empty), 14);
            Boss.X = ini.ReadInteger("boss", "X", 0);
            Boss.Y = ini.ReadInteger("boss", "Y", 0);
            Boss.MonsterName = ToNativeShortString(
                ini.ReadString("boss", "Name", string.Empty), 14);
            Boss.Range = 5;
            Boss.Number = 1;
            Boss.Current = 0;

            if (!_isLocalMap(Boss.MapName))
                return;

            for (var index = 1; index < 100; index++)
            {
                var section = "Mon" + index;
                var rawName = ini.ReadString(section, "Name", string.Empty);
                if (string.IsNullOrEmpty(rawName))
                    break;

                var entry = new Entry
                {
                    MonsterName = ToNativeShortString(rawName, 14),
                    X = ini.ReadInteger(section, "X", 0),
                    Y = ini.ReadInteger(section, "Y", 0),
                    MapName = ToNativeShortString(
                        ini.ReadString(section, "Map", string.Empty), 14),
                    Range = ini.ReadInteger(section, "Range", 0),
                    Number = ini.ReadInteger(section, "Num", 0),
                    Current = 0
                };

                if (_isLocalMap(entry.MapName))
                {
                    _waves.Add(entry);
                }
                else
                {
                    _log(fileName + "[Error]: 配置错误 - " +
                         entry.MapName + " 不在本GS！");
                }
            }

            Loaded = true;
            Enabled = true;
        }

        internal string Reload(string fileName)
        {
            Load(fileName);
            return Loaded && Enabled && WaveCount > 0
                ? ReloadSucceeded
                : ReloadFailed;
        }

        internal string Toggle()
        {
            if (!Loaded)
                return NotReady;

            Enabled = !Enabled;
            return Enabled ? Started : Stopped;
        }

        /// <summary>
        /// ProcessMon sub_67C150 @0x67C709..0x67C723: unsigned elapsed
        /// greater than or equal to 500 updates +0x4C before EventExecute.
        /// </summary>
        internal void ProcessIfDue(int schedulerTick)
        {
            if (unchecked((uint)(schedulerTick - OuterTick)) < 500U)
                return;

            OuterTick = schedulerTick;
            ExecuteEvent();
        }

        internal void ExecuteEvent()
        {
            if (!Enabled)
                return;

            try
            {
                var eventTick = _tick();
                if (Boss.Actor != null && Boss.Actor.m_boGhost)
                    Boss.Actor = null;

                switch (State)
                {
                    case 0:
                        ExecuteWaitingState(eventTick);
                        break;
                    case 1:
                        ExecuteBossState(eventTick);
                        break;
                    case 2:
                        ExecuteWaveState();
                        break;
                }
            }
            catch
            {
                _log(EventException);
            }
        }

        private void ExecuteWaitingState(int eventTick)
        {
            if (unchecked((uint)(eventTick - StateTick)) <= 10000U)
                return;

            StateTick = eventTick;
            var seconds = unchecked((int)_now().TimeOfDay.TotalSeconds);
            var delta1 = unchecked(seconds - StartSeconds1);
            var delta2 = unchecked(seconds - StartSeconds2);
            if ((delta1 > 0 && delta1 < 60)
                || (delta2 > 0 && delta2 < 60))
                StartBoss();
        }

        private void StartBoss()
        {
            if (Boss.Actor == null)
            {
                Boss.Actor = _spawn(Boss.MapName, Boss.MonsterName,
                    Boss.X, Boss.Y, 10);
            }

            _broadcast(StartNotice);
            State = 1;
        }

        private void ExecuteBossState(int eventTick)
        {
            var elapsed = unchecked(eventTick - StateTick);
            var threshold = unchecked(DelaySeconds * 1000);
            if (elapsed <= threshold)
                return;

            StateTick = eventTick;
            var boss = Boss.Actor;
            if (boss != null && !boss.m_boGhost && !boss.m_boDeath)
            {
                State = 0;
                _broadcast(FailNotice);
                return;
            }

            State = 2;
            for (var index = _waves.Count - 1; index >= 0; index--)
            {
                var entry = _waves[index];
                entry.Next = Cursor;
                Cursor = entry;
            }
            _broadcast(AttackNotice);
        }

        private void ExecuteWaveState()
        {
            var entry = Cursor;
            if (entry == null)
            {
                State = 0;
                return;
            }

            var next = entry.Next;
            entry.Actor = _spawn(entry.MapName, entry.MonsterName,
                entry.X, entry.Y, entry.Range);
            if (entry.Actor != null)
            {
                entry.Actor.m_nMissionX = entry.Actor.m_nCurrX;
                entry.Actor.m_nMissionY = entry.Actor.m_nCurrY;
                entry.Actor.m_boMission = true;
            }
            else
            {
                _log("[ERROR]: BuildAttackMon " + entry.MonsterName);
            }

            entry.Current = unchecked(entry.Current + 1);
            if (entry.Current >= entry.Number)
                Cursor = next;
        }

        private static int ReadStartSeconds(ConfFile ini, string key)
        {
            try
            {
                var raw = ini.ReadString("Setup", key, string.Empty);
                if (string.IsNullOrWhiteSpace(raw))
                    return 0;

                if (DateTime.TryParse(raw, CultureInfo.CurrentCulture,
                        DateTimeStyles.AllowWhiteSpaces, out var dateTime)
                    || DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                        DateTimeStyles.AllowWhiteSpaces, out dateTime))
                    return unchecked((int)dateTime.TimeOfDay.TotalSeconds);

                if ((TimeSpan.TryParse(raw, CultureInfo.CurrentCulture,
                         out var timeSpan)
                     || TimeSpan.TryParse(raw, CultureInfo.InvariantCulture,
                         out timeSpan))
                    && timeSpan >= TimeSpan.Zero
                    && timeSpan < TimeSpan.FromDays(1))
                    return unchecked((int)timeSpan.TotalSeconds);
            }
            catch
            {
                // sub_67CC74 protects each ReadTime independently and stores zero.
            }
            return 0;
        }

        internal static string ToNativeShortString(string value, int byteCapacity)
        {
            if (string.IsNullOrEmpty(value) || byteCapacity <= 0)
                return string.Empty;

            var bytes = HUtil32.GbkEncoding.GetBytes(value);
            if (bytes.Length <= byteCapacity)
                return value;
            return HUtil32.GbkEncoding.GetString(bytes, 0, byteCapacity);
        }
    }
}
