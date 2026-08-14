namespace SystemModule
{
    /// <summary>
    /// Monster/NPC/Player race types (m_btRaceServer field at offset +0x178).
    ///
    /// Source: M2Server_reunpacked_20260803.exe
    /// Known races extracted from Grobal2.cs and binary analysis.
    ///
    /// Total race range: 0-71 (72 possible values).
    ///
    /// IMPORTANT: Native behavior for unmapped races is fallback = nil.
    /// TSearchMon has 4 pure virtual placeholders (0x4035A4 = @AbstractError).
    /// Implementing unmapped races without proper handlers WILL cause runtime crashes.
    /// See session-state-0804.md line 27-28.
    ///
    /// This enum documents ALL 72 possible race values to enable:
    /// 1. Explicit handling of known races
    /// 2. Detection and safe rejection of unmapped races
    /// 3. Future implementation tracking
    /// </summary>
    public enum RaceType : byte
    {
        /// <summary>Player character (human)</summary>
        RC_PLAYOBJECT = 0,

        /// <summary>Race 1 - UNMAPPED (native returns nil)</summary>
        Race1 = 1,
        Race2 = 2,
        Race3 = 3,
        Race4 = 4,
        Race5 = 5,
        Race6 = 6,
        Race7 = 7,
        Race8 = 8,
        Race9 = 9,

        /// <summary>NPC</summary>
        RC_NPC = 10,

        /// <summary>Guard NPC</summary>
        RC_GUARD = 11,

        Race12 = 12,
        Race13 = 13,
        Race14 = 14,

        /// <summary>Peace NPC (non-combat)</summary>
        RC_PEACENPC = 15,

        Race16 = 16,
        Race17 = 17,
        Race18 = 18,
        Race19 = 19,
        Race20 = 20,
        Race21 = 21,
        Race22 = 22,
        Race23 = 23,
        Race24 = 24,
        Race25 = 25,
        Race26 = 26,
        Race27 = 27,
        Race28 = 28,
        Race29 = 29,
        Race30 = 30,
        Race31 = 31,
        Race32 = 32,
        Race33 = 33,
        Race34 = 34,
        Race35 = 35,
        Race36 = 36,
        Race37 = 37,
        Race38 = 38,
        Race39 = 39,
        Race40 = 40,
        Race41 = 41,
        Race42 = 42,
        Race43 = 43,
        Race44 = 44,
        Race45 = 45,
        Race46 = 46,
        Race47 = 47,
        Race48 = 48,
        Race49 = 49,

        /// <summary>Animal/Pet base race</summary>
        RC_ANIMAL = 50,

        Race51 = 51,
        Race52 = 52,
        Race53 = 53,

        /// <summary>Hero character</summary>
        RC_HEROOBJECT = 54,

        /// <summary>Exercise/Training dummy</summary>
        RC_EXERCISE = 55,

        Race56 = 56,
        Race57 = 57,
        Race58 = 58,
        Race59 = 59,

        /// <summary>Player clone/shadow</summary>
        RC_PLAYCLONE = 60,

        Race61 = 61,
        Race62 = 62,
        Race63 = 63,
        Race64 = 64,
        Race65 = 65,
        Race66 = 66,
        Race67 = 67,
        Race68 = 68,
        Race69 = 69,
        Race70 = 70,
        Race71 = 71,
        Race72 = 72,
        Race73 = 73,
        Race74 = 74,
        Race75 = 75,
        Race76 = 76,
        Race77 = 77,
        Race78 = 78,
        Race79 = 79,

        /// <summary>Monster base race</summary>
        RC_MONSTER = 80,

        Race81 = 81,
        Race82 = 82,
        Race83 = 83,
        Race84 = 84,
        Race85 = 85,
        Race86 = 86,
        Race87 = 87,
        Race88 = 88,
        Race89 = 89,
        Race90 = 90,
        Race91 = 91,
        Race92 = 92,
        Race93 = 93,
        Race94 = 94,
        Race95 = 95,
        Race96 = 96,
        Race97 = 97,
        Race98 = 98,
        Race99 = 99,
        Race100 = 100,
        Race101 = 101,
        Race102 = 102,
        Race103 = 103,
        Race104 = 104,
        Race105 = 105,
        Race106 = 106,
        Race107 = 107,
        Race108 = 108,
        Race109 = 109,
        Race110 = 110,
        Race111 = 111,

        /// <summary>Archer guard (ranged guard unit)</summary>
        RC_ARCHERGUARD = 112,

        Race113 = 113,
        Race114 = 114,
        Race115 = 115,
        Race116 = 116,
        Race117 = 117,
        Race118 = 118,
        Race119 = 119,
        Race120 = 120,
        Race121 = 121,
        Race122 = 122,
        Race123 = 123,
        Race124 = 124,
        Race125 = 125,
        Race126 = 126,
        Race127 = 127,
        Race128 = 128,
        Race129 = 129,
        Race130 = 130,
        Race131 = 131,
        Race132 = 132,
        Race133 = 133,
        Race134 = 134,

        /// <summary>Race 135 (unknown purpose)</summary>
        RC_135 = 135,

        /// <summary>Race 136 (unknown purpose)</summary>
        RC_136 = 136,

        Race137 = 137,
        Race138 = 138,
        Race139 = 139,
        Race140 = 140,
        Race141 = 141,
        Race142 = 142,
        Race143 = 143,
        Race144 = 144,
        Race145 = 145,
        Race146 = 146,
        Race147 = 147,
        Race148 = 148,
        Race149 = 149,
        Race150 = 150,
        Race151 = 151,
        Race152 = 152,

        /// <summary>Race 153 (unknown purpose)</summary>
        RC_153 = 153,

        // Note: Original documentation mentions "72 race" but highest known
        // race constant is 153. The "72" likely refers to a specific context
        // or range, not the absolute maximum. This enum includes all values
        // up to 153 to match native constants.
    }
}
