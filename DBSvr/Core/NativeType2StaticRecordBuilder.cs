using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace DBSvr.Core
{
    /// <summary>
    /// Database-independent input for the native Type2 static record builders.
    /// Text values are the raw ANSI bytes returned by the legacy database.
    /// </summary>
    public sealed class NativeType2StaticRow
    {
        private readonly Dictionary<string, byte[]> _ansiValues;
        private readonly Dictionary<string, int> _int32Values;
        private readonly HashSet<string> _columns;

        public NativeType2StaticRow(
            IReadOnlyDictionary<string, byte[]> ansiValues = null,
            IReadOnlyDictionary<string, int> int32Values = null,
            IEnumerable<string> presentColumns = null)
        {
            _ansiValues = new Dictionary<string, byte[]>(
                StringComparer.OrdinalIgnoreCase);
            _int32Values = new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);
            _columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (presentColumns != null)
                foreach (var column in presentColumns)
                    if (!string.IsNullOrEmpty(column)) _columns.Add(column);

            if (ansiValues != null)
                foreach (var pair in ansiValues)
                {
                    if (string.IsNullOrEmpty(pair.Key))
                        throw new ArgumentException(
                            "column name cannot be empty", nameof(ansiValues));
                    _ansiValues.Add(pair.Key,
                        pair.Value == null ? Array.Empty<byte>()
                            : (byte[])pair.Value.Clone());
                    _columns.Add(pair.Key);
                }

            if (int32Values != null)
                foreach (var pair in int32Values)
                {
                    if (string.IsNullOrEmpty(pair.Key))
                        throw new ArgumentException(
                            "column name cannot be empty", nameof(int32Values));
                    _int32Values.Add(pair.Key, pair.Value);
                    _columns.Add(pair.Key);
                }
        }

        public bool HasColumn(string name) =>
            !string.IsNullOrEmpty(name) && _columns.Contains(name);

        public ReadOnlyMemory<byte> RequireAnsi(string name)
        {
            if (!HasColumn(name)) throw MissingColumn(name);
            if (!_ansiValues.TryGetValue(name, out var value))
                throw new InvalidOperationException(
                    $"column '{name}' does not contain an ANSI byte value");
            return value;
        }

        public int RequireInt32(string name)
        {
            if (!HasColumn(name)) throw MissingColumn(name);
            if (!_int32Values.TryGetValue(name, out var value))
                throw new InvalidOperationException(
                    $"column '{name}' does not contain an Int32 value");
            return value;
        }

        private static KeyNotFoundException MissingColumn(string name) =>
            new($"required column '{name}' is absent");
    }

    /// <summary>
    /// Builds the nine fixed-layout records sent during the Type2 0x003D startup.
    /// All offsets are compatible with the original 32-bit DBServer packets.
    /// </summary>
    public static class NativeType2StaticRecordBuilder
    {
        public const ushort HumanMagicCommand = 0x0065;
        public const ushort HeroMagicCommand = 0x0066;
        public const ushort MonsterCommand = 0x0067;
        public const ushort StdItemsCommand = 0x0068;
        public const ushort AntiqueItemsCommand = 0x0073;
        public const ushort FieldHeroCommand = 0x006C;
        public const ushort SuperForceCommand = 0x0075;
        public const ushort SuperSkillCommand = 0x0076;
        public const ushort ForceMagicCommand = 0x006D;

        private const int HeaderSize = NativeType2Protocol.HeaderSize;

        public static byte[] Build(ushort command, NativeType2StaticRow row,
            bool isLast)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));
            return command switch
            {
                HumanMagicCommand => BuildMagic(row, command, true, isLast),
                HeroMagicCommand => BuildMagic(row, command, false, isLast),
                MonsterCommand => BuildMonster(row, isLast),
                StdItemsCommand => BuildStdItem(row, isLast, false),
                AntiqueItemsCommand => BuildAntiqueItem(row, isLast),
                FieldHeroCommand => BuildFieldHero(row, isLast),
                SuperForceCommand => BuildSuperForce(row, isLast),
                SuperSkillCommand => BuildSuperSkill(row, isLast),
                ForceMagicCommand => BuildForceMagic(row, isLast),
                _ => throw new ArgumentOutOfRangeException(nameof(command),
                    command, "unsupported native Type2 static command")
            };
        }

        public static List<byte[]> BuildRecords(ushort command,
            IReadOnlyList<NativeType2StaticRow> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            EnsureSupported(command);
            var acceptedCount = rows.Count;
            if (command is StdItemsCommand or ForceMagicCommand)
            {
                for (var i = 0; i < rows.Count; i++)
                {
                    var row = rows[i] ?? throw new ArgumentException(
                        "row cannot be null", nameof(rows));
                    var index = command == StdItemsCommand
                        ? row.RequireInt32("idx")
                        : row.RequireInt32("ForceId");
                    if (index == i + 1) continue;
                    acceptedCount = i;
                    break;
                }
            }
            var result = new List<byte[]>(acceptedCount);
            for (var i = 0; i < acceptedCount; i++)
                result.Add(Build(command, rows[i] ?? throw new ArgumentException(
                    "row cannot be null", nameof(rows)), i == acceptedCount - 1));
            return result;
        }

        private static byte[] BuildMagic(NativeType2StaticRow row,
            ushort command, bool includeTimingFields, bool isLast)
        {
            var packet = CreatePacket(command, 0x48, isLast);
            WriteShortString(packet, 0x00, 15, row.RequireAnsi("MagName"));
            WriteUInt16(packet, 0x10, row.RequireInt32("MagicIdx"));
            WriteByte(packet, 0x12, row.RequireInt32("EffectType"));
            WriteByte(packet, 0x13, row.RequireInt32("Effect"));
            WriteByte(packet, 0x14, row.RequireInt32("Spell"));
            WriteByte(packet, 0x15, row.RequireInt32("Power"));
            WriteByte(packet, 0x16, row.RequireInt32("MaxPower"));
            WriteByte(packet, 0x17, row.RequireInt32("DefSpell"));
            WriteByte(packet, 0x18, row.RequireInt32("DefPower"));
            WriteByte(packet, 0x19, row.RequireInt32("DefMaxPower"));
            WriteByte(packet, 0x1A, row.RequireInt32("Job"));
            for (var i = 1; i <= 5; i++)
                WriteByte(packet, 0x1A + i, row.RequireInt32($"NeedLv{i}"));
            for (var i = 1; i <= 4; i++)
            {
                WriteInt32(packet, 0x1C + i * 4,
                    row.RequireInt32($"LvTrain{i}"));
            }
            WriteInt32(packet, 0x30,
                unchecked(row.RequireInt32("Delay") * 10));
            if (includeTimingFields)
            {
                WriteInt32(packet, 0x34, row.RequireInt32("ColdMilSec"));
                WriteInt32(packet, 0x38, row.RequireInt32("SpellMilSec"));
            }
            return packet;
        }

        private static byte[] BuildMonster(NativeType2StaticRow row, bool isLast)
        {
            var packet = CreatePacket(MonsterCommand, 0x74, isLast);
            WriteShortString(packet, 0x04, 15, row.RequireAnsi("MonName"));
            WriteByte(packet, 0x14, row.RequireInt32("Race"));
            WriteByte(packet, 0x15, row.RequireInt32("RaceImg"));
            WriteByte(packet, 0x16, row.RequireInt32("Undead"));
            WriteByte(packet, 0x17, row.RequireInt32("CoolEye"));
            WriteUInt16(packet, 0x18, row.RequireInt32("Appr"));
            WriteUInt16(packet, 0x1A, row.RequireInt32("Level"));
            WriteInt32(packet, 0x1C, row.RequireInt32("Exp"));
            WriteInt32(packet, 0x20, row.RequireInt32("HP"));
            WriteInt32(packet, 0x24, row.RequireInt32("MP"));
            var words = new[]
            {
                "AC", "MAC", "DC", "DcMax", "MC", "SC", "Speed", "Hit",
                "WalkSpd"
            };
            for (var i = 0; i < words.Length; i++)
                WriteUInt16(packet, 0x28 + i * 2, row.RequireInt32(words[i]));
            WriteUInt16(packet, 0x3A, 1);
            WriteUInt16(packet, 0x3E, row.RequireInt32("AttackSpd"));
            WriteUInt16(packet, 0x42, row.RequireInt32("ForceLevel"));
            WriteInt32(packet, 0x44, row.RequireInt32("ForceValue"));
            WriteOptionalInt32(packet, 0x4C, row, "Speciality");
            WriteOptionalInt32(packet, 0x50, row, "SuperForceExp");
            WriteOptionalUInt16(packet, 0x54, row, "SuperForceLv");
            WriteOptionalInt32(packet, 0x58, row, "JobFastness");
            WriteOptionalInt32(packet, 0x5C, row, "JobFastnessVal");
            WriteOptionalInt32(packet, 0x60, row, "SuperPower");
            WriteInt32(packet, 0x64, row.RequireInt32("ForceValue"));
            return packet;
        }

        public static byte[] BuildImportedStdItem(NativeType2StaticRow row,
            bool isLast) => BuildStdItem(row, isLast, true);

        private static byte[] BuildStdItem(NativeType2StaticRow row,
            bool isLast, bool imported)
        {
            var packet = CreatePacket(StdItemsCommand, 0x140, isLast);
            WriteUInt16(packet, 0x00, row.RequireInt32("idx"));
            WriteShortString(packet, 0x04, 15, row.RequireAnsi("Iname"));
            WriteByte(packet, 0x14, row.RequireInt32("Stdmode"));
            WriteByte(packet, 0x15, row.RequireInt32("Shape"));
            WriteByte(packet, 0x16, row.RequireInt32("Need"));
            WriteByte(packet, 0x17, row.RequireInt32("Source"));
            WriteUInt16(packet, 0x18, row.RequireInt32("Looks"));
            var words = new[]
            {
                "Weight", "DuraMax", "AniCount", "NeedConf", "NeedLevel",
                "AC", "MaxAC", "MAC", "MaxMAC", "DC", "MaxDC", "MC",
                "MaxMC", "SC", "MaxSC"
            };
            for (var i = 0; i < words.Length; i++)
                WriteUInt16(packet, 0x1A + i * 2, row.RequireInt32(words[i]));
            if (row.HasColumn("MaxCC"))
            {
                WriteUInt16(packet, 0x38, row.RequireInt32("CC"));
                WriteUInt16(packet, 0x3A, row.RequireInt32("MaxCC"));
            }
            WriteInt32(packet, 0x3C, row.RequireInt32("Price"));
            WriteByte(packet, 0x40, row.RequireInt32("OutLook"));
            WriteByte(packet, 0x41, row.RequireInt32("AntiqueLv"));
            WriteUInt16(packet, 0x42, row.RequireInt32("itemScore"));
            WriteUInt16(packet, 0x44, row.RequireInt32("SuitEquipType"));
            if (!imported)
                WriteUInt16(packet, 0x46, row.RequireInt32("BaseEffectID"));
            WriteUInt16(packet, 0x48, row.RequireInt32("wParam1"));
            WriteUInt16(packet, 0x4A, row.RequireInt32("wParam2"));
            WriteInt32(packet, 0x4C, row.RequireInt32("intParam"));
            WriteInt32(packet, 0x50, row.RequireInt32("intParam2"));
            WriteInt32(packet, 0x54, row.RequireInt32("intParam3"));
            WriteUInt16(packet, 0x58, row.RequireInt32("MaxSteelLv"));
            WriteUInt16(packet, 0x5A, row.RequireInt32("MaxVeinsLv"));
            if (row.HasColumn("ItemExtAbil"))
                WriteShortString(packet, 0x5C, 200,
                    row.RequireAnsi("ItemExtAbil"));
            WriteUInt16(packet, 0x126, row.RequireInt32("OutLook"));
            if (row.HasColumn("NeedJob"))
                WriteByte(packet, 0x128, row.RequireInt32("NeedJob"));
            else if (!imported)
                WriteByte(packet, 0x128, 99);
            WriteInt32(packet, 0x12C, row.RequireInt32("ItemLevel"));
            WriteUInt16(packet, 0x130, row.RequireInt32("ItemConf"));
            return packet;
        }

        private static byte[] BuildAntiqueItem(NativeType2StaticRow row,
            bool isLast)
        {
            var packet = CreatePacket(AntiqueItemsCommand, 0xB6, isLast);
            WriteShortString(packet, 0x00, 15,
                row.RequireAnsi("AntiqueName"));
            WriteShortString(packet, 0x10, 15,
                row.RequireAnsi("baseItemName"));
            for (var i = 1; i <= 4; i++)
            {
                WriteShortString(packet, 0x10 + i * 0x10, 15,
                    row.RequireAnsi($"abilName{i}"));
                WriteShortString(packet, 0x50 + i * 0x10, 15,
                    row.RequireAnsi($"specAbil{i}"));
                WriteByte(packet, 0xA3 + i,
                    row.RequireInt32($"abilVal{i}"));
            }
            WriteByte(packet, 0xA0, row.RequireInt32("antiqueLv"));
            WriteByte(packet, 0xA1, row.RequireInt32("maxAntiqueLv"));
            WriteByte(packet, 0xA2, row.RequireInt32("mysteryCnt"));
            WriteByte(packet, 0xA3, row.RequireInt32("maxMysteryCnt"));
            WriteByte(packet, 0xA8, row.RequireInt32("steelLv"));
            WriteByte(packet, 0xA9, row.RequireInt32("veinslv"));
            return packet;
        }

        private static byte[] BuildFieldHero(NativeType2StaticRow row,
            bool isLast)
        {
            var packet = CreatePacket(FieldHeroCommand, 0x148, isLast);
            WriteShortString(packet, 0x00, 14, row.RequireAnsi("name"));
            WriteByte(packet, 0x0F, row.RequireInt32("sex"));
            WriteByte(packet, 0x10, row.RequireInt32("job"));
            WriteUInt16(packet, 0x12, row.RequireInt32("Lvl"));
            WriteByte(packet, 0x14, row.RequireInt32("BossLevel"));
            WriteByte(packet, 0x15, row.RequireInt32("BodyLuck"));
            WriteByte(packet, 0x16, row.RequireInt32("AddHitPoint"));
            WriteInt32(packet, 0x18, row.RequireInt32("DrinkDrug"));
            WriteInt32(packet, 0x1C, row.RequireInt32("Exp"));

            var equipment = new[]
            {
                "Dress", "Weapon", "Medal", "Necklace", "Helmet", "ArmringL",
                "ArmringR", "RingL", "RingR", "Bujuk", "Belt", "Boots",
                "Charm", "Mask"
            };
            for (var i = 0; i < equipment.Length; i++)
            {
                var offset = 0x20 + i * 0x14;
                WriteShortString(packet, offset, 14,
                    row.RequireAnsi(equipment[i]));
                WriteInt32(packet, offset + 0x10,
                    row.RequireInt32(equipment[i] + "Scatter"));
            }
            return packet;
        }

        private static byte[] BuildSuperForce(NativeType2StaticRow row,
            bool isLast)
        {
            var packet = CreatePacket(SuperForceCommand, 0x8C, isLast);
            WriteInt32(packet, 0x00, row.RequireInt32("Level"));
            WriteInt32(packet, 0x04, row.RequireInt32("NeedExp"));
            var families = new[]
            {
                "AC", "MaxAC", "MAC", "MaxMAC", "MainPower", "MaxMainPower"
            };
            for (var family = 0; family < families.Length; family++)
                for (var level = 1; level <= 5; level++)
                    WriteInt32(packet, 0x08 + family * 0x14
                                            + (level - 1) * 4,
                        row.RequireInt32(families[family] + level));
            return packet;
        }

        private static byte[] BuildSuperSkill(NativeType2StaticRow row,
            bool isLast)
        {
            var packet = CreatePacket(SuperSkillCommand, 0x48, isLast);
            WriteInt32(packet, 0x00, row.RequireInt32("SkillId"));
            WriteShortString(packet, 0x04, 23,
                row.RequireAnsi("SkillName"));
            WriteInt32(packet, 0x1C, row.RequireInt32("BaseParam"));
            WriteInt32(packet, 0x20, row.RequireInt32("LevelParam"));
            for (var i = 1; i <= 9; i++)
                WriteUInt16(packet, 0x22 + i * 2,
                    row.RequireInt32($"NeedLv{i}"));
            WriteByte(packet, 0x36, row.RequireInt32("upItemParam"));
            WriteByte(packet, 0x37, row.RequireInt32("EffectType"));
            for (var i = 1; i <= 4; i++)
                WriteByte(packet, 0x37 + i,
                    row.RequireInt32($"Effect{i}"));
            return packet;
        }

        private static byte[] BuildForceMagic(NativeType2StaticRow row,
            bool isLast)
        {
            var packet = CreatePacket(ForceMagicCommand, 0x50, isLast);
            WriteUInt16(packet, 0x00, row.RequireInt32("ForceId"));
            WriteUInt16(packet, 0x02, row.RequireInt32("MagicIdx"));
            WriteShortString(packet, 0x04, 14, row.RequireAnsi("Name"));
            WriteByte(packet, 0x13, row.RequireInt32("MagKind"));
            WriteByte(packet, 0x14, row.RequireInt32("Effect"));
            WriteByte(packet, 0x15, row.RequireInt32("Spell"));
            WriteByte(packet, 0x16, row.RequireInt32("DefSpell"));
            WriteByte(packet, 0x17, row.RequireInt32("Power"));
            WriteByte(packet, 0x18, row.RequireInt32("DefPower"));
            WriteByte(packet, 0x19, row.RequireInt32("PowerParam"));
            WriteByte(packet, 0x1A, row.RequireInt32("LastLv"));
            WriteByte(packet, 0x1B, row.RequireInt32("Job"));
            for (var i = 1; i <= 5; i++)
            {
                WriteUInt16(packet, 0x1A + i * 2,
                    row.RequireInt32($"NeedL{i}"));
                WriteInt32(packet, 0x24 + i * 4,
                    row.RequireInt32($"L{i}Train"));
                WriteByte(packet, 0x3B + i,
                    row.RequireInt32($"L{i}NeedStone"));
            }
            return packet;
        }

        private static byte[] CreatePacket(ushort command, int length,
            bool isLast)
        {
            var packet = new byte[length];
            BinaryPrimitives.WriteUInt16LittleEndian(packet, command);
            if (isLast)
                BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8, 4), 1);
            return packet;
        }

        private static void WriteShortString(byte[] packet, int bodyOffset,
            int maximumLength, ReadOnlyMemory<byte> value)
        {
            var length = Math.Min(maximumLength, value.Length);
            packet[HeaderSize + bodyOffset] = unchecked((byte)length);
            value.Span[..length].CopyTo(
                packet.AsSpan(HeaderSize + bodyOffset + 1, length));
        }

        private static void WriteByte(byte[] packet, int bodyOffset, int value) =>
            packet[HeaderSize + bodyOffset] = unchecked((byte)value);

        private static void WriteUInt16(byte[] packet, int bodyOffset, int value) =>
            BinaryPrimitives.WriteUInt16LittleEndian(
                packet.AsSpan(HeaderSize + bodyOffset, 2),
                unchecked((ushort)value));

        private static void WriteInt32(byte[] packet, int bodyOffset, int value) =>
            BinaryPrimitives.WriteInt32LittleEndian(
                packet.AsSpan(HeaderSize + bodyOffset, 4), value);

        private static void WriteOptionalUInt16(byte[] packet, int bodyOffset,
            NativeType2StaticRow row, string column)
        {
            if (row.HasColumn(column))
                WriteUInt16(packet, bodyOffset, row.RequireInt32(column));
        }

        private static void WriteOptionalInt32(byte[] packet, int bodyOffset,
            NativeType2StaticRow row, string column)
        {
            if (row.HasColumn(column))
                WriteInt32(packet, bodyOffset, row.RequireInt32(column));
        }

        private static void EnsureSupported(ushort command)
        {
            if (command is HumanMagicCommand or HeroMagicCommand or MonsterCommand
                or StdItemsCommand or AntiqueItemsCommand or FieldHeroCommand
                or SuperForceCommand or SuperSkillCommand or ForceMagicCommand)
                return;
            throw new ArgumentOutOfRangeException(nameof(command), command,
                "unsupported native Type2 static command");
        }
    }
}
