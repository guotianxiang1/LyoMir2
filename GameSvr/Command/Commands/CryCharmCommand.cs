using GameSvr.CommandSystem;

namespace GameSvr
{
    [GameCommand("传", "千里传音", "内容", 0)]
    public sealed class CryCharmCommand : BaseCommond
    {
        [DefaultCommand]
        public void Transmit(TPlayObject playObject)
        {
        }

        internal override string HandleRaw(string rawLine, string parameters,
            byte[] rawPayload, int bodyLength, TPlayObject playObject)
        {
            playObject?.ProcessNativeCryCharmCommand(rawLine, rawPayload,
                bodyLength);
            return string.Empty;
        }
    }
}
