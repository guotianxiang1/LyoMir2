using System;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        /// <summary>Native sub_73F690 — 按技能名删除自身魔法（case-insensitive）。</summary>
        // 73F6B4 test edi / 73F6BC mov eax,[ebx+0x500] / 73F708 mov al,[ebx+0x178]
        // 73F735 cmp al,0x36 -> SM_HERO_DELMAGIC else SM_DELMAGIC(0xD4)
        // 73F797 TList.Delete / 73F79C mov [ebp-1],1
        internal bool DeleteSelfMagic(string magicName)
        {
            if (string.IsNullOrEmpty(magicName))
                return false;

            for (var i = m_MagicList.Count - 1; i >= 0; i--)
            {
                var userMagic = m_MagicList[i];
                if (userMagic?.MagicInfo == null)
                    continue;
                if (!string.Equals(userMagic.MagicInfo.sMagicName, magicName,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                SendDelMagic(userMagic);
                var magicId = userMagic.MagicInfo.wMagicID;
                if (m_MagicArr != null && magicId < m_MagicArr.Length &&
                    ReferenceEquals(m_MagicArr[magicId], userMagic))
                    m_MagicArr[magicId] = null;
                m_MagicList.RemoveAt(i);
                RecalcAbilitys();
                return true;
            }
            return false;
        }
    }
}
