using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private void TriggerNativeMailQuest()
        {
            M2Share.PasEngine?.TryCallScriptLabel(
                "RunMailQuest", "@PlayerCheckNewMail", this);
        }

        private void NotifyNativeMailboxOnLogin()
        {
            if (!TryGetNativeMailRecipientId(out var recipientId)
                || !NativeMailCacheService.TouchExisting(
                    recipientId, m_sCharName, DateTime.UtcNow, out var counts))
                return;

            var body = new byte[sizeof(int) * counts.Length];
            Buffer.BlockCopy(counts, 0, body, 0, body.Length);
            var header = Grobal2.MakeDefaultMsg(
                Grobal2.SM_MAIL_INFO, counts.Sum(), 0, 0, counts.Length);
            SendSocket(header, body);
        }
    }
}
