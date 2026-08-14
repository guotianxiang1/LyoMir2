using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        /// <summary>
        /// CM 1252 / 1253 / 1256 / 1257 — the four read-only list views of the 元宝寄售
        /// subsystem. See NativeYbConsignmentQuery for the SQL, the table DDL and the wire
        /// record layout; this file is only the gate ladder the manager methods run.
        ///
        /// The ladder is the same in all four (0x632A14 / 0x632E7C / 0x632BEC / 0x632D34 differ
        /// only in the throttle slot, the throttle comparison, the row cap and the two queries):
        ///
        ///   0x632A35  A1 50 6D 7D 00 / 8B 00 / E8 43 FD 01 00
        ///                 eax = [[0x7D6D50]] ; call 0x652784   ; UserEngine.GetPlayObject(name)
        ///   0x632A43  85 DB / 0F 84 D8 00 00 00   test ebx,ebx / je   ; not online -> silent
        ///   0x632A4F  E8 FC FB FF FF             call 0x632650        ; map gate
        ///   0x632A54  84 C0 / 0F 84 C7 00 00 00  test al,al / je      ; wrong map -> silent
        ///   0x632A5C  E8 DF 58 DD FF             call 0x408340        ; GetTickCount
        ///   0x632A63  2B 56 20                   sub edx,[esi+0x20]
        ///   0x632A66  83 FA 0A / 0F 86 B4 00 00 00  cmp edx,0x0A / jbe ; throttle
        ///   0x632A6F  89 46 20                   mov [esi+0x20],eax   ; only on the passing arm
        ///
        /// Two details that are easy to get wrong:
        ///  * ESI is the MANAGER at [[0x7D6ABC]], a singleton, so +0x20 and +0x24 are SERVER-WIDE
        ///    throttle slots shared by every player — not per-character cooldowns.
        ///  * the reply goes to the object GetPlayObject returned, not to the packet's sender.
        ///    Normally the same object; kept faithful because a duplicate-name lookup would
        ///    otherwise answer the wrong session.
        ///
        /// NOT MODELLED, and deliberately so: the two pending selectors also run a per-record
        /// hook inside the serialisation loop — 0x6E8288 `2D 7A 04 00 00 sub eax,0x47A / 74 05 je`
        /// reaches 0x6E829D `call 0x6E7FE8` for 0x47A and 0x6E8293 `48 dec eax / 74 0F je`
        /// reaches 0x6E82AA `call 0x6E7F7C` for 0x47B, both taking the wire record in EDX. They
        /// repopulate the per-player linked list at player+0xA0C that 0x6E7F30 / 0x6E7EE4 tore
        /// down before the loop. C# has no model of that list at all (its only consumer would be
        /// the CM 1254 accept path, and NativeYbDealPurchaseStateMachine is host-driven and
        /// dormant), so modelling half of it would create a cache nothing ever reads. Both the
        /// teardown and the refill are therefore absent together, which keeps the wire output —
        /// the only thing a client can observe — exact.
        /// </summary>
        private void ClientYbConsignmentQuery(int cmIdent)
        {
            if (!NativeYbConsignmentQuery.TryGetDescriptor(cmIdent, out var descriptor)) return;

            var target = M2Share.UserEngine?.GetPlayObject(m_sCharName);
            if (target == null) return;
            if (!NativeYbConsignmentQuery.MapAllowsConsignmentQuery(target.m_sMapName)) return;

            var tick = HUtil32.GetTickCount();
            if (!NativeYbConsignmentQuery.TryPassThrottle(descriptor, tick)) return;

            var name = target.m_sCharName;
            var wanted = NativeYbConsignmentQuery.Store.Count(cmIdent, name);
            if (wanted > descriptor.Cap) wanted = descriptor.Cap;

            var page = descriptor.SkipsPageWhenEmpty && wanted <= 0
                ? null
                : NativeYbConsignmentQuery.Store.Page(cmIdent, name, wanted);
            // 0x632AF6 `test eax,eax / jle` — a negative fetch result is written back as 0.
            var count = page == null || page.Count <= 0 ? 0 : page.Count;

            var body = count == 0
                ? System.Array.Empty<byte>()
                : NativeYbConsignmentQuery.BuildReplyBody(page);

            NativeYbConsignmentQuery.EmitQueryDebugLog(this, cmIdent, wanted, 0, count);
            if (page != null)
            {
                foreach (var row in page)
                {
                    NativeYbConsignmentQuery.EmitRowDebugLog(this, row, name,
                        row.CounterpartyName);
                }
            }

            // 0x6E82BB..0x6E82D7: [vtbl+0x254](self, dx = SM ident, ecx = 0 (Recog),
            // push count (Param), push 0 (Tag), push 0 (Series), push buffer, push length).
            target.SendSocket(
                Grobal2.MakeDefaultMsg(descriptor.SmIdent, 0, count, 0, 0), body);
        }
    }
}
