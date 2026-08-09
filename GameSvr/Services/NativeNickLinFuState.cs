using SystemModule.Common;

namespace GameSvr
{
    public sealed class NativeNickLinFuState
    {
        private const int EnabledByteOffset = 1;
        private const byte EnabledMask = 0x02;

        private NativeNickLinFuState(bool enabled, int multiplier)
        {
            Enabled = enabled;
            Multiplier = multiplier;
        }

        public static NativeNickLinFuState Disabled { get; } = new(false, 1);

        public bool Enabled { get; }
        public int Multiplier { get; }

        public static bool TryEnableAndPersist(string shareDirectory, int multiplier,
            ref NativeNickLinFuState state, out string error)
        {
            error = string.Empty;
            if (multiplier is < 1 or > 10)
            {
                error = "LFMultiple must be between 1 and 10";
                return false;
            }
            if (string.IsNullOrWhiteSpace(shareDirectory))
            {
                error = "Share directory is empty";
                return false;
            }

            var actorFile = Path.Combine(shareDirectory, "Mir2Actor.ini");
            if (!File.Exists(actorFile))
            {
                error = "Mir2Actor.ini does not exist";
                return false;
            }

            ActorIni actorIni;
            try
            {
                actorIni = new ActorIni(actorFile);
            }
            catch (Exception ex)
            {
                error = "Mir2Actor.ini: " + ex.Message;
                return false;
            }

            state = new NativeNickLinFuState(true, multiplier);
            try
            {
                actorIni.SetMultiplier(multiplier);
                return true;
            }
            catch (Exception ex)
            {
                error = "Mir2Actor.ini: " + ex.Message;
                return false;
            }
        }

        public static bool TryApplyMirror(int multiplier,
            ref NativeNickLinFuState state)
        {
            if (multiplier is < 1 or > 10)
            {
                return false;
            }

            state = new NativeNickLinFuState(true, multiplier);
            return true;
        }

        public static bool TryLoad(string shareDirectory,
            out NativeNickLinFuState state, out string error)
        {
            if (!NativeServerSwitchStore.TryLoad(shareDirectory,
                    out var switches, out error))
            {
                state = Disabled;
                return false;
            }
            return TryLoad(shareDirectory, switches, out state, out error);
        }

        public static bool TryLoad(string shareDirectory,
            NativeServerSwitchStore switches,
            out NativeNickLinFuState state, out string error)
        {
            state = Disabled;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(shareDirectory))
            {
                error = "Share directory is empty";
                return false;
            }

            var multiplier = 1;
            var actorFile = Path.Combine(shareDirectory, "Mir2Actor.ini");
            if (File.Exists(actorFile))
            {
                try
                {
                    multiplier = new ActorIni(actorFile).GetMultiplier();
                }
                catch (Exception ex) when (ex is IOException ||
                                           ex is UnauthorizedAccessException ||
                                           ex is FormatException)
                {
                    error = "Mir2Actor.ini: " + ex.Message;
                    return false;
                }
            }

            if (switches == null || !switches.Available)
            {
                error = "ServerSwitch.Bin is unavailable";
                return false;
            }

            var enabled = switches.IsBitSet(EnabledByteOffset, EnabledMask);
            state = new NativeNickLinFuState(enabled, multiplier);
            return true;
        }

        private sealed class ActorIni : IniFile
        {
            public ActorIni(string fileName) : base(fileName)
            {
                if (new FileInfo(fileName).Length > 0)
                {
                    Load();
                }
            }

            public int GetMultiplier() => ReadInteger("Setup", "LFMultiple", 1);

            public void SetMultiplier(int multiplier) =>
                WriteInteger("Setup", "LFMultiple", multiplier);
        }
    }
}
