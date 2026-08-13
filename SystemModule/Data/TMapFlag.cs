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
    public int nNEEDSETONFlag;
    public int nNeedONOFF;
    public int nMUSICID;
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
    public bool boMINE2;
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
    /// C# parses the token but has no equivalent gate yet, so a NOMAGIC map does not block
    /// anything here.
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
    /// 战神 map flag <c>LIMITHEROLEVEL(n)</c> -> native <c>word [flag+0xC0]</c>, a THRESHOLD,
    /// not a switch. Parser B @0x77682C <c>66 89 83 C0 00 00 00  mov word [ebx+0xC0],ax</c>
    /// (parser A @0x775869 same, else-arm @0x77587D writes 0); ax comes from StrToIntDef with
    /// <c>33D2 xor edx,edx</c> @0x776824, i.e. default 0. Constructor never writes it, so
    /// TObject.InitInstance leaves 0 = no limit.
    /// <para>
    /// Consumer sub_690300 clamps the hero's reported level:
    ///   0x690315 <c>cmp word [edx+0xC0],0</c> / <c>jbe</c>  -- 0 means unlimited
    ///   0x69032C <c>cmp cx,word [edx+0xBE]</c> / <c>jbe</c>  -- owner level vs LIMITPLAYERLEVEL
    ///   0x690339 <c>cmp cx,word [edx+0xC0]</c> / <c>jbe</c>  -- hero level vs this threshold
    ///   0x690342 <c>mov cx,word [edx+0xC0]</c>               -- clamp to the threshold
    /// All three are unsigned word compares. NOT WIRED in C# yet.
    /// </para>
    /// </summary>
    public ushort nLIMITHEROLEVEL;

    /// <summary>
    /// 战神 map flag <c>LIMITPLAYERLEVEL(n)</c> -> native <c>word [flag+0xBE]</c>.
    /// Parser B @0x7767E6 <c>66 89 83 BE 00 00 00</c>, parser A @0x77580A (else-arm @0x77581E
    /// writes 0). Paired with <see cref="nLIMITHEROLEVEL"/> in sub_690300 @0x69032C. NOT WIRED.
    /// </summary>
    public ushort nLIMITPLAYERLEVEL;

    /// <summary>
    /// 战神 map flag <c>UNIFIEDLEVEL(n)</c> -> native <c>word [flag+0xBC]</c>.
    /// Parser B @0x7767A0 <c>66 89 83 BC 00 00 00</c>, parser A @0x7757AB (else-arm @0x7757BF
    /// writes 0). Read at 0x6BA0D0 <c>cmp word [edx+0xBC],0</c>, 0x6BA0DA, 0x73D60B. NOT WIRED.
    /// </summary>
    public ushort nUNIFIEDLEVEL;

    /// <summary>
    /// 战神 map flag <c>MapSign(n)</c> -> native <c>word [flag+0x62]</c>.
    /// Parser B @0x776514 <c>66 89 43 62</c>, parser A @0x775407 (else-arm @0x775418 writes 0).
    /// </summary>
    public ushort nMapSign;

    /// <summary>
    /// 战神 map flag <c>MAPFIREWALLBURN(n)</c> -> native <c>dword [flag+0x88]</c>.
    /// The parsed value is scaled by 1000 before the store: parser B @0x7764C9
    /// <c>69 C0 E8 03 00 00  imul eax,eax,0x3E8</c> then @0x7764CF
    /// <c>89 83 88 00 00 00  mov dword [ebx+0x88],eax</c> (parser A @0x7753AA / @0x7753BF).
    /// So the config unit is seconds and the stored unit is milliseconds.
    /// </summary>
    public int nMAPFIREWALLBURN;

    /// <summary>
    /// 战神 map flag <c>FLYDROPITEM(a/b/c)</c> -> native <c>[flag+0xB4]</c>, which is a
    /// <c>TMirStringList</c> POINTER (classref [0x49EB3C] -> VMT 0x49EB88, vmtClassName
    /// "TMirStringList"), not a number: parser B @0x776562 <c>mov eax,[0x49EB3C]</c> /
    /// <c>call 0x404660</c> creates it, @0x77657C <c>call [edx+0x44]</c> clears an existing one,
    /// and the @0x776581 loop splits the parenthesised argument on '/' (0x776588
    /// <c>B1 2F  mov cl,0x2F</c>) appending each non-empty piece via <c>call [ecx+0x38]</c>.
    /// An EMPTY argument takes the @0x7765C2 branch instead: clear then
    /// <c>lea eax,[ebx+0xB4] / call 0x414C24</c> (FreeAndNil), i.e. back to null.
    /// The only anchored reader 0x6B73FF <c>cmp dword [eax+0xB4],0</c> is a presence test.
    /// NOT WIRED beyond parsing.
    /// </summary>
    public List<string> FlyDropItemNames;

    /// <summary>
    /// 战神 map flag <c>TRIGGERBOMB</c>.
    /// </summary>
    public bool boTRIGGERBOMB;
}
