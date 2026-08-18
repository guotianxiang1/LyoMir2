using System.Collections.Generic;
using System.Threading;

namespace GameSvr
{
    /// <summary>
    /// Runtime counterpart of the required client version at native
    /// <c>off_7D60D8</c> and its online-player sweep <c>sub_655954</c>.
    /// </summary>
    internal static class NativeClientVersionPolicy
    {
        private const int ClientInfoCollectionByteOffset = 3;
        private const byte ClientInfoCollectionMask = 0x20;

        private static string s_requiredVersion = string.Empty;

        internal static string RequiredVersion =>
            Volatile.Read(ref s_requiredVersion) ?? string.Empty;

        internal static void SetRequiredVersion(string version)
        {
            Volatile.Write(ref s_requiredVersion, version ?? string.Empty);
        }

        internal static bool IsAllowed(string clientVersion)
        {
            var required = RequiredVersion;
            return required.Length == 0 || string.Equals(required,
                clientVersion ?? string.Empty, StringComparison.Ordinal);
        }

        internal static bool IsClientInfoCollectionEnabled() =>
            M2Share.ServerSwitches?.IsBitSet(
                ClientInfoCollectionByteOffset,
                ClientInfoCollectionMask) == true;

        /// <summary>
        /// Native <c>sub_655954</c>: an empty policy is a no-op. Otherwise only
        /// ready, non-GM players that have already reported a version are
        /// inspected, and only mismatches are written. Matches are not promoted.
        /// </summary>
        internal static int RevalidatePlayers(
            IEnumerable<TPlayObject> players)
        {
            var required = RequiredVersion;
            if (required.Length == 0 || players == null)
            {
                return 0;
            }

            var mismatchCount = 0;
            foreach (var player in players)
            {
                if (player == null || !player.m_boReadyRun ||
                    player.m_btPermission >= 3 ||
                    string.IsNullOrEmpty(player.m_sNativeClientVersion))
                {
                    continue;
                }

                if (string.Equals(player.m_sNativeClientVersion, required,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                player.m_boNativeSwitchOffsetB75 = false;
                mismatchCount++;
            }

            return mismatchCount;
        }
    }
}
