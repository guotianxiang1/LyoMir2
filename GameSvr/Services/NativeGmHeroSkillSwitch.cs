using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Native @HeroSkillSwitch setter. The dispatcher calls sub_73D458
    /// (0x0073D458), which resolves a definition by name and then lets
    /// sub_73D38C write the first matching magic entry's WORD at +0x0E.
    /// </summary>
    public static class NativeGmHeroSkillSwitch
    {
        public const int DispatchIndex = 611;
        public const int RequiredPermission = 3;
        public const uint HandlerAddress = 0x006246C6;
        public const uint SetterAddress = 0x0073D458;
        public const uint MagicListSetterAddress = 0x0073D38C;

        public static bool TrySet(TBaseObject owner, TMagic definition, bool enabled)
        {
            if (owner == null || definition == null || owner.m_MagicList == null)
                return false;

            for (var i = 0; i < owner.m_MagicList.Count; i++)
            {
                var entry = owner.m_MagicList[i];
                if (entry?.MagicInfo == null ||
                    entry.MagicInfo.wMagicID != definition.wMagicID)
                    continue;

                entry.SetNativeSkillSwitch(enabled);
                return true;
            }

            return false;
        }

        public static bool TrySetByName(TBaseObject owner, string skillName,
            bool enabled)
        {
            if (owner == null || string.IsNullOrEmpty(skillName) ||
                M2Share.UserEngine == null)
                return false;

            var definition = owner is HeroObject
                ? M2Share.UserEngine.FindHeroMagic(skillName)
                : M2Share.UserEngine.FindMagic(skillName);
            return TrySet(owner, definition, enabled);
        }
    }
}
