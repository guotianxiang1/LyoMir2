using System.IO;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private const int NativeCm4629RecordSize = 0x18;
        private const int NativeCm4629NameCapacity = 15;

        private void ClientNativeCm4629GroupPositions()
        {
            // 0x6F7C8D E8 16 B1 07 00 call 0x772DA8  -> al = [eax+0x74] (death)
            // 0x6F7C9A 80 7F 73 00     cmp byte [edi+0x73], 0  ; ghost
            // either nonzero -> [ebp-4] = -1, no send (0x6F7DE3 / 0x6F7E65 jl)
            if (m_boGhost || m_boDeath)
                return;

            // 0x6F7CA4 8B 87 80 0A 00 00 mov eax,[edi+0xA80]  group object
            // 0x6F7CAA 85 C0 / 0F 84 28 01 00 00 je -> [ebp-4] = -2, no send
            // C# maps [+0xA80] to m_GroupOwner (NativeFlyScriptApiLadders).
            var leader = m_GroupOwner as TPlayObject;
            var members = leader?.m_GroupMembers;
            if (leader == null || members == null)
                return;

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            var count = 0;
            for (var i = 0; i < members.Count; i++)
            {
                var member = members[i];
                if (member == null)
                    continue;
                // Same three member filters as the loop at 0x6F7CC4:
                // death 0x772DA8 [+0x74], ghost [+0x73], different Envir [+0x128], same
                // 64-bit id [+0x588/+0x58C] as the caller (self).
                if (member.m_boGhost || member.m_boDeath)
                    continue;
                if (member.m_PEnvir != m_PEnvir)
                    continue;
                if (ReferenceEquals(member, this))
                    continue;
                WriteNativeCm4629Record(writer, member);
                count++;
            }

            var body = stream.ToArray();
            // 0x6F7E6B 66 8B 45 F8  mov ax,[ebp-8]  push count -> Series
            // 0x6F7E70 6A 00 / 6A 00
            // 0x6F7E74 56          push esi (buf)
            // 0x6F7E75..7E        push count*0x18 (len)
            // 0x6F7E7F 33 C9       xor ecx,ecx (Recog = 0)
            // 0x6F7E81 66 BA 15 12 mov dx,0x1215
            // 0x6F7E89 FF 93 54 02 00 00 call [ebx+0x254]
            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_4629, 0, 0, 0, count);
            SendSocket(m_DefMsg, body);
        }

        private static void WriteNativeCm4629Record(BinaryWriter writer,
            TPlayObject member)
        {
            // 0x6F7D65 lea edx,[ebp-0x118] / call 0x76C4C0
            //   0x76C4CD 8D 93 06 01 00 00 lea edx,[ebx+0x106]  char name
            //   0x76C4D1 E8 9E 92 C9 FF    call 0x405774        LStrFromString
            // 0x6F7D8F B1 0F / E8 4E BC D0 FF call 0x4039E4     ShortString cap 15
            // 0x6F7DA8 89 42 10 mov [edx+0x10], CurrX [+0x12C]
            // 0x6F7DBD 89 42 14 mov [edx+0x14], CurrY [+0x130]
            var bytes = HUtil32.GbkEncoding.GetBytes(member.m_sCharName ?? "");
            if (bytes.Length > NativeCm4629NameCapacity)
                System.Array.Resize(ref bytes, NativeCm4629NameCapacity);
            writer.Write((byte)bytes.Length);
            writer.Write(bytes);
            if (bytes.Length < NativeCm4629NameCapacity)
                writer.Write(new byte[NativeCm4629NameCapacity - bytes.Length]);
            writer.Write((int)member.m_nCurrX);
            writer.Write((int)member.m_nCurrY);
        }
    }
}
