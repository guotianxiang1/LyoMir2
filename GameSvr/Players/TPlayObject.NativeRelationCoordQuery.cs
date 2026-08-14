using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private const string NativeRelationCoordSpousePrefix = "你的配偶：";
        private const string NativeRelationCoordMasterPrefix = "你的师傅：";
        private const string NativeRelationCoordStudentPrefix = "你的徒弟：";

        /// <summary>
        /// 战神 sub_6CF000 @0x006CF000：bl 0/1/2 -> 0x6CF110/0x6CF124/0x6CF138 前缀，
        /// 拼接 [target+0x106] 名、[env+0x12C]/[env+0x130] 图名、坐标后 SysMsg（cx=0xFCFF）。
        /// </summary>
        internal bool TryBuildNativeRelationCoordMessage(byte relationKind,
            TPlayObject subject, out string message)
        {
            message = string.Empty;
            if (subject == null || subject.m_boGhost)
                return false;

            var prefix = relationKind switch
            {
                0 => NativeRelationCoordSpousePrefix,
                1 => NativeRelationCoordMasterPrefix,
                2 => NativeRelationCoordStudentPrefix,
                _ => null
            };
            if (prefix == null)
                return false;

            var mapName = subject.m_PEnvir?.sMapName ?? subject.m_sMapName
                          ?? string.Empty;
            message = prefix + subject.m_sCharName + mapName
                      + subject.m_nCurrX + "," + subject.m_nCurrY;
            return true;
        }

        internal void NotifyNativeRelationOnlineCoord(byte relationKind,
            string relatedName)
        {
            if (string.IsNullOrEmpty(relatedName))
                return;

            var related = M2Share.UserEngine?.GetPlayObject(relatedName);
            if (related == null || related.m_boGhost)
                return;

            if (!TryBuildNativeRelationCoordMessage(relationKind, related,
                    out var message))
                return;

            SysMsg(message, MsgColor.Green, MsgType.Hint);
        }

        internal void NotifyNativeSpouseOnlineCoord()
        {
            if (string.IsNullOrEmpty(m_sDearName))
                return;
            NotifyNativeRelationOnlineCoord(0, m_sDearName);
        }

        internal void NotifyNativeMasterOnlineCoord()
        {
            if (string.IsNullOrEmpty(m_sMasterName))
                return;
            NotifyNativeRelationOnlineCoord(1, m_sMasterName);
        }

        internal void NotifyNativeStudentOnlineCoord(string studentName)
        {
            NotifyNativeRelationOnlineCoord(2, studentName);
        }
    }
}
