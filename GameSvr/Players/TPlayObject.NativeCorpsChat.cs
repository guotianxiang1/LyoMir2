using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        /// <summary>
        /// 战神 corps ("战队") chat ident. <c>sub_705F3C</c> loads it twice — once for the
        /// non-empty body path (<c>705FC0 mov dx,0x6C</c>) and once for the empty-body path
        /// (<c>705FDA mov dx,0x6C</c>) — and hands it to the member fan-out
        /// <c>sub_705954</c>, which forwards it to each online member's
        /// <c>vtable+0x250</c> (SendDefMessage). Image-wide there are exactly 6
        /// <c>mov dx,0x6C</c> sites (0x62E9D6, 0x62E9F0, 0x63A4F8, 0x63A510, 0x705FC0,
        /// 0x705FDA) and 0 <c>mov cx,0x6C</c>, so 108 is not overloaded as another chat ident.
        /// </summary>
        internal const int SM_CORPSMESSAGE = 108;

        /// <summary>
        /// 战神 corps-chat colour word: <c>6F7BCA mov cx,0xFFFA</c> = <b>-6</b>, passed by
        /// <c>sub_6F7B10</c> into <c>sub_705F3C</c> and from there into the fan-out's
        /// <c>SendDefMessage</c> <c>recog</c>… no: the fan-out puts <c>recog=0</c>
        /// (<c>705FBE xor ecx,ecx</c>) and passes this word as the LAST stack argument.
        /// With <c>vtable+0x250</c>'s left-to-right stack order (param, tag, series, sMsg) the
        /// 4 pushes at <c>705F9C..705FBD</c> are <c>param=cx</c>, <c>tag=0</c>, <c>series=0</c>,
        /// <c>sMsg=text</c>.
        /// </summary>
        private const int NativeCorpsMessageParam = -6;

        /// <summary>
        /// 战神 <c>sub_705F3C</c> body clamp: <c>705F72 call Length / 705F77 inc eax</c> gives
        /// <c>n = len+1</c>; when <c>btFlag == 0</c> (<c>705F78 cmp byte [ebp+8],0 / jne</c>)
        /// <c>705F7E cmp eax,0x50 / jle / 705F83 mov eax,0x50</c> clamps <c>n</c> to
        /// <b>0x50 = 80</b>, and <c>705F8A dec ecx</c> makes the copied length <c>n-1</c>.
        /// Net: at most <b>79</b> characters survive.
        /// </summary>
        private const int NativeCorpsMessageMaxLength = 79;

        /// <summary>
        /// Handles the <c>!#</c> chat prefix = 战神 <b>corps/squad (战队) chat</b>.
        /// Dispatch: <c>sub_6BB2F8</c> @<c>6BB74E cmp al,0x23 / 6BB750 jne 0x6bb771</c> then
        /// <c>6BB767 call sub_6F7B10</c>, reached only when the enclosing <c>'!'</c> gate
        /// <c>6BB6DB cmp edi,2 / jle 0x6bb771</c> passes (so <c>len &gt; 2</c>; a bare
        /// <c>"!#"</c> falls through to shout).
        /// <para>
        /// The target is <c>[self+0xAE8]</c>, which is a <b>TCorps</b> (VMT 0x705064,
        /// instancesize 144) — NOT a voice channel: <c>sub_6ADAE4</c> dereferences
        /// <c>[[+0xAE8]+4]</c> as the guild, all five writers of the field live in the corps
        /// manager unit 0x701xxx-0x707xxx (setter fn 0x702328 references the literals
        /// 副队长/卸任了/通过面对面加人加入了/战队), and the fan-out <c>sub_705954</c> walks
        /// <c>[TCorps+0x30]</c> taking each entry's <c>[+0x28]</c> as the online player.
        /// </para>
        /// <para>
        /// Audience = the online members of the sender's own corps. Before this existed, C#
        /// let <c>'!'</c> + an unrecognised second character fall into the SHOUT branch, so
        /// <c>!#text</c> was broadcast to everyone within 50 tiles.
        /// </para>
        /// </summary>
        /// <returns>
        /// <c>true</c> when the prefix was consumed (native returns without reaching the shout
        /// ladder on every one of its branches, including the silent ones).
        /// </returns>
        private bool TryProcessNativeCorpsChat(string sData)
        {
            // 6BB6DB cmp edi,2 / jle 0x6bb771 -- "!#" with no body is NOT corps chat.
            if (sData == null || sData.Length <= 2) return false;
            // 6BB74E cmp al,0x23 ('#')
            if (sData[0] != '!' || sData[1] != '#') return false;

            // 6F7B3C cmp byte [ebp+0xC],0 / jne 0x6f7bda -- the [+0xB99] mute byte the call site
            // forwards at 6BB752. Muted => hint 0x6F7C30 with cx=0x38FF and NO send.
            if (IsNativeChatMuted())
            {
                SysMsg("已经被禁止聊天", MsgColor.Red, MsgType.Hint);
                return true;
            }

            // 6F7B46 mov edi,[ebx+0xAE8] / test edi,edi / je 0x6f7bed -- no corps => SILENT.
            var corpsService = M2Share.CorpsService;
            if (corpsService == null || !corpsService.IsAvailable
                || !corpsService.TryGetPlayerCorps(GetCachedNativeUserId(),
                    out var corps) || corps == null)
            {
                return true;
            }

            // 6F7B54 test esi,esi / je and 6F7B5C test ecx,ecx / jl -- empty/negative body:
            // silent. sData.Length > 2 already guarantees a non-empty body here.
            var body = sData.Substring(2);

            // 6F7B83 lea edx,[ebx+0x106] (name) ... 6F7B94 mov edx,0x6F7C24 (": ")
            // 6F7B9F mov cl,0x10 -- the prefix ShortString is capped to 16 BYTES, then
            // 6F7BC1 call 0x40581c concatenates the body onto it.
            var text = TruncateNativeGbkBytes(m_sCharName + ": ", 16) + body;

            // 705F72..705F8A -- with btFlag == 0 the copied length is min(len, 79).
            text = TruncateNativeGbkBytes(text, NativeCorpsMessageMaxLength);

            BroadcastNativeCorpsMessage(corps, text);
            return true;
        }

        /// <summary>
        /// 战神 <c>sub_705954</c>: walks the corps member list <c>[TCorps+0x30]</c>
        /// (<c>70596A mov eax,[eax+0x30] / 70596D mov esi,[eax+8]</c> = TList.Count), skips
        /// entries whose <c>[+0x28]</c> online-player pointer is nil
        /// (<c>705996 mov edi,[eax+0x28] / 705999 test edi,edi / 70599B je skip</c>) and sends
        /// through that player's <c>vtable+0x250</c> with <c>recog = 0</c>
        /// (<c>705FBE xor ecx,ecx</c>).
        /// </summary>
        private static void BroadcastNativeCorpsMessage(
            Services.NativeCorpsSnapshot corps, string text)
        {
            var userEngine = M2Share.UserEngine;
            if (userEngine == null) return;
            for (var i = 0; i < corps.Members.Count; i++)
            {
                var member = corps.Members[i];
                if (member == null || string.IsNullOrEmpty(member.Name)) continue;
                // GetPlayObject already excludes ghosts, which is the C# analogue of native's
                // "member record has no live player attached" skip.
                var player = userEngine.GetPlayObject(member.Name);
                if (player == null) continue;
                player.SendDefMessage((short)SM_CORPSMESSAGE, 0,
                    NativeCorpsMessageParam, 0, 0, text);
            }
        }

        /// <summary>
        /// 战神 <c>[self+0xB99]</c>: a per-player "chat forbidden" byte threaded into every chat
        /// branch — <c>6BB6BA</c> (whisper), <c>6BB6EB</c> (<c>!!</c> group), <c>6BB719</c>
        /// (<c>!~</c> guild gate), <c>6BB752</c> (<c>!#</c> corps), <c>6BB8FF</c> (normal say).
        /// Its writers are the shutup-list registrar family: <c>621C10 mov byte [eax+0xB99],1</c>
        /// (add), <c>621D88</c> / <c>6220E7 mov byte [...+0xB99],0</c> (remove), and
        /// <c>6B219D mov byte [esi+0xB99],al</c> from <c>sub_621FB8</c> at login — i.e. it is the
        /// cached "am I on the deny-say list" flag, which in C# is
        /// <c>M2Share.g_DenySayMsgList</c> plus the local spam mute.
        /// </summary>
        private bool IsNativeChatMuted()
        {
            return m_boDisableSayMsg
                || NativeMirrorChatBan.Contains(m_sCharName);
        }

        /// <summary>
        /// Delphi ShortString / buffer truncation is by BYTES, so a GBK double-byte character
        /// straddling the cap would be cut in half on the wire. Trim to a whole-character
        /// boundary at or below <paramref name="maxBytes"/>.
        /// </summary>
        private static string TruncateNativeGbkBytes(string value, int maxBytes)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var encoded = HUtil32.GbkEncoding.GetBytes(value);
            if (encoded.Length <= maxBytes) return value;
            for (var length = value.Length - 1; length > 0; length--)
            {
                var candidate = value.Substring(0, length);
                if (HUtil32.GbkEncoding.GetByteCount(candidate) <= maxBytes)
                    return candidate;
            }
            return string.Empty;
        }
    }
}
