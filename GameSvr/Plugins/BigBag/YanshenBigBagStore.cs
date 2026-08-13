using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GameSvr.Plugins.BigBag
{
    /// <summary>Outcome of <see cref="YanshenBigBagStore.Load"/>.</summary>
    public enum YanshenBigBagLoadStatus
    {
        /// <summary>The file was read and decoded.</summary>
        Loaded,

        /// <summary>
        /// The character has no file yet, which simply means no extra-bag items. Callers
        /// may treat this as an empty bag.
        /// </summary>
        Missing,

        /// <summary>
        /// The file exists but could not be read or decoded. Callers must <b>not</b> treat
        /// this as an empty bag: saving over it would destroy the items it still holds.
        /// </summary>
        Failed,
    }

    /// <summary>
    /// Reads and writes the 眼神 plugin's extra-bag files under
    /// <c>Gs1\MyJson\bags\</c>.
    ///
    /// There is <b>one file per character</b>, named <c>角色名.bin</c> — not one shared
    /// server-wide file. (An earlier survey recorded a <c>MyJson/bags.bin</c> string that
    /// does not exist in the runtime dump; the plugin's own GUI help text gives the path
    /// as <c>Gs1\MyJson\bags\角色名字.bin</c>, and the production server has 31 separate
    /// files.) This is also why the plugin's help text lists "合区需要单独复制数据" as a
    /// drawback: merging servers means copying these files by hand.
    ///
    /// This type exposes explicit <see cref="Load"/> and <see cref="TrySave"/> only. The
    /// plugin also flushes every ten minutes and on a clean shutdown, but wiring that up
    /// touches the server lifecycle and is deliberately left to the caller.
    /// </summary>
    public sealed class YanshenBigBagStore
    {
        public const string BagsDirectoryName = "bags";
        public const string FileExtension = ".bin";

        private static readonly Encoding Gbk;
        private static readonly HashSet<string> ReservedDeviceNames = new HashSet<string>(
            new[]
            {
                "CON", "PRN", "AUX", "NUL",
                "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
            },
            StringComparer.OrdinalIgnoreCase);

        // Serialises file access so a save's File.Replace cannot collide with a
        // concurrent read of the same character.
        private readonly object _gate = new object();

        static YanshenBigBagStore()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Gbk = Encoding.GetEncoding(936,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ReplacementFallback);
        }

        public YanshenBigBagStore(string bagsDirectory)
        {
            if (string.IsNullOrWhiteSpace(bagsDirectory))
                throw new ArgumentException("Extra-bag directory is required", nameof(bagsDirectory));
            BagsDirectory = Path.GetFullPath(bagsDirectory);
        }

        /// <summary>
        /// Build a store for the <c>bags</c> subdirectory of a <c>MyJson</c> directory,
        /// which is how the plugin lays it out.
        /// </summary>
        public static YanshenBigBagStore FromMyJsonDirectory(string myJsonDirectory)
        {
            if (string.IsNullOrWhiteSpace(myJsonDirectory))
                throw new ArgumentException("MyJson directory is required", nameof(myJsonDirectory));
            return new YanshenBigBagStore(Path.Combine(myJsonDirectory, BagsDirectoryName));
        }

        public string BagsDirectory { get; }

        /// <summary>
        /// Resolve a character's file path, rejecting any name that would escape
        /// <see cref="BagsDirectory"/> or that Windows would not store literally.
        /// Character names reach this from game data, so the check is fail-closed.
        /// </summary>
        public bool TryGetCharacterFilePath(string characterName, out string path, out string error)
        {
            path = null;
            if (string.IsNullOrEmpty(characterName))
            {
                error = "character name is empty";
                return false;
            }

            if (characterName != characterName.Trim())
            {
                error = $"character name '{characterName}' has leading or trailing whitespace";
                return false;
            }

            if (characterName.EndsWith(".", StringComparison.Ordinal))
            {
                error = $"character name '{characterName}' ends with a dot";
                return false;
            }

            if (characterName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                error = $"character name '{characterName}' contains a character that is not valid in a file name";
                return false;
            }

            // Windows reserves a device name whatever extension follows it, so "CON.x"
            // is the console just as "CON" is.
            var firstDot = characterName.IndexOf('.');
            var deviceCandidate = firstDot < 0 ? characterName : characterName.Substring(0, firstDot);
            if (ReservedDeviceNames.Contains(deviceCandidate))
            {
                error = $"character name '{characterName}' is a reserved device name";
                return false;
            }

            // The on-disk names were written by a GBK process; a name that is not
            // representable in GBK cannot be one of them.
            try
            {
                Gbk.GetBytes(characterName);
            }
            catch (EncoderFallbackException)
            {
                error = $"character name '{characterName}' is not representable in GBK";
                return false;
            }

            var candidate = Path.GetFullPath(Path.Combine(BagsDirectory, characterName + FileExtension));
            var root = BagsDirectory.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? BagsDirectory
                : BagsDirectory + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                error = $"character name '{characterName}' resolves outside the extra-bag directory";
                return false;
            }

            path = candidate;
            error = null;
            return true;
        }

        /// <summary>
        /// Load one character's extra bag. See <see cref="YanshenBigBagLoadStatus"/> for
        /// why a missing file and an unreadable one must be told apart.
        /// </summary>
        public YanshenBigBagLoadStatus Load(string characterName, out YanshenBigBagFile file, out string error)
        {
            file = null;
            if (!TryGetCharacterFilePath(characterName, out var path, out error))
                return YanshenBigBagLoadStatus.Failed;

            lock (_gate)
            {
                if (!File.Exists(path))
                {
                    error = null;
                    return YanshenBigBagLoadStatus.Missing;
                }

                return YanshenBigBagFile.TryLoad(path, out file, out error)
                    ? YanshenBigBagLoadStatus.Loaded
                    : YanshenBigBagLoadStatus.Failed;
            }
        }

        public bool TrySave(string characterName, YanshenBigBagFile file, out string error)
            => TrySave(characterName, file, BagCapacity.PersistableExtraSlots, out error);

        /// <summary>
        /// Write the extra bag. If <paramref name="file"/> holds more records than
        /// <paramref name="maxRecords"/>, refuse rather than truncate — the native
        /// 48-slot silent clip (<c>0x6B171B cmp edi,0x30</c>) must not grow a twin
        /// on the extra-bag path. Plugin <c>0x1007ED74 83 7d 10 30 7f 44</c> skips
        /// deleting <c>MyJson\bags\</c> when the extra count is already &gt; 0x30.
        /// </summary>
        public bool TrySave(string characterName, YanshenBigBagFile file, int maxRecords, out string error)
        {
            if (file == null)
            {
                error = "extra-bag file to save is null";
                return false;
            }

            if (maxRecords < 0)
            {
                error = "extra-bag persistable extra slots cannot be negative";
                return false;
            }

            var count = file.RecordCount;
            if (count > maxRecords)
            {
                error = $"extra-bag has {count} records, persistable extra slots are {maxRecords}; refusing to save rather than truncate";
                return false;
            }

            if (!TryGetCharacterFilePath(characterName, out var path, out error))
                return false;

            lock (_gate)
                return file.TrySave(path, out error);
        }

        public bool Exists(string characterName)
        {
            if (!TryGetCharacterFilePath(characterName, out var path, out _))
                return false;
            lock (_gate)
                return File.Exists(path);
        }

        /// <summary>
        /// Names of every character that currently has a file. Returns an empty list when
        /// the directory does not exist yet.
        /// </summary>
        public IReadOnlyList<string> EnumerateCharacterNames()
        {
            var names = new List<string>();
            lock (_gate)
            {
                if (!Directory.Exists(BagsDirectory))
                    return names;

                foreach (var path in Directory.EnumerateFiles(BagsDirectory, "*" + FileExtension))
                    names.Add(Path.GetFileNameWithoutExtension(path));
            }

            return names;
        }
    }
}
