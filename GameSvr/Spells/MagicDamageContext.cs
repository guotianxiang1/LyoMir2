using SystemModule;

namespace GameSvr
{
    internal sealed class MagicDamageContext
    {
        internal static MagicDamageContext Empty { get; } = new MagicDamageContext(
            null, 0, 0, 0, 0, 0, 0, 0, 0);

        internal MagicDamageContext(TMagic magicInfo, int plusInfoReferenceCache,
            int requiredTrainCache, byte level, ushort magicIndex,
            int trainingPoints, ushort spellMilliseconds,
            ushort coldMilliseconds, byte nativeLevelBonus)
        {
            MagicInfo = magicInfo;
            PlusInfoReferenceCache = plusInfoReferenceCache;
            RequiredTrainCache = requiredTrainCache;
            Level = level;
            MagicIndex = magicIndex;
            TrainingPoints = trainingPoints;
            SpellMilliseconds = spellMilliseconds;
            ColdMilliseconds = coldMilliseconds;
            NativeLevelBonus = nativeLevelBonus;
        }

        internal TMagic MagicInfo { get; }
        internal int PlusInfoReferenceCache { get; }
        internal int RequiredTrainCache { get; }
        internal byte Level { get; }
        internal ushort MagicIndex { get; }
        internal int TrainingPoints { get; }
        internal ushort SpellMilliseconds { get; }
        internal ushort ColdMilliseconds { get; }
        internal byte NativeLevelBonus { get; }

        internal static MagicDamageContext Capture(TUserMagic userMagic)
        {
            if (userMagic == null)
            {
                return Empty;
            }

            TMagic magicInfo = userMagic.MagicInfo;
            int requiredTrainCache = userMagic.btLevel < 3 &&
                magicInfo?.MaxTrain != null &&
                userMagic.btLevel < magicInfo.MaxTrain.Length
                    ? magicInfo.MaxTrain[userMagic.btLevel]
                    : -1;
            return new MagicDamageContext(
                magicInfo,
                // The deployed tree has no MagicDBPlusInfo.ini, matching the
                // native lookup's null result for this snapshot slot.
                0,
                requiredTrainCache,
                userMagic.btLevel,
                userMagic.wMagIdx,
                userMagic.nTranPoint,
                unchecked((ushort)(magicInfo?.SpellMilliseconds ?? 0)),
                unchecked((ushort)(magicInfo?.ColdMilliseconds ?? 0)),
                userMagic.NativeLevelBonus);
        }
    }
}
