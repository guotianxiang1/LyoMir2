using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private bool TryHandleNativeSocialProtocol(TProcessMessage processMessage)
        {
            return TryHandleNativeGroupProtocol(processMessage)
                || TryHandleNativeRelationProtocol(processMessage)
                || TryHandleNativeChannelProtocol(processMessage)
                || TryHandleNativeCorpsCoreProtocol(processMessage)
                || TryHandleNativeCorpsAdminProtocol(processMessage)
                || TryHandleNativeGuildCoreProtocol(processMessage)
                || TryHandleNativeGuildRelationProtocol(processMessage)
                || TryHandleNativeGuildTailProtocol(processMessage);
        }
    }
}
