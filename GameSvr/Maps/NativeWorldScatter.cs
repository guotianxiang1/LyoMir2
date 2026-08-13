using System.IO;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// 战神 <c>TWorldScatterMgr</c> 的一条配置记录，原生是 24 字节的动态数组元素
    /// （<c>0x752CF2 lea eax,[eax+eax*2]</c> 配 <c>0x752CF8 lea eax,[edx+eax*8]</c>
    /// 得出步长 <c>i*24</c>）。字段偏移逐条来自加载器 <c>sub_752D40</c>、
    /// 计时器 <c>sub_7530D8</c> 与匹配器 <c>sub_753124</c>：
    /// <code>
    /// +0x00 word  minLevel   752E48  66 89 06        mov word [esi],ax
    /// +0x02 word  maxPile    752E72  66 89 46 02     mov word [esi+2],ax
    /// +0x04 int   secSpace   752E5D  89 46 04        mov [esi+4],eax
    /// +0x08 int   lastTick   7530EA  89 4B 08        mov [rec+8],ecx
    /// +0x0C obj   prize 表   752E33  89 46 0C        mov [esi+0xC],eax   (TStringList)
    /// +0x10 int   pending    753116  89 7B 10        mov [rec+0x10],edi
    /// +0x14 obj   map 表     753210  89 43 14        mov [ebx+0x14],eax  (TStringList，可为 nil)
    /// </code>
    /// </summary>
    internal sealed class NativeWorldScatterRecord
    {
        internal ushort MinLevel;
        internal ushort MaxPile;
        internal int SecSpace;
        internal int LastTick;
        internal int Pending;
        internal readonly List<string> Prizes = new();

        /// <summary>
        /// 原生 <c>[rec+0x14]</c> 允许为 nil，且 nil 与空表都表示「不限地图」
        /// （<c>0x753163 test esi,esi / je 0x75318A</c> 与
        /// <c>0x75316E dec eax / jl 0x75318A</c> 两条都直接跳到命中臂）。
        /// </summary>
        internal List<string> Maps;
    }

    /// <summary>
    /// 段3 配置只经 <c>0x752D89 mov eax,[0x44C8A0] / 0x752D8E call 0x44C950</c> 建的
    /// <c>TIniFile</c> 读取，用的是 <c>ReadInteger</c>（<c>[vmt+8]</c>）与
    /// <c>ReadString</c>（<c>[vmt+0]</c>）两个槽。本仓既有的 <c>ConfFile</c> 会在
    /// 文件缺失时 <c>File.Create</c>，而原生这条路径连 ini 对象都不建，
    /// 故另起一个只读、无副作用的最小实现。节名与键名照 Win32 profile API
    /// 大小写不敏感，重复键取先出现的那个。
    /// </summary>
    internal sealed class NativeWorldScatterIni
    {
        private readonly Dictionary<string, Dictionary<string, string>> _sections =
            new(StringComparer.OrdinalIgnoreCase);

        internal static NativeWorldScatterIni Load(string fileName)
        {
            var ini = new NativeWorldScatterIni();
            Dictionary<string, string> current = null;
            foreach (var rawLine in File.ReadAllLines(fileName, HUtil32.GbkEncoding))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line[0] == ';')
                {
                    continue;
                }
                if (line[0] == '[')
                {
                    var close = line.IndexOf(']');
                    if (close < 0)
                    {
                        continue;
                    }
                    var name = line.Substring(1, close - 1).Trim();
                    if (!ini._sections.TryGetValue(name, out current))
                    {
                        current = new Dictionary<string, string>(
                            StringComparer.OrdinalIgnoreCase);
                        ini._sections[name] = current;
                    }
                    continue;
                }
                if (current == null)
                {
                    continue;
                }
                var split = line.IndexOf('=');
                if (split <= 0)
                {
                    continue;
                }
                var key = line.Substring(0, split).Trim();
                if (key.Length == 0 || current.ContainsKey(key))
                {
                    continue;
                }
                current[key] = line.Substring(split + 1).Trim();
            }
            return ini;
        }

        internal string ReadString(string section, string key, string defaultValue)
        {
            return _sections.TryGetValue(section, out var entries)
                   && entries.TryGetValue(key, out var value)
                ? value
                : defaultValue;
        }

        /// <summary>Delphi 的 <c>TIniFile.ReadInteger</c> 是 <c>StrToIntDef</c>，
        /// 解析不出来就退回默认值而不是 0。</summary>
        internal int ReadInteger(string section, string key, int defaultValue)
        {
            var text = ReadString(section, key, string.Empty);
            return int.TryParse(text, out var value) ? value : defaultValue;
        }
    }

    /// <summary>
    /// 战神 <c>TWorldScatterMgr</c>（VMT <c>0x74B678</c>，类名 ShortString
    /// <c>0x74B69E</c> = <c>10 'TWorldScatterMgr'</c>，实例大小
    /// <c>[V-0x28] = 0x30</c>）—— 即 <c>sub_71FA20</c> **段3「世界掉落」**
    /// 背后的子系统。单例经 <c>[0x7D71F4] -> [0x7DCB8C]</c> 取得，注册名是
    /// <c>0x7568FC</c> GBK「世界暴率管理」（<c>0x7568CA mov edx,0x7DCB8C /
    /// 0x7568CF mov ecx,0x7568FC / 0x7568D4 mov eax,[0x74B62C]</c>）。
    ///
    /// **它和 <see cref="NativeDropControlRuntime"/>（掉落控制 <c>sub_720278</c>）
    /// 是两套东西**：那一套的记录布局对上 <c>sub_77C580</c>/<c>sub_77C738</c>、
    /// 经 <c>sub_72016C</c> 落地、半径 4；本类经 <c>sub_752CAC</c> 查表、
    /// 由段3 自己用 <c>sub_74DE54</c>(MakeItemByName) 造物、半径 3。
    /// 2026-08-14 曾因把两者混同产生过一次半径回归。
    ///
    /// 三个虚方法（<c>V+0x04 = 0x75307C</c> 计时、<c>V+0x08 = 0x752D40</c> 载入、
    /// <c>V+0x0C = 0x752B8C</c> 清表）由模块框架驱动，非本类自调用。
    /// </summary>
    internal sealed class NativeWorldScatterMgr
    {
        /// <summary>
        /// <c>0x752C3A add eax,0x1B7740</c> —— 构造时把「上次计时」推到 30 分钟以后，
        /// 于是 <c>0x75308B sub eax,[self+0x24]</c> 在开服后头半小时恒为负，
        /// <c>0x75308E cmp eax,0x3E8 / jle</c> 直接返回，整个子系统静默。
        /// </summary>
        internal const int NativeWarmUpMs = 0x1B7740;

        /// <summary><c>0x75308E cmp eax,0x3E8 / 0x753093 jle</c>：严格大于 1000ms 才走一轮。</summary>
        internal const int NativeRunIntervalMs = 0x3E8;

        /// <summary><c>0x7530FA mov ecx,0x3E8</c> 后 <c>xor edx,edx / div ecx</c>（无符号）。</summary>
        internal const int NativeMillisPerSecond = 0x3E8;

        /// <summary><c>0x752EEE cmp ebx,0x64 / 0x752EF1 jne</c>：prize 键读到第 99 个为止。</summary>
        internal const int NativePrizeKeyLimit = 0x64;

        /// <summary>
        /// 段3 落地半径，<c>0x71FF3D B9 03 00 00 00 mov ecx,3</c> 的立即数。
        /// 与掉落控制的 4（<c>0x720213</c>）、怪物自有表的 3（<c>0x71FDCF</c>）
        /// 各自独立。
        /// </summary>
        internal const int NativeScatterRange = 3;

        internal const string NativeSettingSection = "setting";   // 0x752FD0
        internal const string NativeTypeNumKey = "typeNum";       // 0x752FC0
        internal const string NativeSectionPrefix = "type";       // 0x752FE0
        internal const string NativeMinLevelKey = "minLevel";     // 0x752FF0
        internal const string NativeSecSpaceKey = "secSpace";     // 0x753004
        internal const string NativeMaxPileKey = "maxPile";       // 0x753018
        internal const string NativeMapKey = "map";               // 0x753028
        internal const string NativePrizeKey = "prize";           // 0x753034

        /// <summary><c>0x753224-0x753230</c> 就地拼出的四个分隔符 <c>20 7C 09 2C</c>，
        /// 随 <c>push 3</c>（Delphi 开放数组的 high）交给 <c>sub_4C6BA4</c>。</summary>
        private static readonly char[] NativeMapDividers = { ' ', '|', '\t', ',' };

        private readonly List<NativeWorldScatterRecord> _records = new();
        private int _lastRunTick;
        private bool _armed;

        /// <summary>
        /// 原生两条日志都走 <c>0x79DF74</c>（<c>eax=[[0x7D5ECC]]</c>、<c>cl=1</c>），
        /// 即主窗口输出。开成可替换的槽只为让审计工具能在不引 <c>M2Share</c>
        /// 静态构造（它要读一串 ini）的前提下跑加载器。
        /// </summary>
        internal Action<string> OutMessage = message => M2Share.MainOutMessage(message);

        internal NativeWorldScatterMgr()
        {
            // 0x752C2E [self+0x1C]=0 / 0x752C31 byte [self+0x28]=0 /
            // 0x752C35 call GetTickCount / 0x752C3A add eax,0x1B7740 / 0x752C3F [self+0x24]=eax
            _lastRunTick = HUtil32.GetTickCount() + NativeWarmUpMs;
        }

        internal IReadOnlyList<NativeWorldScatterRecord> Records => _records;

        internal bool Armed => _armed;

        /// <summary>
        /// <c>sub_752B8C</c>（VMT +0x0C）：逐条释放 <c>[rec+0xC]</c> 与非 nil 的
        /// <c>[rec+0x14]</c>，再把动态数组长度设回 0。
        /// </summary>
        internal void Clear()
        {
            _records.Clear();
        }

        /// <summary>
        /// <c>sub_752D40</c>（VMT +0x08）。文件不存在时
        /// （<c>0x752D7C test al,al / 0x752D7E je 0x752F6A</c>）**一声不吭地返回**，
        /// 既不建表也不打日志；只有 <c>typeNum &lt;= 0</c> 才走
        /// <c>0x752F2C</c> 那条「[Error]:找不到世界爆率文件」。
        /// </summary>
        internal void LoadConfig(string fileName)
        {
            Clear();
            if (string.IsNullOrEmpty(fileName) || !File.Exists(fileName))
            {
                return;
            }

            var ini = NativeWorldScatterIni.Load(fileName);

            // 0x752DA4 push 0 / mov ecx,"typeNum" / mov edx,"setting" / call [ini+8]
            var typeNum = ini.ReadInteger(NativeSettingSection, NativeTypeNumKey, 0);
            if (typeNum <= 0)
            {
                // 0x752F2C LStrCat3("[Error]:找不到世界爆率文件", path) -> 0x79DF74
                OutMessage?.Invoke("[Error]:找不到世界爆率文件" + fileName);
                return;
            }

            for (var i = 0; i < typeNum; i++)
            {
                // 0x752DFC IntToStr(i+1) / 0x752E13 LStrCat3("type", n)
                var section = NativeSectionPrefix + (i + 1);
                var record = new NativeWorldScatterRecord
                {
                    // 0x752E48 / 0x752E72 都是 `mov word`，高 16 位被截掉。
                    MinLevel = unchecked((ushort)ini.ReadInteger(section, NativeMinLevelKey, 0)),
                    SecSpace = ini.ReadInteger(section, NativeSecSpaceKey, 0),
                    MaxPile = unchecked((ushort)ini.ReadInteger(section, NativeMaxPileKey, 1))
                };

                ParseMapList(record, ini.ReadString(section, NativeMapKey, string.Empty));

                // 0x752E95 先读裸键 prize，再在循环里读 prize1..prize99；
                // 0x752EAF 遇空串即停，所以键必须连号。
                var prize = ini.ReadString(section, NativePrizeKey, string.Empty);
                for (var n = 1; n != NativePrizeKeyLimit; n++)
                {
                    if (string.IsNullOrEmpty(prize))
                    {
                        break;
                    }
                    record.Prizes.Add(prize);
                    prize = ini.ReadString(section, NativePrizeKey + n, string.Empty);
                }

                _records.Add(record);
            }

            // 0x752EFF LStrCatN("已加载", path, "！") -> 0x79DF74
            OutMessage?.Invoke("已加载" + fileName + "！");
        }

        /// <summary>
        /// <c>sub_7531D0</c>：按 <c>{' ', '|', TAB, ','}</c> 切词、逐词
        /// <c>UpperCase</c>（<c>0x753259 call 0x40BCBC</c>）后加入
        /// <c>[rec+0x14]</c>。入参为空串时（<c>0x7531F7 cmp [ebp-4],0 / je</c>）
        /// 连表都不建，于是该记录不限地图。
        /// </summary>
        private static void ParseMapList(NativeWorldScatterRecord record, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            // 0x753200 表已存在就 Clear（[vmt+0x44]），否则新建。
            record.Maps ??= new List<string>();
            record.Maps.Clear();

            var rest = value;
            while (rest.Length > 0)
            {
                var token = string.Empty;
                rest = HUtil32.GetValidStr3(rest, ref token, NativeMapDividers);
                if (!string.IsNullOrEmpty(token))
                {
                    record.Maps.Add(token.ToUpper());
                }
            }
        }

        /// <summary>
        /// <c>sub_75307C</c>（VMT +0x04），模块框架每轮传当前 tick 进来。
        /// 一秒一轮，每轮先把总闸清零再按记录逐条重算，
        /// <c>0x7530BB..0x7530CB</c> 那段三路跳转就是 <c>armed |= ready</c>。
        /// </summary>
        internal void Run(int now)
        {
            // 0x75308B sub eax,[self+0x24] / 0x75308E cmp eax,0x3E8 / 0x753093 jle
            if (now - _lastRunTick <= NativeRunIntervalMs)
            {
                return;
            }

            _armed = false;              // 0x753095 mov byte [self+0x28],0
            _lastRunTick = now;          // 0x753099

            for (var i = 0; i < _records.Count; i++)
            {
                _armed = TickRecord(_records[i], now) || _armed;
            }
        }

        /// <summary>
        /// <c>sub_7530D8</c>：把「距上次发放过了几个 secSpace」折成待发数量，
        /// 上限 maxPile。首次见到的记录只落时间戳、不发东西。
        /// <code>
        /// 7530E4  83 7B 08 00        cmp dword [rec+8],0
        /// 7530E8  75 05              jne 0x7530EF
        /// 7530EA  89 4B 08           mov [rec+8],ecx          ; 首轮只播种，返回 false
        /// 7530EF  83 7B 04 00        cmp dword [rec+4],0 / jle -> false
        /// 7530F7  2B 43 08           sub eax,[rec+8]
        /// 7530FA  B9 E8 03 00 00     mov ecx,1000
        /// 7530FF  33 D2 / F7 F1      xor edx,edx / div ecx    ; 无符号，得秒数
        /// 753107  99 / F7 7B 04      cdq / idiv dword [rec+4] ; 有符号除 secSpace
        /// 75310B  0F B7 53 02        movzx edx,word [rec+2]   ; maxPile
        /// 75310F  E8 F8 3E D7 FF     call 0x4C700C            ; _MIN(eax,edx)
        /// 753116  89 7B 10           mov [rec+0x10],edi
        /// 75311B  0F 9F C0           setg al                  ; 返回 pending > 0
        /// </code>
        /// </summary>
        private static bool TickRecord(NativeWorldScatterRecord record, int now)
        {
            if (record.LastTick == 0)
            {
                record.LastTick = now;
                return false;
            }
            if (record.SecSpace <= 0)
            {
                return false;
            }

            var elapsedSeconds = unchecked(
                (int)(unchecked((uint)(now - record.LastTick)) / (uint)NativeMillisPerSecond));
            var pending = elapsedSeconds / record.SecSpace;
            record.Pending = Math.Min(pending, record.MaxPile);
            return record.Pending > 0;
        }

        /// <summary>
        /// <c>sub_752CAC</c> —— 段3 唯一的入口。总闸没开就直接空手而回；
        /// 命中第一条合格记录后**把总闸关掉**（<c>0x752D0C mov byte [self+0x28],0</c>），
        /// 所以每个 <see cref="Run"/> 周期全服最多放一次世界掉落。
        /// </summary>
        /// <param name="monsterLevel"><c>0x71FEBA movzx edx,word [self+0x278]</c>，怪物等级。</param>
        /// <param name="mapName"><c>0x71FEB4 mov ecx,[PEnvir+0x44]</c>，地图名。</param>
        /// <param name="repeatCount">命中记录的待发数量，即段3 外层循环的圈数。</param>
        internal IReadOnlyList<string> Query(int monsterLevel, string mapName,
            out int repeatCount)
        {
            repeatCount = 0;
            if (!_armed)                                  // 0x752CD5 / 0x752CD9 je
            {
                return null;
            }

            for (var i = 0; i < _records.Count; i++)      // 0x752CDB esi=[self+0x1C]
            {
                var prizes = MatchRecord(_records[i], monsterLevel, mapName,
                    out repeatCount);
                if (prizes == null)
                {
                    continue;
                }
                _armed = false;                           // 0x752D0C
                return prizes;
            }

            repeatCount = 0;
            return null;
        }

        /// <summary>
        /// <c>sub_753124</c>：三道判据全过才算命中。等级门是
        /// <c>0x753155 cmp eax,[ebp-4] / 0x753158 jg</c>，即 <c>minLevel &lt;= 怪物等级</c>；
        /// 数量门是 <c>0x75315A cmp dword [rec+0x10],0 / jle</c>；地图门只在
        /// <c>[rec+0x14]</c> 非空时生效，用 <c>TStringList.IndexOf</c>
        /// （<c>0x753184 call [ecx+0x54]</c>，<c>CaseSensitive</c> 默认 False）。
        /// 命中后把数量交出去并清零、同时把 <c>[rec+8]</c> 换成**新取的** tick
        /// （<c>0x75319A call 0x408340</c>，不是调用方传进来的那个）。
        /// </summary>
        private static IReadOnlyList<string> MatchRecord(
            NativeWorldScatterRecord record, int monsterLevel, string mapName,
            out int repeatCount)
        {
            repeatCount = 0;
            if (record.MinLevel > monsterLevel)
            {
                return null;
            }
            if (record.Pending <= 0)
            {
                return null;
            }
            if (record.Maps != null && record.Maps.Count > 0)
            {
                var found = false;
                for (var i = 0; i < record.Maps.Count; i++)
                {
                    if (!string.Equals(record.Maps[i], mapName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    found = true;
                    break;
                }
                if (!found)
                {
                    return null;
                }
            }

            repeatCount = record.Pending;
            record.Pending = 0;
            record.LastTick = HUtil32.GetTickCount();
            return record.Prizes;
        }
    }

    /// <summary>
    /// <c>sub_71FA20</c> 段3（<c>0x71FEA7-0x71FFA7</c>）本体，外加单例与配置文件定位。
    /// </summary>
    internal static class NativeWorldScatter
    {
        /// <summary>
        /// 配置文件名的来源是 <c>sub_4D9D38(self, "世界爆率文件1", @path)</c>：
        /// 先在模块设置表 <c>[self+0x14]</c> 里按名查值，查不到就往
        /// <c>&lt;模块目录&gt;main.ini</c> 写一份默认描述再返回空串 ——
        /// <code>
        /// 4D9E43  B9 18 9F 4D 00  mov ecx,0x4D9F18      ; ".txt"
        /// 4D9E48  8B 55 F8        mov edx,[ebp-8]       ; 键 = "世界爆率文件1"
        /// 4D9E4B  E8 CC B9 F2 FF  call 0x40581C         ; 值 = 键 + ".txt"
        /// 4D9E54  B9 28 9F 4D 00  mov ecx,0x4D9F28      ; "FileName"
        /// 4D9E61  FF 53 04        call [ini+4]          ; WriteString(键, "FileName", 值)
        /// 4D9E64  68 3C 9F 4D 00  push 0x4D9F3C         ; "True"
        /// 4D9E69  B9 4C 9F 4D 00  mov ecx,0x4D9F4C      ; "AutoLoad"
        /// </code>
        /// 所以**默认文件名由二进制自己写出来**，就是键名加 <c>.txt</c>。
        ///
        /// 目录一侧 BLOCKED：原生是
        /// <c>&lt;[[0x7D6B2C]]&gt; + "EngineConfig\" + &lt;[self+0x18]&gt; + "\"</c>
        /// （<c>sub_4D9F58</c>，4 段 <c>_LStrCatN</c>），其中 <c>[self+0x18]</c> 是模块名、
        /// <c>[0x7D6B2C]</c> 是框架根目录，两者都不在本子系统内。返回值还要过
        /// <c>0x4D9EB2 cmp byte [Objects[i]+0x100],0</c> 这个未定性的旗标，为 0 就把
        /// 路径清空。C# 侧只能取本仓既有的 Envir 目录作替身，见
        /// <see cref="ResolveConfigFileName"/>。
        /// </summary>
        internal const string NativeConfigKey = "世界爆率文件1";

        internal const string DefaultConfigFileName = NativeConfigKey + ".txt";

        private static readonly NativeWorldScatterMgr s_instance = new();
        private static bool s_configLoaded;

        internal static NativeWorldScatterMgr Instance => s_instance;

        internal static string ResolveConfigFileName()
        {
            var envirDir = M2Share.g_Config?.sEnvirDir ?? string.Empty;
            return Path.Combine(M2Share.sConfigPath ?? string.Empty, envirDir,
                DefaultConfigFileName);
        }

        internal static void LoadConfig()
        {
            s_configLoaded = true;
            s_instance.LoadConfig(ResolveConfigFileName());
        }

        /// <summary>
        /// 段3 <c>0x71FEA7-0x71FFA7</c>。外层跑 <c>*outCount</c> 圈
        /// （<c>0x71FEE4 mov ebx,[ebp-0x18]</c>），每圈把命中记录的 prize 表整个
        /// 造一遍；造不出物品就**整圈作废**（<c>0x71FF30 je 0x71FFA6</c> 跳的是
        /// 外层的 <c>dec ebx</c>，不是内层的 continue），落地失败则只丢这一件
        /// （<c>0x71FF94 call 0x414C24</c> 后 <c>jmp 0x71FF9C</c> 回内层）。
        /// </summary>
        internal static void Scatter(TBaseObject dyingObject,
            IList<KeyValuePair<string, string>> scatteredItems)
        {
            if (dyingObject?.m_PEnvir == null || M2Share.UserEngine == null)
            {
                return;
            }

            if (!s_configLoaded)
            {
                LoadConfig();
            }

            // 原生的一秒一轮由模块框架驱动；本仓的每 tick 入口 UserEngine.Run 是
            // 禁改文件，故在查询前自驱一次。Run 自带 0x75308E 的 1000ms 闸，
            // 一旦框架侧接线成功这里就是空转，见 docs/drop_order_and_worlddrop_20260814.md。
            s_instance.Run(HUtil32.GetTickCount());

            var prizes = s_instance.Query(dyingObject.m_Abil.Level,
                dyingObject.m_PEnvir.sMapName, out var repeatCount);
            if (prizes == null || repeatCount <= 0)      // 0x71FED0 / 0x71FEDA 两道
            {
                return;
            }

            for (var round = 0; round < repeatCount; round++)
            {
                for (var i = 0; i < prizes.Count; i++)   // 0x71FEF6 call [vmt+0x14] = Count
                {
                    // 0x71FF24 call 0x74DE54 —— 只按名造物，段3 不调 [item vmt+0x28]，
                    // 所以耐久停在构造函数给的值，与掉落控制那条腿不同。
                    TUserItem userItem = null;
                    if (!M2Share.UserEngine.CopyToUserItemFromName(prizes[i], ref userItem)
                        || userItem == null)
                    {
                        break;
                    }

                    // 0x71FF32 push 1 / push 0 / push 0 / push 0x720134("世界掉落")
                    // 0x71FF3D mov ecx,3 / 0x71FF45 mov eax,[ebp-0xC] / call 0x7688A0
                    // [ebp+0x14]=1 -> boDieDrop；[ebp+0xC]=0 -> ItemOfCreat 为 nil，
                    // 与段2 压杀手（0x71FDC6 push [ebp-8]）正相反。
                    if (!dyingObject.DropItemDown(userItem, NativeWorldScatterMgr.NativeScatterRange,
                            true, null, dyingObject))
                    {
                        continue;
                    }

                    // 0x71FF51 取物品名 -> 0x71FF7A LStrCatN(name, "=", "1")
                    // （0x4058E6 的 mov eax,[esp+ebx*4+0x18] 自 ebx=argCnt 递减，
                    //  即先拷第一个被 push 的 name，故成串是 "<名字>=1"）。
                    scatteredItems?.Add(new KeyValuePair<string, string>(prizes[i], "1"));
                }
            }
        }
    }
}
