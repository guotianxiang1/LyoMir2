using System.Buffers.Binary;
using SystemModule;
namespace GameSvr
{
    public partial class TBaseObject
    {
        protected virtual void AttackDir(TBaseObject TargeTBaseObject, short wHitMode, byte nDir)
        {
            TBaseObject AttackTarget;
            bool boPowerHit;
            bool boFireHit;
            bool bo41;
            bool boTwinHit;
            short wIdent;
            const string sExceptionMsg = "[Exception] TBaseObject::AttackDir";
            try
            {
                if ((wHitMode == 5) && (m_MagicArr[SpellsDef.SKILL_BANWOL] != null)) 
                {
                    if (m_WAbil.MP > 0)
                    {
                        DamageSpell((ushort)GetMagicSpell(m_MagicArr[SpellsDef.SKILL_BANWOL]));
                        HealthSpellChanged();
                    }
                    else
                    {
                        wHitMode = Grobal2.RM_HIT;
                    }
                }
                if ((wHitMode == 12) && (m_MagicArr[SpellsDef.SKILL_REDBANWOL] != null))
                {
                    if (m_WAbil.MP > 0)
                    {
                        DamageSpell((ushort)GetMagicSpell(m_MagicArr[SpellsDef.SKILL_REDBANWOL]));
                        HealthSpellChanged();
                    }
                    else
                    {
                        wHitMode = Grobal2.RM_HIT;
                    }
                }
                if ((wHitMode == 8) && (m_MagicArr[SpellsDef.SKILL_CROSSMOON] != null))
                {
                    if (m_WAbil.MP > 0)
                    {
                        DamageSpell((ushort)GetMagicSpell(m_MagicArr[SpellsDef.SKILL_CROSSMOON]));
                        HealthSpellChanged();
                    }
                    else
                    {
                        wHitMode = Grobal2.RM_HIT;
                    }
                }
                m_btDirection = nDir;
                if (TargeTBaseObject == null)
                {
                    AttackTarget = GetPoseCreate();
                }
                else
                {
                    AttackTarget = TargeTBaseObject;
                }
                if (m_UseItems[Grobal2.U_WEAPON] != null && m_UseItems[Grobal2.U_WEAPON].btValue[9] > 0)
                {
                    if ((AttackTarget != null) && (m_UseItems[Grobal2.U_WEAPON].wIndex > 0))
                    {
                        CheckWeaponUpgrade();
                    }
                }
                boPowerHit = m_boPowerHit;
                boFireHit = m_boFireHitSkill;
                bo41 = m_bo41kill;
                boTwinHit = m_boTwinHitSkill;
                if (_Attack(ref wHitMode, AttackTarget))
                {
                    SetTargetCreat(AttackTarget);
                }
                wIdent = Grobal2.RM_HIT;
                if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                {
                    switch (wHitMode)
                    {
                        case 0:
                            wIdent = Grobal2.RM_HIT;
                            break;
                        case 1:
                            wIdent = Grobal2.RM_HEAVYHIT;
                            break;
                        case 2:
                            wIdent = Grobal2.RM_BIGHIT;
                            break;
                        case 3:
                            if (boPowerHit)
                            {
                                wIdent = Grobal2.RM_POWERHIT;
                            }
                            break;
                        case 4:
                            if (m_MagicArr[SpellsDef.SKILL_ERGUM] != null)
                            {
                                wIdent = Grobal2.RM_LONGHIT;
                            }
                            break;
                        case 5:
                            if (m_MagicArr[SpellsDef.SKILL_BANWOL] != null)
                            {
                                wIdent = Grobal2.RM_WIDEHIT;
                            }
                            break;
                        case 7:
                            if (boFireHit)
                            {
                                wIdent = Grobal2.RM_FIREHIT;
                            }
                            break;
                        case 8:
                            if (m_MagicArr[SpellsDef.SKILL_CROSSMOON] != null)
                            {
                                wIdent = Grobal2.RM_CRSHIT;
                            }
                            break;
                        case 9:
                            if (boTwinHit)
                            {
                                wIdent = Grobal2.RM_TWINHIT;
                            }
                            break;
                        case 12:
                            if (m_MagicArr[SpellsDef.SKILL_REDBANWOL] != null)
                            {
                                wIdent = Grobal2.RM_WIDEHIT;
                            }
                            break;
                    }
                }
                SendAttackMsg(wIdent, m_btDirection, m_nCurrX, m_nCurrY);
            }
            catch (Exception e)
            {
                M2Share.ErrorMessage(sExceptionMsg);
                M2Share.ErrorMessage(e.Message);
            }
        }

        protected void SendAttackMsg(short wIdent, byte btDir, int nX, int nY)
        {
            SendRefMsg(wIdent, btDir, nX, nY, 0, "");
        }

        
        
        
        
        private bool CheckWeaponUpgradeStatus(ref TUserItem UserItem)
        {
            var status = UserItem.btValue[9];
            var upgraded = false;
            if ((UserItem.btValue[0] + UserItem.btValue[1] + UserItem.btValue[2]) < 7)
            {
                if (HUtil32.RangeInDefined(status, 10, 13))
                {
                    UserItem.btValue[0] = (byte)(UserItem.btValue[0] + status - 9);
                    upgraded = true;
                }
                else if (HUtil32.RangeInDefined(status, 20, 23))
                {
                    UserItem.btValue[1] = (byte)(UserItem.btValue[1] + status - 19);
                    upgraded = true;
                }
                else if (HUtil32.RangeInDefined(status, 30, 33))
                {
                    UserItem.btValue[2] = (byte)(UserItem.btValue[2] + status - 29);
                    upgraded = true;
                }
                else if (HUtil32.RangeInDefined(status, 40, 43))
                {
                    upgraded = true;
                }
            }
            if (!upgraded && (UserItem.UpgradeFlags & 0x80) == 0)
            {
                UserItem.wIndex = 0;
            }
            UserItem.btValue[9] = 0;
            UserItem.UpgradeFlags = 0;
            return upgraded;
        }

        private void CheckWeaponUpgrade()
        {
            TUserItem UseItems;
            TPlayObject PlayObject;
            GoodItem StdItem;
            if (m_UseItems[Grobal2.U_WEAPON] != null && m_UseItems[Grobal2.U_WEAPON].btValue[9] > 0)
            {
                UseItems = new TUserItem(m_UseItems[Grobal2.U_WEAPON]);
                var upgraded = CheckWeaponUpgradeStatus(ref m_UseItems[Grobal2.U_WEAPON]);
                if (m_UseItems[Grobal2.U_WEAPON].wIndex == 0)
                {
                    SysMsg(M2Share.g_sTheWeaponBroke, MsgColor.Red, MsgType.Hint);
                    PlayObject = this as TPlayObject;
                    PlayObject.SendDelItems(UseItems);
                    SendRefMsg(Grobal2.RM_BREAKWEAPON, 0, 0, 0, 0, "");
                    StdItem = M2Share.UserEngine.GetStdItem(UseItems.wIndex);
                    if (StdItem != null)
                    {
                        if (StdItem.NeedIdentify == 1)
                        {
                            M2Share.AddGameDataLog("21" + "\t" + m_sMapName + "\t" + m_nCurrX + "\t" + m_nCurrY + "\t" + m_sCharName + "\t" + StdItem.Name + "\t" + UseItems.MakeIndex + "\t" + '1' + "\t" + '0');
                        }
                    }
                    FeatureChanged();
                }
                else
                {
                    SysMsg(upgraded ? M2Share.sTheWeaponRefineSuccessfull : "你的武器升级失败", MsgColor.Red, MsgType.Hint);
                    PlayObject = this as TPlayObject;
                    PlayObject.SendUpdateItem(m_UseItems[Grobal2.U_WEAPON]);
                    StdItem = M2Share.UserEngine.GetStdItem(UseItems.wIndex);
                    if (StdItem.NeedIdentify == 1)
                    {
                        M2Share.AddGameDataLog("20" + "\t" + m_sMapName + "\t" + m_nCurrX + "\t" + m_nCurrY + "\t" + m_sCharName + "\t" + StdItem.Name + "\t" + UseItems.MakeIndex + "\t" + '1' + "\t" + '0');
                    }
                    RecalcAbilitys();
                    SendMsg(this, Grobal2.RM_ABILITY, 0, 0, 0, 0, "");
                }
            }
            UseItems = null;
        }

        
        private bool _Attack_DirectAttack(TBaseObject BaseObject, int nSecPwr)
        {
            bool result = false;
            if ((m_btRaceServer == Grobal2.RC_PLAYOBJECT) || (BaseObject.m_btRaceServer == Grobal2.RC_PLAYOBJECT) || !(InSafeZone() && BaseObject.InSafeZone()))
            {
                if (IsProperTarget(BaseObject))
                {
                    if (M2Share.RandomNumber.Random(BaseObject.m_wSpeedPoint) < m_btHitPoint)
                    {
                        // Native 0x66B8C5 / 0x6829C1 / 0x682EA9: the wrapper
                        // receives the attacker as a stack arg
                        // (`mov ecx,[ebp+0x18]; xchg edx,ecx`) and forwards it
                        // to +0xA8. Here that attacker is `this`.
                        nSecPwr = BaseObject.ApplyNativePhysicalCritical(this, nSecPwr);
                        BaseObject.StruckDamage(nSecPwr, this);
                        BaseObject.SendDelayMsg(Grobal2.RM_STRUCK, Grobal2.RM_10101, (short)nSecPwr, BaseObject.m_WAbil.HP, BaseObject.m_WAbil.MaxHP, ObjectId, "", 500);
                        if (BaseObject.m_btRaceServer != Grobal2.RC_PLAYOBJECT)
                        {
                            BaseObject.SendMsg(BaseObject, Grobal2.RM_STRUCK, (short)nSecPwr, BaseObject.m_WAbil.HP, BaseObject.m_WAbil.MaxHP, ObjectId, "");
                        }
                        result = true;
                    }
                }
            }
            return result;
        }

        
        
        
        
        
        private bool SwordLongAttack(ref int nSecPwr)
        {
            bool result = false;
            short nX = 0;
            short nY = 0;
            nSecPwr = HUtil32.Round(nSecPwr * M2Share.g_Config.nSwordLongPowerRate / 100);
            if (m_PEnvir.GetNextPosition(m_nCurrX, m_nCurrY, m_btDirection, 2, ref nX, ref nY))
            {
                TBaseObject BaseObject = (TBaseObject)m_PEnvir.GetMovingObject(nX, nY, true);
                if (BaseObject != null)
                {
                    if ((nSecPwr > 0) && IsProperTarget(BaseObject))
                    {
                        result = _Attack_DirectAttack(BaseObject, nSecPwr);
                        SetTargetCreat(BaseObject);
                    }
                    result = true;
                }
            }
            return result;
        }

        
        
        
        
        
        private bool SwordWideAttack(ref int nSecPwr)
        {
            bool result = false;
            int nC = 0;
            int n10 = 0;
            short nX = 0;
            short nY = 0;
            TBaseObject BaseObject;
            while (true)
            {
                n10 = (m_btDirection + M2Share.g_Config.WideAttack[nC]) % 8;
                if (m_PEnvir.GetNextPosition(m_nCurrX, m_nCurrY, n10, 1, ref nX, ref nY))
                {
                    BaseObject = (TBaseObject)m_PEnvir.GetMovingObject(nX, nY, true);
                    if ((nSecPwr > 0) && (BaseObject != null) && IsProperTarget(BaseObject))
                    {
                        result = _Attack_DirectAttack(BaseObject, nSecPwr);
                        SetTargetCreat(BaseObject);
                    }
                }
                nC++;
                if (nC >= 3)
                {
                    break;
                }
            }
            return result;
        }

        private bool CrsWideAttack(int nSecPwr)
        {
            bool result = false;
            int nC = 0;
            int n10 = 0;
            short nX = 0;
            short nY = 0;
            TBaseObject BaseObject;
            while (true)
            {
                n10 = (m_btDirection + M2Share.g_Config.CrsAttack[nC]) % 8;
                if (m_PEnvir.GetNextPosition(m_nCurrX, m_nCurrY, n10, 1, ref nX, ref nY))
                {
                    BaseObject = (TBaseObject)m_PEnvir.GetMovingObject(nX, nY, true);
                    if ((nSecPwr > 0) && (BaseObject != null) && IsProperTarget(BaseObject))
                    {
                        result = _Attack_DirectAttack(BaseObject, nSecPwr);
                        SetTargetCreat(BaseObject);
                    }
                }
                nC++;
                if (nC >= 7)
                {
                    break;
                }
            }
            return result;
        }

        // A player's half moon runs sub_771E9C, reached from the client command
        // dispatcher sub_6EC078 (0x006EC280 E8 23 45 08 00 call 0x7707A8 with
        // cx = 0x3ED).
        //   0x0077203A  E8 2D 69 D5 FF     call sub_4C896C -> effective level
        //   0x0077203F  25 FF 00 00 00     and eax,0xFF
        //   0x00772044  83 C0 02           add eax,2
        //   0x00772050  D8 35 48 21 77 00  fdiv dword ptr [0x772148]
        // [0x772148] = 00 00 70 41 = float32 15.0, a literal, not btTrainLv + 10.
        // The AttackDir/monster twin sub_769F90 uses the same 15.0 constant at
        // [0x76A5C8]. Effective level 4 diverts to a whole-map TList sweep
        // (0x00771F2F 3C 04 cmp al,4) that C# does not implement; see
        // staging/ys_skills_impl_20260813.md.
        internal static int CalculateHalfMoonWideAttackPower(int nPower, int trainLevel,
            int skillLevel, Plugins.YanshenApi yanshenApi)
        {
            int effectiveLevel = Math.Min(unchecked((byte)skillLevel), trainLevel);
            // Same two-constant swap as 刺杀 (0x100B42F2 -> imm8 of 0x00772044
            // `83 C0 02`, 0x100B4360 -> [0x00772148]), with one asymmetry that
            // matters: 半月 is the only one of the six whose high-level arm the
            // plugin leaves alone. 刺杀 gets `EB 17` over 0x00771C25 and 烈火
            // gets `EB 15` over 0x0077231D, but no blob patch targets the
            // half-moon branch in either _Attack copy, so above the scaling
            // level the native arm still wins and A/B never enter the result.
            if (effectiveLevel <= 3 && yanshenApi != null && yanshenApi.IsHalfMoon())
            {
                float divisor = yanshenApi.HalfMoonB();
                if (divisor > 0)
                {
                    return HUtil32.Round((double)nPower / divisor
                        * (effectiveLevel + yanshenApi.HalfMoonA()));
                }
            }

            return HUtil32.Round((double)nPower / 15.0 * (effectiveLevel + 2));
        }

        // A player's stab sword runs sub_771BC4, reached from the client command
        // dispatcher sub_6EC078 (0x006EC26B E8 38 45 08 00 call 0x7707A8 with
        // cx = 0x3EC).
        //   0x00771C1E / 0x00771C44  call sub_4C896C  -> effective level
        //   0x00771C23  3C 04              cmp al,4
        //   0x00771C2A  DB 2D 18 1D 77 00  fld tbyte ptr [0x771D18]  (80-bit 1.05)
        //   0x00771C39  83 C7 05           add edi,5
        //   0x00771C5A  D8 35 24 1D 77 00  fdiv dword ptr [0x771D24]
        // [0x771D24] = 00 00 A0 40 = float32 5.0. The divisor is that literal;
        // btTrainLv (+0x1A) is only ever the level cap inside sub_4C896C. The
        // AttackDir/monster twin sub_769F90 carries byte-identical constants at
        // [0x76A5C4] and [0x76A5B8].
        internal static int CalculateStabSwordLongAttackPower(int nPower, int trainLevel,
            int skillLevel, Plugins.YanshenApi yanshenApi)
        {
            int effectiveLevel = Math.Min(unchecked((byte)skillLevel), trainLevel);
            // Override = the same native chain with two constants swapped:
            //   0x00771C4E  83 C0 02              add eax,2   <- imm8 becomes A
            //   0x00771C5A  D8 35 24 1D 77 00     fdiv [0x00771D24] <- becomes B
            //   plugin 0x100B40BB / 0x100B4129 write those two
            // Divide first, then multiply: that is the fdiv/fmulp order, and B
            // is the float32 the patch stores, not a double.
            // The btLevel==4 tier is patched out while the override is on --
            // 0x100B417C splices `EB 17` over 0x00771C25 `75 17`.
            if (yanshenApi != null && yanshenApi.IsStabSword())
            {
                float divisor = yanshenApi.StabSwordB();
                if (divisor > 0)
                {
                    return HUtil32.Round((double)nPower / divisor
                        * (effectiveLevel + yanshenApi.StabSwordA()));
                }
            }

            if (effectiveLevel == 4)
            {
                return MultiplyByNative105(nPower) + 5;
            }
            return HUtil32.Round((double)nPower / 5.0 * (effectiveLevel + 2));
        }

        // tbyte[0x771D18] = 66 66 66 66 66 66 66 86 FF 3F, exactly
        // 4842270319348757299 / 2^62 = 21/20 - 1/(5*2^62): strictly below 1.05,
        // whereas IEEE double 1.05 is strictly above it. The product differs from
        // 21n/20 by less than the .5-boundary granularity except when 21n/20 is
        // exactly a half-integer (n % 20 == 10), and there the deficit only
        // survives the 64-bit-significand rounding when 4n > 5*2^e for
        // 2^e <= 21n/20 < 2^(e+1). Checked against an exact-rational x87 model
        // over n in 0..1000000: zero mismatches.
        private static int MultiplyByNative105(int nPower)
        {
            long scaled = 21L * nPower;
            if (nPower > 0 && nPower % 20 == 10)
            {
                long pow2 = 1;
                while (pow2 * 40 <= scaled)
                {
                    pow2 <<= 1;
                }
                if (4L * nPower > 5L * pow2)
                {
                    return (int)(scaled / 20);
                }
            }
            return HUtil32.Round(scaled / 20.0);
        }

        // The 攻杀 override is not a damage-time multiplier. The plugin rewrites
        // the imm8 of the recalc site that produces m_nHitPlus:
        //   host  0x0076B027  8B C6 E8 40 D9 D5 FF   mov eax,esi; call sub_4C896C
        //   host  0x0076B02C  04 05                  add al,5
        //   host  0x0076B02E  88 83 90 00 00 00      mov [ebx+0x90],al
        //   plugin 0x100B3F5A A2 2D B0 76 00         mov byte[0x0076B02D],al
        // Two consequences the old expression missed: the level is sub_4C896C's
        // EFFECTIVE level, and `add al` is 8-bit, so (level + A) wraps at 256
        // instead of saturating at 255.
        internal static int CalculateThrustingHitPlus(int nativeHitPlus, int skillLevel,
            Plugins.YanshenApi yanshenApi)
        {
            if (yanshenApi != null && yanshenApi.IsThrusting())
            {
                return unchecked((byte)(yanshenApi.ThrustingA() + skillLevel));
            }

            return nativeHitPlus;
        }

        // The plugin rewrites the two instructions that build m_nHitDouble and
        // nothing else -- the damage arithmetic below is untouched native code,
        // so the override changes only the byte fed into it:
        //   host   0x0076B0EC  C1 E0 02        shl eax,2   <- A picks the shift
        //   host   0x0076B0EF  04 04           add al,4    <- imm8 becomes B
        //   plugin 0x100B45A3  blob patch 3 bytes @0x0076B0EC
        //   plugin 0x100B4550  A2 F0 B0 76 00  mov byte[0x0076B0F0],al
        // `shl eax,k` cannot encode an arbitrary A, so FireSwordLevelFactor
        // holds the five representable multipliers; and because the addend is
        // 8-bit the result wraps at 256 rather than stopping at the 25.5x the
        // dialog text advertises (that number is just 255/10).
        //
        // The plugin also kills the btLevel==4 arm outright:
        //   host   0x0077231D  75 15   jne 0x00772334
        //   plugin 0x100B45DA  blob patch 2 bytes @0x0077231D with EB 15 (jmp)
        // so the fixed 1.8x tier is unreachable while the override is on.
        internal static int CalculateFireSwordAttackPower(int nPower, int nativeHitDouble,
            int skillLevel, Plugins.YanshenApi yanshenApi)
        {
            if (yanshenApi != null && yanshenApi.IsFireSword())
            {
                var factor = Plugins.YanshenApi.FireSwordLevelFactor(yanshenApi.FireSwordA());
                nativeHitDouble = unchecked((byte)(
                    unchecked((byte)(skillLevel * factor)) + yanshenApi.FireSwordB()));
            }

            // A player's fire sword runs sub_7722BC: the client command
            // dispatcher sub_6EC078 (its only callers are 0x006D9F06 and
            // 0x006D9FA2, the attack-family CM handlers) reaches it through
            // 0x006EC295 E8 0E 45 08 00 call 0x7707A8 with cx = 0x3EF. That
            // branch is pure integer arithmetic -- no FPU, no rounding:
            //   0x00772336  8A 87 91 00 00 00  mov al, byte ptr [edi+0x91]
            //   0x0077233C  F7 6D FC           imul dword ptr [ebp-4]
            //   0x0077233F  B9 0A 00 00 00     mov ecx,0xA
            //   0x00772344  99                 cdq
            //   0x00772345  F7 F9              idiv ecx
            //   0x00772347  01 45 FC           add dword ptr [ebp-4], eax
            // idiv truncates toward zero, matching C# integer division. The
            // fdiv/@ROUND form at 0x0076A06F belongs to sub_769F90, which only
            // the AttackDir path (monsters and AI actors) reaches.
            return nPower + nPower * nativeHitDouble / 10;
        }

        // Native builds the multiplier byte from a table and then divides with
        // integer idiv; the override replaces the table lookup and the divisor
        // literal, and deletes the clamp that guarded the lookup:
        //   host   0x0076B13E  73 1D                  jae 0x0076B15D  (L>=4 -> Tbl[3])
        //   host   0x0076B14C  8A 80 28 4B 7D 00      mov al,[eax+0x7D4B28]
        //   host   0x0076B152  88 83 92 00 00 00      mov [ebx+0x92],al
        //   host   0x00771DA0  F7 6D FC               imul [ebp-4]
        //   host   0x00771DA3  B9 0A 00 00 00         mov ecx,0xA
        //   host   0x00771DA9  F7 F9                  idiv ecx
        //   plugin 0x100B4787  blob 2 bytes @0x0076B13E -> 90 90 (clamp gone)
        //   plugin 0x100B4750  blob 6 bytes @0x0076B14C -> 04 07 90 90 90 90
        //   plugin 0x100B47D9  A2 4D B1 76 00         -> the `add al` imm8 = A
        //   plugin 0x100B4847  A3 A4 1D 77 00         -> the idiv divisor = B
        internal static int CalculateSunSwordAttackPower(int power, int effectiveLevel,
            Plugins.YanshenApi yanshenApi)
        {
            if (yanshenApi != null && yanshenApi.IsSunSword())
            {
                var divisor = yanshenApi.SunSwordB();
                if (divisor > 0)
                {
                    // `add al` is 8-bit, so the multiplier wraps at 256; and the
                    // native division is idiv, which truncates toward zero.
                    var multiplier = unchecked((byte)(effectiveLevel + yanshenApi.SunSwordA()));
                    return power * multiplier / divisor;
                }
            }

            var level = Math.Min(Math.Max(effectiveLevel, 0), 3);
            return power * (12 + 3 * level) / 10;
        }

        internal static int CalculateSunSwordDistancePower(int scaledPower, int distance)
        {
            return scaledPower * (11 - distance) / 10;
        }

        internal static byte[] BuildSunSwordPhysicalAttackBody(int skillLevel,
            int direction, int x, int y)
        {
            return BuildSunSwordPhysicalAttackBody(1015, skillLevel,
                direction, x, y);
        }

        private static byte[] BuildSunSwordPhysicalAttackBody(int attackId,
            int skillLevel, int direction, int x, int y)
        {
            var body = new byte[12];
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(0, 2),
                unchecked((ushort)attackId));
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(2, 2),
                unchecked((ushort)skillLevel));
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(4, 2), 0);
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(6, 2),
                unchecked((ushort)direction));
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(8, 2),
                unchecked((ushort)x));
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(10, 2),
                unchecked((ushort)y));
            return body;
        }

        // sub_4C896C @0x004C896C:
        //   8A 50 0C     mov dl,[eax+0x0C]        btLevel
        //   02 50 18     add dl,[eax+0x18]        + NativeLevelBonus   (8-bit)
        //   8B 08 / 8A 49 1A / 3A D1 / 76 02 / 8B D1   min(.., btTrainLv)
        // Every recalc arm the plugin patches feeds off this, never off btLevel.
        internal static int NativeEffectiveMagicLevel(TUserMagic magic)
        {
            if (magic == null) return 0;
            if (magic.MagicInfo == null) return magic.btLevel;
            return Math.Min(unchecked((byte)(magic.btLevel + magic.NativeLevelBonus)),
                magic.MagicInfo.btTrainLv);
        }

        private static int GetSunSwordEffectiveLevel(TUserMagic magic)
        {
            return magic == null
                ? 0
                : Math.Min(unchecked((byte)(magic.btLevel + magic.NativeLevelBonus)),
                    magic.MagicInfo.btTrainLv);
        }

        private TUserMagic GetSunSwordFallbackMagic()
        {
            TUserMagic fallback = null;
            for (var index = 0; index < m_MagicList.Count; index++)
            {
                var candidate = m_MagicList[index];
                if (candidate?.MagicInfo?.wMagicID ==
                        SpellsDef.SKILL_ONESWORD ||
                    candidate?.MagicInfo?.wMagicID ==
                        SpellsDef.SKILL_ILKWANG)
                {
                    fallback = candidate;
                }
            }
            return fallback;
        }

        protected bool ReleaseSunSword(byte direction)
        {
            var player = this as TPlayObject;
            var magic = m_MagicArr[SpellsDef.SKILL_58];
            if (player == null || magic == null || !m_boSunSwordReady)
            {
                return false;
            }

            void SendPhysicalAttack(int attackId, int skillLevel)
            {
                SendRefMsg(Grobal2.RM_PHYSICAL_ATT, attackId,
                    m_nCurrX, m_nCurrY, 0, "",
                    BuildSunSwordPhysicalAttackBody(attackId, skillLevel,
                        direction, m_nCurrX, m_nCurrY));
            }

            var spellPoint = player.GetSpellPoint(magic);
            if (m_WAbil.MP < spellPoint)
            {
                AttackDir(null, 0, direction);
                SendPhysicalAttack(1000,
                    GetSunSwordEffectiveLevel(GetSunSwordFallbackMagic()));
                return true;
            }

            if (spellPoint > 0)
            {
                DamageSpell(spellPoint);
                HealthSpellChanged();
            }
            m_boSunSwordReady = false;
            m_btDirection = direction;

            var effectiveLevel = Math.Min(
                unchecked((byte)(magic.btLevel + magic.NativeLevelBonus)),
                magic.MagicInfo.btTrainLv);
            var basePower = GetAttackPower(HUtil32.LoWord(m_WAbil.DC),
                HUtil32.HiWord(m_WAbil.DC) - HUtil32.LoWord(m_WAbil.DC));
            var scaledPower = CalculateSunSwordAttackPower(basePower, effectiveLevel,
                new Plugins.YanshenApi(player, null, M2Share.PluginManager));
            var canTrain = false;

            for (var distance = 1; distance <= 4; distance++)
            {
                short x = 0;
                short y = 0;
                if (!m_PEnvir.GetNextPosition(m_nCurrX, m_nCurrY, direction,
                        distance, ref x, ref y))
                {
                    continue;
                }

                var target = m_PEnvir.GetMovingObject(x, y, true) as TBaseObject;
                if (target == null || !IsProperTarget(target))
                {
                    continue;
                }

                if (target.m_btRaceServer >= Grobal2.RC_ANIMAL)
                {
                    canTrain = true;
                }
                if (m_TargetCret == null)
                {
                    SetTargetCreat(target);
                }
                if (M2Share.RandomNumber.Random(target.m_wSpeedPoint) >= m_btHitPoint)
                {
                    continue;
                }

                var damage = (int)target.GetHitStruckDamage(this,
                    CalculateSunSwordDistancePower(scaledPower, distance));
                damage = target.ApplyNativePhysicalCritical(this, damage);
                // Native supplies the attacker in ecx at every +0xA8 site, e.g.
                // 0x666CEA `call [ebx+0x1B4]` then 0x666D06 `mov ecx,edi;
                // call [ebx+0xA8]` with the SAME edi. `this` is that edi here.
                target.StruckDamage(damage, this);
                target.SendDelayMsg(Grobal2.RM_STRUCK, Grobal2.RM_10101,
                    damage, target.m_WAbil.HP, target.m_WAbil.MaxHP, ObjectId, "", 500);
                if (target.m_btRaceServer != Grobal2.RC_PLAYOBJECT)
                {
                    target.SendMsg(target, Grobal2.RM_STRUCK, damage,
                        target.m_WAbil.HP, target.m_WAbil.MaxHP, ObjectId, "");
                }
            }

            if (canTrain && magic.btLevel < 3 &&
                magic.MagicInfo.TrainLevel[magic.btLevel] <= m_Abil.Level)
            {
                player.TrainSkill(magic, M2Share.RandomNumber.Random(3) + 1);
                if (!player.CheckMagicLevelup(magic))
                {
                    SendUpdateDelayMsg(this, Grobal2.RM_MAGIC_LVEXP, 0,
                        magic.MagicInfo.wMagicID, magic.btLevel, magic.nTranPoint, "", 3000);
                }
            }

            SendPhysicalAttack(1015, effectiveLevel);
            return true;
        }

        public void _Attack_sub_4C1E5C_sub_4C1DC0(ref TBaseObject BaseObject, byte btDir, ref short nX, ref short nY, int nSecPwr)
        {
            if (m_PEnvir.GetNextPosition(m_nCurrX, m_nCurrY, btDir, 1, ref nX, ref nY))
            {
                BaseObject = (TBaseObject)m_PEnvir.GetMovingObject(nX, nY, true);
                if ((nSecPwr > 0) && (BaseObject != null))
                {
                    _Attack_DirectAttack(BaseObject, nSecPwr);
                }
            }
        }

        public void _Attack_sub_4C1E5C(int nSecPwr)
        {
            short nX = 0;
            short nY = 0;
            TBaseObject BaseObject = null;
            byte btDir = m_btDirection;
            m_PEnvir.GetNextPosition(m_nCurrX, m_nCurrY, btDir, 1, ref nX, ref nY);
            _Attack_sub_4C1E5C_sub_4C1DC0(ref BaseObject, btDir, ref nX, ref nY, nSecPwr);
            btDir = M2Share.sub_4B2F80(m_btDirection, 2);
            _Attack_sub_4C1E5C_sub_4C1DC0(ref BaseObject, btDir, ref nX, ref nY, nSecPwr);
            btDir = M2Share.sub_4B2F80(m_btDirection, 6);
            _Attack_sub_4C1E5C_sub_4C1DC0(ref BaseObject, btDir, ref nX, ref nY, nSecPwr);
        }

        public bool _Attack(ref short wHitMode, TBaseObject AttackTarget)
        {
            int n20;
            bool result = false;
            try
            {
                bool bo21 = false;
                int nWeaponDamage = 0;
                int nPower = 0;
                int nSecPwr = 0;
                if (AttackTarget != null)
                {
                    nPower = GetAttackPower(HUtil32.LoWord(m_WAbil.DC), HUtil32.HiWord(m_WAbil.DC) - HUtil32.LoWord(m_WAbil.DC));
                    if ((wHitMode == 3) && m_boPowerHit)
                    {
                        m_boPowerHit = false;
                        var magic = m_MagicArr[SpellsDef.SKILL_YEDO];
                        nPower += CalculateThrustingHitPlus(
                            m_nHitPlus,
                            NativeEffectiveMagicLevel(magic),
                            m_btRaceServer == Grobal2.RC_PLAYOBJECT && magic != null
                                ? new Plugins.YanshenApi(this as TPlayObject, null, M2Share.PluginManager)
                                : null);
                        bo21 = true;
                    }
                    if ((wHitMode == 7) && m_boFireHitSkill)
                    {
                        m_boFireHitSkill = false;
                        var magic = m_MagicArr[SpellsDef.SKILL_FIRESWORD];
                        nPower = CalculateFireSwordAttackPower(
                            nPower,
                            m_nHitDouble,
                            NativeEffectiveMagicLevel(magic),
                            m_btRaceServer == Grobal2.RC_PLAYOBJECT && magic != null
                                ? new Plugins.YanshenApi(this as TPlayObject, null, M2Share.PluginManager)
                                : null);
                        bo21 = true;
                    }
                    if ((wHitMode == 9) && m_boTwinHitSkill)
                    {
                        m_boTwinHitSkill = false;
                        m_dwLatestTwinHitTick = HUtil32.GetTickCount();// Jacky 禁止双烈火
                        nPower = nPower + HUtil32.Round(nPower / 100.0 * (m_nHitDouble * 10));
                        bo21 = true;
                    }
                }
                else
                {
                    nPower = GetAttackPower(HUtil32.LoWord(m_WAbil.DC), HUtil32.HiWord(m_WAbil.DC) - HUtil32.LoWord(m_WAbil.DC));
                    if ((wHitMode == 3) && m_boPowerHit)
                    {
                        m_boPowerHit = false;
                        var magic = m_MagicArr[SpellsDef.SKILL_YEDO];
                        nPower += CalculateThrustingHitPlus(
                            m_nHitPlus,
                            NativeEffectiveMagicLevel(magic),
                            m_btRaceServer == Grobal2.RC_PLAYOBJECT && magic != null
                                ? new Plugins.YanshenApi(this as TPlayObject, null, M2Share.PluginManager)
                                : null);
                        bo21 = true;
                    }

                    // Only consume FireHit/TwinHit when there's actually a target to hit
                    if (AttackTarget != null)
                    {
                        if ((wHitMode == 7) && m_boFireHitSkill)
                        {
                            m_boFireHitSkill = false;
                            nPower = nPower * 260 / 100;  // FireHit: 2.6x power multiplier
                        }
                        if ((wHitMode == 9) && m_boTwinHitSkill)
                        {
                            m_boTwinHitSkill = false;
                            m_dwLatestTwinHitTick = HUtil32.GetTickCount();
                            nPower = nPower * 180 / 100;  // TwinHit: 1.8x power multiplier
                        }
                    }
                }
                if (wHitMode == 4)
                {

                    nSecPwr = 0;
                    if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                    {
                        if (m_MagicArr[SpellsDef.SKILL_ERGUM] != null)
                        {
                            var magic = m_MagicArr[SpellsDef.SKILL_ERGUM];
                            nSecPwr = CalculateStabSwordLongAttackPower(
                                nPower,
                                magic.MagicInfo.btTrainLv,
                                magic.btLevel,
                                new Plugins.YanshenApi(this as TPlayObject, null, M2Share.PluginManager));
                        }
                    }
                    if (nSecPwr > 0)
                    {
                        if (!SwordLongAttack(ref nSecPwr) && M2Share.g_Config.boLimitSwordLong)
                        {
                            wHitMode = 0;
                        }
                    }
                }
                if (wHitMode == 5)
                {
                    nSecPwr = 0;
                    if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                    {
                        if (m_MagicArr[SpellsDef.SKILL_BANWOL] != null)
                        {
                            var magic = m_MagicArr[SpellsDef.SKILL_BANWOL];
                            nSecPwr = CalculateHalfMoonWideAttackPower(
                                nPower,
                                magic.MagicInfo.btTrainLv,
                                magic.btLevel,
                                new Plugins.YanshenApi(this as TPlayObject, null, M2Share.PluginManager));
                        }
                    }
                    if (nSecPwr > 0)
                    {
                        SwordWideAttack(ref nSecPwr);
                    }
                }
                if (wHitMode == 12)
                {
                    nSecPwr = 0;
                    if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                    {
                        if (m_MagicArr[SpellsDef.SKILL_REDBANWOL] != null)
                        {
                            nSecPwr = HUtil32.Round((double)nPower / (m_MagicArr[SpellsDef.SKILL_REDBANWOL].MagicInfo.btTrainLv + 10) * (m_MagicArr[SpellsDef.SKILL_REDBANWOL].btLevel + 2));
                        }
                    }
                    if (nSecPwr > 0)
                    {
                        SwordWideAttack(ref nSecPwr);
                    }
                }
                // wHitMode 6: removed dead code (nSecPwr set to 0 then immediately checked >0)
                if (wHitMode == 8)
                {
                    nSecPwr = 0;
                    if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                    {
                        if (m_MagicArr[SpellsDef.SKILL_CROSSMOON] != null)
                        {
                            nSecPwr = HUtil32.Round((double)nPower / (m_MagicArr[SpellsDef.SKILL_CROSSMOON].MagicInfo.btTrainLv + 10) * (m_MagicArr[SpellsDef.SKILL_CROSSMOON].btLevel + 2));
                        }
                    }
                    if (nSecPwr > 0)
                    {
                        CrsWideAttack(nSecPwr);
                    }
                }
                if (AttackTarget == null)
                {
                    return result;
                }
                if (IsProperTarget(AttackTarget))
                {
                    if (M2Share.RandomNumber.Random(AttackTarget.m_wSpeedPoint) < m_btHitPoint)
                    {
                        // HIT -- nPower stays as computed
                    }
                    else
                    {
                        nPower = 0;
                    }
                }
                else
                {
                    nPower = 0;
                }
                if (nPower > 0)
                {
                    nPower = AttackTarget.GetHitStruckDamage(this, nPower);
                    nPower = AttackTarget.ApplyNativePhysicalCritical(this, nPower);
                    nWeaponDamage = M2Share.RandomNumber.Random(5) + 2 - m_AddAbil.btWeaponStrong;
                }
                if (nPower > 0)
                {
                    // Native 0x71D9C2 `call [edi+0x1B4]` then 0x71D9D8
                    // `mov ecx,ebx; call [edi+0xA8]` — the same ebx feeds
                    // GetHitStruckDamage and StruckDamage.
                    AttackTarget.StruckDamage(nPower, this);
                    AttackTarget.SendDelayMsg(Grobal2.RM_STRUCK, Grobal2.RM_10101, nPower, AttackTarget.m_WAbil.HP, AttackTarget.m_WAbil.MaxHP, ObjectId, "", 200);
                    TryApplyNativeState26AfterPhysicalDamage(AttackTarget, nPower);
                    // POIS-36/37 — native hit-poison @0x666D2F-0x666D4E, bytes verified:
                    //   666D2F  0F B7 86 6C 02 00 00  movzx eax, word [esi+0x26C]  ; target resistance
                    //   666D36  83 C0 14              add   eax, 0x14              ; +20, hardcoded
                    //   666D39  E8 0E CE D9 FF        call  0x403B4C               ; Random(res+20)
                    //   666D3E  85 C0 / 75 12         test  eax,eax / jne skip     ; only ==0 passes
                    //   666D42  6A 01                 push  1                      ; level 1
                    //   666D44  66 B9 1E 00           mov   cx, 0x1E               ; 30 seconds
                    //   666D48  B2 1F                 mov   dl, 0x1F               ; bodyState 0x1F = green poison
                    //   666D4E  FF 93 C8 00 00 00     call  [ebx+0xC8]             ; MakePosion wrapper
                    // Three corrections against the previous line:
                    //  1. dl=0x1F is the damage-over-time green poison. POISON_STONE (slot 5)
                    //     maps to bit 26 = bodyState 0x1A = petrify, a hard disable that no
                    //     native site applies with 30s/level 1. The slot->bit mapping
                    //     (0x80000000 >> slot, so slot 0 -> bit 31 -> 0x1F) is correct, so the
                    //     "different index spaces" premise behind POISON_STONE does not hold.
                    //  2. the roll reads Self+0x26C. Band state 0x5E (@0x773A2A `add word
                    //     [esi+8], ax`, esi = Self+0x264) buffs that same word and is modelled
                    //     as m_wEffectResistance, so Self+0x26C == m_wEffectResistance,
                    //     not m_btAntiPoison.
                    //  3. +20 and 30 seconds are immediates, not configuration.
                    // POIS-37 — native paralysis-weapon roll @0x6670AB: no UnParalysis gate
                    // (contrast AOE green poison @0x666D26, same +0x26C/+0x14/==0 shape).
                    if (m_boParalysis && (M2Share.RandomNumber.Random(AttackTarget.m_wEffectResistance + 20) == 0))
                    {
                        AttackTarget.MakePosion(Grobal2.POISON_DECHEALTH, 30, 1);
                    }
                    if (m_nHongMoSuite > 0)// 虹魔，吸血
                    {
                        m_db3B0 = nPower / 100.0 * m_nHongMoSuite;
                        if (m_db3B0 >= 2.0)
                        {
                            n20 = Convert.ToInt32(m_db3B0);
                            m_db3B0 = n20;
                            DamageHealth(-n20);
                        }
                    }
                    if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                    {
                        TUserMagic attackMagic = null;
                        if ((m_MagicArr[SpellsDef.SKILL_ILKWANG] != null))
                        {
                            attackMagic = GetAttrackMagic(SpellsDef.SKILL_ILKWANG);
                            if ((attackMagic.btLevel < 3) && (attackMagic.MagicInfo.TrainLevel[attackMagic.btLevel] <= m_Abil.Level))
                            {
                                (this as TPlayObject).TrainSkill(attackMagic, M2Share.RandomNumber.Random(3) + 1);
                                if (!(this as TPlayObject).CheckMagicLevelup(attackMagic))
                                {
                                    SendDelayMsg(this, Grobal2.RM_MAGIC_LVEXP, 0, attackMagic.MagicInfo.wMagicID, attackMagic.btLevel, attackMagic.nTranPoint, "", 3000);
                                }
                            }
                        }
                        if (bo21 && (m_MagicArr[SpellsDef.SKILL_YEDO] != null))
                        {
                            attackMagic = GetAttrackMagic(SpellsDef.SKILL_YEDO);
                            if ((attackMagic.btLevel < 3) && (attackMagic.MagicInfo.TrainLevel[attackMagic.btLevel] <= m_Abil.Level))
                            {
                                (this as TPlayObject).TrainSkill(attackMagic, M2Share.RandomNumber.Random(3) + 1);
                                if (!(this as TPlayObject).CheckMagicLevelup(attackMagic))
                                {
                                    SendDelayMsg(this, Grobal2.RM_MAGIC_LVEXP, 0, attackMagic.MagicInfo.wMagicID, attackMagic.btLevel, attackMagic.nTranPoint, "", 3000);
                                }
                            }
                        }
                        switch (wHitMode)
                        {
                            case 4:
                                attackMagic = GetAttrackMagic(SpellsDef.SKILL_ERGUM);
                                if (attackMagic != null)
                                {
                                    if (attackMagic.btLevel < 3 && (attackMagic.MagicInfo.TrainLevel[attackMagic.btLevel] <= m_Abil.Level))
                                    {
                                        (this as TPlayObject).TrainSkill(attackMagic, 1);
                                        if (!(this as TPlayObject).CheckMagicLevelup(attackMagic))
                                        {
                                            SendDelayMsg(this, Grobal2.RM_MAGIC_LVEXP, 0, attackMagic.MagicInfo.wMagicID, attackMagic.btLevel, attackMagic.nTranPoint, "", 3000);
                                        }
                                    }
                                }
                                break;
                            case 5:
                                attackMagic = GetAttrackMagic(SpellsDef.SKILL_BANWOL);
                                if (attackMagic != null)
                                {
                                    if ((attackMagic.btLevel < 3) && (attackMagic.MagicInfo.TrainLevel[attackMagic.btLevel] <= m_Abil.Level))
                                    {
                                        (this as TPlayObject).TrainSkill(attackMagic, 1);
                                        if (!(this as TPlayObject).CheckMagicLevelup(attackMagic))
                                        {
                                            SendDelayMsg(this, Grobal2.RM_MAGIC_LVEXP, 0, attackMagic.MagicInfo.wMagicID, attackMagic.btLevel, attackMagic.nTranPoint, "", 3000);
                                        }
                                    }
                                }
                                break;
                            case 12:
                                attackMagic = GetAttrackMagic(SpellsDef.SKILL_REDBANWOL);
                                if (attackMagic != null)
                                {
                                    if ((attackMagic.btLevel < 3) && (attackMagic.MagicInfo.TrainLevel[attackMagic.btLevel] <= m_Abil.Level))
                                    {
                                        (this as TPlayObject).TrainSkill(attackMagic, 1);
                                        if (!(this as TPlayObject).CheckMagicLevelup(attackMagic))
                                        {
                                            SendDelayMsg(this, Grobal2.RM_MAGIC_LVEXP, 0, attackMagic.MagicInfo.wMagicID, attackMagic.btLevel, attackMagic.nTranPoint, "", 3000);
                                        }
                                    }
                                }
                                break;
                            case 7:
                                attackMagic = GetAttrackMagic(SpellsDef.SKILL_FIRESWORD);
                                if (attackMagic != null)
                                {
                                    if ((attackMagic.btLevel < 3) && (attackMagic.MagicInfo.TrainLevel[attackMagic.btLevel] <= m_Abil.Level))
                                    {
                                        (this as TPlayObject).TrainSkill(attackMagic, 1);
                                        if (!(this as TPlayObject).CheckMagicLevelup(attackMagic))
                                        {
                                            SendDelayMsg(this, Grobal2.RM_MAGIC_LVEXP, 0, attackMagic.MagicInfo.wMagicID, attackMagic.btLevel, attackMagic.nTranPoint, "", 3000);
                                        }
                                    }
                                }
                                break;
                            case 8:
                                attackMagic = GetAttrackMagic(SpellsDef.SKILL_CROSSMOON);
                                if (attackMagic != null)
                                {
                                    if ((attackMagic.btLevel < 3) && (attackMagic.MagicInfo.TrainLevel[attackMagic.btLevel] <= m_Abil.Level))
                                    {
                                        (this as TPlayObject).TrainSkill(attackMagic, 1);
                                        if (!(this as TPlayObject).CheckMagicLevelup(attackMagic))
                                        {
                                            SendDelayMsg(this, Grobal2.RM_MAGIC_LVEXP, 0, attackMagic.MagicInfo.wMagicID, attackMagic.btLevel, attackMagic.nTranPoint, "", 3000);
                                        }
                                    }
                                }
                                break;
                        }
                    }
                    result = true;

                    if (M2Share.g_Config.boMonDelHptoExp)
                    {
                        if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                        {
                            if (this.m_boAI)
                            {
                                if ((this as RobotPlayObject).m_WAbil.Level <= M2Share.g_Config.MonHptoExpLevel)
                                {
                                    if (!M2Share.GetNoHptoexpMonList(AttackTarget.m_sCharName))
                                    {
                                        (this as RobotPlayObject).GainExp(nPower * M2Share.g_Config.MonHptoExpmax);
                                    }
                                }
                            }
                            else
                            {
                                if ((this as TPlayObject).m_WAbil.Level <= M2Share.g_Config.MonHptoExpLevel)
                                {
                                    if (!M2Share.GetNoHptoexpMonList(AttackTarget.m_sCharName))
                                    {
                                        (this as TPlayObject).GainExp(nPower * M2Share.g_Config.MonHptoExpmax);
                                    }
                                }
                            }
                        }
                        if (m_btRaceServer == Grobal2.RC_PLAYCLONE)
                        {
                            if (m_Master != null)
                            {
                                if (m_Master.m_boAI)
                                {
                                    if ((m_Master as RobotPlayObject).m_WAbil.Level <= M2Share.g_Config.MonHptoExpLevel)
                                    {
                                        if (!M2Share.GetNoHptoexpMonList(AttackTarget.m_sCharName))
                                        {
                                            (m_Master as RobotPlayObject).GainExp(nPower * M2Share.g_Config.MonHptoExpmax);
                                        }
                                    }
                                }
                                else
                                {
                                    if ((m_Master as TPlayObject).m_WAbil.Level <= M2Share.g_Config.MonHptoExpLevel)
                                    {
                                        if (!M2Share.GetNoHptoexpMonList(AttackTarget.m_sCharName))
                                        {
                                            (m_Master as TPlayObject).GainExp(nPower * M2Share.g_Config.MonHptoExpmax);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                {
                    TrainCurrentSkill(wHitMode);
                }
                // DURA-11: Cursed weapons (btValue[3] > 0) do not lose durability
                if ((nWeaponDamage > 0) && (m_UseItems[Grobal2.U_WEAPON] != null) && (m_UseItems[Grobal2.U_WEAPON].wIndex > 0))
                {
                    if (m_UseItems[Grobal2.U_WEAPON].btValue[3] == 0)
                    {
                        DoDamageWeapon(nWeaponDamage);
                    }
                }
                if (AttackTarget.m_btRaceServer != Grobal2.RC_PLAYOBJECT)
                {
                    AttackTarget.SendMsg(AttackTarget, Grobal2.RM_STRUCK, (short)nPower, AttackTarget.m_WAbil.HP, AttackTarget.m_WAbil.MaxHP, ObjectId, "");
                }
            }
            catch (Exception e)
            {
                M2Share.ErrorMessage(e.Message);
            }
            return result;
        }

        private void TrainCurrentSkill(int wHitMode)
        {
            int nCLevel = m_Abil.Level;
            if ((m_MagicArr[SpellsDef.SKILL_ONESWORD] != null) && ((m_btRaceServer == Grobal2.RC_PLAYOBJECT)))
            {
                if ((m_MagicArr[SpellsDef.SKILL_ONESWORD].btLevel < m_MagicArr[SpellsDef.SKILL_ONESWORD].MagicInfo.btTrainLv) && (m_MagicArr[SpellsDef.SKILL_ONESWORD].MagicInfo.TrainLevel[m_MagicArr[SpellsDef.SKILL_ONESWORD].btLevel] <= nCLevel))
                {
                    ((this) as TPlayObject).TrainSkill(m_MagicArr[SpellsDef.SKILL_ONESWORD], M2Share.RandomNumber.Random(3) + 1);
                    if (!((this) as TPlayObject).CheckMagicLevelup(m_MagicArr[SpellsDef.SKILL_ONESWORD]))
                    {
                        SendDelayMsg(this, Grobal2.RM_MAGIC_LVEXP, 0, m_MagicArr[SpellsDef.SKILL_ONESWORD].MagicInfo.wMagicID, m_MagicArr[SpellsDef.SKILL_ONESWORD].btLevel, m_MagicArr[SpellsDef.SKILL_ONESWORD].nTranPoint, "", 3000);
                    }
                }
            }
            if ((m_MagicArr[SpellsDef.SKILL_ILKWANG] != null) && ((m_btRaceServer == Grobal2.RC_PLAYOBJECT)))
            {
                if ((m_MagicArr[SpellsDef.SKILL_ILKWANG].btLevel < m_MagicArr[SpellsDef.SKILL_ILKWANG].MagicInfo.btTrainLv) && (m_MagicArr[SpellsDef.SKILL_ILKWANG].MagicInfo.TrainLevel[m_MagicArr[SpellsDef.SKILL_ILKWANG].btLevel] <= nCLevel))
                {
                    ((this) as TPlayObject).TrainSkill(m_MagicArr[SpellsDef.SKILL_ILKWANG], M2Share.RandomNumber.Random(3) + 1);
                    if (!((this) as TPlayObject).CheckMagicLevelup(m_MagicArr[SpellsDef.SKILL_ILKWANG]))
                    {
                        SendDelayMsg(this, Grobal2.RM_MAGIC_LVEXP, 0, m_MagicArr[SpellsDef.SKILL_ILKWANG].MagicInfo.wMagicID, m_MagicArr[SpellsDef.SKILL_ILKWANG].btLevel, m_MagicArr[SpellsDef.SKILL_ILKWANG].nTranPoint, "", 3000);
                    }
                }
            }
            if ((wHitMode == 3) && (m_MagicArr[SpellsDef.SKILL_YEDO] != null) && (m_btRaceServer == Grobal2.RC_PLAYOBJECT))
            {
                if ((m_MagicArr[SpellsDef.SKILL_YEDO].btLevel < m_MagicArr[SpellsDef.SKILL_YEDO].MagicInfo.btTrainLv) && (m_MagicArr[SpellsDef.SKILL_YEDO].MagicInfo.TrainLevel[m_MagicArr[SpellsDef.SKILL_YEDO].btLevel] <= nCLevel))
                {
                    ((this) as TPlayObject).TrainSkill(m_MagicArr[SpellsDef.SKILL_YEDO], M2Share.RandomNumber.Random(3) + 1);
                    if (!((this) as TPlayObject).CheckMagicLevelup(m_MagicArr[SpellsDef.SKILL_YEDO]))
                    {
                        SendDelayMsg(this, Grobal2.RM_MAGIC_LVEXP, 0, m_MagicArr[SpellsDef.SKILL_YEDO].MagicInfo.wMagicID, m_MagicArr[SpellsDef.SKILL_YEDO].btLevel, m_MagicArr[SpellsDef.SKILL_YEDO].nTranPoint, "", 3000);
                    }
                }
            }
            if ((wHitMode == 4) && (m_MagicArr[SpellsDef.SKILL_ERGUM] != null) && ((m_btRaceServer == Grobal2.RC_PLAYOBJECT)))
            {
                if ((m_MagicArr[SpellsDef.SKILL_ERGUM].btLevel < m_MagicArr[SpellsDef.SKILL_ERGUM].MagicInfo.btTrainLv) && (m_MagicArr[SpellsDef.SKILL_ERGUM].MagicInfo.TrainLevel[m_MagicArr[SpellsDef.SKILL_ERGUM].btLevel] <= nCLevel))
                {
                    ((this) as TPlayObject).TrainSkill(m_MagicArr[SpellsDef.SKILL_ERGUM], 1);
                    if (!((this) as TPlayObject).CheckMagicLevelup(m_MagicArr[SpellsDef.SKILL_ERGUM]))
                    {
                        SendDelayMsg(this, Grobal2.RM_MAGIC_LVEXP, 0, m_MagicArr[SpellsDef.SKILL_ERGUM].MagicInfo.wMagicID, m_MagicArr[SpellsDef.SKILL_ERGUM].btLevel, m_MagicArr[SpellsDef.SKILL_ERGUM].nTranPoint, "", 3000);
                    }
                }
            }
            if ((wHitMode == 5) && (m_MagicArr[SpellsDef.SKILL_BANWOL] != null) && ((m_btRaceServer == Grobal2.RC_PLAYOBJECT)))
            {
                if ((m_MagicArr[SpellsDef.SKILL_BANWOL].btLevel < m_MagicArr[SpellsDef.SKILL_BANWOL].MagicInfo.btTrainLv) && (m_MagicArr[SpellsDef.SKILL_BANWOL].MagicInfo.TrainLevel[m_MagicArr[SpellsDef.SKILL_BANWOL].btLevel] <= nCLevel))
                {
                    ((this) as TPlayObject).TrainSkill(m_MagicArr[SpellsDef.SKILL_BANWOL], 1);
                    if (!((this) as TPlayObject).CheckMagicLevelup(m_MagicArr[SpellsDef.SKILL_BANWOL]))
                    {
                        SendDelayMsg(this, Grobal2.RM_MAGIC_LVEXP, 0, m_MagicArr[SpellsDef.SKILL_BANWOL].MagicInfo.wMagicID, m_MagicArr[SpellsDef.SKILL_BANWOL].btLevel, m_MagicArr[SpellsDef.SKILL_BANWOL].nTranPoint, "", 3000);
                    }
                }
            }
            if ((wHitMode == 13) && (m_MagicArr[56] != null) && ((m_btRaceServer == Grobal2.RC_PLAYOBJECT)))
            {
                if ((m_MagicArr[56].btLevel < m_MagicArr[56].MagicInfo.btTrainLv) && (m_MagicArr[56].MagicInfo.TrainLevel[m_MagicArr[56].btLevel] <= nCLevel))
                {
                    ((this) as TPlayObject).TrainSkill(m_MagicArr[56], 1);
                    if (!((this) as TPlayObject).CheckMagicLevelup(m_MagicArr[56]))
                    {
                        SendDelayMsg(this, Grobal2.RM_MAGIC_LVEXP, 0, m_MagicArr[56].MagicInfo.wMagicID, m_MagicArr[56].btLevel, m_MagicArr[56].nTranPoint, "", 3000);
                    }
                }
            }
            if ((wHitMode == 8) && (m_MagicArr[40] != null) && ((m_btRaceServer == Grobal2.RC_PLAYOBJECT)))
            {
                if ((m_MagicArr[40].btLevel < m_MagicArr[40].MagicInfo.btTrainLv) && (m_MagicArr[40].MagicInfo.TrainLevel[m_MagicArr[40].btLevel] <= nCLevel))
                {
                    ((this) as TPlayObject).TrainSkill(m_MagicArr[40], 1);
                    if (!((this) as TPlayObject).CheckMagicLevelup(m_MagicArr[40]))
                    {
                        SendDelayMsg(this, Grobal2.RM_MAGIC_LVEXP, 0, m_MagicArr[40].MagicInfo.wMagicID, m_MagicArr[40].btLevel, m_MagicArr[40].nTranPoint, "", 3000);
                    }
                }
            }
            if ((wHitMode == 10) && (m_MagicArr[42] != null) && ((m_btRaceServer == Grobal2.RC_PLAYOBJECT)))
            {
                if ((m_MagicArr[42].btLevel < m_MagicArr[42].MagicInfo.btTrainLv) && (m_MagicArr[42].MagicInfo.TrainLevel[m_MagicArr[42].btLevel] <= nCLevel))
                {
                    ((this) as TPlayObject).TrainSkill(m_MagicArr[42], 1);
                    if (!((this) as TPlayObject).CheckMagicLevelup(m_MagicArr[42]))
                    {
                        SendDelayMsg(this, Grobal2.RM_MAGIC_LVEXP, 0, m_MagicArr[42].MagicInfo.wMagicID, m_MagicArr[42].btLevel, m_MagicArr[42].nTranPoint, "", 3000);
                    }
                }
            }
            if ((wHitMode == 61) && (m_MagicArr[61] != null) && ((m_btRaceServer == Grobal2.RC_PLAYOBJECT)))
            {
                if ((m_MagicArr[61].btLevel < m_MagicArr[61].MagicInfo.btTrainLv) && (m_MagicArr[61].MagicInfo.TrainLevel[m_MagicArr[61].btLevel] <= nCLevel))
                {
                    ((this) as TPlayObject).TrainSkill(m_MagicArr[61], 1);
                    if (!((this) as TPlayObject).CheckMagicLevelup(m_MagicArr[61]))
                    {
                        SendDelayMsg(this, Grobal2.RM_MAGIC_LVEXP, 0, m_MagicArr[61].MagicInfo.wMagicID, m_MagicArr[61].btLevel, m_MagicArr[61].nTranPoint, "", 3000);
                    }
                }
            }
            if ((wHitMode == 20) && (m_MagicArr[101] != null) && ((m_btRaceServer == Grobal2.RC_PLAYOBJECT)))
            {
                if ((m_MagicArr[101].MagicInfo.TrainLevel[m_MagicArr[101].btLevel] <= nCLevel))
                {
                    ((this) as TPlayObject).TrainSkill(m_MagicArr[101], 1);
                    if (!((this) as TPlayObject).CheckMagicLevelup(m_MagicArr[101]))
                    {
                        SendDelayMsg(this, Grobal2.RM_MAGIC_LVEXP, 0, m_MagicArr[101].MagicInfo.wMagicID, m_MagicArr[101].btLevel, m_MagicArr[101].nTranPoint, "", 3000);
                    }
                }
            }
            if ((wHitMode == 21) && (m_MagicArr[102] != null) && ((m_btRaceServer == Grobal2.RC_PLAYOBJECT)))
            {
                if ((m_MagicArr[102].MagicInfo.TrainLevel[m_MagicArr[102].btLevel] <= nCLevel))
                {
                    ((this) as TPlayObject).TrainSkill(m_MagicArr[102], 1);
                    if (!((this) as TPlayObject).CheckMagicLevelup(m_MagicArr[102]))
                    {
                        SendDelayMsg(this, Grobal2.RM_MAGIC_LVEXP, 0, m_MagicArr[102].MagicInfo.wMagicID, m_MagicArr[102].btLevel, m_MagicArr[102].nTranPoint, "", 3000);
                    }
                }
            }
            if ((wHitMode == 22) && (m_MagicArr[103] != null) && ((m_btRaceServer == Grobal2.RC_PLAYOBJECT)))
            {
                if ((m_MagicArr[103].MagicInfo.TrainLevel[m_MagicArr[103].btLevel] <= nCLevel))
                {
                    ((this) as TPlayObject).TrainSkill(m_MagicArr[103], 1);
                    if (!((this) as TPlayObject).CheckMagicLevelup(m_MagicArr[103]))
                    {
                        SendDelayMsg(this, Grobal2.RM_MAGIC_LVEXP, 0, m_MagicArr[103].MagicInfo.wMagicID, m_MagicArr[103].btLevel, m_MagicArr[103].nTranPoint, "", 3000);
                    }
                }
            }
            if ((wHitMode == 23) && (m_MagicArr[114] != null) && ((m_btRaceServer == Grobal2.RC_PLAYOBJECT)))
            {
                if ((m_MagicArr[114].MagicInfo.TrainLevel[m_MagicArr[114].btLevel] <= nCLevel))
                {
                    ((this) as TPlayObject).TrainSkill(m_MagicArr[114], 1);
                    if (!((this) as TPlayObject).CheckMagicLevelup(m_MagicArr[114]))
                    {
                        SendDelayMsg(this, Grobal2.RM_MAGIC_LVEXP, 0, m_MagicArr[114].MagicInfo.wMagicID, m_MagicArr[114].btLevel, m_MagicArr[114].nTranPoint, "", 3000);
                    }
                }
            }
            if ((wHitMode == 24) && (m_MagicArr[113] != null) && ((m_btRaceServer == Grobal2.RC_PLAYOBJECT)))
            {
                if ((m_MagicArr[113].MagicInfo.TrainLevel[m_MagicArr[113].btLevel] <= nCLevel))
                {
                    ((this) as TPlayObject).TrainSkill(m_MagicArr[113], 1);
                    if (!((this) as TPlayObject).CheckMagicLevelup(m_MagicArr[113]))
                    {
                        SendDelayMsg(this, Grobal2.RM_MAGIC_LVEXP, 0, m_MagicArr[113].MagicInfo.wMagicID, m_MagicArr[113].btLevel, m_MagicArr[113].nTranPoint, "", 3000);
                    }
                }
            }
            if ((wHitMode == 25) && (m_MagicArr[115] != null) && ((m_btRaceServer == Grobal2.RC_PLAYOBJECT)))
            {
                if ((m_MagicArr[115].MagicInfo.TrainLevel[m_MagicArr[115].btLevel] <= nCLevel))
                {
                    ((this) as TPlayObject).TrainSkill(m_MagicArr[115], 1);
                    if (!((this) as TPlayObject).CheckMagicLevelup(m_MagicArr[115]))
                    {
                        SendDelayMsg(this, Grobal2.RM_MAGIC_LVEXP, 0, m_MagicArr[115].MagicInfo.wMagicID, m_MagicArr[115].btLevel, m_MagicArr[115].nTranPoint, "", 3000);
                    }
                }
            }
        }

        private TUserMagic GetAttrackMagic(int magicId)
        {
            if (magicId >= 0 && magicId < m_MagicArr.Length)
            {
                return m_MagicArr[magicId];
            }
            return null;
        }
    }
}
