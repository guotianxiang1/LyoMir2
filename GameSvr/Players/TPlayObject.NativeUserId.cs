namespace GameSvr
{
    public partial class TPlayObject
    {
        internal void LoadNativeMailRecipientId(long recipientId)
        {
            _nativeMailRecipientId = recipientId;
        }

        internal long GetCachedNativeUserId() => _nativeMailRecipientId;
    }
}
