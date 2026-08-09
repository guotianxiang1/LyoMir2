using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        internal const uint NativeMagicTrainingFlushDelay = 3000;

        internal TUserMagic m_NativeMagicTrainingPending;
        internal uint m_dwNativeMagicTrainingTick;

        internal bool TrainNativeMotaeboMagic(TUserMagic magic,
            int trainingPoints, int currentTick)
        {
            if (!CanTrainNativeMotaebo(magic))
                return false;

            int awardedPoints = m_boFastTrain
                ? unchecked(trainingPoints * 3)
                : trainingPoints;
            magic.nTranPoint = unchecked(magic.nTranPoint + awardedPoints);

            bool crossedThreshold = false;
            int requiredTraining = GetNativeMotaeboRequiredTraining(magic);
            if (requiredTraining != -1)
            {
                while (unchecked((uint)requiredTraining) <=
                       unchecked((uint)magic.nTranPoint))
                {
                    magic.nTranPoint = unchecked(
                        magic.nTranPoint - requiredTraining);
                    crossedThreshold = true;
                    if (magic.btLevel >= magic.MagicInfo.btTrainLv)
                        break;

                    magic.btLevel = unchecked((byte)(magic.btLevel + 1));
                    requiredTraining =
                        GetNativeMotaeboRequiredTraining(magic);
                }
            }

            if (crossedThreshold)
            {
                RecalcAbilitys();
                SendMsg(this, Grobal2.RM_ABILITY, 0, 0, 0, 0,
                    string.Empty);
            }

            QueueNativeMagicTrainingSnapshot(magic,
                unchecked((uint)currentTick));
            return true;
        }

        internal void RunNativeMagicTraining(int currentTick)
        {
            if (m_NativeMagicTrainingPending == null || m_boDeath ||
                m_WAbil.HP <= 0 ||
                unchecked((uint)currentTick - m_dwNativeMagicTrainingTick) <
                NativeMagicTrainingFlushDelay)
                return;

            var magic = m_NativeMagicTrainingPending;
            SendNativeMagicTrainingSnapshot(magic);
            m_NativeMagicTrainingPending = null;
        }

        private void QueueNativeMagicTrainingSnapshot(TUserMagic magic,
            uint currentTick)
        {
            if (m_NativeMagicTrainingPending != null &&
                !ReferenceEquals(m_NativeMagicTrainingPending, magic))
            {
                SendNativeMagicTrainingSnapshot(
                    m_NativeMagicTrainingPending);
            }

            m_NativeMagicTrainingPending = magic;
            m_dwNativeMagicTrainingTick = currentTick;
        }

        private void SendNativeMagicTrainingSnapshot(TUserMagic magic)
        {
            int effectiveLevel = Math.Min(
                unchecked((byte)(magic.btLevel + magic.NativeLevelBonus)),
                magic.MagicInfo.btTrainLv);
            SendMsg(this, Grobal2.RM_MAGIC_LVEXP,
                magic.MagicInfo.wMagicID, effectiveLevel,
                magic.nTranPoint,
                GetNativeMotaeboRequiredTraining(magic), string.Empty);
        }

        private static int GetNativeMotaeboRequiredTraining(
            TUserMagic magic)
        {
            if (magic?.MagicInfo?.MaxTrain == null || magic.btLevel >= 3 ||
                magic.btLevel >= magic.MagicInfo.MaxTrain.Length)
                return -1;

            return magic.MagicInfo.MaxTrain[magic.btLevel];
        }
    }
}
