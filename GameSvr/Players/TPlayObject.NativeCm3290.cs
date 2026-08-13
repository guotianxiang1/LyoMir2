using System;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        // Native 0x792BEC (engine object ctor) calls SysUtils.Now @ 0x40F0A4,
        // fstp qword [off_7D6A88], then copies those 8 bytes into [off_7D70E4].
        // CODE xrefs of 0x7D70E4: the copy at 0x792C45 (only writer) and the
        // CM 3290 reader at 0x6DA354. The snapshot is therefore process-lifetime,
        // not re-sampled on each request.
        private static readonly byte[] NativeCm3290ClockBytes =
            BitConverter.GetBytes(DateTime.Now.ToOADate());

        private void ClientNativeCm3290ClockSnapshot()
        {
            // 0x6DA34E  6A 00 / 6A 00 / 6A 00     push 0 x3  (Param/Tag/Series)
            // 0x6DA354  A1 E4 70 7D 00            mov eax,[0x7D70E4]
            // 0x6DA359  50                        push eax   (Buf)
            // 0x6DA35A  6A 08                     push 8     (Len)
            // 0x6DA35C  33 C9                     xor ecx,ecx (Recog = 0)
            // 0x6DA35E  66 BA D9 0C               mov dx,0xCD9 (SM 3289)
            // 0x6DA367  FF 93 54 02 00 00         call [ebx+0x254]
            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_3289, 0, 0, 0, 0);
            SendSocket(m_DefMsg, NativeCm3290ClockBytes);
        }
    }
}
