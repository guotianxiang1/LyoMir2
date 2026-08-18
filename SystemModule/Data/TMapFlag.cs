using System.Collections.Generic;

namespace SystemModule;

public class TMapFlag
{
    // The 战神 map-flag domain is the two parallel Delphi AnsiString pools at 0x775BFC
    // (54 records / 52 flag names after the 2 separators) and 0x776B20 (60 records / 55
    // flag names after the 5 separators). The following names are NOT in either pool and
    // are not in the image at all -- each was scanned as a Delphi AnsiString record
    // (FFFFFFFF + len32 + chars + NUL), as len32+chars+NUL, as len8+chars+NUL, as
    // chars+NUL, bare, and UTF-16LE, all case-insensitive, and every form returned 0:
    //
    //   MINE2 NOHUMNOMON MUSIC EXPRATE PKWINLEVEL PKWINEXP PKLOSTLEVEL PKLOSTEXP
    //   DECHP INCHP DECGAMEGOLD DECGAMEPOINT INCGAMEGOLD INCGAMEPOINT RUNHUMAN RUNMON
    //   NOGUILDRECALL NODEARRECALL NOMASTERRECALL NOTHROWITEM NODROPITEM NOHORSE NOCHAT
    //   KILLFUNC NEEDSET_ON NEEDSET_OFF
    //
    // (The three bare "EXPRATE" byte hits at 0x6AD5F6 / 0x72C759 / 0x7D0618 are the tails
    // of "MultiTempExpRate" and "MonExpRate", not standalone tokens.) The parser no longer
    // recognises any of them -- 战神 parser B silently ignores an unknown token
    // (0x776AD3 falls straight into the next loop iteration) -- so the fields below that
    // belong to those names stay at their zero/-1 defaults for the whole process lifetime.
    // The declarations survive only because Envirnoment.cs's diagnostic map dump and a
    // handful of now-unreachable gates still name them; do not re-wire the parser.
    public bool boSAFE;
    public int nL;

    // NEEDSET_ON / NEEDSET_OFF / MUSIC 是凭空发明的 token，解析臂已移除
    // （见 Maps.cs 的 §INVENTED）。这三个字段还留着是因为消费点尚未清理；
    // 种子值必须是 -1（关闭哨兵），否则默认 0 会让
    // TBaseObject.cs 的 `if (Envir.Flag.nNEEDSETONFlag >= 0)` 在所有不走
    // LoadMapInfo 的构造路径（动态地图房间、Envirnoment.Initialize）上误触发。
    public int nNEEDSETONFlag = -1;
    public int nNeedONOFF = -1;
    public int nMUSICID = -1;
    public bool boDarkness;
    public bool boDayLight;
    public bool boFightZone;
    public bool boFight3Zone;
    public byte SceneType; // Native +0x8C semantic field.
    public bool boFREEPK;
    public bool boQUIZ;
    public bool boNORECONNECT;
    public string sNoReConnectMap;
    public bool boMUSIC;
    public bool boEXPRATE;
    public int nEXPRATE;
    public bool boPKWINLEVEL;
    public int nPKWINLEVEL;
    public bool boPKWINEXP;
    public int nPKWINEXP;
    public bool boPKLOSTLEVEL;
    public int nPKLOSTLEVEL;
    public bool boPKLOSTEXP;
    public int nPKLOSTEXP;
    public bool boDECHP;
    public int nDECHPPOINT;
    public int nDECHPTIME;
    public bool boINCHP;
    public int nINCHPPOINT;
    public int nINCHPTIME;
    public bool boDECGAMEGOLD;
    public int nDECGAMEGOLD;
    public int nDECGAMEGOLDTIME;
    public bool boDECGAMEPOINT;
    public int nDECGAMEPOINT;
    public int nDECGAMEPOINTTIME;
    public bool boINCGAMEGOLD;
    public int nINCGAMEGOLD;
    public int nINCGAMEGOLDTIME;
    public bool boINCGAMEPOINT;
    public int nINCGAMEPOINT;
    public int nINCGAMEPOINTTIME;
    public bool boRUNHUMAN;
    public bool boRUNMON;
    public bool boNEEDHOLE;
    public bool boNORECALL;
    public bool boNOGUILDRECALL;
    public bool boNODEARRECALL;
    public bool boNOMASTERRECALL;
    public bool boNORANDOMMOVE;
    public bool boLIMITITEMMOVE;
    public bool boBLACKROOM;
    public bool boFOXMAP;
    public bool boNODRUG;
    public bool boMINE;
    public bool boNOPOSITIONMOVE;
    public bool boPICKUP;
    public bool boNODROPITEM;
    public bool boNOTHROWITEM;
    public bool boNOHORSE;
    /// <summary>
    /// 战神 map flag NORIDE -> native [flag+0x85]
    /// (parser B sub_776008 @0x7768A5 token compare, @0x7768B6 writes byte [ebx+0x85],1).
    /// Tested in mount-summon path sub_6EE174 @0x6EE197 and @0x70F75B.
    /// Refusal text "当前地图不能召唤坐骑！" at 0x6EE248 (Blue 0xFCFF).
    /// Gates riding/mount summoning, not walking.
    /// </summary>
    public bool boNORIDE;
    public bool boNOCHAT;
    public bool boKILLFUNC;
    public int nKILLFUNCNO;
    /// <summary>
    /// NOT A 战神 FLAG — permanently false; see the census note at the top of the class.
    /// Kept only so the diagnostic map dump in Envirnoment.cs keeps its column layout.
    /// The parser does not set it and the monster-regen gate in UsrEngn.cs does not read
    /// it. Do not re-wire it.
    /// </summary>
    public bool boNOHUMNOMON;

    /// <summary>
    /// 战神 map flag <c>ONLYDROPSPEC</c> -> native <c>[flag+0x76]</c>
    /// (parser <c>sub_774D98</c> @0x775AC7 token compare, @0x775ADC <c>mov byte [ebx+0x76],1</c>).
    /// Read by the death-drop policy <c>sub_741368</c> @0x741417 and @0x74143E, where it
    /// both re-enables dropping on a FIGHT/FIGHT3/safe map and selects the exclusive
    /// worker <c>sub_740300</c>.
    /// </summary>
    public bool boONLYDROPSPEC;

    /// <summary>
    /// 战神 map flag <c>LIMITBAGITEMDROP</c> -> native <c>[flag+0x77]</c>
    /// (parser <c>sub_774D98</c> @0x775AFB token compare, @0x775B10 <c>mov byte [ebx+0x77],1</c>).
    /// Read by <c>sub_741368</c> @0x741426 and @0x74144E, selecting the exclusive worker
    /// <c>sub_748D48</c>.
    /// </summary>
    public bool boLIMITBAGITEMDROP;

    /// <summary>
    /// 战神 map flag <c>NoRelive</c> -> native <c>[flag+0x72]</c>
    /// (parser <c>sub_774D98</c>: token string @0x775FA0, compare @0x775A1A,
    /// @0x775A28 <c>mov byte [ebx+0x72],1</c>, else-arm @0x775A39 writes 0).
    /// Read by the revive handler <c>sub_7436F8</c> @0x743726; when set the whole
    /// handler returns FALSE immediately (<c>jne 0x7439BC</c> @0x74372A).
    /// </summary>
    public bool boNoRelive;

    /// <summary>
    /// 战神 map flag <c>RELIVEBACK</c> -> native <c>[flag+0x7D]</c>
    /// (parser <c>sub_774D98</c>: token string @0x775E28, compare @0x77552A,
    /// @0x775538 <c>mov byte [ebx+0x7D],1</c>, else-arm @0x775549 writes 0).
    /// Read by <c>sub_7436F8</c> @0x743952: on a successful revive the player is
    /// relocated back to the death spot with a +-2 jitter.
    /// </summary>
    public bool boRELIVEBACK;

    /// <summary>
    /// 战神 map flag <c>AUTORELIVE</c> -> native <c>[flag+0x7E]</c>
    /// (parser <c>sub_774D98</c>: token string @0x775E78, compare @0x775686,
    /// @0x775694 <c>mov byte [ebx+0x7E],1</c>, else-arm @0x7756A5 writes 0).
    /// Read by <c>sub_7436F8</c> @0x74392B, but ONLY when no item path already
    /// revived (@0x743921 <c>test bl,bl</c> / <c>jne</c>).
    /// </summary>
    public bool boAUTORELIVE;

    /// <summary>
    /// 战神 map flag <c>NOEQUIPRELIVE</c> -> native <c>[flag+0x7F]</c>
    /// (parser <c>sub_774D98</c>: token string @0x775E8C, compare @0x7756BA,
    /// @0x7756C8 <c>mov byte [ebx+0x7F],1</c>, else-arm @0x7756D9 writes 0).
    /// Read by <c>sub_7436F8</c> @0x743730; when set it <b>jumps to the TAIL</b>
    /// (<c>jne 0x74390B</c> @0x743734) rather than returning, so AUTORELIVE and
    /// RELIVEBACK still run — only the two item-driven paths are skipped.
    /// </summary>
    public bool boNOEQUIPRELIVE;

    /// <summary>
    /// 战神 map flag <c>NOC2C</c> -> native <c>[flag+0x82]</c>
    /// (parser sub_774D98 @0x7756F7 dec edi / sete al writes computed value;
    /// parser sub_776008 @0x776483 mov byte [ebx+0x82],1 writes immediate 1).
    /// Consumer at 0x6F0A3F (sub_6F09C4, opcode 0x546). Both parsers write the
    /// same offset 0x82. Flag name appears in native token pool B alongside
    /// AUTORELIVE, NOEQUIPRELIVE, NOHERO, etc.
    /// </summary>
    public bool boNOC2C;

    /// <summary>
    /// 战神 map flag <c>RUNFLAG(n)</c> -> native <c>[flag+0xB0]</c>, the
    /// over-encumbered run exemption. Non-zero means EXEMPT (the weight test
    /// is skipped), which is why the constructor seeds it to 1.
    /// <para>
    /// Writers (all five write the same byte):
    ///   ctor            0x774A5C  mov byte [esi+0xB0],1   -- default = EXEMPT
    ///   MapInfo parser  0x77558A  call StrToIntDef, then
    ///                   0x77559F  mov byte [ebx+0xB0],1   -- RUNFLAG(non-zero)
    ///                   0x775593  mov byte [ebx+0xB0],0   -- RUNFLAG(0)
    ///                   0x7755B3  mov byte [ebx+0xB0],0   -- bare RUNFLAG, no arg
    ///   second parser   0x776653 / 0x776647 -- same pair, token @0x776D7C
    ///   NORUN/CANRUN    0x77B842 mov 0 / 0x77B861 mov 1 (tokens @0x77B964/0x77B974)
    /// Readers: 0x6BBFDC and 0x6BC0F4 -- <c>cmp byte [eax+0xB0],0 / jne</c>
    /// skips the <c>[obj+0x2C4] vs [obj+0x2C8]</c> weight comparison.
    /// </para>
    /// NOTE the token semantics are direct, not inverted: value 0 stores 0
    /// (restricted) and non-zero stores 1 (exempt).
    /// </summary>
    public bool boRUNFLAG = true;

    public byte BreakLevel;
    public ushort CrazyBreakLevel;

    // MFLG-12/MFLG-24: Additional map flags from 战神 token census
    /// <summary>
    /// 战神 map flag <c>NOMAGIC</c> -> native <c>[flag+0x81]</c>. The field has exactly ONE
    /// reader, 0x6DA12B <c>80 B8 81 00 00 00 00  cmp byte [eax+0x81],0</c> / 0x6DA132 <c>jne</c>,
    /// reached through the standard <c>mov eax,[actor+0x128]</c> map-pointer form at 0x6DA125.
    /// The reader sits in the CM_SPELL dispatcher (sub_6D7D68) just before the DoSpell call
    /// (sub_6BC510): a set flag jumps to the silent reject at 0x6DA17A (spell fail/ack 0x276,
    /// no cast, no text). Consumer reproduced by
    /// <c>TPlayObject.NativeNoMagicMapForbidsSpell()</c> (GameSvr\Players\
    /// TPlayObject.NativeNoMagicMap.cs); wiring into the CM_SPELL case is handed to the parent
    /// agent (TPlayObject.Message.cs is off-limits to that shard).
    /// </summary>
    public bool boNOMAGIC;

    /// <summary>
    /// 战神 map flag <c>NOTHROUGH</c>.
    /// </summary>
    public bool boNOTHROUGH;

    /// <summary>
    /// 战神 map flag <c>DARE</c>.
    /// </summary>
    public bool boDARE;

    /// <summary>
    /// 战神 map flag <c>MONATTACK</c>.
    /// </summary>
    public bool boMONATTACK;

    /// <summary>
    /// 战神 map flag <c>LIMITHEROLEVEL(n)</c> -> native <c>[flag+0xC0]</c>, a WORD
    /// threshold (0 = no limit, which is also the InitInstance zero-fill default).
    /// Writers: parser A <c>0x775869 mov word [ebx+0xC0],ax</c> (and
    /// <c>0x77587D</c> writes 0 on the GM toggle-off arm), parser B
    /// <c>0x77682C</c>. Readers <c>sub_690300</c>:
    /// <c>0x690315 cmp word [edx+0xC0],0 / jbe skip</c>,
    /// <c>0x690339 cmp cx,word [edx+0xC0] / jbe skip</c>,
    /// <c>0x690342 mov cx,word [edx+0xC0]</c> -- a numeric comparison against the
    /// hero level, then a clamp. Was a bool, which discarded the threshold.
    /// </summary>
    public ushort LimitHeroLevel;

    /// <summary>
    /// 战神 map flag <c>LIMITPLAYERLEVEL(n)</c> -> native <c>[flag+0xBE]</c> WORD.
    /// Parser A <c>0x77580A</c> / <c>0x77581E</c>=0, parser B <c>0x7767E6</c>.
    /// Read by <c>sub_690300</c> @<c>0x69032C cmp cx,word [edx+0xBE]</c>
    /// alongside LimitHeroLevel, plus <c>0x6BA0FD</c> / <c>0x6BA107</c> /
    /// <c>0x6BA114</c>.
    /// </summary>
    public ushort LimitPlayerLevel;

    /// <summary>
    /// 战神 map flag <c>UNIFIEDLEVEL(n)</c> -> native <c>[flag+0xBC]</c> WORD.
    /// Parser A <c>0x7757AB</c> / <c>0x7757BF</c>=0, parser B <c>0x7767A0</c>.
    /// Readers <c>0x6BA0D0</c>, <c>0x6BA0DA</c>, <c>0x73D60B</c>.
    /// </summary>
    public ushort UnifiedLevel;

    /// <summary>
    /// 战神 map flag <c>MapSign(n)</c> -> native <c>[flag+0x62]</c> WORD.
    /// Parser A <c>0x775407 mov word [ebx+0x62],ax</c> / <c>0x775418</c>=0,
    /// parser B <c>0x776514</c>. No read point anchored yet.
    /// </summary>
    public ushort MapSign;

    /// <summary>
    /// 战神 map flag <c>MAPFIREWALLBURN(n)</c> -> native <c>[flag+0x88]</c> DWORD,
    /// stored in MILLISECONDS: both parsers multiply the parsed argument by 1000
    /// before storing (<c>0x7753A4</c> / <c>0x7764C9</c>
    /// <c>69 C0 E8 03 00 00  imul eax,eax,0x3E8</c>), so the configured value is
    /// in seconds.
    /// Writers <c>0x7753AA</c> (and <c>0x7753BD/BF</c> writes 0 on toggle-off),
    /// <c>0x7764CF</c>. TFireBurnEvent reads it at <c>0x7178BC</c> and, when
    /// positive, writes it over the event duration at <c>0x7178C6</c>.
    /// </summary>
    public int MapFireWallBurnMs;

    /// <summary>
    /// 战神 map flag <c>FLYDROPITEM(a/b/c)</c> -> native <c>[flag+0xB4]</c>. NOT a number:
    /// the arm lazily constructs a <c>TMirStringList</c> and fills it with the '/'-separated
    /// pieces of the parenthesised argument.
    /// <para>
    /// Class identity is pinned through the VMT rather than inferred: classref
    /// <c>[0x49EB3C] = 0x49EB88</c>, and vmtClassName at <c>VMT-0x2C = 0x49EC20</c> is the
    /// ShortString <c>len=14 'TMirStringList'</c>.
    /// </para>
    /// <para>
    /// Parser B, token compare <c>0x77651D B9 0B 00 00 00 mov ecx,0xB</c> /
    /// <c>0x776522 BA 54 6D 77 00 mov edx,0x776D54</c> ("FLYDROPITEM", 11 chars) /
    /// <c>0x77652A call 0x4C6E94</c>; argument pulled by <c>0x77654C call 0x4C6964</c> with
    /// <c>ecx=0x776B30</c> (")") and <c>edx=0x776B3C</c> ("("). Then:
    ///   <c>0x776551 cmp dword [ebp-0xC],0 / je 0x7765C2</c>  -- empty argument
    ///   <c>0x776557 cmp dword [ebx+0xB4],0 / jne 0x776574</c>
    ///   <c>0x776560 mov dl,1 / mov eax,[0x49EB3C] / call 0x404660 / 0x77656C
    ///      mov [ebx+0xB4],eax</c>                            -- lazy create
    ///   <c>0x776574 mov eax,[ebx+0xB4] / mov edx,[eax] / 0x77657C call [edx+0x44]</c> -- Clear
    ///   <c>0x776581</c> loop: <c>0x776588 B1 2F mov cl,0x2F</c> ('/') /
    ///      <c>0x77658D call 0x4C6AEC</c> split, remainder stored back at <c>0x776598</c>
    ///   <c>0x77659D cmp dword [ebp-0x10],0 / je</c>          -- empty piece skipped
    ///   <c>0x7765AE call [ecx+0x38]</c>                      -- TStrings.Add
    ///   <c>0x7765B4 call 0x4057D0 / test eax,eax / 0x7765BB jg 0x776581</c> -- do..while Len&gt;0
    /// The empty-argument arm <c>0x7765C2</c> clears via <c>[edx+0x44]</c> then
    /// <c>0x7765DA lea eax,[ebx+0xB4] / 0x7765E0 call 0x414C24</c> (FreeAndNil) -> null.
    /// Parser A is the same shape at 0x775452..0x7754C7.
    /// </para>
    /// <para>
    /// ELEMENT SEMANTICS ARE ITEM NAMES -- this was previously left BLOCKED as "could be
    /// names or ids", and the consumer settles it. sub_77BA38(eax=mapflag, edx=name):
    ///   <c>0x77BA59 mov esi,[ebx+0xB4] / test esi,esi / je</c>   -- no list =&gt; false
    ///   <c>0x77BA67 call [edx+0x14] / test eax,eax / jle</c>     -- Count &lt;= 0 =&gt; false
    ///   <c>0x77BA71 mov eax,[ebx+0xB4] / 0x77BA79 call [ecx+0x54]</c> -- TStringList.IndexOf
    ///   <c>0x77BA7C 40 inc eax / 0x77BA7D 0F 9F C0 setg al</c>   -- result = IndexOf &gt;= 0
    /// Its one caller passes an item name: <c>0x6B73F9 mov eax,[edi+0x128]</c> (map) /
    /// <c>0x6B73FF cmp dword [eax+0xB4],0 / je 0x6B74A3</c> presence gate, then
    /// <c>0x6B740C lea edx,[ebp-8] / call 0x784568</c> -- and sub_784568 is
    /// <c>0x784573 mov edx,[ebx+0x1C] / add edx,4</c>, i.e. it reads the item's StdItem
    /// record and offsets to the Name field.
    /// </para>
    /// NOT WIRED beyond parsing: the 0x6B73FF gate has no C# counterpart yet.
    /// </summary>
    public List<string> FlyDropItemNames;

    /// <summary>
    /// 战神 map flag <c>TRIGGERBOMB</c> -> native <c>[flag+0x83]</c>. Parsers write it
    /// (A 0x7758DF/0x7758F3, B 0x776894); a whole-image scan finds exactly ONE reader,
    /// 0x789752 <c>cmp byte [eax+0x83],0</c> inside <c>sub_7896FC</c>.
    /// <para>
    /// This field 1:1 reproduces the PROVEN parse write. Its EFFECT is deliberately left
    /// unmodelled and is <b>BLOCKED</b> (fail-closed): <c>sub_7896FC</c> is a method of the
    /// class <c>TTimerBomb</c> (classptr 0x781304, parent <c>TBaseItem</c>) at its VMT+0x18,
    /// and TTimerBomb is <b>never instantiated</b> anywhere in the image — the classptr value
    /// 0x781304 appears only in the class's own vmtSelfPtr (0x7812B8) and RTTI self-ref
    /// (0x781371), never as a code immediate/classref, and the name has no FindClass ref. So
    /// the sole boTRIGGERBOMB reader is dead code and the flag has NO run-time effect in
    /// native. A live C# consumer would DIVERGE from native (fabrication). The effect itself
    /// (throttled per-player spawn of a "朱火弹(幻)" bomb monster; off-flag SysMsg
    /// "在这里无法使用！") also needs the unmodelled TTimerBomb/TBaseItem placeable-object
    /// subsystem. Full reverse-engineering + proof: GameSvr\Players\
    /// TPlayObject.NativeTriggerBombMap.cs. Do NOT wire a consumer off this field.
    /// </para>
    /// </summary>
    public bool boTRIGGERBOMB;

    /// <summary>
    /// 战神 map flag <c>PAODIAN</c> (token literal 0x776E68, len 7, pool-B only;
    /// MFLG-06 / MOVE-92). The parser B arm proves the write is a SET-ONLY byte at
    /// native <c>Envir[+0x91]</c>:
    ///   <c>0x77685F mov edx,0x776E68 / 0x776867 call 0x4C6E94</c> (token compare) ->
    ///   <c>0x776870 mov dl,1 / 0x776874 call 0x77BEDC</c>, and <c>sub_77BEDC</c> does
    ///   <c>0x77BEE2 mov byte [ebx+0x91],1</c> (ignores <c>dl</c>, so it can never be
    ///   cleared) and, if <c>[ebx+0x94]==0</c>, lazily constructs a manager object
    ///   (<c>0x77BF04 call 0x77CD18</c>, classref <c>[0x774800]=0x77484C</c>; the ctor
    ///   seeds a 600000 ms interval at obj+8 and two lists at obj+0x10/+0x14) and stores
    ///   it at <c>[ebx+0x94]</c>.
    /// <para>
    /// This field 1:1 reproduces the PROVEN parse write (+0x91 set-only). Its EFFECT is
    /// deliberately left unmodelled and is <b>BLOCKED</b> (fail-closed, so an unread flag
    /// enables nothing): the +0x94 manager and its consumers are not attributed —
    /// <c>0x76A077 mov al,[esi+0x91]</c> (byte-&gt;float multiplier), <c>0x772336 mov
    /// al,[edi+0x91]</c> (<c>imul / idiv 10</c>), and <c>0x777D6B cmp byte [eax+0x91],0 /
    /// cmp dword [eax+0x94],0 / call [+0x94 method] 0x77CDD0</c>. Modelling them without
    /// resolving the +0x94 class (sub_77CD18) would be fabrication. Do not wire a consumer
    /// off this field until that subsystem is reverse-engineered.
    /// </para>
    /// </summary>
    public bool boPAODIAN;

    // ==================== MFLG-24 配置解析器补全（8 个真 token） ====================
    // 以下 8 个 token 是双解析器（配置 sub_776008 / GM sub_774D98）都写字段、且不在
    // Maps.cs §INVENTED 名单的真 token。每个 token 字面串各命中恰好 2 条 Delphi
    // AnsiString 记录（池 A ~0x775xxx / 池 B ~0x776xxx，FF FF FF FF + len32 + chars +
    // NUL），与"两 token 池各一份"的结构吻合，证明非发明。偏移逐一对上 eqv-18 普查。
    // 比较器：带 `mov ecx,len` 的 0x4C6E94 = 前缀比较（ASCII UpCase，大小写不敏感）→
    // C# HUtil32.CompareLStr；无 ecx 的 0x40BD78 = 全等（repe cmpsb + UpCase 折叠，
    // 大小写不敏感）→ C# .Equals(OrdinalIgnoreCase)。
    // 效果层：eqv-18 普查确认这 8 个 offset 均无已证 C# 消费者；按铁律 fail-closed，
    // 本波仅 1:1 复刻解析写入，不臆造消费者、不接线效果。

    /// <summary>
    /// 战神 map flag <c>UserNoKill</c> -> native <c>byte[+0x71]=1</c>（另清 <c>word[+0x74]=0</c>）。
    /// 前缀比较（len 10, 0x4C6E94）。
    /// 配置 B <c>0x7768E6</c>: <c>mov ecx,0xA / edx=0x776EAC ("UserNoKill") / call 0x4C6E94</c> ->
    /// <c>0x7768F7 mov byte [ebx+0x71],1</c> / <c>0x7768FB mov word [ebx+0x74],0</c>。
    /// GM A <c>0x775938</c> 同前缀：置位臂 <c>0x77594D [+0x71]=1</c> + <c>0x775951 word[+0x74]=0</c>；
    /// 取消臂 <c>0x775964 [+0x71]=0</c> + <c>0x775968 word[+0x74]=0</c>。
    /// <para>
    /// <c>word[+0x74]</c> 在置位/取消两臂恒被清 0；其 producer/consumer 未归因，且配置解析
    /// 面对的是新建（全零）TMapFlag，清 0 为 no-op。按 fail-closed 不建模语义未证的独立字段
    /// （不臆造），观测状态与原版一致。效果层 BLOCKED，无已证消费者，未接线。
    /// </para>
    /// </summary>
    public bool boUserNoKill;

    /// <summary>
    /// 战神 <c>word[map+0x74]</c> —— UserNoKill 的**等级上限**。上文注释原按 fail-closed 未建模，
    /// 现已证语义：两个解析器的 on/off 臂都只写立即数 0（<c>66 C7 43 74 00 00</c>，
    /// 配置 B <c>0x7768FB</c> / GM A <c>0x775951</c>、<c>0x775968</c>），
    /// 全镜像**唯一**写非零值的是运行期 <c>sub_6CDBBC @0x6CDBE3 mov word [eax+0x74],si</c>：
    /// 该函数先经 <c>0x6CDBD7</c> 由 <c>[人物+0x128]</c> 取地图，再 <c>0x6CDBDD cmp byte [eax+0x71],0</c>
    /// —— 未置 UserNoKill 则回“该地图无法设定此命令”，置位才写等级上限并回“已成功设定等级上限为N级”。
    /// 故解析层的 1:1 行为就是“命中则清零”，本字段承载运行期由 GM 命令写入的上限值。
    /// 消费者（读 <c>+0x74</c> 做等级判定处）尚未归因，保持不接线。
    /// </summary>
    public ushort UserNoKillLevelCap;

    /// <summary>
    /// 战神 map flag <c>DROPTOMAP(destMap)</c> -> native <c>byte[+0x65]=1</c> + AnsiString
    /// <c>[+0x9c]=destMap</c>。前缀比较（len 9, 0x4C6E94），与 NORECONNECT 同形取括号参数。
    /// 配置 B <c>0x7762B4</c>: <c>mov ecx,9 / edx=0x776C2C ("DROPTOMAP") / call 0x4C6E94</c> ->
    /// <c>0x7762C5 mov byte [ebx+0x65],1</c> -> <c>0x7762DE call 0x4C6964</c>（取 "(...)"）->
    /// <c>0x7762E3 lea eax,[ebx+0x9c] / 0x7762EC call 0x405554</c>（AnsiString 赋值）->
    /// <c>0x7762F1 cmp dword [ebx+0x9c],0 / jne done</c>，空参数 <c>0x7762FE mov esi,0xFFFFFFF4</c>
    /// （result=-12）。GM A <c>0x7750D7</c> 同形：置位臂写 <c>[+0x65]=1</c>+<c>[+0x9c]</c>，
    /// 空参数 <c>0x775124 [ebp-4]=0xFFFFFFF4</c>；取消臂 <c>0x775138 [+0x65]=0</c> +
    /// <c>0x77513C..call 0x405500</c> 清空 <c>[+0x9c]</c>。
    /// 效果层 BLOCKED（+0x65/+0x9c 无已证 C# 消费者），仅复刻解析与存储。
    /// </summary>
    public bool boDROPTOMAP;

    /// <summary>
    /// <c>DROPTOMAP</c> 的目标地图名 -> native AnsiString <c>[+0x9c]</c>（见 <see cref="boDROPTOMAP"/>）。
    /// </summary>
    public string sDropToMap;

    /// <summary>
    /// 战神 map flag <c>NOHERO</c> -> native <c>byte[+0x6e]=1</c>。前缀比较（len 6, 0x4C6E94）。
    /// 配置 B <c>0x77672D</c>: <c>mov ecx,6 / edx=0x776DE4 / call 0x4C6E94 -> 0x77673E mov byte [ebx+0x6e],1</c>。
    /// GM A <c>0x77570B</c>: 置位 <c>0x775720 [+0x6e]=1</c> / 取消 <c>0x775731 [+0x6e]=0</c>。
    /// 效果层 BLOCKED（无已证消费者；注意与 NativeRegisteredBodyScriptApiLadders 的 hero-gate
    /// NoHero 无关，那是英雄存在性门，非本地图旗标）。
    /// </summary>
    public bool boNOHERO;

    /// <summary>
    /// 战神 map flag <c>DREAMCASTLEMAP</c> -> native <c>byte[+0x6f]=1</c>。前缀比较（len 14, 0x4C6E94）。
    /// 配置 B <c>0x77674C</c>: <c>mov ecx,0xE / edx=0x776DF4 / call 0x4C6E94 -> 0x77675D mov byte [ebx+0x6f],1</c>。
    /// GM A <c>0x77573F</c>: 置位 <c>0x775754 [+0x6f]=1</c> / 取消 <c>0x775765 [+0x6f]=0</c>。
    /// 效果层 BLOCKED（无已证消费者）。
    /// </summary>
    public bool boDREAMCASTLEMAP;

    /// <summary>
    /// 战神 map flag <c>NEWMJNORMALPRIZE</c> -> native <c>byte[+0x78]=1</c>。前缀比较（len 16, 0x4C6E94）。
    /// 配置 B <c>0x77694A</c>: <c>mov ecx,0x10 / edx=0x776ED4 / call 0x4C6E94 -> 0x77695B mov byte [ebx+0x78],1</c>。
    /// GM A <c>0x7759DF</c>: 置位 <c>0x7759F4 [+0x78]=1</c> / 取消 <c>0x775A05 [+0x78]=0</c>。
    /// 效果层 BLOCKED（无已证消费者）。
    /// </summary>
    public bool boNEWMJNORMALPRIZE;

    /// <summary>
    /// 战神 map flag <c>MINGJIANG</c> -> native <c>byte[+0x7a]=1</c>。全等比较（0x40BD78，大小写不敏感）。
    /// 配置 B <c>0x776407</c>: <c>mov edx=0x776CCC / call 0x40BD78 / test eax,eax / jne -> 0x776418 mov byte [ebx+0x7a],1</c>。
    /// GM A <c>0x7752A5</c>: 置位 <c>0x7752BA [+0x7a]=1</c> / 取消 <c>0x7752CB [+0x7a]=0</c>。
    /// 效果层 BLOCKED（无已证消费者）。
    /// </summary>
    public bool boMINGJIANG;

    /// <summary>
    /// 战神 map flag <c>HACKQUEST</c> -> native <c>byte[+0x7b]=1</c>。全等比较（0x40BD78，大小写不敏感）。
    /// 配置 B <c>0x776421</c>: <c>mov edx=0x776CE0 / call 0x40BD78 / test eax,eax / jne -> 0x776432 mov byte [ebx+0x7b],1</c>。
    /// GM A <c>0x7752D4</c>: 置位 <c>0x7752E9 [+0x7b]=1</c> / 取消 <c>0x7752FA [+0x7b]=0</c>。
    /// 效果层 BLOCKED（无已证消费者）。
    /// </summary>
    public bool boHACKQUEST;

    /// <summary>
    /// 战神 map flag <c>NOEXPLORE</c> -> native <c>byte[+0x80]=1</c>。全等比较（0x40BD78，大小写不敏感）。
    /// 配置 B <c>0x776455</c>: <c>mov edx=0x776D08 / call 0x40BD78 / test eax,eax / jne -> 0x776466 mov byte [ebx+0x80],1</c>。
    /// GM A <c>0x775332</c>: 置位 <c>0x775347 [+0x80]=1</c> / 取消 <c>0x77535B [+0x80]=0</c>。
    /// 效果层 BLOCKED（无已证消费者）。
    /// </summary>
    public bool boNOEXPLORE;
}
