using System;
using SystemModule;

namespace GameSvr
{
    // MOVE-89 / TRIGGERBOMB consumer.
    //
    // ── Identity ────────────────────────────────────────────────────────────────
    // Native class **TTimerBomb** (classname ShortString @0x781354, classref 0x781304,
    // InstanceSize 0x100, parent "ObjItem"). This is the *item-function* class for
    // NativeItemFactory StdMode=3 / Shape=32 ("TTimerBomb" -- NativeItemFactory.cs:101),
    // NOT the race-159 monster TimerBombMon (classref 0x6801CC, size 0x4E0). The two are
    // related only by the spawned name below.
    //
    // The use handler is the class VMT slot **+0x18** (ptr @0x78131C = 0x789694 =
    // TTimerBomb.UseNormalCrystal), exactly the slot every other ported item-function
    // class uses (cf. TFixedCoordStone/TDoubleExpProp/TColorSayProp in Operate.cs).
    // 0x789694 body calls the inner worker **sub_7896FC** (the sole caller, at 0x7896BD).
    //
    // ── OUTER  TTimerBomb.UseNormalCrystal  0x789694 ────────────────────────────
    //   0x7896A0  mov eax,esi / mov edx,[0x6AC87C] / call 0x404828   ; (Obj **is TPlayer**)
    //             [0x6AC87C]->classref 0x6AC8C8, classname "TPlayer" (@0x6ACC72); 0x404828 = @IsClass
    //   0x7896B1  cmp word[edi+0x26],0x3E8 / jb  ->fail              ; item.Dura >= 1000
    //   0x7896BD  call sub_7896FC / test al / je ->fail              ; inner spawn
    //   0x7896C6  sub word[edi+0x26],0x3E8                           ; item.Dura -= 1000
    //   0x7896CC  cmp word[edi+0x26],0x3E8 / setb bl                 ; bl = (remaining Dura < 1000)
    //   0x7896DA  mov ax,[edi+0x28];push / push 0 / cx=[edi+0x26];
    //             edx=[edi+0x18]; call [Obj.vtable+0x260]            ; SendUpdateItem  (VtblSendUpdateItem=0x260)
    //   0x7896F2  mov eax,ebx / ret 4                                ; RESULT = bl = "crystal now exhausted"
    //   Self field map (item-function object built by the use dispatcher from the bag item):
    //     +0x18 = TUserItem (item)   +0x1C = StdItem (stdItem)
    //     +0x26 = Dura (word)        +0x28 = DuraMax (word)
    //
    // ── INNER  sub_7896FC  (Self=item, Obj=this player) ─────────────────────────
    //   0x789723  test ebx / je      ; Obj<>nil
    //   0x78972B  cmp byte[ebx+0x178],0 / jne ->fail                 ; m_btRaceServer == 0 (player)
    //   0x78973A  x=[esi+0x12C] y=[esi+0x130] env=[esi+0x128]        ; CurrX / CurrY / PEnvir
    //   0x789752  cmp byte[eax+0x83],0 / je 0x7897CE                 ; **boTRIGGERBOMB** map gate  <-- MOVE-89 test point
    //   0x7897CE  (flag clear) cx=0xFCFF / edx=0x78982C / call [vtable+0xD4]
    //             = SysMsg "在这里无法使用！" colour 0xFCFF(Blue)     ; string @0x78982C len16
    //   0x78975B  call 0x408340 (GetTickCount)
    //   0x789764  eax = abs(tick - [esi+0x3B4]) ; cmp 0x12C / jl     ; 300ms per-player latch (obj+0x3B4)
    //   0x789776  [esi+0x3B4] = tick                                 ; latch store
    //   0x789790  eax = word[[edi+0x1C]+0x24] (StdItem.Ac)           ; std ability words: Ac@0x24/Mac@0x28/Sc@0x34
    //   0x789794  call 0x40C89C (IntToStr)  -> str14 = IntToStr(Ac)
    //   0x7897A4  call 0x40581C (_LStrCat3) -> name = "朱火弹(幻)"(@0x789818) + str14
    //   0x7897B9  eax=[[0x7D5D9C]] (UserEngine) edx=env ecx=name; call 0x67BDCC
    //             = TUserEngine generate-by-name at (x,y), count 1 (inner factory sub_679F8C,
    //               the same race factory TimerBombMon documents) == RegenMonsterByName.
    //   0x7897C2  mov [newobj+0x4DC],esi                             ; newmon.m_Master = Obj (owner)
    //   0x7897C8  result = true
    //
    // ── Faithfulness / fail-closed ──────────────────────────────────────────────
    // Every gate, the 300ms latch, the Dura cost, both user-visible messages, and the
    // spawn (name + owner) are grounded above, so nothing here is fail-closed. The
    // spawn uses the audited RegenMonsterByName (0x67BDCC's inner factory is sub_679F8C);
    // an unknown DB name makes RegenMonsterByName return null -> we return false, matching
    // the native null-template skip. m_dwTriggerBombTick mirrors the native per-object
    // dword at +0x3B4 (0 at construction, so the first use always passes the latch).
    public partial class TPlayObject
    {
        // Native per-object latch dword at obj+0x3B4 (300ms throttle for TTimerBomb use).
        private int m_dwTriggerBombTick;

        // TTimerBomb.UseNormalCrystal (VMT 0x781304 slot +0x18 = 0x789694).
        private bool UseNativeTimerBomb(GoodItem stdItem, TUserItem item)
        {
            // 0x7896A0: (Obj is TPlayer). The is-check target class "TPlayer" is race 0
            // (a human PlayObject); represented by m_btRaceServer == 0. In this dispatch
            // path `this` is always the human master, so the guard never rejects a real use.
            if (m_btRaceServer != 0)
                return false;
            // 0x7896B1: cmp word[Self+0x26],0x3E8 / jb -> fail.
            if (item.Dura < 1000)
                return false;
            // 0x7896BD: call sub_7896FC / test al / je -> fail.
            if (!TriggerBombInnerSpawn(stdItem))
                return false;
            // 0x7896C6: sub word[Self+0x26],0x3E8.
            item.Dura -= 1000;
            // 0x7896DA-EC: Obj.vtable[0x260] = SendUpdateItem(item) (dura already updated).
            SendUpdateItem(item);
            // 0x7896CC/0x7896F2: RESULT = bl = (remaining Dura < 1000) => remove whole item
            // only once the crystal is exhausted; otherwise the dispatcher keeps the item
            // (SM_EAT_FAIL cancels the client's optimistic removal) after the SendUpdateItem above.
            return item.Dura < 1000;
        }

        // sub_7896FC (Self=item-function object, Obj=this player).
        private bool TriggerBombInnerSpawn(GoodItem stdItem)
        {
            // 0x78972B: cmp byte[Obj+0x178],0 / jne -> fail  (m_btRaceServer, 0 = player).
            if (m_btRaceServer != 0)
                return false;
            // 0x78974C: env = Obj.PEnvir ([Obj+0x128]).
            var env = m_PEnvir;
            if (env == null)
                return false;
            // 0x789752: cmp byte[env+0x83],0 / je -> "cannot use here"  (boTRIGGERBOMB gate).
            if (!env.Flag.boTRIGGERBOMB)
            {
                // 0x7897CE: SysMsg cx=0xFCFF (Blue) msg @0x78982C.
                SysMsg("在这里无法使用！", MsgColor.Blue, MsgType.Hint);
                return false;
            }
            // 0x78975B-0x789774: 300ms latch on obj+0x3B4 with abs(tick-last) < 0x12C.
            int tick = HUtil32.GetTickCount();
            if (Math.Abs(tick - m_dwTriggerBombTick) < 0x12C)
                return false;
            // 0x789776: latch store.
            m_dwTriggerBombTick = tick;
            // 0x789790-0x7897A4: name = "朱火弹(幻)" + IntToStr(word[StdItem+0x24] = Ac).
            string sMonName = "朱火弹(幻)" + stdItem.Ac;
            // 0x7897B9: TUserEngine generate-by-name at (CurrX,CurrY), count 1.
            var monObject = M2Share.UserEngine.RegenMonsterByName(env, m_nCurrX, m_nCurrY, sMonName);
            if (monObject == null)
                return false;
            // 0x7897C2: newmon.m_Master = Obj ([newobj+0x4DC] = esi).
            monObject.m_Master = this;
            return true;
        }
    }
}
