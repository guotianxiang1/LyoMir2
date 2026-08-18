namespace GameSvr.Services
{
    /// <summary>
    /// Pure pre-allocation portion of native TMonFortress.CreateFieldHero
    /// (sub_604E3C). Actor allocation, fame mutation, initialization and
    /// publication deliberately remain outside this boundary.
    /// </summary>
    public static class NativeFieldHeroFactoryPreflight
    {
        public const int OriginalFactoryAddress = 0x00604E3C;
        public const int OriginalAnsiLowerCaseAddress = 0x0040BCBC;
        public const int FallbackProbeCount = 31;

        /// <summary>
        /// Reproduces sub_40BCBC byte-for-byte: every byte in ASCII A..Z is
        /// increased by 0x20. The native routine does not decode multibyte
        /// text, so a GBK trail byte in that range is folded as well.
        /// </summary>
        public static byte[] CanonicalizeLookupName(
            ReadOnlySpan<byte> nameBytes)
        {
            var result = nameBytes.ToArray();
            for (var index = 0; index < result.Length; index++)
            {
                if (result[index] is >= (byte)'A' and <= (byte)'Z')
                    result[index] = unchecked((byte)(result[index] + 0x20));
            }
            return result;
        }

        /// <summary>
        /// Resolves the coordinates used before FieldHero allocation. The
        /// first probe ignores cell occupants; only its failure enters the 31
        /// ordinary probes. A failed final probe still advances coordinates
        /// and consumes the same native random draws before returning false.
        /// </summary>
        public static bool TryResolvePlacement(int mapWidth, int mapHeight,
            int initialX, int initialY,
            Func<int, int, bool, bool> canWalk,
            Func<int, int> random,
            out int resolvedX, out int resolvedY)
        {
            if (canWalk == null)
                throw new ArgumentNullException(nameof(canWalk));
            if (random == null)
                throw new ArgumentNullException(nameof(random));

            var x = initialX;
            var y = initialY;
            if (canWalk(x, y, true))
            {
                resolvedX = x;
                resolvedY = y;
                return true;
            }

            var step = mapWidth < 50 ? 2 : 3;
            var margin = mapHeight >= 250
                ? 50
                : mapHeight >= 30 ? 20 : 2;

            for (var remaining = FallbackProbeCount;
                 remaining > 0;
                 remaining--)
            {
                if (canWalk(x, y, false))
                {
                    resolvedX = x;
                    resolvedY = y;
                    return true;
                }

                if (x < mapWidth - margin - 1)
                {
                    x += step;
                }
                else
                {
                    x = margin + random(mapWidth / 2);
                    if (y < mapHeight - margin - 1)
                        y += step;
                    else
                        y = margin + random(mapHeight / 2);
                }
            }

            resolvedX = x;
            resolvedY = y;
            return false;
        }
    }
}
