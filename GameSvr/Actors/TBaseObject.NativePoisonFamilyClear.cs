namespace GameSvr
{
    public partial class TBaseObject
    {
        // POIS-16 — 连续族清除 state 0x18..0x1F（清 8 个 bodyState 的原生私有助手）。
        //
        // ── 原生字节（flat_image.bin，base 0x400000）─────────────────────────────
        //   sub_76B4DC  逐状态循环，bl 从 0x18 递增到 0x1F（cmp bl,0x20 为排除上界）:
        //     0076B4E1  8BF0            mov esi, eax            ; esi = self
        //     0076B4E3  B318            mov bl, 0x18            ; 循环起点 0x18
        //     0076B4E5  8BD3            mov edx, ebx            ; edx = 状态 id
        //     0076B4E7  8BC6            mov eax, esi
        //     0076B4E9  E8E2FFFFFF      call 0x76B4D0          ; RemoveState 薄封装
        //     0076B4EE  43              inc ebx
        //     0076B4EF  80FB20          cmp bl, 0x20
        //     0076B4F2  75F1            jne 0x76B4E5           ; 直到 0x20 停（含 0x1F）
        //   sub_76B4D0  仅 12 字节，透传到状态权威 RemoveState:
        //     0076B4D3  E8E87C0000      call 0x7731C0          ; = RemoveState(self, id)
        //   RemoveState(0x7731C0) 语义 = C# RemoveTimedAbilityInternal（先 HasState 门、
        //   再走 self+0xDC 链解链、清位集、发到期通知），已在本域 TimedAbility.cs 复刻。
        //
        // ── 唯一调用链（refs：0x76B4DC 仅 1 个 E8 调用者、0 个 VMT/数据引用）───────
        //   0x76B4DC ← 0x674FB8（在 sub_674F58 内）：
        //     0074FAD  cmp dword[ebx+0x564], 4 / jne 跳过      ; 仅当 [self+0x564]==4 才清毒族
        //     0074FB8  call 0x76B4DC
        //   sub_674F58 是「低血特殊」：进入前于 sub_675678 内被门控——
        //     067573E  add eax,eax / cmp eax,[ebx+0x2B0] / jge ; HP*2 < MaxHP（血量<50%）
        //     0675748  sub edi,[ebx+0x574] / cmp 0x7530 / jbe  ; 距上次 >30000ms(30s)
        //     0675759  call 0x674F58
        //   sub_675678 = VMT 槽 34（+0x88 之上的特殊 think）；该指针只出现在两张 VMT：
        //     0x66EBD0 = TKingOfIceMon（冰霜之王，race 146，instSize 1424）
        //     0x66EE98 = TKingOfIceMonBB（冰王 BB，race 154，←TKingOfIceMon，instSize 1432）
        //   即：毒族清除是「冰霜之王」低血召唤特殊技的独占分支，不是通用解毒。
        //
        // ── 处置：fail-closed（跳过，不复刻）──────────────────────────────────────
        //   TKingOfIceMon / TKingOfIceMonBB 的父类 TSearchMon（VMT 0x66D320，较 TAnimal
        //   多 slot131-142 共 12 个新虚槽）在 C# 未移植，故 race 146/154 依既定策略
        //   fail-closed，从不构造（见 Monster/RaceFactory_High.cs §B）。slot-34 特殊技
        //   在 C# 没有可覆写入口，本清除助手也没有任何其它调用点。
        //   若此处凭空补出 ClearPoison(0x18..0x1F) 而无调用方，即制造死代码——恰是
        //   fail-closed「宁缺毋滥 / 有据不臆造」所禁止的新背离。
        //   ⇒ 待 TSearchMon→TKingOfIceMon 整条类链移植后，再在 slot-34 特殊技里
        //     连同调用点一并补齐（届时清除语义直接用 RemoveTimedAbilityInternal 循环
        //     state 0x18..0x1F 即可 1:1 对上 sub_76B4DC）。
    }
}
