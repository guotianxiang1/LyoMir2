namespace SystemModule;

public class TMapFlag
{
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
    public bool boNOCHAT;
    public bool boKILLFUNC;
    public int nKILLFUNCNO;
    /// <summary>
    /// NOT A 战神 FLAG — permanently false. Kept only so the diagnostic map dump
    /// in Envirnoment.cs keeps its column layout. 战神 has no NOHUMNOMON token:
    /// 0 byte hits image-wide for every spelling, and the complete map-flag token
    /// census (the two parallel blocks at 0x775BFC and 0x776B20, 46 tokens each)
    /// has no equivalent. The parser no longer sets it and the monster-regen gate
    /// in UsrEngn.cs no longer reads it. Do not re-wire it.
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

    public byte BreakLevel;
    public ushort CrazyBreakLevel;
}
