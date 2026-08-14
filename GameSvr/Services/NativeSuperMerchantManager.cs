using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SystemModule;

namespace GameSvr.Services
{
    /// <summary>
    /// 大药商人 <c>TSuperMerchant</c>：900s 库存刷新广播 <c>sub_6160A4</c>，
    /// ini 读 <c>sub_616258</c> / 写 <c>sub_616484</c>，全局实例原生 <c>0x7D6D10</c>。
    /// </summary>
    public sealed class NativeSuperMerchantManager
    {
        public const uint TickEa = 0x006160A4;
        public const uint LoadIniEa = 0x00616258;
        public const uint SaveIniEa = 0x00616484;
        public const int RefreshIntervalMs = 0xDBBA0; // 900000 ms @0x6160AF

        private const string IniRelativePath = "Config\\SuperMerchant.ini";
        private const string BroadcastTitle = "大药商人"; // 0x61612C
        private const string StockFmt = "大药商人当前库存 %s %d"; // 0x616154

        private static readonly string[] DefaultGoodsNames =
        {
            string.Empty,
            "疗伤药包",   // type 1 @0x7B45AC
            "万年雪霜包"  // type 2
        };

        private readonly object _sync = new();
        private readonly NativeSuperMerchantSlot[] _slots =
        {
            new(), // index 0 unused; native loops ebx=1..2
            new(20, 2500, 1000), // ctor defaults @0x615FC4
            new(20, 2500, 1000)
        };

        private int _lastTick;
        private bool _dirty;

        public bool IsAvailable => true;

        public void EnsureLoaded(string shareDirectory)
        {
            lock (_sync)
            {
                TryLoad(Path.Combine(shareDirectory ?? string.Empty, IniRelativePath));
                if (_lastTick == 0)
                    _lastTick = HUtil32.GetTickCount();
            }
        }

        /// <summary><c>sub_6160A4(Self, now)</c></summary>
        public void RunTick(int nowTick)
        {
            lock (_sync)
            {
                if (unchecked((uint)(nowTick - _lastTick)) < RefreshIntervalMs)
                    return;

                _lastTick = nowTick;
                if (!_dirty)
                    return;

                SaveIfDirty(Path.Combine(M2Share.sRootPath ?? string.Empty,
                    M2Share.g_Config?.sBaseDir ?? "Share", IniRelativePath));

                BroadcastRestock();
                _dirty = false;
            }
        }

        internal void MarkDirty() => _dirty = true;

        private void BroadcastRestock()
        {
            // 0x6160D4..0x616117: two world broadcasts via sub_79D3D8, dx=0xA.
            for (var type = 1; type <= 2; type++)
            {
                var name = DefaultGoodsNames[type];
                var current = _slots[type].Current;
                var msg = string.Format(StockFmt.Replace("%s", "{0}").Replace("%d", "{1}"),
                    name, current);
                M2Share.UserEngine?.SendBroadCastMsg(msg, MsgType.System);
            }
        }

        private void TryLoad(string path)
        {
            if (!File.Exists(path))
                return;

            var lines = File.ReadAllLines(path);
            string section = null;
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
                    continue;
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    section = line[1..^1];
                    continue;
                }
                var eq = line.IndexOf('=');
                if (eq <= 0 || section == null)
                    continue;
                if (!TryMapSection(section, out var type))
                    continue;
                var key = line[..eq].Trim();
                var val = line[(eq + 1)..].Trim();
                if (!int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out var n))
                    continue;
                switch (key)
                {
                    case "MinStorage":
                        _slots[type].Min = n;
                        break;
                    case "MaxStorage":
                        _slots[type].Max = n;
                        break;
                    case "CurrentStorage":
                        _slots[type].Current = n;
                        break;
                }
            }
        }

        private void SaveIfDirty(string path)
        {
            if (!_dirty)
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            var lines = new List<string>();
            for (var type = 1; type <= 2; type++)
            {
                lines.Add($"[GoodsInfo{type}]");
                lines.Add($"ItemName={DefaultGoodsNames[type]}");
                lines.Add($"MinStorage={_slots[type].Min}");
                lines.Add($"MaxStorage={_slots[type].Max}");
                lines.Add($"CurrentStorage={_slots[type].Current}");
                lines.Add(string.Empty);
            }
            File.WriteAllLines(path, lines);
        }

        private static bool TryMapSection(string section, out int type)
        {
            type = 0;
            if (!section.StartsWith("GoodsInfo", StringComparison.OrdinalIgnoreCase))
                return false;
            return int.TryParse(section["GoodsInfo".Length..],
                NumberStyles.Integer, CultureInfo.InvariantCulture, out type)
                   && type is >= 1 and <= 2;
        }

        private sealed class NativeSuperMerchantSlot
        {
            internal NativeSuperMerchantSlot() { }
            internal NativeSuperMerchantSlot(int min, int max, int current)
            {
                Min = min;
                Max = max;
                Current = current;
            }

            public int Min { get; set; }
            public int Max { get; set; }
            public int Current { get; set; }
        }
    }
}
