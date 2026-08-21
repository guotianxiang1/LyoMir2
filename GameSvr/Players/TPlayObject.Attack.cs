using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private bool ClientHitXY(int wIdent, int nX, int nY, byte nDir, bool boLateDelivery, ref int dwDelayTime)
        {
            var result = false;
            short n14 = 0;
            short n18 = 0;
            const string sExceptionMsg = "[Exception] TPlayObject::ClientHitXY";
            dwDelayTime = 0;
            try
            {
                if (!m_boCanHit)
                {
                    return result;
                }
                if (m_boDeath || m_wStatusTimeArr[Grobal2.POISON_STONE] != 0 && !M2Share.g_Config.ClientConf.boParalyCanHit)// 防麻
                {
                    return result;
                }
                if (wIdent == Grobal2.CM_SWORD_HIT &&
                    (!m_boSunSwordReady || m_MagicArr[SpellsDef.SKILL_58] == null))
                {
                    return result;
                }
                if (!M2Share.g_Config.boSpeedHackCheck)
                {
                    if (!boLateDelivery)
                    {
                        if (!CheckActionStatus(wIdent, ref dwDelayTime))
                        {
                            m_boFilterAction = false;
                            return result;
                        }
                        m_boFilterAction = true;
                        int dwAttackTime = HUtil32._MAX(0, M2Share.g_Config.dwHitIntervalTime - m_nHitSpeed * M2Share.g_Config.ClientConf.btItemSpeed);
                        int dwCheckTime = HUtil32.GetTickCount() - m_dwAttackTick;
                        if (dwCheckTime < dwAttackTime)
                        {
                            m_dwAttackCount++;
                            dwDelayTime = dwAttackTime - dwCheckTime;
                            if (dwDelayTime > M2Share.g_Config.dwDropOverSpeed)
                            {
                                if (m_dwAttackCount >= 4)
                                {
                                    m_dwAttackTick = HUtil32.GetTickCount();
                                    m_dwAttackCount = 0;
                                    dwDelayTime = M2Share.g_Config.dwDropOverSpeed;
                                    if (m_boTestSpeedMode)
                                    {
                                        SysMsg("攻击攻击忙复位忙复位!!!" + dwDelayTime, MsgColor.Red, MsgType.Hint);
                                    }
                                }
                                else
                                {
                                    m_dwAttackCount = 0;
                                }
                                return result;
                            }
                            else
                            {
                                if (m_boTestSpeedMode)
                                {
                                    SysMsg("攻击步忙!!!" + dwDelayTime, MsgColor.Red, MsgType.Hint);
                                }
                                return result;
                            }
                        }

                    }
                }
                // MINE-07 / MINE-53: 区域限制门是 ClientHitXY 的第一条语句，对
                // **所有** hit ident 生效，查的是**玩家自己站的格**，失败时弹绿字：
                //   0x6EC08B  33 D2              xor edx,edx      ; 技能键 = 0
                //   0x6EC08D  8B C3              mov eax,ebx
                //   0x6EC08F  E8 BC 69 08 00     call 0x772A50
                //     0x772A5C  8B 8E 30 01 00 00  mov ecx,[esi+0x130] ; ★ 自己的 Y
                //     0x772A62  8B 96 2C 01 00 00  mov edx,[esi+0x12C] ; ★ 自己的 X
                //     0x772A6E  E8 15 94 00 00     call 0x77BE88       ; 读 cell+4
                //     0x772A83  E8 6C 92 00 00     call 0x77BCF4       ; LimitSkill 表含 0？
                //   0x6EC094  84 C0              test al,al
                //   0x6EC096  75 1C              jne 0x6EC0B4
                //   0x6EC098  66 B9 DB FF        mov cx,0xFFDB          ; 绿
                //   0x6EC09C  BA 7C C3 6E 00     mov edx,0x6EC37C       ; len 20
                //   0x6EC0A5  FF 93 D4 00 00 00  call [vmt+0xD4]        ; SysMsg
                //   0x6EC0AB  C6 45 FF 00        mov byte [ebp-1],0     ; 返回 false
                // 0x6EC37C 的 Delphi 长串前缀 len=20，字节
                //   B5 B1 C7 B0 C7 F8 D3 F2 B2 BB BF C9 CA B9 D3 C3 BC BC C4 DC
                // 按 GBK 解正是「当前区域不可使用技能」。
                // 原来 C# 查的是**目标格** (n14,n18) 且只在挖矿分支里、失败静默，
                // 方向正好反了：站在受限格上照挖，站在正常格朝受限格挖反被拒。
                // m_boCanHit / 死亡麻痹 / 测速这些前置在原版属于调用方
                // (0x6D9EBC 派发器)，所以门排在它们之后、坐标校验之前。
                if (!m_PEnvir.IsSkillAllowedAt(m_nCurrX, m_nCurrY, 0))
                {
                    SysMsg("当前区域不可使用技能", MsgColor.Green, MsgType.Hint);
                    return result;
                }
                if (nX == m_nCurrX && nY == m_nCurrY)
                {
                    result = true;
                    m_dwAttackTick = HUtil32.GetTickCount();
                    // 挖矿触发 @OnDig：眼神 trampoline 挂在 0x6EC111（挖矿臂的 Dura 门
                    // `cmp word[item+0x26],0`）之上，桩体先派发 @OnDig 再重放该门。故原生发射
                    // 条件 = CM_HEAVYHIT + boMINE + 武器非空 + StdItem.Shape==19，且**早于** Dura 门、
                    // GetFrontPosition 与地形门（哪怕耐久为 0 或身前无墙也照发一次）。惰性门 Armed
                    // 打头，插件缺席时不取 StdItem、主干零影响。见 YanshenTriggerDispatch。
                    if (GameSvr.Plugins.YanshenTriggerDispatch.Armed
                        && wIdent == Grobal2.CM_HEAVYHIT && m_PEnvir.Flag.boMINE
                        && m_UseItems[Grobal2.U_WEAPON] != null)
                    {
                        GoodItem digStd = M2Share.UserEngine.GetStdItem(m_UseItems[Grobal2.U_WEAPON].wIndex);
                        if (digStd != null && digStd.Shape == 19)
                        {
                            GameSvr.Plugins.YanshenTriggerDispatch.FireOnDig(this);
                        }
                    }
                    // MINE-08: 原版在派发器里测 MINE 旗标，位置在一切之前——
                    // 紧跟 ident 判断之后、取工具之前：
                    //   0x6EC0F1  66 81 FF C7 0B     cmp di,0xBC7        ; CM_HEAVYHIT
                    //   0x6EC0F6  75 62              jne 0x6EC15A
                    //   0x6EC0F8  8B 83 28 01 00 00  mov eax,[ebx+0x128] ; map
                    //   0x6EC0FE  80 78 6A 00        cmp byte [eax+0x6A],0
                    //   0x6EC102  74 56              je  0x6EC15A        ; ★ 落回跳表
                    //   0x6EC104  85 F6              test esi,esi        ; 武器非空
                    //   0x6EC10B  80 78 15 13        cmp byte [std+0x15],0x13
                    //   0x6EC111  66 83 7E 26 00     cmp word [item+0x26],0
                    // 0x6EC15A 是普通 ident 跳表，3015 在表里落 slot3 = 普攻。
                    // 所以非 MINE 图上手持 shape-19 镐子的重击**降级为普攻**，
                    // 且不进 DigXY、不扣矿点次数、不抽任何签。
                    if (wIdent == Grobal2.CM_HEAVYHIT && m_PEnvir.Flag.boMINE && m_UseItems[Grobal2.U_WEAPON] != null && m_UseItems[Grobal2.U_WEAPON].Dura > 0)// 挖矿
                    {
                        if (GetFrontPosition(ref n14, ref n18))
                        {
                            GoodItem StdItem = M2Share.UserEngine.GetStdItem(m_UseItems[Grobal2.U_WEAPON].wIndex);
                            if (StdItem != null && StdItem.Shape == 19)
                            {
                                // MINE-06: native ClientHitXY @0x6EC131 call 0x6BC1EC DISCARDS
                                // the return; 0x6EC136-0x6EC155 unconditionally deducts 30/50 tick
                                // and jmp 0x6EC366 — no CanWalk pre-filter here. Terrain gate
                                // lives inside PileStones/DigXY @0x6BC24A only.
                                PileStones(n14, n18);
                                m_nHealthTick -= 30;
                                m_nSpellTick -= 50;
                                m_nSpellTick = HUtil32._MAX(0, m_nSpellTick);
                                DecreaseHealthSpellRecoveryStep(2);
                                return result;
                            }
                        }
                    }
                    switch (wIdent)
                    {
                        case Grobal2.CM_HIT:
                            // Native sub_6EC078 @0x6EC1E2 dispatches job 3
                            // through action 0x400; every other job keeps the
                            // ordinary action-1000 arm @0x6EC200.
                            if (m_btJob == 3)
                            {
                                m_btDirection = nDir;
                                RunNativeAction1024();
                            }
                            else
                            {
                                AttackDir(null, 0, nDir);
                            }
                            break;
                        case Grobal2.CM_HEAVYHIT:
                            AttackDir(null, 1, nDir);
                            break;
                        case Grobal2.CM_BIGHIT:
                            AttackDir(null, 2, nDir);
                            break;
                        case Grobal2.CM_POWERHIT:
                            AttackDir(null, 3, nDir);
                            break;
                        case Grobal2.CM_LONGHIT:
                            AttackDir(null, 4, nDir);
                            break;
                        case Grobal2.CM_WIDEHIT:
                            AttackDir(null, 5, nDir);
                            break;
                        case Grobal2.CM_FIREHIT:
                            AttackDir(null, 7, nDir);
                            break;
                        case Grobal2.CM_CRSHIT:
                            // 3026 reaches sub_7707A8 with action code 1018
                            // (0x6EC178[24] = 0x0A -> slot 0x6EC2B1 -> `mov cx,0x3FA`),
                            // whose arm 0x77092A ends in `call 0x771BB8` — and 0x771BB8
                            // is an empty stub: `55 8B EC 33 C0 5D C2 04 00`
                            // (push ebp; mov ebp,esp; xor eax,eax; pop ebp; ret 4).
                            // The only lasting effect is 0x7707E3
                            // `88 86 54 01 00 00  mov [esi+0x154],al`, i.e. the facing
                            // update ([+0x154] is m_btDirection: 0x771BE6 feeds it to
                            // GetNextPosition alongside [+0x12C]/[+0x130] CurrX/CurrY).
                            // No attack of any kind is performed.
                            m_btDirection = nDir;
                            break;
                        case Grobal2.CM_HORSERUN:
                            // ID3035 — 3035 is an attack ident, not a mount run.
                            // The dispatcher sends it to HIT CASE1 alongside the
                            // other ten hit opcodes:
                            //   0x6D85F0  2D D4 0B 00 00     sub eax,0xBD4   ; 3028
                            //   0x6D85FB  83 E8 02           sub eax,2       ; 3030
                            //   0x6D8604  83 E8 02           sub eax,2       ; 3032
                            //   0x6D860D  83 E8 03           sub eax,3       ; 3035
                            //   0x6D8610  0F 84 99 18 00 00  je 0x6D9EAF
                            // and sub_6EC078's own window ends exactly on it
                            // (0x6EC15D `add eax,-0xBBA` / 0x6EC162 `cmp eax,0x21`
                            // = 3002..3035), byte table 0x6EC178[33] = 0x09,
                            // slot 0x6EC19A[9] = 0x6EC29C:
                            //   0x6EC29C  33 C0 / 8A 45 08 / 50   ; push direction
                            //   0x6EC2A2  66 B9 F9 03             ; mov cx,0x3F9 = 1017
                            //   0x6EC2AA  E8 F9 44 08 00          ; call 0x7707A8
                            // The native mount run is CM_RUN3 (4108) at 0x6D9D99,
                            // which opens with `B2 33 mov dl,0x33` / `call 0x772960`
                            // and refuses with 0x276 when the rider is not mounted.
                            //
                            // The facing update is the one unconditional effect of
                            // sub_7707A8 for every code in the 1000..1033 window
                            // (0x7707E3 `88 86 54 01 00 00 mov [esi+0x154],al`,
                            // [+0x154] = m_btDirection), and it runs BEFORE the
                            // target lookup, which reads that same byte.
                            m_btDirection = nDir;
                            // ACT1017: the swing half. Arm 0x770ABF ends in
                            // `E8 90 18 00 00 call 0x772388`, a worker whose only
                            // rel32 caller in the image is that instruction. It
                            // takes the target sub_7707A8's prologue already
                            // resolved (0x7707F6 `call 0x767E80` = GetPoseCreate),
                            // falls back to a two-cell probe along the facing
                            // (0x7723A5 `6A 02` into GetNextPosition 0x778BE8),
                            // computes damage through VMT+0x4C = 0x744388, applies
                            // it via 0x76E268 with the action code 0x3F9, sends
                            // ident 0x2740 to the victim and trains the cached
                            // 65..68 record by Random(3)+1 through VMT+0x3C.
                            // Transcribed in TBaseObject.NativeAction1017.cs.
                            //
                            // 1018 (CM_CRSHIT) stays facing-only above because its
                            // worker 0x771BB8 really is the empty stub.
                            RunNativeAction1017();
                            break;
                        // CM_TWINHIT (3028) has no arm on purpose: 0x6EC178[26] = 0x0B
                        // selects jump-table slot 11 = 0x6EC2D7, which is the tail of
                        // sub_6EC078 itself, so 3028 never even reaches sub_7707A8 and
                        // does not update the facing either. It still runs the shared
                        // position check, the SKILL_YEDO counter and the health/spell
                        // tick block below, and still answers SM_ACT_GOOD.
                        //
                        // CM_42HIT (42) is gone from here as well. sub_6EC078 selects the
                        // action with `0F B7 C7 movzx eax,di` / `05 46 F4 FF FF add
                        // eax,-0xBBA` / `83 F8 21 cmp eax,0x21` / `0F 87 .. ja 0x6EC2C6`
                        // at 0x6EC15A, so 42 underflows the 3002..3035 window and lands on
                        // the default arm, which forwards 42 unchanged to sub_7707A8 -
                        // where `05 18 FC FF FF add eax,-0x3E8` / `83 F8 21 cmp eax,0x21` /
                        // `0F 87 AF 04 00 00 ja 0x770CC4` at 0x770803 rejects it again.
                        // Native performs no swing for 42 by either route, and hit modes
                        // 10/11 are not reachable from any client opcode.
                        case NativeAction1011Code:
                        case NativeAction1012Code:
                            RunNativeCrossMoonAction(wIdent, nDir);
                            break;
                        case Grobal2.CM_SWORD_HIT:
                            if (!ReleaseSunSword(nDir))
                            {
                                return false;
                            }
                            break;
                    }
                    if (m_MagicArr[SpellsDef.SKILL_YEDO] != null &&
                        m_UseItems[Grobal2.U_WEAPON] != null &&
                        m_UseItems[Grobal2.U_WEAPON].Dura > 0)
                    {
                        m_btAttackSkillCount -= 1;
                        if (m_btAttackSkillPointCount == m_btAttackSkillCount)
                        {
                            m_boPowerHit = true;
                            // 0x6EC2F8 mov byte [ebx+0x93],1 / 6A 00 x4 / 33 C9 /
                            // 66 BA 73 02 mov dx,0x273 / FF 93 50 02 00 00.
                            // "+PWR" appears zero times in the native image.
                            SendSocket(Grobal2.MakeDefaultMsg(
                                Grobal2.SM_POWERHITSKILL, 0, 0, 0, 0));
                        }
                        if (m_btAttackSkillCount <= 0)
                        {
                            m_btAttackSkillCount = (byte)(7 - m_MagicArr[SpellsDef.SKILL_YEDO].btLevel);
                            m_btAttackSkillPointCount = (byte)M2Share.RandomNumber.Random(m_btAttackSkillCount);
                        }
                    }
                    m_nHealthTick -= 30;
                    m_nSpellTick -= 100;
                    m_nSpellTick = HUtil32._MAX(0, m_nSpellTick);
                    DecreaseHealthSpellRecoveryStep(2);
                }
            }
            catch (Exception e)
            {
                M2Share.ErrorMessage(sExceptionMsg);
                M2Share.ErrorMessage(e.Message);
            }
            return result;
        }

        private bool ClientHorseRunXY(short wIdent, int nX, int nY, bool boLateDelivery, ref int dwDelayTime)
        {
            var result = false;
            byte n14;
            int dwCheckTime;
            dwDelayTime = 0;
            if (!m_boCanRun)
            {
                return result;
            }
            // MOVE-15 — C# extension (no native opcode). Cast lock must block
            // horse run just like it blocks walk/run/turn/pose on foot.
            if (IsNativeCanActBlockedByForcedMove())
            {
                return result;
            }
            if (m_boDeath || m_wStatusTimeArr[Grobal2.POISON_STONE] != 0 && !M2Share.g_Config.ClientConf.boParalyCanRun)// 防麻
            {
                return result;
            }
            if (!M2Share.g_Config.boSpeedHackCheck)
            {
                if (!boLateDelivery)
                {
                    if (!CheckActionStatus(wIdent, ref dwDelayTime))
                    {
                        m_boFilterAction = false;
                        return result;
                    }
                    m_boFilterAction = true;
                    dwCheckTime = HUtil32.GetTickCount() - m_dwMoveTick;
                    if (dwCheckTime < M2Share.g_Config.dwRunIntervalTime)
                    {
                        m_dwMoveCount++;
                        dwDelayTime = M2Share.g_Config.dwRunIntervalTime - dwCheckTime;
                        if (dwDelayTime > M2Share.g_Config.dwDropOverSpeed)
                        {
                            if (m_dwMoveCount >= 4)
                            {
                                m_dwMoveTick = HUtil32.GetTickCount();
                                m_dwMoveCount = 0;
                                dwDelayTime = M2Share.g_Config.dwDropOverSpeed;
                                if (m_boTestSpeedMode)
                                {
                                    SysMsg("马跑步忙复位!!!" + dwDelayTime, MsgColor.Red, MsgType.Hint);
                                }
                            }
                            else
                            {
                                m_dwMoveCount = 0;
                            }
                            return result;
                        }
                        else
                        {
                            if (m_boTestSpeedMode)
                            {
                                SysMsg("马跑步忙!!!" + dwDelayTime, MsgColor.Red, MsgType.Hint);
                            }
                            return result;
                        }
                    }
                }
            }
            m_dwMoveTick = HUtil32.GetTickCount();
            m_bo316 = false;
#if Debug
            Debug.WriteLine(format("当前X:{0} 当前Y:{1} 目标X:{2} 目标Y:{3}", new object[] {this.m_nCurrX, this.m_nCurrY, nX, nY}), TMsgColor.c_Green, TMsgType.t_Hint);
#endif
            n14 = M2Share.GetNextDirection(m_nCurrX, m_nCurrY, nX, nY);
            if (HorseRunTo(n14, false))
            {
                if (m_boTransparent && m_boHideMode)
                {
                    m_wStatusTimeArr[Grobal2.STATE_TRANSPARENT] = 1;
                }
                if (m_bo316 || m_nCurrX == nX && m_nCurrY == nY)
                {
                    SendMapDescription();
                    result = true;
                }
                m_nHealthTick -= 60;
                m_nSpellTick -= 10;
                m_nSpellTick = HUtil32._MAX(0, m_nSpellTick);
                DecreaseHealthSpellRecoveryStep(1);
            }
            else
            {
                m_dwMoveCount = 0;
                m_dwMoveCountA = 0;
            }
            return result;
        }

        private bool ClientSpellXY(short wIdent, int nKey, int nTargetX, int nTargetY, TBaseObject TargeTBaseObject, bool boLateDelivery, bool boBypassNativeCanActBlock, ref int dwDelayTime)
        {
            var result = false;
            dwDelayTime = 0;
            if (!m_boCanSpell)
            {
                return result;
            }
            if (!boBypassNativeCanActBlock
                && (m_boDeath || m_wStatusTimeArr[Grobal2.POISON_STONE] != 0
                    && !M2Share.g_Config.ClientConf.boParalyCanSpell))// 防麻
            {
                return result;
            }
            // MAGIC-U0 / ID 200 hijack — native sub_6BC510 runs interceptor
            // sub_6BCD48 @0x6BC52F, ahead of the sub_772A50 skill-forbid gate below
            // and of GetMagicInfo. nKey==200 is swallowed: return TRUE, no normal
            // dispatch (detonation branch fail-closed — see
            // TPlayObject.NativeMagic200Hijack.cs).
            if (TryNativeMagic200Hijack(nKey))
            {
                return true;
            }
            // Native sub_6BC510 @0x6BC541-0x6BC566 — the per-map / per-cell
            // skill-forbid gate, which sits BEFORE every other stage: before the
            // +0xA24/+0xA4C switch-cleanup calls (0x6BC576/0x6BC586), before
            // GetTickCount (0x6BC597) and all interval / strike-counter
            // bookkeeping, and before GetMagicInfo (VMT+0xE8 @0x6BC5CB).
            //   6BC541  movzx edx,bx          ; edx = the RAW wire skill index
            //   6BC546  call  sub_772A50
            //   6BC54D  jne   0x6bc56b        ; TRUE = allowed
            //   6BC54F  mov   cx,0xFFDB       ; -37
            //   6BC553  mov   edx,0x6BCD18    ; "当前区域不可使用该技能"
            //   6BC55C  call  [ebx+0xD4]      ; SysMsg
            //   6BC562  mov   byte [ebp-5],0  ; result = FALSE
            // sub_772A50 @0x772A5A-0x772A8E: starts allowed (`mov bl,1`), then
            //   (a) sub_77BE88(m_PEnvir[+0x128], CurrX[+0x12C], CurrY[+0x130])
            //       = the per-cell byte at map[+0x38] + (x*map[+0x40]+y)*12 + 4;
            //       non-zero -> DENY, and the id list is then NOT consulted;
            //   (b) sub_77BCF4(m_PEnvir, magIdx) = linear scan of the map's
            //       +0x28 int TList for the raw index -> DENY.
            // sub_77BE88 @0x77BE8D-0x77BE9D returns 0 (=allowed) for x<0, y<0,
            // x>=Width[+0x3C] or y>=Height[+0x40], i.e. out-of-bounds fails OPEN.
            // Envirnoment.IsSkillAllowedAt reproduces both halves in that order.
            // Because the gate runs before GetMagicInfo, it is keyed on nKey (the
            // raw wire index), not on a resolved UserMagic.
            if (m_PEnvir == null ||
                !m_PEnvir.IsSkillAllowedAt(m_nCurrX, m_nCurrY, nKey))
            {
                // GBK bytes at 0x6BCD18, long-string length field [0x6BCD14] = 22.
                // 0x6BC54F sends cx=0xFFDB. cx unpacks as FColor = cx & 0xFF,
                // BColor = cx >> 8, so 0xFFDB is 0xDB/0xFF == MsgColor.Green.
                // (MsgColor.Red is 0x38FF -- the wrong channel here.)
                SysMsg("当前区域不可使用该技能", MsgColor.Green, MsgType.Hint);
                return result;
            }
            var UserMagic = GetMagicInfo(nKey);
            if (UserMagic == null)
            {
                return result;
            }
            var boIsWarrSkill = M2Share.MagicManager.IsWarrSkill(UserMagic.wMagIdx);
            if (!boLateDelivery && !boIsWarrSkill && (!M2Share.g_Config.boSpeedHackCheck))
            {
                if (!CheckActionStatus(wIdent, ref dwDelayTime))
                {
                    m_boFilterAction = false;
                    return result;
                }
                m_boFilterAction = true;
                var dwCheckTime = HUtil32.GetTickCount() - m_dwMagicAttackTick;
                if (dwCheckTime < m_dwMagicAttackInterval)
                {
                    m_dwMagicAttackCount++;
                    dwDelayTime = m_dwMagicAttackInterval - dwCheckTime;
                    if (dwDelayTime > M2Share.g_Config.dwMagicHitIntervalTime / 3)
                    {
                        if (m_dwMagicAttackCount >= 4)
                        {
                            m_dwMagicAttackTick = HUtil32.GetTickCount();
                            m_dwMagicAttackCount = 0;
                            dwDelayTime = M2Share.g_Config.dwMagicHitIntervalTime / 3;
                            if (m_boTestSpeedMode)
                            {
                                SysMsg("魔法忙复位!!!" + dwDelayTime, MsgColor.Red, MsgType.Hint);
                            }
                        }
                        else
                        {
                            m_dwMagicAttackCount = 0;
                        }
                        return result;
                    }
                    else
                    {
                        if (m_boTestSpeedMode)
                        {
                            SysMsg("魔法忙!!!" + dwDelayTime, MsgColor.Red, MsgType.Hint);
                        }
                        return result;
                    }
                }
            }
            m_nSpellTick -= 450;
            m_nSpellTick = HUtil32._MAX(0, m_nSpellTick);
            if (!boIsWarrSkill)
            {
                m_dwMagicAttackInterval = UserMagic.MagicInfo.dwDelayTime + M2Share.g_Config.dwMagicHitIntervalTime;
            }
            m_dwMagicAttackTick = HUtil32.GetTickCount();
            ushort nSpellPoint;
            switch (UserMagic.wMagIdx)
            {
                // ids 3, 4 and 7 are acknowledged and dropped. The outer
                // ladder's own jump table (base id 3, `add eax,-3` /
                // `cmp eax,0x18` @0x6BC69C, table at 0x6BC6AF) holds
                // 0x6BC7DC in the slots for all three (0x6BC6AF, 0x6BC6B3,
                // 0x6BC6BF), and 0x6BC7DC is two instructions:
                //   006BC7DC  c6 45 fb 01     mov byte [ebp-5],1
                //   006BC7E0  e9 1d 05 00 00  jmp 0x6BCD02
                // i.e. return TRUE having sent nothing and spent nothing.
                // Without this arm they reach the default below, where
                // DoSpell refuses them for being warrior skills and the
                // caller answers with a RM_MAGICFIREFAIL native never sends.
                case SpellsDef.SKILL_ONESWORD:
                case SpellsDef.SKILL_ILKWANG:
                case SpellsDef.SKILL_YEDO:
                    result = true;
                    break;
                // ids 116, 234, 314 and 317 are refused by the ladder itself,
                // each with its own `je 0x6BCD02` straight to the epilogue:
                //   0x6BC713  83 f8 74  cmp eax,0x74      / 0x6BC717 je
                //   0x6BC749  83 e8 42  sub eax,0x42 (234)/ 0x6BC74C je
                //   0x6BC7C3  2d 3a 01 00 00  sub eax,0x13A (314) / 0x6BC7C8
                //   0x6BC7CE  83 e8 03  sub eax,3 (317)   / 0x6BC7D1 je
                // [ebp-5] was zeroed at 0x6BC59F and nothing on these paths
                // touches it, so all four return FALSE without spending mana
                // or sending an effect, and the CM_SPELL caller answers with
                // RM_MOVEFAIL + SM_ACT_FAIL. 314 and 317 previously reached
                // the default arm and were cast as ordinary spells.
                case SpellsDef.SKILL_116:
                case SpellsDef.SKILL_234:
                case SpellsDef.SKILL_314:
                case SpellsDef.SKILL_317:
                    break;
                // Four more outer ids whose callee is `33 C0 C3` (xor eax,eax
                // / ret). Result stays the 0 written at 0x6BC59F.
                //   115  0x6BCBAD E8 84 2E 03 00 call 0x6EFA38 (`33 C0 C3`)
                //   269  0x6BCADF E8 E7 FE 02 00 call 0x6EC9D0 (`33 C0 C3`)
                //   270  0x6BCB86 FF 91 60 01 00 00 call [ecx+0x160];
                //        TPlayer VMT 0x6AC8C8+0x160 = 0x774154 (`33 C0 C3`)
                //   287  0x6BCBBC FF 91 20 02 00 00 call [ecx+0x220];
                //        TPlayer VMT+0x220 = 0x6ED268 (`33 C0 C3`); the
                //        0x769258 tail at 0x6BCBD6 is behind
                //        `cmp [ebp-5],0 / je 0x6BCD02` and never runs.
                // Without these arms they fall into default, DoSpell DEFAULT
                // succeeds, and the client sees a 0x27E fire native never
                // sends. CM_SPELL answers RM_MOVEFAIL + SM_ACT_FAIL.
                case SpellsDef.SKILL_115:
                case SpellsDef.SKILL_269:
                case SpellsDef.SKILL_270:
                case SpellsDef.SKILL_287:
                    break;
                case SpellsDef.SKILL_65:
                    result = TryActivateNativeSkill65Charge();
                    break;
                case SpellsDef.SKILL_290:
                    result = TryActivateNativeSkill290(nTargetX);
                    break;
                case SpellsDef.SKILL_237:
                    result = TryActivateNativeSkill237Dragon(UserMagic);
                    break;
                case SpellsDef.SKILL_261:
                    result = TryActivateNativeSkill261(UserMagic);
                    break;
                case SpellsDef.SKILL_262:
                    result = TryActivateNativeSkill262Poison(UserMagic);
                    break;
                case SpellsDef.SKILL_267:
                    result = TryActivateNativeSkill267(UserMagic);
                    break;
                case SpellsDef.SKILL_273:
                    TBaseObject skill273Target = null;
                    if (CretInNearXY(TargeTBaseObject, nTargetX, nTargetY))
                    {
                        skill273Target = TargeTBaseObject;
                    }
                    TryActivateNativeSkill273DragonBreak(UserMagic,
                        skill273Target);
                    break;
                case SpellsDef.SKILL_168:
                    result = TryActivateNativeSkill168Charge(nTargetX,
                        nTargetY);
                    break;
                case SpellsDef.SKILL_68:
                    result = TryActivateNativeSkill68Charge(UserMagic,
                        nTargetX, nTargetY);
                    break;
                case SpellsDef.SKILL_265:
                    result = TryActivateNativeSkill265(UserMagic, nTargetX);
                    break;
                case SpellsDef.SKILL_266:
                    result = TryActivateNativeSkill266Blink(UserMagic,
                        nTargetX, nTargetY);
                    break;
                case SpellsDef.SKILL_ERGUM:
                    if (m_MagicArr[SpellsDef.SKILL_ERGUM] != null)
                    {
                        if (!m_boUseThrusting)
                        {
                            ThrustingOnOff(true);
                            // 0x6BDFE6 xor ecx,ecx / 66 BA 70 02 mov dx,0x270
                            SendSocket(Grobal2.MakeDefaultMsg(
                                Grobal2.SM_THRUSTING, 0, 0, 0, 0));
                        }
                        else
                        {
                            ThrustingOnOff(false);
                            // 0x6BE001 mov ecx,1 / 66 BA 70 02 mov dx,0x270
                            SendSocket(Grobal2.MakeDefaultMsg(
                                Grobal2.SM_THRUSTING, 1, 0, 0, 0));
                        }
                    }
                    result = true;
                    break;
                case SpellsDef.SKILL_BANWOL:
                    if (m_MagicArr[SpellsDef.SKILL_BANWOL] != null)
                    {
                        if (!m_boUseHalfMoon)
                        {
                            HalfMoonOnOff(true);
                            // 0x6BE036 xor ecx,ecx / 66 BA 71 02 mov dx,0x271
                            SendSocket(Grobal2.MakeDefaultMsg(
                                Grobal2.SM_HALFMOON, 0, 0, 0, 0));
                        }
                        else
                        {
                            HalfMoonOnOff(false);
                            // 0x6BE049 mov ecx,1 / 66 BA 71 02 mov dx,0x271
                            SendSocket(Grobal2.MakeDefaultMsg(
                                Grobal2.SM_HALFMOON, 1, 0, 0, 0));
                        }
                    }
                    result = true;
                    break;
                case SpellsDef.SKILL_FIRESWORD:
                    if (m_MagicArr[SpellsDef.SKILL_FIRESWORD] != null)
                    {
                        if (AllowFireHitSkill())
                        {
                            nSpellPoint = GetSpellPoint(UserMagic);
                            if (m_WAbil.MP >= nSpellPoint)
                            {
                                if (nSpellPoint > 0)
                                {
                                    DamageSpell(nSpellPoint);
                                    HealthSpellChanged();
                                }
                                // 0x6BC856 6A00 x4 / 33 C9 / 66 BA 72 02 mov dx,0x272 /
                                // FF 93 50 02 00 00.  "+FIR" is absent from the image.
                                SendSocket(Grobal2.MakeDefaultMsg(
                                    Grobal2.SM_FIREHITSKILL, 0, 0, 0, 0));
                            }
                        }
                        result = true;
                    }
                    break;
                case SpellsDef.SKILL_58:
                    if (m_MagicArr[SpellsDef.SKILL_58] != null)
                    {
                        var now = HUtil32.GetTickCount();
                        if (!m_boSunSwordReady &&
                            unchecked(now - m_dwLatestSunSwordTick) >= 15 * 1000)
                        {
                            nSpellPoint = GetSpellPoint(UserMagic);
                            if (m_WAbil.MP >= nSpellPoint)
                            {
                                if (nSpellPoint > 0)
                                {
                                    DamageSpell(nSpellPoint);
                                    HealthSpellChanged();
                                }
                                m_dwLatestSunSwordTick = now;
                                var readyMessage = Grobal2.MakeDefaultMsg(
                                    Grobal2.SM_SWORDHIT_ON, 0, 0, 0, 0);
                                SendSocket(readyMessage);
                                m_boSunSwordReady = true;
                            }
                        }
                    }
                    result = true;
                    break;
                case SpellsDef.SKILL_MOOTEBO:
                    result = true;
                    if (TryStartNativeMotaeboForcedMove(
                            UserMagic, (byte)nTargetX))
                    {
                        TrainNativeMotaeboMagic(UserMagic,
                            M2Share.RandomNumber.Random(3) + 1,
                            HUtil32.GetTickCount());
                    }
                    break;
                default:
                    // ID 38 is intentionally handled here. The outer native
                    // ladder sends it to sub_6ED62C, whose own dispatcher
                    // converges on the successful default spell tail.
                    m_btDirection = M2Share.GetNextDirection(m_nCurrX, m_nCurrY, nTargetX, nTargetY); ;
                    TBaseObject BaseObject = null;
                    if (CretInNearXY(TargeTBaseObject, nTargetX, nTargetY)) 
                    {
                        BaseObject = TargeTBaseObject;
                        nTargetX = BaseObject.m_nCurrX;
                        nTargetY = BaseObject.m_nCurrY;
                    }
                    if (!DoSpell(UserMagic, (short)nTargetX, (short)nTargetY, BaseObject))
                    {
                        SendRefMsg(Grobal2.RM_MAGICFIREFAIL, 0, 0, 0, 0, "");
                    }
                    result = true;
                    break;
            }
            return result;
        }

        private bool ClientRunXY(int wIdent, int nX, int nY, int nFlag, ref int dwDelayTime)
        {
            bool result = false;
            byte nDir;
            dwDelayTime = 0;
            if (!m_boCanRun)
            {
                return result;
            }
            // MOVE-11 — run handler 3013 runs the cancel hook sub_6BCE2C at
            // 0x6D9D08, i.e. BEFORE gate3 (`call [edx+0xBC]` @0x6D9D12), gate4
            // (`call [ecx+0x40]` @0x6D9D23) and the run primitive sub_6BBFBC
            // (@0x6D9D39): a run that is later refused has still cancelled the
            // channels. Only the bodyState 0x34 test @0x6D9CEC precedes it, and
            // that one bails to 0x6DBC2C without ever reaching here (MOVE-10).
            // m_boCanRun above has no native counterpart on this path (it is the
            // login/boLockRunAction lock), so the hook sits after it rather than
            // inventing a cancel inside a C#-only swallow window.
            CancelNativeActionChannels();
            // MOVE-15 — same gate on the run ladder: `call [ecx+0x40]` at
            // 0x6D9D23 (run case 3013), ahead of the run primitive
            // sub_6BBFBC at 0x6D9D39. Run passes dl=1 to the inherited
            // predicate and walk passes 0 (that arg only selects the
            // bodyState 0x18 term at 0x76B398, `test al,bl`); the +0x574
            // term at 0x6E6716 is arg-independent, so it applies identically
            // to walk and run.
            if (IsNativeCanActBlockedByForcedMove())
            {
                return result;
            }
            // MOVE-13/14 — the other half of gate 4. Native case 3013 reaches
            // the run primitive only through `mov dl,1 / call [ecx+0x40]` at
            // 0x6D9D1C-0x6D9D23, i.e. TPlayer VMT+0x40 sub_6E6700, which chains
            // into the base can-act ladder sub_76B354. Foot running went
            // straight past it because the ladder lived only on the mounted
            // CM_RUN3 path, so states 0x1D/0x01/0x1A/0x3E stopped a walk but not
            // a run, and 0x18 (run-only, arg-dependent at 0x76B398) stopped
            // nothing at all.
            if (IsNativeCanActBlocked(1))
            {
                return result;
            }
            if (m_boDeath || m_wStatusTimeArr[Grobal2.POISON_STONE] != 0 && !M2Share.g_Config.ClientConf.boParalyCanRun)
            {
                return result;
            }
            if (m_PEnvir == null)
            {
                return result;
            }
            if (nFlag != wIdent && (!M2Share.g_Config.boSpeedHackCheck))
            {
                if (!CheckActionStatus(wIdent, ref dwDelayTime))
                {
                    m_boFilterAction = false;
                    return result;
                }
                m_boFilterAction = true;
                int dwCheckTime = HUtil32.GetTickCount() - m_dwMoveTick;
                if (dwCheckTime < M2Share.g_Config.dwRunIntervalTime)
                {
                    m_dwMoveCount++;
                    dwDelayTime = M2Share.g_Config.dwRunIntervalTime - dwCheckTime;
                    if (dwDelayTime > M2Share.g_Config.dwRunIntervalTime / 3)
                    {
                        if (m_dwMoveCount >= 4)
                        {
                            m_dwMoveTick = HUtil32.GetTickCount();
                            m_dwMoveCount = 0;
                            dwDelayTime = M2Share.g_Config.dwRunIntervalTime / 3;
                            if (m_boTestSpeedMode)
                            {
                                SysMsg("跑步忙复位!!!" + dwDelayTime, MsgColor.Red, MsgType.Hint);
                            }
                        }
                        else
                        {
                            m_dwMoveCount = 0;
                        }
                        return result;
                    }
                    else
                    {
                        if (m_boTestSpeedMode)
                        {
                            SysMsg("跑步忙!!!" + dwDelayTime, MsgColor.Red, MsgType.Hint);
                        }
                        return result;
                    }
                }
            }
            m_dwMoveTick = HUtil32.GetTickCount();
            m_bo316 = false;
            // MOVE-16/17/18/19 — sub_6BBFBC opens with the switch / map RUNFLAG
            // / weight / CanRun ladder before it ever calls the run mover, and
            // whatever fails it drops through 0x6BC001 into the clamp-and-walk
            // degrade rather than refusing. Native 3013 never tests the mounted
            // state 0x33 (only 4108 does, at 0x6D9D99), so foot runners belong
            // on this ladder too; C# gated the whole ladder behind "is mounted"
            // and left this path with no weight rule, no paralysis rule and no
            // degrade. Sharing ClientNativeRun3Fallback mirrors native, where
            // both run primitives fall into the same walk primitive sub_6BBCD8.
            //
            // Gate 3 first. Native runs `call [edx+0xBC]` at 0x6D9D12, i.e. at
            // handler level ahead of the primitive, so a blocked actor never
            // reaches either the run mover or the degrade. C# carries that term
            // (internal state 0x2D) inside RunTo/WalkTo, which the degrade would
            // otherwise walk straight past.
            if (HasTimedAbility(13))
            {
                return result;
            }
            if (!IsNativeRunLadderAllowed())
            {
                return ClientNativeRun3Fallback(nX, nY);
            }
            nDir = M2Share.GetNextDirection(m_nCurrX, m_nCurrY, nX, nY);
            if (RunTo(nDir, false, nX, nY))
            {
                if (m_boTransparent && m_boHideMode)
                {
                    m_wStatusTimeArr[Grobal2.STATE_TRANSPARENT] = 1;
                }
                if (m_bo316 || m_nCurrX == nX && m_nCurrY == nY)
                {
                    SendMapDescription();
                    result = true;
                }
                m_nHealthTick -= 60;
                m_nSpellTick -= 10;
                m_nSpellTick = HUtil32._MAX(0, m_nSpellTick);
                DecreaseHealthSpellRecoveryStep(1);
            }
            else
            {
                m_dwMoveCount = 0;
                m_dwMoveCountA = 0;
            }
            return result;
        }

        private bool ClientWalkXY(int wIdent, int nX, int nY, bool boLateDelivery, ref int dwDelayTime)
        {
            bool result = false;
            int n14;
            int n18;
            int n1C;
            dwDelayTime = 0;
            if (!m_boCanWalk)
            {
                return result;
            }
            // MOVE-11 — walk handler 3011 runs the cancel hook sub_6BCE2C at
            // 0x6D9BEC, ahead of gate3 (`call [edx+0xBC]` @0x6D9BF6), gate4
            // (`call [ecx+0x40]` @0x6D9C07) and the walk primitive sub_6BBCD8
            // (@0x6D9C1D), so a walk that is later refused with 0x276 has still
            // cancelled the channels. Same placement rationale as ClientRunXY.
            CancelNativeActionChannels();
            // MOVE-15 — gate 4 of the native walk ladder is `call [ecx+0x40]`
            // at 0x6D9C07 (walk case 3011), the TPlayer can-act override
            // sub_6E6700, which refuses while the cast lock +0x574 is set.
            // It runs BEFORE the walk primitive sub_6BBCD8 (0x6D9C1D), so the
            // refusal must precede CheckActionStatus and all interval
            // bookkeeping here. Leaving dwDelayTime at 0 makes the caller
            // answer SM_ACT_FAIL(630) with X/Y/Dir, which is native's 0x276
            // correction at 0x6D9C4B.
            if (IsNativeCanActBlockedByForcedMove())
            {
                return result;
            }
            // MOVE-13 — VMT+0x40 at 0x6E6700 calls inherited gate 0x76B354,
            // which tests four states that must block walk/run/turn/pose:
            // State 29 (0x1D) at 0x76B368, State 1 (0x01) at 0x76B375,
            // State 26 (0x1A) at 0x76B382, State 62 (0x3E) at 0x76B39C.
            // State 24 (0x18) is tested at 0x76B38F but blocks only run
            // (arg-dependent: walk passes dl=0, run passes dl=1).
            // State 45 (0x2D) is tested one level out in VMT+0xC4 at 0x73D441.
            if (HasNativeActiveState(29) ||  // 0x1D
                HasNativeActiveState(1) ||   // 0x01
                HasNativeActiveState(26) ||  // 0x1A
                HasNativeActiveState(62))    // 0x3E
            {
                return result;
            }
            if (m_boDeath || m_wStatusTimeArr[Grobal2.POISON_STONE] != 0 && !M2Share.g_Config.ClientConf.boParalyCanWalk)
            {
                return result;
            }
            if (!boLateDelivery && (!M2Share.g_Config.boSpeedHackCheck))
            {
                if (!CheckActionStatus(wIdent, ref dwDelayTime))
                {
                    m_boFilterAction = false;
                    return result;
                }
                m_boFilterAction = true;
                int dwCheckTime = HUtil32.GetTickCount() - m_dwMoveTick;
                if (dwCheckTime < M2Share.g_Config.dwWalkIntervalTime)
                {
                    m_dwMoveCount++;
                    dwDelayTime = M2Share.g_Config.dwWalkIntervalTime - dwCheckTime;
                    if (dwDelayTime > M2Share.g_Config.dwWalkIntervalTime / 3)
                    {
                        if (m_dwMoveCount >= 4)
                        {
                            m_dwMoveTick = HUtil32.GetTickCount();
                            m_dwMoveCount = 0;
                            dwDelayTime = M2Share.g_Config.dwWalkIntervalTime / 3;
                            if (m_boTestSpeedMode)
                            {
                                SysMsg("走路忙复位!!!" + dwDelayTime, MsgColor.Red, MsgType.Hint);
                            }
                        }
                        else
                        {
                            m_dwMoveCount = 0;
                        }
                        return result;
                    }
                    else
                    {
                        if (m_boTestSpeedMode)
                        {
                            SysMsg("走路忙!!!" + dwDelayTime, MsgColor.Red, MsgType.Hint);
                        }
                        return result;
                    }
                }
            }
            m_dwMoveTick = HUtil32.GetTickCount();
            m_bo316 = false;
            n18 = m_nCurrX;
            n1C = m_nCurrY;
            n14 = M2Share.GetNextDirection(m_nCurrX, m_nCurrY, nX, nY);
            if (!m_boClientFlag)
            {
                if (n14 == 0 && m_nStep == 0)
                {
                    m_nStep++;
                }
                else if (n14 == 4 && m_nStep == 1)
                {
                    m_nStep++;
                }
                else if (n14 == 6 && m_nStep == 2)
                {
                    m_nStep++;
                }
                else if (n14 == 2 && m_nStep == 3)
                {
                    m_nStep++;
                }
                else if (n14 == 1 && m_nStep == 4)
                {
                    m_nStep++;
                }
                else if (n14 == 5 && m_nStep == 5)
                {
                    m_nStep++;
                }
                else if (n14 == 7 && m_nStep == 6)
                {
                    m_nStep++;
                }
                else if (n14 == 3 && m_nStep == 7)
                {
                    m_nStep++;
                }
                else
                {
                    m_nGameGold -= m_nStep;
                    GameGoldChanged();
                    m_nStep = 0;
                }
                if (m_nStep != 0)
                {
                    m_nGameGold++;
                    GameGoldChanged();
                }
            }
            // MOVE-71：原版 CM_WALK 处理器 sub_6BBCE0 在 0x6BBD0C `mov cl,[ebx+0x3FE]`
            // 把缓存的穿透判定当作 WalkTo 第三参(boFlag)，再经 sub_741224(0x74122D 存、
            // 0x7412B3 压) 传给 MoveToMovingObject 的 boIgnoreOccupancy(0x779870)。此处
            // 原先恒传 false，导致玩家在安全区永不可穿人——与原版分歧。改传穿透判定。
            // MOVE-73：0x6BBD0C 是 `mov cl,[ebx+0x3FE]` —— 读缓存，不重算。
            if (WalkTo((byte)n14, m_boThroughOccupancyCache))
            {
                if (m_bo316 || m_nCurrX == nX && m_nCurrY == nY)
                {
                    SendMapDescription();
                    result = true;
                }
                m_nHealthTick -= 10;
            }
            else
            {
                m_dwMoveCount = 0;
                m_dwMoveCountA = 0;
            }
            return result;
        }
    }
}
