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
            bool boCrsHit;
            bool bo41;
            bool boTwinHit;
            bool bo43;
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
                boCrsHit = m_boCrsHitkill;
                bo41 = m_bo41kill;
                boTwinHit = m_boTwinHitSkill;
                bo43 = m_bo43kill;
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

        // Native _Attack = sub_769F90, half-moon branch @0x0076A11D-0x0076A16D.
        //   0x0076A136  E8 31 E8 D5 FF     call sub_4C896C   -> effective level
        //   0x0076A13B  3C 03              cmp al,3
        //   0x0076A13D  76 05              jbe 0x0076A144
        //   0x0076A13F  8B 5D F8           mov ebx,[ebp-8]   (level > 3: unscaled)
        //   0x0076A154  83 C0 02           add eax,2
        //   0x0076A160  D8 35 C8 A5 76 00  fdiv dword ptr [0x76A5C8]
        // [0x76A5C8] = 00 00 70 41 = float32 15.0, a literal, not btTrainLv + 10.
        internal static int CalculateHalfMoonWideAttackPower(int nPower, int trainLevel,
            int skillLevel, Plugins.YanshenApi yanshenApi)
        {
            int effectiveLevel = Math.Min(unchecked((byte)skillLevel), trainLevel);
            if (yanshenApi != null && yanshenApi.IsHalfMoon())
            {
                var divisor = yanshenApi.HalfMoonB();
                if (divisor > 0)
                {
                    return HUtil32.Round(nPower * (yanshenApi.HalfMoonA() + effectiveLevel) / divisor);
                }
            }

            if (effectiveLevel > 3)
            {
                return nPower;
            }
            return HUtil32.Round((double)nPower / 15.0 * (effectiveLevel + 2));
        }

        // Native _Attack = sub_769F90, stab branch @0x0076A096-0x0076A0F8. That is
        // the function AttackDir sub_76A5D4 actually calls
        // (0x0076A76D E8 1E F8 FF FF call 0x769F90); sub_7707A8 is a separate
        // entry used by the client command handlers and is NOT this code path.
        //   0x0076A0AF / 0x0076A0D5  call sub_4C896C   -> effective level
        //   0x0076A0B4  3C 04              cmp al,4
        //   0x0076A0BB  DB 2D B8 A5 76 00  fld tbyte[0x76A5B8]   (80-bit 1.05)
        //   0x0076A0CA  83 C3 05           add ebx,5
        //   0x0076A0EB  D8 35 C4 A5 76 00  fdiv dword[0x76A5C4]
        // [0x76A5C4] = 00 00 A0 40 = float32 5.0. The divisor is that literal;
        // btTrainLv (+0x1A) is only ever the level cap inside sub_4C896C.
        internal static int CalculateStabSwordLongAttackPower(int nPower, int trainLevel,
            int skillLevel, Plugins.YanshenApi yanshenApi)
        {
            int effectiveLevel = Math.Min(unchecked((byte)skillLevel), trainLevel);
            if (yanshenApi != null && yanshenApi.IsStabSword())
            {
                var divisor = yanshenApi.StabSwordB();
                if (divisor > 0)
                {
                    return HUtil32.Round(nPower * (yanshenApi.StabSwordA() + effectiveLevel) / divisor);
                }
            }

            if (effectiveLevel == 4)
            {
                return MultiplyByNative105(nPower) + 5;
            }
            return HUtil32.Round((double)nPower / 5.0 * (effectiveLevel + 2));
        }

        // tbyte[0x76A5B8] = 66 66 66 66 66 66 66 86 FF 3F, exactly
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

        internal static int CalculateThrustingHitPlus(int nativeHitPlus, int skillLevel,
            Plugins.YanshenApi yanshenApi)
        {
            if (yanshenApi != null && yanshenApi.IsThrusting())
            {
                return Math.Clamp(HUtil32.Round(yanshenApi.ThrustingA() + skillLevel), 0, 255);
            }

            return nativeHitPlus;
        }

        internal static int CalculateFireSwordAttackPower(int nPower, int nativeHitDouble,
            int skillLevel, Plugins.YanshenApi yanshenApi)
        {
            if (yanshenApi != null && yanshenApi.IsFireSword())
            {
                var multiplier = Math.Min(
                    (yanshenApi.FireSwordA() * skillLevel + yanshenApi.FireSwordB()) / 10 + 1,
                    25.5);
                return HUtil32.Round(nPower * multiplier);
            }

            return nPower + HUtil32.Round(nPower / 100.0 * (nativeHitDouble * 10));
        }

        internal static int CalculateSunSwordAttackPower(int power, int effectiveLevel,
            Plugins.YanshenApi yanshenApi)
        {
            if (yanshenApi != null && yanshenApi.IsSunSword())
            {
                var divisor = yanshenApi.SunSwordB();
                if (divisor > 0)
                {
                    return HUtil32.Round(power *
                        Math.Min(yanshenApi.SunSwordA() + effectiveLevel, 255) / divisor);
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
                if (candidate.wMagIdx == SpellsDef.SKILL_ONESWORD ||
                    candidate.wMagIdx == SpellsDef.SKILL_ILKWANG)
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
                            magic?.btLevel ?? 0,
                            m_btRaceServer == Grobal2.RC_PLAYOBJECT && magic != null
                                ? new Plugins.YanshenApi(this as TPlayObject, null, M2Share.PluginManager)
                                : null);
                        bo21 = true;
                    }
                    if ((wHitMode == 7) && m_boFireHitSkill)
                    {
                        m_boFireHitSkill = false;
                        m_dwLatestFireHitTick = HUtil32.GetTickCount();// Jacky 禁止双烈火
                        var magic = m_MagicArr[SpellsDef.SKILL_FIRESWORD];
                        nPower = CalculateFireSwordAttackPower(
                            nPower,
                            m_nHitDouble,
                            magic?.btLevel ?? 0,
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
                            magic?.btLevel ?? 0,
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
                            m_dwLatestFireHitTick = HUtil32.GetTickCount();
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
                    if (!AttackTarget.m_boUnParalysis && m_boParalysis && (M2Share.RandomNumber.Random(AttackTarget.m_btAntiPoison + M2Share.g_Config.nAttackPosionRate) == 0))
                    {
                        // Native 0x666D42: push 1 (level), not 0. C# applies POISON_STONE due to different index spaces (POIS-37).
                        AttackTarget.MakePosion(Grobal2.POISON_STONE, M2Share.g_Config.nAttackPosionTime, 1);
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
                            case 9:
                                attackMagic = GetAttrackMagic(SpellsDef.SKILL_TWINBLADE);
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
            if ((wHitMode == 7) && (m_MagicArr[SpellsDef.SKILL_FIRESWORD] != null) && ((m_btRaceServer == Grobal2.RC_PLAYOBJECT)))
            {
                if ((m_MagicArr[SpellsDef.SKILL_FIRESWORD].btLevel < m_MagicArr[SpellsDef.SKILL_FIRESWORD].MagicInfo.btTrainLv) && (m_MagicArr[SpellsDef.SKILL_FIRESWORD].MagicInfo.TrainLevel[m_MagicArr[SpellsDef.SKILL_FIRESWORD].btLevel] <= nCLevel))
                {
                    ((this) as TPlayObject).TrainSkill(m_MagicArr[SpellsDef.SKILL_FIRESWORD], 1);
                    if (!((this) as TPlayObject).CheckMagicLevelup(m_MagicArr[SpellsDef.SKILL_FIRESWORD]))
                    {
                        SendDelayMsg(this, Grobal2.RM_MAGIC_LVEXP, 0, m_MagicArr[SpellsDef.SKILL_FIRESWORD].MagicInfo.wMagicID, m_MagicArr[SpellsDef.SKILL_FIRESWORD].btLevel, m_MagicArr[SpellsDef.SKILL_FIRESWORD].nTranPoint, "", 3000);
                    }
                }
            }
            if ((wHitMode == 9) && (m_MagicArr[43] != null) && ((m_btRaceServer == Grobal2.RC_PLAYOBJECT)))
            {
                if ((m_MagicArr[43].btLevel < m_MagicArr[43].MagicInfo.btTrainLv) && (m_MagicArr[43].MagicInfo.TrainLevel[m_MagicArr[43].btLevel] <= nCLevel))
                {
                    ((this) as TPlayObject).TrainSkill(m_MagicArr[43], 1);
                    if (!((this) as TPlayObject).CheckMagicLevelup(m_MagicArr[43]))
                    {
                        SendDelayMsg(this, Grobal2.RM_MAGIC_LVEXP, 0, m_MagicArr[43].MagicInfo.wMagicID, m_MagicArr[43].btLevel, m_MagicArr[43].nTranPoint, "", 3000);
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
            if ((wHitMode == 12) && (m_MagicArr[66] != null) && ((m_btRaceServer == Grobal2.RC_PLAYOBJECT)))
            {
                if ((m_MagicArr[66].btLevel < m_MagicArr[66].MagicInfo.btTrainLv) && (m_MagicArr[66].MagicInfo.TrainLevel[m_MagicArr[66].btLevel] <= nCLevel))
                {
                    ((this) as TPlayObject).TrainSkill(m_MagicArr[66], 1);
                    if (!((this) as TPlayObject).CheckMagicLevelup(m_MagicArr[66]))
                    {
                        SendDelayMsg(this, Grobal2.RM_MAGIC_LVEXP, 0, m_MagicArr[66].MagicInfo.wMagicID, m_MagicArr[66].btLevel, m_MagicArr[66].nTranPoint, "", 3000);
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
