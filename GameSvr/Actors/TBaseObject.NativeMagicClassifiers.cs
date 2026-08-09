namespace GameSvr
{
    public partial class TBaseObject
    {
        internal static bool IsNativeMagicFirstClassifier(int skillId)
        {
            ushort id = unchecked((ushort)skillId);
            return id is >= 70 and <= 99 or >= 3071 and <= 3118;
        }

        internal static bool IsNativeMagicSecondClassifier(int skillId)
        {
            ushort id = unchecked((ushort)skillId);
            return id is >= 50 and <= 55 or >= 300 and <= 302;
        }

        internal static bool IsNativeMagicThirdClassifier(int skillId)
        {
            ushort id = unchecked((ushort)skillId);
            return id is >= 116 and <= 118 or >= 125 and <= 127 or
                >= 129 and <= 131 or 270;
        }
    }
}
