using System;
using SystemModule;

namespace GameSvr
{
    internal sealed class NativeMagicProducerPushPayload
    {
        internal NativeMagicProducerPushPayload(TBaseObject target,
            byte direction)
        {
            Target = target;
            Direction = direction;
        }

        internal TBaseObject Target { get; }
        internal byte Direction { get; }
    }

    public partial class TPlayObject
    {
        internal const int NativeMagicProducerPushIdent = 10417;

        /// <summary>原生 self+0x50C —— wMagicID 62 "圣兽" 的【收回】时间戳,
        /// 30 秒门读它: 0x6EDC7A `2B 83 0C 05 00 00` = sub eax,[ebx+0x50C]。
        /// 与 skill 111 的 +0x510 一样是对象上的裸 dword, 不是 coldTime 表项。
        ///
        /// SKILL-62 取证 (2026-08-14): 本字段的【唯一】运行期写入点是
        /// THolyMonster.Die = sub_66C2F4 (VMT 0x663060 槽 +0x84) 的
        /// 0x66C31C-0x66C327 —— 也就是圣兽【死亡/被收回】时把 tick 记到召唤者身上,
        /// 与提示串 "圣兽刚收回不到30秒" 完全一致。SKILL_62 施法块 0x6EDC71-0x6EDCA9
        /// 【没有】任何对 +0x50C 的写入, 所以造宠成功后不得在此处补盖时间戳
        /// (那会把语义变成"召唤后 30 秒内不能再召唤")。落点见
        /// <see cref="HolyMonster.Die"/>。其余 +0x50C 触点只有存档读写:
        /// 0x688B9C(载入: tick-30000+剩余秒×1000)、0x6B0832(同形)、
        /// 0x6891F0 / 0x6B1517(存档: 把剩余毫秒折成秒写回)。</summary>
        internal int m_dwMagic62LastTick;

        /// <summary>原生 0x76EF74 处的字面量 GBK 长串 "圣兽" (len 前缀 @0x76EF70 = 4)。
        /// sub_76EEF4 @0x76EF2D 是 `BA 74 EF 76 00` = `mov edx,0x76EF74`, 直接取
        /// 字符串【数据地址】而不是从某个全局变量间接装载, 所以这个名字在原生里
        /// 不可配置 —— 不能用 g_Config.sAngel(默认 "精灵") 之类的可配置名替代。</summary>
        internal const string NativeHolyBeastName = "圣兽";

        /// <summary>sub_76EEF4 @0x76EF16 `6A 01` —— MakeSlave 的 nMaxMob。</summary>
        private const int NativeHolyBeastMaxMob = 1;

        /// <summary>sub_76EEF4 @0x76EF18 `68 00 2F 0D 00` = 0xD2F00 = 864000 秒
        /// (10 天) —— MakeSlave 的 dwRoyaltySec。</summary>
        private const int NativeHolyBeastRoyaltySec = 0xD2F00;

        // =================================================================
        // 原生 TPlayer VMT+0x124 = sub_76EEF4 (VMT 基址 0x6AC8C8, +0x124 槽)。
        // wMagicID 62 的施法块 0x6EDCA3 `FF 91 24 01 00 00` 就是唯一派发点
        // (全镜像对 0x76EEF4 没有任何 E8 直调)。逐地址:
        //   0076EEFF  xor esi,esi                     ; Result := nil
        //   0076EF01  cmp [ebp-4],0 / je 0x76EF65     ; UserMagic = nil -> nil
        //   0076EF07  mov dx,0x28A1 / call 0x7661E8   ; CheckServerMakeSlave
        //   0076EF14  jne 0x76EF65                    ; 队列里已有 RM_10401 -> nil
        //   0076EF16  push 1 / push 0xD2F00 / push 0 / push 0xA
        //   0076EF24  call sub_4C896C -> cl           ; 有效技能等级
        //   0076EF2D  mov edx,0x76EF74                ; "圣兽" 字面量
        //   0076EF36  call [esi+0xEC]                 ; TPlayer.MakeSlave = 0x6CB070
        //   0076EF3E  test esi,esi / je 0x76EF65      ; 造宠失败 -> nil
        //   0076EF49  call sub_66C630(slave, self, UserMagic)   ; THolyMonster 初始化
        //   0076EF4E  eax=3 / call 0x403B4C / inc ecx ; Random(3)+1
        //   0076EF62  call [ebx+0x3C]                 ; TrainSkill(UserMagic, 上式)
        //   0076EF65  mov eax,esi / ret               ; 返回 slave (调用点丢弃)
        // 失败语义 (三处 je 0x76EF65) 全部是【静默返回 nil】: 不退护身符、不发任何
        // 消息、不调 RecallSlave、不写 +0x50C。
        //
        // fail-closed —— sub_66C630 (0x66C630-0x66C70F) 只移植了 0x66C6BB 一条
        // (`89 B3 F8 04 00 00` dword[slave+0x4F8] := master, 见 HolyMonster)。其余
        // 各条都写 THolyMonster 在父类 TATMonster(size 0x4E8) 之后新增、C# 无命名
        // 落点的字段, 且消费者是同样未移植的 holy-seize 机制 (Operate 0x66C7C8 /
        // Run 0x66C8AC), 强行落地必然臆造:
        //   0x66C63E byte[+0x4F5]:=0        0x66C645 byte[+0x2E3]:=0
        //   0x66C650 word[+0x4F0]:=word[+0x0C]
        //   0x66C65F word[+0x4F2]:=byte[+0x179]
        //   0x66C675 UserMagic 非空时 rep movsd+movsb 把 25 字节 TUserMagic 记录
        //            整体拷进 [+0x4FC]
        //   0x66C67F [+0x80]:=GetTickCount ; 0x66C689 call 0x765DEC
        //   0x66C6B2 [vmt+0xD8] 广播 cx=0x27D8
        //   0x66C6D8 [+0x4EC]:=Round([master+0x2A0] * tbyte@0x66C710)
        //   0x66C6E6 call 0x76C7B8(master, 0x22)
        // 另一个调用者 sub_6CB6C4 @0x6CB74D (按 byte[slave+0x178]==0x97 即 race 151
        // 分流, UserMagic 传 nil) 属另一条造宠链, 本次不涉及。
        // =================================================================

        /// <summary>原生 sub_76EEF4 —— wMagicID 62 的 "圣兽" 造宠原语。
        /// 返回造出的奴仆; 任一道门未过均返回 null 且【不产生任何可见反馈】。
        /// 调用点 0x6EDCA3 丢弃返回值, 所以它不影响 boSpellFail。</summary>
        public TBaseObject NativeMakeHolyBeastSlave(TUserMagic UserMagic)
        {
            if (UserMagic == null)
            {
                return null;                                   // 0x76EF01 / 0x76EF05
            }
            if (CheckServerMakeSlave())                        // 0x76EF0D sub_7661E8(0x28A1)
            {
                return null;                                   // 0x76EF14 jne
            }
            // 0x6CB070 writes ECX to both level bytes and stores the pushed
            // literal 10 at TAnimal +0x48C.
            int nMakeLevel = GetNativeMagicProducerEffectiveLevel(UserMagic);
            var slave = MakeNativeSlave(NativeHolyBeastName, nMakeLevel,
                NativeHolyBeastMaxMob, NativeHolyBeastRoyaltySec,
                fromHero: false, hpAfterSlave: 10);                 // 0x76EF36
            if (slave == null)
            {
                return null;                                   // 0x76EF3E / 0x76EF40
            }
            // 0x76EF49 sub_66C630 的唯一可落地条: 记录召唤者, 供 THolyMonster.Die
            // 回写 30 秒门的时间戳。原生这一调用是【无条件】的 (0x76EF49 是 E8 直调,
            // 不走虚表, 不检查类型), 因为 MonItems 里 "圣兽" 的 race 必是
            // 151/170; 若被改成别的 race, 原生会按 THolyMonster 布局往越界偏移写,
            // 无法也不应复刻 —— C# 用 as 判定收敛到"配置正确时行为一致"。
            (slave as HolyMonster)?.NativeBindHolyBeastSummoner(this);
            // 0x76EF62 [vmt+0x3C] = 0x76AD30, 本工程的忠实移植是 TrainNativeMagicProducer
            // (m_boFastTrain ×3 -> sub_4C8910 等级门/升级循环), 不是只加点数的 TrainSkill。
            TrainNativeMagicProducer(UserMagic,
                NextNativeMagicProducerRandom(3) + 1);
            return slave;
        }

        internal static ushort GetNativeMagicProducerMpCost(
            TUserMagic magic)
        {
            return unchecked((ushort)(magic.MagicInfo.btDefSpell +
                HUtil32.Round((double)magic.MagicInfo.wSpell / 4.0d *
                    (magic.btLevel + 1))));
        }

        internal static int GetNativeMagicProducerEffectiveLevel(
            TUserMagic magic)
        {
            return Math.Min(
                unchecked((byte)(magic.btLevel + magic.NativeLevelBonus)),
                magic.MagicInfo.btTrainLv);
        }

        internal static int CalculateNativeMagicProducerSkillPower(
            TUserMagic magic)
        {
            int magicPower = unchecked(magic.MagicInfo.wPower +
                NextNativeMagicProducerRandom(
                    magic.MagicInfo.wMaxPower - magic.MagicInfo.wPower));
            int defaultPower = NextNativeMagicProducerRandom(
                magic.MagicInfo.btDefMaxPower - magic.MagicInfo.btDefPower);
            return unchecked(magic.MagicInfo.btDefPower +
                HUtil32.Round((double)(magic.btLevel + 1) * magicPower /
                    4.0d) + defaultPower);
        }

        // Native sub_4C870C (@0x4C870C-0x4C875C): the externally-supplied-power
        // twin of CalculateNativeMagicProducerSkillPower (sub_4C8658). It is a
        // DISTINCT native function, not an alias, and differs in exactly two ways:
        //   * the default-power roll is drawn FIRST (@0x4C8725), before the
        //     level-scaled term, so the RandSeed order is inverted vs sub_4C8658;
        //   * the level factor is the EFFECTIVE level (sub_4C896C @0x4C872D,
        //     btLevel + NativeLevelBonus clamped to btTrainLv), not raw btLevel.
        // Divisor is `fdiv dword ptr [0x4C8760]` @0x4C8740 — a 4-byte float32
        // holding raw bytes 00 00 80 40 = 4.0f. btTrainLv is NEVER read as a
        // divisor: it only ever appears as the level CAP inside sub_4C896C.
        // Rounding is sub_403574 = `fistp qword` (round-half-to-even) = HUtil32.Round.
        // Native call sites: 魔法盾 shield power @0x76FA06 and 地火 fire-cross
        // hold-time @0x77069B (both pass an externally computed nPower).
        internal static int CalculateNativeMagicProducerScaledPower(
            TUserMagic magic, int basePower)
        {
            int defaultPower = NextNativeMagicProducerRandom(
                magic.MagicInfo.btDefMaxPower - magic.MagicInfo.btDefPower);
            return unchecked(defaultPower +
                HUtil32.Round((double)(
                    GetNativeMagicProducerEffectiveLevel(magic) + 1) *
                    basePower / 4.0d) + magic.MagicInfo.btDefPower);
        }

        // Native sub_4C8764 (@0x4C8764-0x4C87BB) — the "type 13" power helper.
        // Exact body order:
        //   @0x4C877D  Random(btDefMaxPower - btDefPower)   <- drawn FIRST
        //   @0x4C8787  += btDefPower                        (pushed as one term)
        //   @0x4C878C  sub_4C896C  = EFFECTIVE level (btLevel + NativeLevelBonus,
        //                            clamped to btTrainLv)
        //   @0x4C8796  add eax,eax / add eax,3 / add eax,3  = 2*effLevel + 6
        //   @0x4C879E  imul edi                             = * nInt
        //   @0x4C87A6  fdiv dword ptr [0x4C87BC]            = float32 12.0f
        //                                                     (raw 00 00 40 41)
        //   @0x4C87AC  sub_403574 = fistp qword (round-half-to-even)
        //   @0x4C87B4  + the default-power term
        // There is NO btTrainLv divisor and no nInt/3 split anywhere in the body.
        // Governs 降魔/地狱雷光/神圣战甲/隐身/大隐身 power AND the 隐身 duration.
        internal static int CalculateNativeMagicProducer13Power(
            TUserMagic magic, int nInt)
        {
            int defaultPower = unchecked(NextNativeMagicProducerRandom(
                magic.MagicInfo.btDefMaxPower - magic.MagicInfo.btDefPower) +
                magic.MagicInfo.btDefPower);
            return unchecked(defaultPower +
                HUtil32.Round((double)(2 *
                    GetNativeMagicProducerEffectiveLevel(magic) + 6) *
                    nInt / 12.0d));
        }

        internal int NativeLuckOnlyRoll(int basePower, int spread)
        {
            spread = Math.Max(spread, 0);
            if (m_nLuck > 0)
            {
                if (NextNativeMagicProducerRandom(
                        10 - Math.Min(9, m_nLuck)) == 0)
                    return unchecked(basePower + spread);

                return unchecked(basePower +
                    NextNativeMagicProducerRandom(spread + 1));
            }

            int result = unchecked(basePower +
                NextNativeMagicProducerRandom(spread + 1));
            if (m_nLuck < 0 &&
                NextNativeMagicProducerRandom(
                    10 - Math.Max(0, -m_nLuck)) == 0)
                result = basePower;
            return result;
        }

        internal bool TryProduceNativeMagic1Or5(TUserMagic magic,
            TBaseObject target)
        {
            if (!TryAdmitNativeMagicProducerTarget(target, true))
                return false;

            int rawDamage = CalculateNativeMagicProducerRawDamage(magic);
            QueueNativeMagicProducerEffect(magic, target, rawDamage);
            if (target.m_btRaceServer >= Grobal2.RC_ANIMAL)
            {
                TrainNativeMagicProducer(magic,
                    NextNativeMagicProducerRandom(3) + 1);
            }
            return true;
        }

        internal bool TryProduceNativeMagic11(TUserMagic magic,
            TBaseObject target)
        {
            if (!TryAdmitNativeMagicProducerTarget(target, false))
                return false;

            int effectiveLevel = GetNativeMagicProducerEffectiveLevel(magic);
            int rawDamage = CalculateNativeMagicProducerRawDamage(magic);
            if (effectiveLevel == 4)
            {
                if (!IsNativeMagicProducerHumanKind(target))
                    rawDamage = unchecked(rawDamage * 2);
            }
            else if (target.m_btLifeAttrib == Grobal2.LA_UNDEAD)
            {
                rawDamage = HUtil32.Round(rawDamage * 1.5d);
            }

            QueueNativeMagicProducerEffect(magic, target, rawDamage);
            if (target.m_btRaceServer > Grobal2.RC_ANIMAL)
            {
                TrainNativeMagicProducer(magic,
                    NextNativeMagicProducerRandom(3) + 1);
            }
            return true;
        }

        internal bool TryProduceNativeMagic35(TUserMagic magic,
            TBaseObject target)
        {
            if (!TryAdmitNativeMagicProducerTarget(target, false))
                return false;

            int effectiveLevel = GetNativeMagicProducerEffectiveLevel(magic);
            int rawDamage = CalculateNativeMagicProducerRawDamage(magic);
            int lowMagic = HUtil32.LoWord(m_WAbil.MC);
            int highMagic = HUtil32.HiWord(m_WAbil.MC);
            if (effectiveLevel == 4)
            {
                rawDamage = unchecked(rawDamage +
                    NativeLuckOnlyRoll(lowMagic, highMagic - lowMagic) / 4);
            }
            if (target.m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                rawDamage = HUtil32.Round(rawDamage * 1.25d);
            if (effectiveLevel == 5)
                rawDamage = HUtil32.Round(rawDamage * 1.1d);
            if (effectiveLevel == 6)
                rawDamage = HUtil32.Round(rawDamage * 1.2d);

            int spellDamage = rawDamage / 2;
            if (effectiveLevel == 5)
                spellDamage = HUtil32.Round(spellDamage * 1.1d);
            if (effectiveLevel == 6)
                spellDamage = HUtil32.Round(spellDamage * 1.2d);
            DamageNativeMagicProducerSpell(target, spellDamage);

            QueueNativeMagicProducerEffect(magic, target, rawDamage);
            if (target.m_btRaceServer > Grobal2.RC_ANIMAL)
            {
                TrainNativeMagicProducer(magic,
                    NextNativeMagicProducerRandom(3) + 1);
            }
            return true;
        }

        internal bool TryProduceNativeMagic39(TUserMagic magic,
            TBaseObject target)
        {
            if (!TryAdmitNativeMagicProducerTarget(target, false))
                return false;

            int rawDamage = CalculateNativeMagicProducerRawDamage(magic);
            if (target.m_btLifeAttrib == Grobal2.LA_UNDEAD)
                rawDamage = HUtil32.Round(rawDamage * 1.2d);

            QueueNativeMagicProducerEffect(magic, target, rawDamage);
            TrainNativeMagicProducer(magic,
                NextNativeMagicProducerRandom(3) + 1);
            int effectiveLevel = GetNativeMagicProducerEffectiveLevel(magic);
            if (effectiveLevel >= 3 && m_Abil.Level > target.m_Abil.Level &&
                NextNativeMagicProducerRandom(100) <= 30)
            {
                byte direction = M2Share.GetNextDirection(m_nCurrX,
                    m_nCurrY, target.m_nCurrX, target.m_nCurrY);
                SendDelayMsg(this, NativeMagicProducerPushIdent, direction,
                    1, 0, 0, string.Empty, 700,
                    new NativeMagicProducerPushPayload(target, direction));
            }
            return true;
        }

        internal bool TrainNativeMagicProducer(TUserMagic magic,
            int trainingPoints)
        {
            if (magic?.MagicInfo == null ||
                !TryGetNativeMagicProducerTrainingValue(
                    magic.MagicInfo.TrainLevel, magic.btLevel,
                    out int requiredActorLevel) ||
                m_Abil.Level < requiredActorLevel)
                return false;

            int awardedPoints = m_boFastTrain
                ? unchecked(trainingPoints * 3)
                : trainingPoints;
            magic.nTranPoint = unchecked(magic.nTranPoint + awardedPoints);

            bool crossedThreshold = false;
            bool leveled = false;
            int requiredTraining = GetNativeMagicProducerRequiredTraining(magic);
            while (requiredTraining >= 0 &&
                   magic.nTranPoint >= requiredTraining)
            {
                magic.nTranPoint = unchecked(
                    magic.nTranPoint - requiredTraining);
                crossedThreshold = true;
                if (magic.btLevel >= magic.MagicInfo.btTrainLv)
                    break;

                magic.btLevel = unchecked((byte)(magic.btLevel + 1));
                leveled = true;
                requiredTraining =
                    GetNativeMagicProducerRequiredTraining(magic);
            }

            if (crossedThreshold)
            {
                RecalcAbilitys();
                SendMsg(this, Grobal2.RM_ABILITY, 0, 0, 0, 0,
                    string.Empty);
            }
            QueueNativeMagicProducerTrainingSnapshot(magic,
                leveled ? 800 : 3000);
            return true;
        }

        internal bool TryHandleNativeMagicProducerMessage(
            TProcessMessage processMessage)
        {
            if (processMessage?.wIdent != NativeMagicProducerPushIdent)
                return false;

            var payload = processMessage.Payload as
                NativeMagicProducerPushPayload;
            if (!m_boDeath && payload?.Target != null &&
                !payload.Target.m_boGhost)
                payload.Target.CharPushed(payload.Direction, 1);
            return true;
        }

        internal static bool IsNativeMagicProducerHumanKind(
            TBaseObject target)
        {
            return target is TPlayObject || target is HeroObject;
        }

        private bool TryAdmitNativeMagicProducerTarget(TBaseObject target,
            bool requireLineOfSight)
        {
            if (target == null || !IsProperTarget(target))
                return false;
            if (target.m_btRaceServer == Grobal2.RC_GUARD)
                return false;

            int chance = GetNativeMagicHitChance(m_wNativeType74MagicHit,
                target.m_nAntiMagic);
            if (NextNativeMagicProducerRandom(100) >= chance)
                return false;
            return !requireLineOfSight ||
                MagCanHitTarget(m_nCurrX, m_nCurrY, target);
        }

        private int CalculateNativeMagicProducerRawDamage(TUserMagic magic)
        {
            int skillPower = CalculateNativeMagicProducerSkillPower(magic);
            int lowMagic = HUtil32.LoWord(m_WAbil.MC);
            int highMagic = HUtil32.HiWord(m_WAbil.MC);
            Plugins.YanshenSkillPatches.MainAttr(this, magic.MagicInfo.wMagicID,
                lowMagic, highMagic, out lowMagic, out highMagic);
            return NativeLuckOnlyRoll(unchecked(lowMagic + skillPower),
                highMagic - lowMagic + 1);
        }

        private void QueueNativeMagicProducerEffect(TUserMagic magic,
            TBaseObject target, int rawDamage)
        {
            ushort magicId = magic.MagicInfo.wMagicID;
            rawDamage = Plugins.YanshenSkillPatches.ScaleDamage(this, magicId,
                rawDamage);
            Plugins.YanshenSkillPatches.ProducerDispatch(this, magicId, 2, 1,
                out byte range, out ushort category);
            QueueNativeMagicEffect(category, target, rawDamage,
                magicId, target.m_nCurrX,
                target.m_nCurrY, range, true, 0,
                MagicDamageContext.Capture(magic), 600);
        }

        private static void DamageNativeMagicProducerSpell(
            TBaseObject target, int amount)
        {
            int remaining = unchecked(target.m_WAbil.MP - amount);
            if (amount > 0)
            {
                target.m_WAbil.MP = remaining > 0 ? remaining : 0;
            }
            else
            {
                target.m_WAbil.MP = remaining < target.m_WAbil.MaxMP
                    ? remaining
                    : target.m_WAbil.MaxMP;
            }
        }

        private static int GetNativeMagicProducerRequiredTraining(
            TUserMagic magic)
        {
            return TryGetNativeMagicProducerTrainingValue(
                magic.MagicInfo.MaxTrain, magic.btLevel, out int value)
                    ? value
                    : -1;
        }

        private void QueueNativeMagicProducerTrainingSnapshot(
            TUserMagic magic, int delayMilliseconds)
        {
            HUtil32.EnterCriticalSection(M2Share.ProcessMsgCriticalSection);
            try
            {
                int index = 0;
                while (index < m_MsgList.Count)
                {
                    SendMessage message = m_MsgList[index];
                    if (message.wIdent != Grobal2.RM_MAGIC_LVEXP)
                    {
                        index++;
                        continue;
                    }
                    if (message.nParam1 == magic.MagicInfo.wMagicID)
                    {
                        if (!message.boLateDelivery)
                        {
                            index++;
                            continue;
                        }
                        m_MsgList.RemoveAt(index);
                        Dispose(message);
                        continue;
                    }
                    if (message.boLateDelivery)
                    {
                        message.dwDeliveryTime = 0;
                        message.boLateDelivery = false;
                        m_MsgList[index] = message;
                    }
                    index++;
                }
            }
            finally
            {
                HUtil32.LeaveCriticalSection(
                    M2Share.ProcessMsgCriticalSection);
            }

            SendDelayMsg(this, Grobal2.RM_MAGIC_LVEXP, 0,
                magic.MagicInfo.wMagicID, magic.btLevel,
                magic.nTranPoint, string.Empty, delayMilliseconds);
        }

        private static bool TryGetNativeMagicProducerTrainingValue<T>(
            T[] values, byte level, out int value) where T : struct
        {
            if (values == null || level >= values.Length)
            {
                value = -1;
                return false;
            }

            value = Convert.ToInt32(values[level]);
            return true;
        }

        private static int NextNativeMagicProducerRandom(int range)
        {
            if (range > 0)
                return M2Share.RandomNumber.Random(range);
            if (range == 0)
            {
                // Delphi Random(0) advances RandSeed and returns zero.
                _ = M2Share.RandomNumber.Random();
                return 0;
            }

            // Native sub_4C866E / 0x4C8683 pass wMax-wPower / defMax-defPower
            // with no test. A negative bound is the UInt32 bit pattern of
            // Random(n); the fused body then `add esi` / `imul (level+1)` /
            // `fild` (signed) / `fdiv 4.0` / ROUND — no subsequent clamp.
            return M2Share.RandomNumber.Random(range);
        }
    }
}
