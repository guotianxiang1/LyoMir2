using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        // Shared 6-bit decoder for native CM SOCIAL message bodies (relation / group / corps / gild /
        // channel / firework). GateService delivers processMessage.Payload = the raw 6-bit-ENCODED client
        // body (MsgBuff[ClientPacket.PackSize..]); the DECODED content is what native reads (CM dispatch
        // sub_6D7D68) and what GateService.DecodeClientMessageBody produces for sMsg. These native handlers
        // read binary ids / GBK names out of the body, so they MUST decode the Payload first — reading the
        // raw ENCODED bytes yields garbage (binary ids scrambled, names undecodable). Decode with the raw
        // 6-bit decoder (NO GBK, so binary int64 ids stay byte-exact); GBK is applied only by the per-op
        // codecs on the decoded name slice, where it is correct. Mirrors DecodeClientMessageBody's trailing
        // single-0 strip so the byte view is identical to the legacy sMsg path.
        //
        // NOTE: reading the GBK sMsg instead would corrupt any id byte >= 0x80 (GbkEncoding is lossy for
        // binary), which is why the fix decodes the Payload to raw bytes rather than reusing sMsg.
        private static byte[] DecodeNativeSocialBody(object payload)
        {
            if (payload is not byte[] raw || raw.Length == 0)
                return Array.Empty<byte>();
            var textLength = raw.Length;
            if (raw[textLength - 1] == 0) textLength--; // mirror DecodeClientMessageBody trailing-0 strip
            if (textLength == 0) return Array.Empty<byte>();
            return Misc.Decode6BitBufDirect(raw, textLength); // raw 6-bit decode, NO GBK
        }
    }
}
