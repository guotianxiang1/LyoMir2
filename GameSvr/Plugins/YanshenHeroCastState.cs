namespace GameSvr.Plugins
{
    internal static class YanshenHeroCastState
    {
        private readonly record struct Command(byte MagicId, byte Repeat);

        private static readonly object SyncRoot = new();
        private static readonly Dictionary<string, Command> Commands =
            new(StringComparer.Ordinal);

        internal static int Set(string playerName, int magicId, int isRun)
        {
            if (magicId <= 0)
                return -1;

            var normalizedRun = isRun > 0 ? 1 : isRun;
            var command = new Command(
                (byte)Math.Min(magicId, byte.MaxValue),
                unchecked((byte)normalizedRun));

            lock (SyncRoot)
                Commands[playerName ?? string.Empty] = command;

            return normalizedRun;
        }

        internal static bool TryConsume(string playerName, out byte magicId)
        {
            var key = playerName ?? string.Empty;
            lock (SyncRoot)
            {
                if (!Commands.TryGetValue(key, out var command) ||
                    command.MagicId == 0)
                {
                    magicId = 0;
                    return false;
                }

                magicId = command.MagicId;
                if (command.Repeat == 0)
                    Commands[key] = default;
                return true;
            }
        }

        internal static bool TryPeek(string playerName, out byte magicId,
            out byte repeat)
        {
            lock (SyncRoot)
            {
                if (!Commands.TryGetValue(playerName ?? string.Empty,
                        out var command))
                {
                    magicId = 0;
                    repeat = 0;
                    return false;
                }

                magicId = command.MagicId;
                repeat = command.Repeat;
                return command.MagicId != 0;
            }
        }
    }
}
