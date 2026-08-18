using System;

namespace SystemModule
{
    public class Grobal2
    {
        public const int VERSION_NUMBER = 20020522;
        public const int CLIENT_VERSION_NUMBER = 120040918;
        public const int CM_POWERBLOCK = 0;
        public const int MapNameLen = 16;
        public const int ActorNameLen = 14;
        public const int DR_UP = 0;
        public const int DR_UPRIGHT = 1;
        public const int DR_RIGHT = 2;
        public const int DR_DOWNRIGHT = 3;
        public const int DR_DOWN = 4;
        public const int DR_DOWNLEFT = 5;
        public const int DR_LEFT = 6;
        public const int DR_UPLEFT = 7;
        
        
        
        public const int U_DRESS = 0;
        
        
        
        public const int U_WEAPON = 1;
        
        
        
        public const int U_RIGHTHAND = 2;
        
        
        
        public const int U_NECKLACE = 3;
        
        
        
        public const int U_HELMET = 4;
        
        
        
        public const int U_ARMRINGL = 5;
        
        
        
        public const int U_ARMRINGR = 6;
        
        
        
        public const int U_RINGL = 7;
        
        
        
        public const int U_RINGR = 8;
        
        
        
        public const int U_BUJUK = 9;
        
        
        
        public const int U_BELT = 10;
        
        
        
        public const int U_BOOTS = 11;
        
        
        
        public const int U_CHARM = 12;
        public const int U_MASK = 13;
        public const int U_WARDRUM = 14;
        public const int U_YUPEI = U_WARDRUM;
        public const int U_MOUNT = 15;
        public const int U_SHIELD = U_MOUNT;
        public const int U_HORSE = U_MOUNT;
        public const int HUMAN_EQUIPPED_ITEM_COUNT = 16;
        public const int DEFBLOCKSIZE = 16;
        public const int BUFFERSIZE = 10000;
        public const int LOGICALMAPUNIT = 40;
        public const int UNITX = 48;
        public const int UNITY = 32;
        public const int HALFX = 24;
        public const int HALFY = 16;
        public const int MAXBAGITEM = 48;
        public const int HOWMANYMAGICS = 20;

        
        
        
        
        public const int USERITEMMAX = 48;
        public const int MaxSkillLevel = 3;

        
        
        
        public const int MAX_STATUS_ATTRIBUTE = 12;
        
        
        
        public const int POISON_DECHEALTH = 0;
        
        
        
        public const int POISON_DAMAGEARMOR = 1;
        
        
        
        public const int POISON_LOCKSPELL = 2;
        
        
        
        public const int POISON_DONTMOVE = 4;
        
        
        
        public const int POISON_STONE = 5;
        
        
        
        public const int STATE_LOCKRUN = 3;
        public const int POISON_68 = 68;
        
        
        
        public const int STATE_TRANSPARENT = 8;
        
        
        
        public const int STATE_DEFENCEUP = 9;
        
        
        
        public const int STATE_MAGDEFENCEUP = 10;
        
        
        
        public const int STATE_BUBBLEDEFENCEUP = 11;
        
        
        
        public const int STATE_STONE_MODE = 0x00000001;
        public const int STATE_OPENHEATH = 0x00000002;
        public const int STATE_CELEBRITY = 105;
        public const int ET_DIGOUTZOMBI = 1;
        /// <summary>
        /// INVENTED, kept deliberately. Native mine points are TStoneMineEvent
        /// (self-pointer 0x71683C, VMT 0x716888, size 36), whose parent is
        /// TBaseObj — a sibling of TMapEvent, not a subclass — so a mine has no
        /// event-type byte at all: its <c>[+0x0C]</c> is the ore count
        /// (<c>0x71769F mov eax,0xC8 / Random -> [ebx+0x0C]</c>) and its
        /// <c>[+0x04]</c> is 8, not the 3 that TMapEvent's constructor writes at
        /// 0x717322. Nothing native ever stamps 2.
        /// <para>
        /// No collision with a real type 2: the engine factory sub_7189DC routes
        /// only 5, 8, 15 and 21 to dedicated classes (cumulative chain
        /// 0x718A27 sub dl,5 / 0x718A2C sub dl,3 / 0x718A31 sub dl,7 /
        /// 0x718A36 sub dl,6) and would build a plain TMapEvent for 2, while the
        /// single C# consumer — TPlayObject.cs:2168 — casts the lookup with
        /// <c>as StoneMineEvent</c>, so a genuine type-2 TMapEvent on the same
        /// cell is filtered out rather than mistaken for a mine. Removing the
        /// constant would mean inventing a different lookup key, which is worse.
        /// </para>
        /// </summary>
        public const int ET_MINE = 2;
        public const int ET_PILESTONES = 3;
        public const int ET_HOLYCURTAIN = 4;
        public const int ET_FIRE = 5;
        public const int ET_SCULPEICE = 6;
        public const int ET_YANHUA_TEXT = 23;
        /// <summary>TCakeFireEvent.Create @0x718025 `6A 08 push 8`.</summary>
        public const int ET_CAKEFIRE = 8;
        /// <summary>TFireDragonPoint.Create @0x718BD9 `6A 0F push 0xF`.</summary>
        public const int ET_FIREDRAGONPOINT = 0x0F;
        /// <summary>TBTFireBurnEvent.Create @0x717A97 `C6 43 0C 15`.</summary>
        public const int ET_BTFIREBURN = 0x15;
        /// <summary>
        /// Shared by TDebuffTrapEvent (@0x717CC7 `6A 19`) and
        /// TOnceDamageTrapEvent (@0x717E5B `6A 19`) — two distinct classes
        /// that both stamp type 0x19.
        /// </summary>
        public const int ET_TRAP = 0x19;
        /// <summary>TPrisonEvent.Create @0x7198E4 `6A 1D push 0x1D`.</summary>
        public const int ET_PRISON = 0x1D;
        /// <summary>TDamageTrapEvent.Create @0x717C82 `C6 46 0C 1C`.</summary>
        public const int ET_DAMAGETRAP = 0x1C;
        /// <summary>TMapScriptEvt.Create @0x719B78 `6A 23 push 0x23`.</summary>
        public const int ET_MAPSCRIPT = 0x23;
        /// <summary>TStallEvent.Create @0x719A20 `6A 29 push 0x29`.</summary>
        public const int ET_STALL = 0x29;
        public const int RCC_MERCHANT = 50;
        public const int RCC_GUARD = 12;
        public const int RCC_USERHUMAN = 0;
        public const int CM_QUERYUSERSTATE = 82;
        public const int CM_QUERYUSERNAME = 80;
        public const int CM_QUERYBAGITEMS = 81;
        /// <summary>
        /// Native handler 0x6D8CA2 (dispatch arm <c>0x6D80DB</c>). The client's own
        /// cheat self-report: header gate is <c>Series == 0xFF</c>
        /// (<c>0x6D8CA5 66 81 78 0A FF 00 cmp word[msg+0xA],0xFF / 0x6D8CAB jne default</c>)
        /// plus <c>Param &gt; 0</c> unsigned
        /// (<c>0x6D8CB4 66 83 78 06 00 / 0x6D8CB9 jbe default</c>).
        /// 战神 has no symbolic name for it, so it keeps the numeric form.
        /// </summary>
        public const int CM_205 = 205;
        public const int CM_QUERYCHR = 100;
        public const int CM_NEWCHR = 101;
        public const int CM_DELCHR = 102;
        public const int CM_SELCHR = 103;
        public const int CM_QUERYDELCHR = 105;
        public const int CM_RESDELCHR = 106;
        
        
        
        public const int CM_SELECTSERVER = 104;
        public const int CM_ATTACKMODE = 545;
        
        
        
        public const int CM_OPENDOOR = 1002;
        public const int CM_SOFTCLOSE = 1009;
        public const int CM_DROPITEM = 1000;
        public const int CM_PICKUP = 1001;
        public const int CM_PICKUP_RANGE = 4278;
        // Native CM 4314 handler 0x6DB040: `66 8B 50 06 mov dx,[msg+6]` then
        // `E8 ED 78 01 00 call 0x6F293C`. Callee 0x6F293C is a single `C3 ret`.
        public const int CM_4314 = 4314;
        // Native CM 4315 handler 0x6DB054: `66 8B 50 06 mov dx,[msg+6]` then
        // `E8 DD 78 01 00 call 0x6F2940`. Callee 0x6F2940 is a single `C3 ret`
        // (followed by `8D 40 00` alignment padding, not a second instruction).
        public const int CM_4315 = 4315;

        // ---- CM dispatch tail: ident 4125..4651 -------------------------------
        // Every ident below owns a real arm in 战神's CM dispatcher sub_6D7D68
        // (selector tree rooted at 0x6D805C, shared exit label 0x6DBC2C). They are
        // grouped here because 战神 carries no symbolic name for any of them; the
        // handler VA on each line is the tree leaf the ident resolves to.
        // Field roles on the wire record the dispatcher keeps at [ebp-0x34]:
        //   [msg+0]=Recog -> nParam1, [msg+6]=Param -> nParam2,
        //   [msg+8]=Tag   -> nParam3, [msg+0xA]=Series -> wParam,
        //   [ebp-8]=body string -> sMsg, ESI=body length -> nBodyLen.
        public const int CM_4125 = 4125;  // 0x6DAE25 -> 0x746C34
        public const int CM_4126 = 4126;  // 0x6DAE74 -> 0x6BF75C
        public const int CM_4127 = 4127;  // 0x6DAE8D -> 0x747CF4 + 0x74730C
        public const int CM_4128 = 4128;  // 0x6DAF23 -> 0x6B7184
        public const int CM_4150 = 4150;  // 0x6DAF51 -> 0x6F2924 -> 0x699B68
        public const int CM_4151 = 4151;  // 0x6DAF5E -> 0x6999D4
        public const int CM_4173 = 4173;  // 0x6DB068 -> 0x6E600C
        public const int CM_4204 = 4204;  // 0x6DAF87 -> 0x6F03E8
        public const int CM_4205 = 4205;  // 0x6DAFAF -> 0x6F01E4
        public const int CM_4215 = 4215;  // 0x6DAFCA -> 0x6E8684
        public const int CM_4218 = 4218;  // 0x6DB00C -> 0x6F3104
        public const int CM_4408 = 4408;  // 0x6DB08A -> 0x6F37EC(dl=0)
        public const int CM_4409 = 4409;  // 0x6DB0B2 -> 0x6F38A8(dl=0)
        public const int CM_4410 = 4410;  // 0x6DB0D0 -> 0x6F37EC(dl=1)
        public const int CM_4411 = 4411;  // 0x6DB0F8 -> 0x6F38A8(dl=1)
        public const int CM_4417 = 4417;  // 0x6DB1BF -> 0x699EB4
        public const int CM_4446 = 4446;  // 0x6DBB37 -> 0x6F75C4
        public const int CM_4496 = 4496;  // 0x6DBBDC -> 0x6FAC8C
        public const int CM_4626 = 4626;  // 0x6DB394 -> 0x6AE260
        public const int CM_4646 = 4646;  // 0x6DBBEB -> 0x6FBB90
        public const int CM_4647 = 4647;  // 0x6DBBF5 -> 0x6FB6FC
        public const int CM_4648 = 4648;  // 0x6DBBFF -> 0x6FB874
        public const int CM_4649 = 4649;  // 0x6DBC09 -> 0x6FBB28
        public const int CM_4650 = 4650;  // 0x6DBC18 -> 0x6FB51C
        public const int CM_4651 = 4651;  // 0x6DB1D8 -> 0x6FC054

        // 0x6BF75C answers CM 4126 on every leg through [vmt+0x250] with
        // `66 BA C2 0F mov dx,0xFC2`. The four legs differ only in the SECOND
        // pushed dword, which sub_6D7CB0 stores at [ebp-0xC] — the Tag slot:
        //   0x6BF7C5 push 0/1/0/0  Tag=1  用洗灵石成功
        //   0x6BF7E2 push 0/2/0/0  Tag=2  没有可用的洗灵石
        //   0x6BF7FF push 0/3/0/0  Tag=3  已洗到上限
        //   0x6BF81C push 0/0/0/0  Tag=0  目标不具备洗灵状态
        //   0x6BF8E9 push 0/0/0/0  Tag=0  Tag 选择子既不是 0 也不是有英雄的 1
        public const int SM_4034 = 4034;

        public const int CM_TAKEONITEM = 1003;
        public const int CM_TAKEOFFITEM = 1004;
        public const int CM_1005 = 1005;
        public const int CM_EAT = 1006;
        public const int CM_QUEST_ORDER = 1060;
        public const int CM_1069 = 1069;
        /// <summary>
        /// Native handler 0x6DAC1C (jump-table slot <c>0x6D8482[0]</c>, base ident 1325).
        /// It loads Param into <c>dx</c> and calls 0x6EE11C, whose ENTIRE body is
        /// <c>55 8B EC 51 89 45 FC 59 5D C3</c> — an empty Delphi procedure that stores
        /// Self into a stack local and returns without reading <c>dx</c>. 0x6EE11C has
        /// exactly one caller (this handler), so the opcode is a proven no-op.
        /// 战神 has no symbolic name for it.
        /// </summary>
        public const int CM_1325 = 1325;
        /// <summary>
        /// Native handler 0x6DA3A2 (jump-table slot <c>0x6D8315[39]</c>, base ident 1200).
        /// Self-contained: <c>0x6DA3A5 66 83 78 06 00 cmp word [msg+6],0 / 0x6DA3AA jne</c>
        /// then <c>0x6DA3AF mov byte [self+0x1898],1</c> or
        /// <c>0x6DA3BE mov byte [self+0x1898],0</c>. Param == 0 turns the hint ON.
        /// 战神 has no symbolic name for it.
        /// </summary>
        public const int CM_1239 = 1239;
        /// <summary>
        /// Native handler 0x6DA9C8 (dispatch arm <c>0x6D8439</c>). Self-contained
        /// three-way on Param: <c>0x6DA9CF 66 85 C0 test ax,ax / 75 0F</c> then
        /// <c>0x6DA9D7 mov byte [self+0x18AC],0</c>; <c>0x6DA9E3 66 83 F8 01 cmp ax,1 /
        /// 0F 85 jne default</c> then <c>0x6DA9F0 mov byte [self+0x18AC],1</c>.
        /// Param values other than 0 and 1 leave the flag untouched.
        /// 战神 has no symbolic name for it.
        /// </summary>
        public const int CM_1281 = 1281;
        public const int CM_COMMON_INFORMATION = 1099;
        public const int CM_YANHUA_TEXT = 1290;
        public const int CM_BUTCH = 1007;
        public const int CM_MAGICKEYCHANGE = 1008;
        public const int CM_CLICKNPC = 1010;
        public const int CM_MERCHANTDLGSELECT = 1011;
        public const int CM_MERCHANTQUERYSELLPRICE = 1012;
        public const int CM_USERSELLITEM = 1013;
        public const int CM_USERBUYITEM = 1014;
        public const int CM_USERGETDETAILITEM = 1015;
        public const int CM_DROPGOLD = 1016;
        public const int CM_1017 = 1017;
        
        
        
        public const int CM_LOGINNOTICEOK = 1018;
        public const int CM_GROUPMODE = 1019;
        public const int CM_CREATEGROUP = 1020;
        public const int CM_ADDGROUPMEMBER = 1021;
        public const int CM_DELGROUPMEMBER = 1022;
        public const int CM_USERREPAIRITEM = 1023;
        public const int CM_MERCHANTQUERYREPAIRCOST = 1024;
        public const int CM_DEALTRY = 1025;
        public const int CM_DEALADDITEM = 1026;
        public const int CM_DEALDELITEM = 1027;
        public const int CM_DEALCANCEL = 1028;
        public const int CM_DEALCHGGOLD = 1029;
        public const int CM_DEALEND = 1030;
        public const int CM_USERSTORAGEITEM = 1031;
        public const int CM_USERTAKEBACKSTORAGEITEM = 1032;
        public const int CM_WANTMINIMAP = 1033;
        public const int CM_USERMAKEDRUGITEM = 1034;
        public const int CM_MERCHANT_QUERY = 1110;
        public const int CM_SPEEDHACKUSER = 1042;
        public const int CM_ADJUST_BONUS = 1043;
        public const int CM_CATTLE_REVEAL_PRIZE = 1081;
        public const int CM_CATTLE_CLAIM_PRIZE = 1082;
        public const int CM_MERCHANTQUERYEXCHGBOOK = 1085;
        public const int CM_EXCHANGEBOOK_ROTATE = 1086;
        public const int CM_EXCHANGEBOOK_GET_PRIZE = 1087;
        public const int CM_EXCHANGEBOOK_CLOSE = 1088;
        public const int CM_PROTOCOL = 2000;
        public const int CM_IDPASSWORD = 2001;
        
        
        
        public const int CM_ADDNEWUSER = 2002;
        
        
        
        public const int CM_CHANGEPASSWORD = 2003;
        
        
        
        public const int CM_UPDATEUSER = 2004;
        public const int CM_SWORD_HIT = 3002;
        public const int CM_TURN = 3010;
        
        
        
        public const int CM_WALK = 3011;
        
        
        
        public const int CM_SITDOWN = 3012;
        
        
        
        public const int CM_RUN = 3013;
        public const int CM_SHANGMA_OK = 4106;
        public const int CM_XIAMA = 4107;
        public const int CM_RUN3 = 4108;
        public const int CM_YAOQING_SHANGMA = 4109;
        public const int CM_INVITE_HORSE = 4110;
        public const int CM_RIDER_DOWN = 4111;
        
        
        
        public const int CM_HIT = 3014;
        public const int CM_HEAVYHIT = 3015;
        public const int CM_BIGHIT = 3016;
        public const int CM_SPELL = 3017;
        public const int CM_POWERHIT = 3018;
        public const int CM_LONGHIT = 3019;
        public const int CM_WIDEHIT = 3024;
        public const int CM_FIREHIT = 3025;
        public const int CM_SAY = 3030;
        public const int CM_SWITCH_LISTEN = 3032;
        public const int CM_SPEEDHACKMSG = 3500;
        public const int SM_SWORD_HIT = 2;
        // SM_41 = 4 removed: the name promised wire ident 41 but held 4. Wire 41 is
        // SM_FEATURECHANGED, sent at 0x6F2E2B `66 BA 29 00 mov dx,0x29` ->
        // 0x6F2E33 `FF 93 54 02 00 00 call [ebx+0x254]` (134,873 production packets).
        // 4 has no send-slot site anywhere in CODE and zero production packets.
        public const int SM_RUSH = 6;
        public const int SM_RUSHKUNG = 7;
        
        
        
        public const int SM_FIREHIT = 8;
        public const int SM_BACKSTEP = 9;
        
        
        
        public const int SM_TURN = 10;
        
        
        
        public const int SM_WALK = 11;
        public const int SM_SITDOWN = 12;
        public const int SM_RUN = 13;
        
        
        
        public const int SM_HIT = 14;
        public const int SM_HEAVYHIT = 15;
        public const int SM_BIGHIT = 16;
        
        
        
        public const int SM_SPELL = 17;
        
        
        
        public const int SM_POWERHIT = 18;
        public const int SM_LONGHIT = 19;
        public const int SM_DIGUP = 20;
        public const int SM_DIGDOWN = 21;
        public const int SM_FLYAXE = 22;
        public const int SM_LIGHTING = 23;
        public const int SM_WIDEHIT = 24;
        public const int SM_CRSHIT = 25;
        public const int SM_TWINHIT = 26;
        public const int SM_ALIVE = 27;
        public const int SM_MOVEFAIL = 28;
        public const int SM_HIDE = 29;
        public const int SM_DISAPPEAR = 30;
        
        
        
        public const int SM_STRUCK = 31;
        public const int SM_DEATH = 32;
        public const int SM_SKELETON = 33;
        public const int SM_NOWDEATH = 34;
        public const int SM_HEAR = 40;

        /// <summary>
        /// 彩色文字 hear, ident 105. Proven by exhaustive scan: the immediate 0x69
        /// occurs in exactly two places image-wide -- <c>mov cx,0x69</c> at
        /// 0x6C9485 (the direct send in the say consumer) and <c>mov dx,0x69</c> at
        /// 0x6B4B51 (the RM 10033 handler) -- and nowhere else, so it is a
        /// dedicated opcode rather than a reused constant.
        /// </summary>
        public const int SM_COLORHEAR = 105;
        // CM 3295 worker 0x6EB8E4 emits this after consuming a TMicroWhelk.
        public const int SM_MICROWHELK = 106;
        public const int SM_FEATURECHANGED = 41;
        public const int SM_USERNAME = 42;
        public const int SM_43 = 43;
        public const int SM_WINEXP = 44;
        public const int SM_LEVELUP = 45;
        public const int SM_DAYCHANGING = 46;
        public const int SM_LOGON = 50;
        public const int SM_NEWMAP = 51;
        public const int SM_ABILITY = 52;
        public const int SM_HEALTHSPELLCHANGED = 53;
        public const int SM_MAPDESCRIPTION = 54;
        public const int SM_SPELL2 = 117;
        public const int SM_HWID = 113;
        public const int SM_SYSMESSAGE = 100;
        public const int SM_GROUPMESSAGE = 101;
        public const int SM_CRY = 102;
        public const int SM_WHISPER = 103;
        public const int SM_GUILDMESSAGE = 104;
        public const int SM_ADDITEM = 200;
        public const int SM_BAGITEMS = 201;
        public const int SM_DELITEM = 202;
        public const int SM_UPDATEITEM = 203;
        public const int SM_ADDMAGIC = 210;
        public const int SM_SENDMYMAGIC = 211;
        public const int SM_DELMAGIC = 212;
        /// <summary>
        /// Revive/rebirth notice body. Native RM 12308 is emitted by
        /// <c>sub_73C910</c> and converted at 0x6B4E77 into SM 213 with
        /// Recog=BaseObject, Param=wParam, Tag=Series=0.
        /// </summary>
        public const int SM_REVIVE_MESSAGE = 213;
        // 战神 attack-mode notify ident = 0x221 (545), NOT 213.
        // All three native senders load dx=0x221 and go through VMT+0x250 (SendDefMessage):
        //   @0x6B210C  UserLogon sub_6B1AA0  (mov cl,[esi+0xAED] = MyAttackMode; mov dx,0x221)
        //   @0x6F2D33  sub_6F2D10 SetAttackMode
        //   @0x623A2E  ChangeAttackMode GM handler inside sub_622820
        // Native's 213 (0xD5) is the body-bearing revive/rebirth notice above,
        // not attack-mode state. The request ident CM_ATTACKMODE is also 545:
        // 战神 deliberately echoes the request id.
        public const int SM_ATTACKMODE = 545;
        public const int CM_CHECKTIME = 15999;
        public const int SM_BAGITEMDURACHG = 641;
        public const int SM_STORAGE_ADDITEM = 717;
        public const int SM_STORAGEITEMDURACHG = 790;

        // MINE-50: Mining success self-notification
        // EA=0x006BC2F8: 66 BA 74 02 (mov dx, 0x274)
        public const int SM_MINESUCCESS = 628;



        public const int SM_CERTIFICATION_SUCCESS = 500;
        
        
        
        public const int SM_CERTIFICATION_FAIL = 501;
        
        
        
        public const int SM_ID_NOTFOUND = 502;
        public const int SM_PASSWD_FAIL = 503;
        public const int SM_NEWID_SUCCESS = 504;
        public const int SM_NEWID_FAIL = 505;
        public const int SM_CHGPASSWD_SUCCESS = 506;
        public const int SM_CHGPASSWD_FAIL = 507;
        
        
        
        public const int SM_QUERYCHR = 520;
        
        
        
        public const int SM_NEWCHR_SUCCESS = 521;
        
        
        
        public const int SM_NEWCHR_FAIL = 522;
        
        
        
        public const int SM_DELCHR_SUCCESS = 523;
        
        
        
        public const int SM_DELCHR_FAIL = 524;
        public const int SM_STARTPLAY = 525;
        public const int SM_STARTFAIL = 526;
        public const int SM_QUERYCHR_FAIL = 527;
        public const int SM_OUTOFCONNECTION = 528;
        public const int SM_PASSOK_SELECTSERVER = 529;
        public const int SM_SELECTSERVER_OK = 530;
        public const int SM_NEEDUPDATE_ACCOUNT = 531;
        public const int SM_UPDATEID_SUCCESS = 532;
        public const int SM_UPDATEID_FAIL = 533;
        public const int SM_QUERYDELCHR = 534;
        public const int SM_QUERYDELCHR_FAIL = 535;
        public const int SM_RESDELCHR_SUCCESS = 536;
        public const int SM_RESDELCHR_FAIL = 537;
        public const int SM_DROPITEM_SUCCESS = 600;
        public const int SM_DROPITEM_FAIL = 601;
        public const int SM_ITEMSHOW = 610;
        public const int SM_ITEMHIDE = 611;
        public const int SM_OPENDOOR_OK = 612;
        public const int SM_OPENDOOR_LOCK = 613;
        public const int SM_CLOSEDOOR = 614;
        public const int SM_TAKEON_OK = 615;
        public const int SM_TAKEON_FAIL = 616;
        public const int SM_TAKEOFF_OK = 619;
        public const int SM_TAKEOFF_FAIL = 620;
        public const int SM_SENDUSEITEMS = 621;
        public const int SM_WEIGHTCHANGED = 622;
        public const int SM_ACT_GOOD = 629;
        public const int SM_ACT_FAIL = 630;
        public const int SM_CLEAROBJECTS = 633;
        public const int SM_CHANGEMAP = 634;
        public const int SM_EAT_OK = 635;
        public const int SM_EAT_FAIL = 636;
        public const int SM_BUTCH = 637;
        public const int SM_MAGICFIRE = 638;
        public const int SM_MAGICFIRE_FAIL = 639;
        public const int SM_MAGIC_LVEXP = 640;
        public const int SM_DURACHANGE = 642;
        public const int SM_MERCHANTSAY = 643;
        public const int SM_MASTERRELATION = 2820;
        public const int SM_MOVEMESSAGE = 99;
        public const int SM_MERCHANTDLGCLOSE = 644;
        public const int SM_SENDGOODSLIST = 645;
        public const int SM_MERCHANT_QUERY = 2831;
        public const int SM_2821 = 2821;
        public const int SM_CATTLE_SYSMESSAGE = 2828;
        public const int SM_CATTLE_BAR_SHOW = 2844;
        public const int SM_CATTLE_BAR_HIDE = 2845;
        public const int SM_CATTLE_BAR_CHANGE = 2846;
        public const int SM_SENDUSERSELL = 646;
        public const int SM_SENDBUYPRICE = 647;
        public const int SM_USERSELLITEM_OK = 648;
        public const int SM_USERSELLITEM_FAIL = 649;
        public const int SM_BUYITEM_SUCCESS = 650;
        public const int SM_BUYITEM_FAIL = 651;
        public const int SM_SENDDETAILGOODSLIST = 652;
        public const int SM_GOLDCHANGED = 653;
        public const int SM_CHANGELIGHT = 654;
        public const int SM_LAMPCHANGEDURA = 655;
        public const int SM_CHANGENAMECOLOR = 656;
        public const int SM_CHARSTATUSCHANGED = 657;
        public const int SM_SENDNOTICE = 658;
        public const int SM_GROUPMODECHANGED = 659;
        public const int SM_CREATEGROUP_OK = 660;
        public const int SM_CREATEGROUP_FAIL = 661;
        public const int SM_GROUPADDMEM_OK = 662;
        public const int SM_GROUPDELMEM_OK = 663;
        public const int SM_GROUPADDMEM_FAIL = 664;
        public const int SM_GROUPDELMEM_FAIL = 665;
        public const int SM_GROUPCANCEL = 666;
        public const int SM_GROUPMEMBERS = 667;
        public const int SM_SENDUSERREPAIR = 668;
        public const int SM_USERREPAIRITEM_OK = 669;
        public const int SM_USERREPAIRITEM_FAIL = 670;
        public const int SM_SENDREPAIRCOST = 671;
        public const int SM_DEALMENU = 673;
        public const int SM_DEALTRY_FAIL = 674;
        public const int SM_DEALADDITEM_OK = 675;
        public const int SM_DEALADDITEM_FAIL = 676;
        public const int SM_DEALDELITEM_OK = 677;
        public const int SM_DEALDELITEM_FAIL = 678;
        public const int SM_DEALCANCEL = 681;
        public const int SM_DEALREMOTEADDITEM = 682;
        public const int SM_DEALREMOTEDELITEM = 683;
        public const int SM_DEALCHGGOLD_OK = 684;
        public const int SM_DEALCHGGOLD_FAIL = 685;
        public const int SM_DEALREMOTECHGGOLD = 686;
        public const int SM_DEALSUCCESS = 687;
        public const int SM_SENDUSERSTORAGEITEM = 700;
        public const int SM_STORAGE_OK = 701;
        public const int SM_STORAGE_FULL = 702;
        public const int SM_STORAGE_FAIL = 703;
        public const int SM_SAVEITEMLIST = 704;
        public const int SM_TAKEBACKSTORAGEITEM_OK = 705;
        public const int SM_TAKEBACKSTORAGEITEM_FAIL = 706;
        public const int SM_TAKEBACKSTORAGEITEM_FULLBAG = 707;
        public const int SM_AREASTATE = 766;
        public const int SM_MYSTATUS = 708;
        public const int SM_DELITEMS = 709;
        public const int SM_READMINIMAP_OK = 710;
        public const int SM_READMINIMAP_FAIL = 711;
        public const int SM_SENDUSERMAKEDRUGITEMLIST = 712;
        public const int SM_MAKEDRUG_SUCCESS = 713;
        public const int SM_MAKEDRUG_FAIL = 714;
        public const int SM_716 = 716;
        public const int SM_STORAGE_SPACE = 718;
        public const int SM_CHANGEGUILDNAME = 750;
        public const int SM_SENDUSERSTATE = 751;
        public const int SM_SUBABILITY = 752;
        public const int SM_BUILDGUILD_OK = 762;
        public const int SM_BUILDGUILD_FAIL = 763;
        public const int SM_DONATE_OK = 764;
        public const int SM_MENU_OK = 767;
        public const int SM_DLGMSG = 772;
        public const int SM_BUILDHERO = 773;
        public const int SM_HEROLISTINFO = 971;
        public const int SM_SPACEMOVE_HIDE = 800;
        public const int SM_SPACEMOVE_SHOW = 801;
        public const int SM_RECONNECT = 802;
        public const int SM_GHOST = 803;
        /// <summary>
        /// Pinned to the byte by walking the RM pump forward to its send slot,
        /// per REPLICATION_RULES 4.20 — searching for the 0x324 immediate
        /// directly is the noisy direction and finds nothing usable.
        /// <para>
        /// RM_SHOWEVENT = 10334 = 0x285E resolves through the ident dispatcher:
        /// <c>0x6B3EE4 movzx eax,word [ebx]</c>, <c>0x6B3EEC jg 0x6B412B</c>,
        /// <c>0x6B4130 jg 0x6B42AB</c> is not taken (0x285E &lt; 0x28A1),
        /// <c>0x6B4141 jg 0x6B41B2</c>, <c>0x6B41B7 jg 0x6B421E</c>, then
        /// <c>0x6B421E add eax,0xFFFFD7BD</c> (= -0x2843, base ident 10307),
        /// <c>0x6B4223 cmp eax,0x1D</c>, <c>0x6B422C jmp [0x6B4233 + eax*4]</c>.
        /// Index 27 holds 0x6B5A09.
        /// </para>
        /// <para>
        /// That arm splits on the event type byte at
        /// <c>0x6B5A16 cmp byte [esi+0x0C],0x29</c> and both halves end in the
        /// same wire ident: stall <c>0x6B5AB3 66 BA 24 03 mov dx,0x324</c> with
        /// body length <c>0x6B5AAE 6A 40</c> = 64, normal
        /// <c>0x6B5B1F 66 BA 24 03</c> with <c>0x6B5B1A 6A 0C</c> = 12, both
        /// through the body-carrying send slot <c>call [VMT+0x254]</c>.
        /// The 12/64 split and the ShortString capacities (3 for the normal
        /// owner name at <c>0x6B5AED mov cl,3</c>, 14 and 30 for the stall's two
        /// at <c>0x6B5A49 mov cl,0x0E</c> / <c>0x6B5A91 mov cl,0x1E</c>) are the
        /// same numbers PacketRoundTripCheck already asserts.
        /// </para>
        /// </summary>
        public const int SM_SHOWEVENT = 804;
        /// <summary>
        /// Same dispatcher, index 26 -> 0x6B5B33, which loads
        /// <c>0x6B5B47 66 BA 25 03 mov dx,0x325</c> and sends through the plain
        /// slot <c>call [VMT+0x250]</c> with a zero body length
        /// (<c>0x6B5B42 6A 00</c>).
        /// </summary>
        public const int SM_HIDEEVENT = 805;
        public const int SM_SPACEMOVE_HIDE2 = 806;
        public const int SM_SPACEMOVE_SHOW2 = 807;
        public const int SM_TIMECHECK_MSG = 810;
        public const int SM_ADJUST_BONUS = 811;
        public const int SM_SHOPITEMS = 812;
        public const int SM_RESHOPITEMS_OK = 813;
        public const int SM_RESHOPITEMS_FAIL = 814;
        public const int SM_FIRSTSHOP = 815;
        public const int SM_DOSHOP_FAIL = 816;
        public const int SM_TOHEROBAG_OK = 817;
        public const int SM_TOHEROBAG_FAIL = 818;
        public const int SM_TOHUMBAG_OK = 819;
        public const int SM_TOHUMBAG_FAIL = 820;
        /// <summary>
        /// GP 禁售物品名表 (config\GPForbidItems.txt). Native sub_63C194
        /// <c>0x63C1C3 66 BA 35 03 mov dx,0x335</c> via [obj+0x254].
        /// Recog=player, Param=count, Tag=0, Series=0, body N×16 ShortString[15].
        /// After SM_SHOPITEMS (0x63A2D4) and SM_RESHOPITEMS_OK (0x63A38A);
        /// skipped when count&lt;=0 (0x63C1A4 jle).
        /// </summary>
        public const int SM_GPFORBIDITEMS = 821;
        public const int SM_OPENHEALTH = 1100;
        public const int SM_CLOSEHEALTH = 1101;
        public const int SM_CHANGEFACE = 1104;
        public const int SM_BREAKWEAPON = 1102;
        public const int SM_INSTANCEHEALGUAGE = 1103;
        public const int SM_VERSION_FAIL = 1106;
        public const int SM_GETDIAMNUM_EXT = 1202;
        public const int SM_SECHERO_EST = 1215;
        public const int SM_ITEMUPDATE = 1500;
        public const int SM_MONSTERSAY = 1501;
        public const int SM_TASK_BRIEF_INFO = 1504;
        public const int SM_TASK_DETAIL_INFO = 1505;
        public const int SM_TASK_PROGRESS_INFO = 1506;
        public const int SM_TASK_DELETE = 1507;
        public const int SM_TASK_CLEAR_ALL = 1508;
        public const int SM_TASK_LIST_CHANGED = 1509;
        public const int SM_PUSH_SINGLE_TASK = 1530;
        public const int SM_SEND_TITLEINFO = 2870;
        public const int SM_EXCHGTAKEON_OK = 65023;
        public const int SM_EXCHGTAKEON_FAIL = 65024;
        public const int SM_TEST = 65037;
        public const int SM_ACTION_MIN = 65070;
        public const int SM_ACTION_MAX = 65071;
        public const int SM_ACTION2_MIN = 65072;
        public const int SM_ACTION2_MAX = 65073;
        public const int CM_SERVERREGINFO = 65074;
        public const int CM_GETGAMELIST = 5001;
        public const int SM_SENDGAMELIST = 5002;
        
        
        
        public const int CM_GETBACKPASSWORD = 5003;
        
        
        
        public const int SM_GETBACKPASSWD_SUCCESS = 5005;
        
        
        
        public const int SM_GETBACKPASSWD_FAIL = 5006;
        
        
        
        public const int SM_SERVERCONFIG = 5007;
        public const int SM_GAMEGOLDNAME = 5008;
        public const int SM_PASSWORD = 1105;
        public const int SM_CLIENT_CONF = 2953;
        public const int SM_SHANGMA_OK = 3413;
        public const int SM_XIAMA_OK = 3414;
        public const int SM_NATIVE_HORSE_CALL_STOP = 3415;
        public const int SM_RUN3 = 3416;
        public const int SM_INVITE_HORSE = 3417;
        public const int SM_SHANGMA_OK2 = 3418;
        public const int SM_XIAMA_2 = 3419;
        /// <summary>
        /// 0xDE5. Broadcast when a caster teleports onto a cell: magic 266
        /// (0x773FC6 `66 BA E5 0D` then VMT+0xE0) and magic 168 冲锋陷阵
        /// (0x6EF206). Series carries the magic id, Param/Tag the destination.
        /// VMT+0xE0 = 0x6DC0C0 -> sub_7651EC -> sub_5F7778 -> sub_5F6C24, the
        /// `33 AA BB 77` gate multicast, so this is a real wire ident.
        /// </summary>
        public const int SM_NATIVE_BLINK_MOVE = 3557;
        /// <summary>
        /// 0xDE6. Same broadcast slot, emitted by magic 68 at 0x6EC8D4 with
        /// Series = the charge direction rather than a magic id.
        /// </summary>
        public const int SM_NATIVE_CHARGE_MOVE = 3558;
        /// <summary>
        /// 战神 CM opcode 3420 (0xD5C) — 定位石记录当前坐标 (TFixedCoordStone setter).
        /// Dispatch: M2Server.exe 0x6D873F `sub eax,0xD5C` / 0x6D8745 `je 0x6DADE3`.
        /// Handler body 0x6DADE3 -> setter sub_6E9BAC (call site 0x6DAE1B).
        /// </summary>
        public const int CM_SETFIXEDCOORD = 3420;
        /// <summary>
        /// 定位石已记录坐标回包 (map name + X/Y). Same value as the request
        /// <see cref="CM_SETFIXEDCOORD"/>: native answers on the ident it was asked on.
        /// <para>
        /// 0x3026 (12326) is the INTERNAL queue tag, not the wire ident. sub_765E68 is
        /// an enqueue, not a send: it allocates a record via 0x764D68 and fills it,
        ///   0x765E96  66 89 03        mov word [ebx],   ax   ; tag from cx
        ///   0x765E9D  66 89 43 02     mov word [ebx+2],  ax
        ///   0x765EA4  89 43 04        mov [ebx+4],  eax
        ///   0x765EAA  89 43 08        mov [ebx+8],  eax
        ///   0x765EB0  89 43 0C        mov [ebx+0xC], eax
        ///   0x765EDA / 0x765EF9       [ebx+0x14] = len+1, [ebx+0x10] = body
        /// with no call through the send slots [+0x250]/[+0x254] anywhere in it.
        /// </para>
        /// <para>
        /// The wire packet is emitted later by the RM handler at 0x6B6036, which reads
        /// back those very fields and only then sends:
        ///   0x6B6036  66 8B 43 02        mov ax, word [ebx+2]
        ///   0x6B603B  66 8B 43 08        mov ax, word [ebx+8]
        ///   0x6B6040  66 8B 43 0C        mov ax, word [ebx+0xC]
        ///   0x6B6045  8B 43 10           mov eax, [ebx+0x10]
        ///   0x6B6049  0F B7 43 14        movzx eax, word [ebx+0x14]
        ///   0x6B604E  8B 4B 04           mov ecx, [ebx+4]
        ///   0x6B6051  66 BA 5C 0D        mov dx, 0xD5C          ; = 3420
        ///   0x6B605A  FF 93 54 02 00 00  call [ebx+0x254]       ; send slot
        /// </para>
        /// A whole-image scan settles the split: `mov cx,0x3026` appears at exactly the
        /// two enqueue sites, 0x6B2414 (login replay, inside UserLogon fn~0x6B1AA0,
        /// guarded by `cmp byte [esi+0x18F8],0` at 0x6B23E3) and 0x6E9CFE (the setter),
        /// while 0xD5C appears as an ident exactly once, at the send above.
        /// Emitting 12326 on the wire, as this constant used to, means the original
        /// client never recognises the reply and the recorded position never displays.
        /// </summary>
        public const int SM_FIXEDCOORD = 3420;
        public const int SM_HORSERUN = 5010;
        public const int UNKNOWMSG = 199;
        public const int SS_OPENSESSION = 100;
        public const int SS_CLOSESESSION = 101;
        public const int SS_KEEPALIVE = 104;
        public const int SS_KICKUSER = 111;
        public const int SS_SERVERLOAD = 113;
        // SS_200..214 reuse the same integers as SM_ADDITEM/BAGITEMS/DELITEM
        // (200..203) and as ISM 200..214. Native ProcessOthGsMsg (0x657144
        // add edx,-202 / cmp 0x37 / ja 0x6573A0) only accepts 202..257 on
        // the DB type-2 OthGs channel. These SS_* aliases are not a second
        // native ident family — sending them through [obj+0x250]/[+0x254]
        // would collide with the live client SM_ item idents (srv_AppearTimes
        // 200=2060999, 201=81693, 202=162358, 203=11067).
        public const int SS_200 = 200;
        public const int SS_201 = 201;
        public const int SS_202 = 202;
        public const int SS_203 = 203;
        public const int SS_204 = 204;
        public const int SS_205 = 205;
        public const int SS_206 = 206;
        public const int SS_207 = 207;
        public const int SS_208 = 208;
        public const int SS_209 = 209;
        public const int SS_210 = 210;
        public const int SS_211 = 211;
        public const int SS_212 = 212;
        public const int SS_213 = 213;
        public const int SS_214 = 214;
        public const int SS_WHISPER = 299;
        
        
        
        public const int SS_SERVERINFO = 103;
        
        
        
        public const int SS_SOFTOUTSESSION = 102;
        public const int SS_LOGINCOST = 30002;
        public const int DBR_FAIL = 2000;
        public const int DB_LOADHUMANRCD = 100;
        public const int DB_SAVEHUMANRCD = 101;
        public const int DB_SAVEHUMANRCDEX = 102;
        public const int DBR_LOADHUMANRCD = 1100;
        public const int DBR_SAVEHUMANRCD = 1102;
        public const int SG_FORMHANDLE = 32001;
        public const int SG_STARTNOW = 32002;
        public const int SG_STARTOK = 32003;
        public const int SG_CHECKCODEADDR = 32004;
        public const int SG_USERACCOUNT = 32005;
        public const int SG_USERACCOUNTCHANGESTATUS = 32006;
        public const int SG_USERACCOUNTNOTFOUND = 32007;
        public const int GS_QUIT = 32101;
        public const int GS_USERACCOUNT = 32102;
        public const int GS_CHANGEACCOUNTINFO = 32103;
        public const int WM_SENDPROCMSG = 32104;
        // LOMCN process-local sentinel, not a wire magic. Full-image scan of
        // 0xAA9AAA9A / 0xAA55AA55 / 0x55AA55AA = 0 hits. Live frame magic is
        // 0x33AABB77 (26 sites, e.g. 0x7130B3 C7 00 77 BB AA 33). Do not
        // change the value; do not treat this as a GameGate/SGRP ident.
        public const uint RUNGATECODE = 0xAA55AA55 + 0x00450045;
        public const int GM_OPEN = 1;
        public const int GM_CLOSE = 2;
        public const int GM_CHECKSERVER = 3;
        public const int GM_CHECKCLIENT = 4;
        public const int GM_DATA = 5;
        public const int GM_SERVERUSERINDEX = 6;
        public const int GM_RECEIVE_OK = 7;
        public const int SM_RUNGATELOGOUT = 599;
        public const int SM_PLAYERCONFIG = 560;
        public const int GM_TEST = 20;
        public const int GROUPMAX = 11;
        public const int CM_42HIT = 42;
        public const int CM_PASSWORD = 2001;
        public const int CM_CHGPASSWORD = 2002;
        public const int CM_SETPASSWORD = 2004;
        // ID3035: 值对，名字不对。3035 是真实可达的 ident，但派发器把它送进
        // HIT CASE1，与另外十个攻击 opcode 同组 —— 0x6D8610
        // `0F 84 99 18 00 00 je 0x6D9EAF`，经累减链 0x6D85F0 `sub eax,0xBD4` /
        // 0x6D85FB `sub eax,2` / 0x6D8604 `sub eax,2` / 0x6D860D `sub eax,3` 到达。
        // sub_6EC078 的窗口恰好收在它上面（0x6EC15D `add eax,-0xBBA` /
        // 0x6EC162 `cmp eax,0x21`），字节表 0x6EC178[33]=0x09 选中槽
        // 0x6EC19A[9]=0x6EC29C `66 B9 F9 03 mov cx,0x3F9`，即动作码 1017 ——
        // 比 CM_CRSHIT 的 1018 小一号；而 1018 的 worker 0x771BB8 是空壳
        // `55 8B EC 33 C0 5D C2 04 00`，1017 的 worker 0x772388 是活的。
        // 原生的骑乘跑是 CM_RUN3(4108)，其 handler 0x6D9D99 开篇即
        // `B2 33 mov dl,0x33` / `call 0x772960`，未骑乘就发 0x276 拒绝。
        // 这个名字来自上游 Delphi 客户端头 MirClient/Common/Grobal2z.pas:1043，
        // 那里的注释原字节 CE B4 D6 AA CF FB CF A2 C2 EB 按 GBK 解码是
        // 「未知消息码」—— 上游自己就标了是占位名。保留名字是沿用本仓既有惯例
        // （CM_3037 = 3027 同属"留上游名、纠上游值"）。
        public const int CM_HORSERUN = 3035;
        // 3026/3027/3028, not 3036/3037/3038. Native dispatch table 0x6D8592 is based
        // at ident 3010, and entry [16] (0x6D85D2) = 0x6D9EAF is 3026; 3027 is the
        // `0x6D8502 3D D3 0B 00 00 cmp eax,0xBD3` / `0x6D850D je 0x6D9F4B` arm; 3028 is
        // `0x6D85F0 2D D4 0B 00 00 sub eax,0xBD4` / `0x6D85F5 je 0x6D9EAF`.
        // 3036/3037/3038 have zero comparison-form encodings anywhere in CODE
        // (0x401000..0x7A10D0): no 3D/2D/05 imm32, no 81 /0 /5 /7 imm32, no 66-prefixed
        // imm16 and no 83-form, so the native dispatcher cannot reach them at all.
        public const int CM_CRSHIT = 3026;
        public const int CM_3037 = 3027;
        public const int CM_TWINHIT = 3028;
        public const int CM_QUERYUSERSET = 3040;
        public const int CM_QUERY_TASK_DETAIL = 3051;
        public const int CM_QUERY_TASK_ALL = 3052;
        public const int CM_DO_TASK_COMMAND = 3053;
        public const int CM_QUERY_SINGLE_TASK = 3054;
        public const int SM_PLAYDICE = 1200;
        public const int SM_PASSWORDSTATUS = 8002;

        // Native hero protocol. Visible actions still use the common RM_/SM_ action path.
        public const int SM_HERO_RUSH = 4;
        public const int SM_HERO_RUSHKUNG = 5;
        public const int SM_HERO_LONGHIT = 25;
        public const int SM_HERO_LASTHIT = 26;
        /// <summary>
        /// Login version handshake. Native UserLogon sub_6B1D64 @0x6B23C6
        /// calls sub_6F05D8, which at <c>0x6F05F2 66 BA 78 03 mov dx,0x378</c>
        /// sends via [obj+0x250]: Recog=0x3EA (1002), Param=0x3E7 (999),
        /// Tag=0, Series=0, empty body. Always paired with SM_LOGIN_NOW (889).
        /// </summary>
        public const int SM_LOGIN_VER = 888;
        /// <summary>
        /// Login clock/now extension. Same sender sub_6F05D8 immediately after 888:
        /// <c>0x6F063A 66 BA 79 03 mov dx,0x379</c> via [obj+0x254].
        /// Recog=0x3F1 (1009), Param=0x3E7 (999), Tag=word at [[0x7D5FA0]]
        /// (image snapshot 0x1009), Series=0, 24-byte body:
        /// word 0x14, word 0x2E, 4 pad, TDateTime Now (0x40F0A4), dword [[0x7D6558]], 4 pad.
        /// </summary>
        public const int SM_LOGIN_NOW = 889;
        public const int SM_HERO_QUITMAGIC = 896;
        public const int SM_HERO_LOGMAGIC = 897;
        public const int SM_HERO_NAME = 898;
        public const int SM_HERO_LOGON = 899;
        public const int SM_HERO_ABILITY = 900;
        public const int SM_HERO_SUBABILITY = 901;
        public const int SM_HERO_BAGITEMS = 902;
        public const int SM_HERO_SENDUSEITEMS = 903;
        public const int SM_HERO_SENDMYMAGIC = 904;
        public const int SM_HERO_ADDITEM = 905;
        public const int SM_HERO_DELITEM = 906;
        public const int SM_HERO_TAKEON_OK = 907;
        public const int SM_HERO_TAKEON_FAIL = 908;
        public const int SM_HERO_TAKEOFF_OK = 909;
        public const int SM_HERO_TAKEOFF_FAIL = 910;
        public const int SM_HERO_EAT_OK = 911;
        public const int SM_HERO_EAT_FAIL = 912;
        public const int SM_HERO_ADDMAGIC = 913;
        public const int SM_HERO_LEVELUP = 914;
        public const int SM_HERO_WINEXP = 915;
        public const int SM_HERO_MAGIC_LVEXP = 916;
        public const int SM_HERO_DELMAGIC = 2972;
        public const int SM_HERO_DELITEMS = 917;
        public const int SM_HERO_LOGOUT = 918;
        public const int SM_HERO_DURACHANGE = 919;
        public const int SM_HERO_DROPITEM_SUCCESS = 920;
        public const int SM_HERO_DROPITEM_FAIL = 921;
        public const int SM_HERO_BAGITEMDURACHG = 922;
        public const int SM_HERO_UNIONSTATUS = 923;
        public const int SM_HERO_SPLITSHADOW = 924;
        public const int SM_HERO_HELPOP_OK = 925;
        public const int SM_CATTLE_PRIZE_OPEN = 950;
        public const int SM_CATTLE_PRIZE_REVEAL = 952;
        public const int SM_CATTLE_PRIZE_CLAIM = 953;
        public const int SM_GLORYFEALTY = 960;
        public const int SM_MERCHANTQUERYEXCHGBOOK = 961;
        public const int SM_EXCHANGEBOOK_ROTATE = 962;
        public const int SM_EXCHANGEBOOK_GET_PRIZE = 963;
        public const int SM_PHYSICAL_ATT = 1230;
        public const int SM_NATIVE_UNION_EFFECT = 1230;
        /// <summary>
        /// Channel-magic cancel broadcast. Native sub_6EE128
        /// <c>0x6EE164 66 BA D0 04 mov dx,0x4D0</c> via [obj+0xE0].
        /// Recog=Self, Param=magicId from [obj+0xA24], Tag=0, Series=0.
        /// C# queues this ident through SendRefMsg; Operate must emit the wire packet.
        /// </summary>
        public const int SM_CHANNEL_MAGIC_CANCEL = 1232;
        /// <summary>
        /// Location-channel magic cancel. Native sub_6EF5D0
        /// <c>0x6EF62E 66 BA D2 04 mov dx,0x4D2</c> via [obj+0xE0].
        /// Recog=Self, Param=magicId from [obj+0xA4C], Tag=0, Series=0.
        /// Skipped when magicId==0 (0x6EF5EA jbe).
        /// </summary>
        public const int SM_LOCATION_CHANNEL_MAGIC_CANCEL = 1234;
        public const int SM_SECHERO_PRACTICE = 1216;

        // === Native hero client commands ===
        public const int CM_HERO_LOGON = 1050;           // Client requests hero spawn
        public const int CM_HERO_LOGOUT = 1051;          // Client requests hero despawn
        public const int CM_HERO_TOHEROBAG = 1100;       // Move item from master bag to hero bag
        public const int CM_HERO_TOHUMBAG = 1101;        // Move item from hero bag to master bag
        public const int CM_HERO_TAKEON = 1102;          // Hero equip item
        public const int CM_HERO_TAKEOFF = 1103;         // Hero unequip item
        public const int CM_HERO_EAT = 1104;             // Hero use/consume item
        public const int CM_HERO_APPTARG = 1105;         // Hero approach target
        public const int CM_HERO_DROPITEM = 1106;        // Hero drop item
        public const int CM_HERO_CHGSTATE = 1107;        // Hero change state (follow/attack/stand)
        public const int CM_HERO_POWERUP = 1108;         // Hero power-up / strong-hit command
        public const int SM_ORDER_LIST = 1108;           // Server response to CM_QUEST_ORDER
        public const int CM_HERO_SKILL_HOTKEY = 1109;    // Enable or disable a hero skill
        public const int CM_SECHERO_PRACTICE = 1216;

        // === 4000-series Mobile Login Protocol (战神 client) ===
        public const int SM_SERVER_LIST = 4001;   // Server sends server list to client
        public const int CM_SELECT_SERVER = 4002; // Client selects server
        public const int SM_SELECT_SERVER = 4002; // Server responds with IP/port
        public const int SM_LOGIN = 4003;          // Server prompts client to login
        public const int CM_LOGIN_AUTH = 4004;    // Client sends auth ticket
        public const int SM_LOGIN_AUTH = 4004;    // Server auth response
        public const int SM_CHR_LIST = 4010;      // Character list (maps from SM_QUERYCHR=520)
        public const int CM_NEWCHR4012 = 4012;    // Create new character (mobile)
        public const int CM_DELCHR4013 = 4013;    // Delete character (mobile)
        public const int SM_DELCHR4013 = 4013;
        public const int CM_QUERYDELCHR4014 = 4014; // Query deleted chars (mobile)
        public const int CM_RECOVERCHR4015 = 4015;  // Recover character (mobile)
        // 0xFB0 = 角色改名。原版 DBServer 内层 opcode 派发（fn_5CDF6C，12 字节头、
        // opcode 取自 word[msg+4]）：idx = 4016 - 0xFAC = 0x04 -> grp = byte[0x5CE345+idx] = 5
        // -> dword[0x5CE363+5*4] = 0x5CE404 -> `call 0x5CD2EC`（校验层宿主）
        // -> 0x5CD3AF `call 0x5A8DDC`（主档 user_index/user_data）
        // -> 0x5A912F `call 0x5A923C`（22 条三库级联，打 19 张表）
        // 该族其余 5 个 opcode（4012/4013/4014/4015/4017）此前已有常量，唯 4016 缺失。
        public const int CM_RENAMECHR4016 = 4016;   // Rename character (mobile)
        public const int SM_RENAMECHR4016 = 4016;
        public const int CM_SELCHR4017 = 4017;      // Select character (mobile)
        public const int SM_SELCHR4017 = 4017;
        public const int SM_OUTOFCONNECTION_4018 = 4018;
        public const int SM_RECONNECT_MOBILE = 4031;
        public const int CM_RECONNECT_MOBILE = 4031;

        // === Combat / Visual / NPC SM_ messages (战神 client) ===
        public const int SM_SLAVE_BORN = 791;        // Slave monster spawned
        public const int SM_SLAVE_VANISH = 792;      // Slave monster despawned
        public const int SM_SHOWBODY_EFFECT = 793;    // Visual body effect
        public const int SM_COMMON_INFORMATION = 2821; // Obj+0x3FE pass-through transition (0x6B30AD/0x6B30C8)
        public const int SM_SAFE_ZONE_INFO = 4230;     // Map safe zone information
        // UserLogon 0x6B1DC2: all-zero header, body = sorted MapInfoEx.txt Text.
        public const int SM_MAPINFO_EX = 1281;
        public const int SM_BIGMONMAGIC = 797;        // Monster magic effect
        public const int SM_NPCWALK = 798;            // NPC walk animation
        public const int SM_FIREON = 779;             // Fire hit mode ON
        public const int SM_SWORDHIT_ON = 2819;       // Sword hit mode ON
        // Outer id-65 arm 0x6BCA33 `66 BA 3B 0B mov dx,0xB3B` then
        // `FF 93 50 02 00 00 call [ebx+0x250]` (SendDefMessage). Recog and
        // the four stack words are all 0.
        public const int SM_CHARGED_COUNTER = 2875;
        // 战神 thrusting (刺杀剑术) toggle notify = ident 624 (0x270), param1 = 1.
        // Native UserLogon @0x6B225B: gated on [obj+0xA8]!=0 && [obj+0x94]==0, latches
        // [obj+0x94]=1, then `push 1; push 0; push 0; push 0; xor ecx,ecx;
        // mov dx,0x270; call [ebx+0x250]` (SendDefMessage) — recog=0, nParam=1.
        public const int SM_THRUSTING = 624;

        // 攻杀剑术 (SKILL_YEDO) charge-ready notify = ident 627 (0x273).
        // Native sub_6EC078 @0x6EC2E4: `dec byte [ebx+0x9A]` (m_btAttackSkillCount),
        // `mov al,[ebx+0x9B] / cmp al,[ebx+0x9A] / jne` (m_btAttackSkillPointCount ==
        // m_btAttackSkillCount), then `mov byte [ebx+0x93],1` (m_boPowerHit) and
        // `push 0 x4; xor ecx,ecx; mov dx,0x273; call [ebx+0x250]` — Recog and all
        // four words are 0. Production srv_AppearTimes: 627 = 2,688,495.
        public const int SM_POWERHITSKILL = 627;

        // 烈火剑法 (SKILL_FIRESWORD) on/off notify = ident 626 (0x272).
        // ON  0x6BC860 (magic-dispatcher arm for id 26, after AllowFireHitSkill and
        //     the MP check): `push 0 x4; xor ecx,ecx; mov dx,0x272` — Recog = 0.
        // OFF 0x6B2F47 (Run, 20 s after m_dwLatestFireHitTick, clears [obj+0x96]):
        //     `push 0 x4; mov ecx,1; mov dx,0x272` — Recog = 1.
        // Same Recog convention as SM_SWORDHIT_ON (0xB03) at 0x6BC8E5 / 0x6B2F8D.
        // Production srv_AppearTimes: 626 = 56,720.
        public const int SM_FIREHITSKILL = 626;

        // 半月弯刀 (SKILL_BANWOL) toggle notify = ident 625 (0x271).
        // Native sub_6BE018, the only producer, reached only from the magic-dispatcher
        // arm 0x6BC809 (`cmp dword [esi+0xAC],0 / je / call 0x6BE018`):
        //   006BE01E  80B29500000001  xor byte [edx+0x95],1   ; m_boUseHalfMoon
        //   006BE025  80BA9500000000  cmp byte [edx+0x95],0
        //   006BE02C  741B            je 0x6BE049
        //   006BE036  33C9 / 66BA7102 xor ecx,ecx ; mov dx,0x271   ; now ON  -> Recog 0
        //   006BE049  B901000000 / 66BA7102                        ; now OFF -> Recog 1
        // Same shape as SM_THRUSTING (0x270) in sub_6BDFC8 on [obj+0x94].
        // Production srv_AppearTimes: 625 = 405,926.
        public const int SM_HALFMOON = 625;

        // NOT a 战神 ident: `mov dx,781` occurs ZERO times in the native image and no
        // native call site ever loads 781 into the SendDefMessage ident register.
        // The real native thrusting toggle is SM_THRUSTING=624 (@0x6B225B) above.
        // Kept only so the historical name resolves; must never be sent on the wire.
        public const int SM_LNGHITONOFF = 781;        // UNVERIFIED / native-absent
        public const int SM_WIDEHITONOFF = 782;       // Wide hit mode toggle
        public const int SM_HundredHit = 783;         // Hundred hit animation
        public const int SM_SQUARE_HIT = 784;         // Square area hit
        public const int SM_HORIZONHIT = 785;         // Horizontal area hit
        public const int SM_WWJATTACK = 60;
        public const int SM_WSJATTACK = 61;
        public const int SM_WTJATTACK = 62;
        public const int SM_DRINKEXP_STATUS = 2818;   // Drink EXP buff status
        public const int SM_DRINK_STATUS = 2816;      // Drink status buff
        public const int SM_DRINK_DRUG_STATUS = 2817; // Drink drug status

        // Native social relation protocol. Responses deliberately reuse request ids.
        public const int CM_QUERY_RELATION_FRIEND = 4430;
        public const int SM_SEND_RELATION_FRIEND = 4430;
        public const int CM_QUERY_RELATION_ATTENTION = 4431;
        public const int SM_SEND_RELATION_ATTENTION = 4431;
        public const int CM_QUERY_RELATION_NORMBLACKLIST = 4432;
        public const int SM_SEND_RELATION_NORMBLACKLIST = 4432;
        public const int CM_ADD_RELATION_FRIEND = 4433;
        public const int SM_ADD_RELATION_FRIEND_OK = 4433;
        public const int SM_ADD_RELATION_FRIEND_FAIL = 4434;
        public const int CM_ADD_RELATION_ATTENTION = 4435;
        public const int SM_ADD_RELATION_ATTENTION = 4435;
        public const int CM_ADD_RELATION_NORMBLACKLIST = 4436;
        public const int SM_ADD_RELATION_NORMBLACKLIST = 4436;
        public const int CM_DEL_RELATION_FRIEND = 4437;
        public const int SM_DEL_RELATION_FRIEND = 4437;
        public const int CM_DEL_RELATION_ATTENTION = 4438;
        public const int SM_DEL_RELATION_ATTENTION = 4438;
        public const int CM_DEL_RELATION_NORMBLACKLIST = 4439;
        public const int SM_DEL_RELATION_NORMBLACKLIST = 4439;
        public const int CM_UPDATE_ATTENTION_COLOR = 4440;
        public const int SM_UPDATE_ATTENTION_COLOR = 4440;

        // 4441/4442/4443 — single-record panel pushes of the native relation
        // classes TFriendRelation / TAttentionRelation / TNormalBlackRelation
        // (VMT class names read off the image at 0x6FC92C `0F 'TFriendRelation'`,
        // 0x6FC9A8 `12 'TAttentionRelation'`, 0x6FCA28 `14 'TNormalBlackRelation'`).
        // Each class exposes a virtual entry method (VMT slots 0x6FC928->0x6FF3FC,
        // 0x6FC9A4->0x6FFD94, 0x6FCA24->0x700888) that emits ONE record on the
        // +0x254 (Buf/Len) slot with Recog=0, Param=al(operation selector 0/1/2),
        // Tag=Series=0. The records are byte-identical to the LIST replies
        // (4430/4431/4432) already encoded by NativeRelationWireCodec.Encode:
        //   friend    Len=0x24 {name[16],level u16@0x10,job@0x12,guild[15]@0x13,online@0x23}
        //             0x6FF4D1 `66 BA 59 11 mov dx,0x1159` -> call [ebx+0x254]
        //   attention Len=0x16 {name[16],level u16@0x10,job@0x12,color@0x13,online@0x14}
        //             0x6FFE28 `66 BA 5A 11 mov dx,0x115A`
        //   blacklist Len=0x14 {name[16],level u16@0x10,online@0x12}
        //             0x700910 `66 BA 5B 11 mov dx,0x115B`
        // srv_AppearTimes 4441=66, 4442=1, 4443=0.
        // DEFERRED (BLOCKED-D5): the TFriendRelation family is a VMT-dispatched
        // reverse-notify object (its watcher TList is at [obj+0x10]); the trigger
        // is that object graph, not a CM arm. C# NativeRelationService is a
        // query-on-demand MySQL store with no reverse watcher index / push. The
        // record codec is present; the object model and trigger are NOT. Do not
        // emit until that subsystem is ported (see docs/m_sm_d_r2_20260813.md).
        public const int SM_RELATION_FRIEND_ENTRY = 4441;
        public const int SM_RELATION_ATTENTION_ENTRY = 4442;
        public const int SM_RELATION_BLACKLIST_ENTRY = 4443;

        // 4444/4445 — TFriendRelation logon/logoff broadcast. For the subject
        // player (edx), the object walks its watcher TList [this+0x10], resolves
        // each still-online watcher via UserEngine (0x656C14) and pushes the
        // subject's name ([player+0x106]) with Param=this[+4]. Direction inferred
        // from the wrappers: 4444 registers the subject key first (0x6FE704 stores
        // [+0x588]/[+0x58C] then two VMT calls) => LOGON; 4445 broadcasts then
        // tears down (0x6FE6F8 -> 0x6FEEB8) => LOGOFF.
        //   4444 0x6FE921 `66 BA 5C 11 mov dx,0x115C` -> call [ebx+0x250]
        //   4445 0x6FE85D `66 BA 5D 11 mov dx,0x115D` -> call [ebx+0x250]
        // srv_AppearTimes 4444=928, 4445=900. DEFERRED (BLOCKED-D5): same missing
        // reverse watcher index; needs login/logout hooks over that index.
        public const int SM_RELATION_FRIEND_LOGON = 4444;
        public const int SM_RELATION_FRIEND_LOGOFF = 4445;

        // 4446 — 元宝寄售 (YB consignment) deal-set status notify, NOT a relation
        // packet (the prior audit mis-grouped it). Sender 0x6F75C4 reads the
        // player's TYBDealSetInfo sub-object at [player+0x192C] (constructed at
        // 0x6AD9B3 via 0x712820, class VMT [0x7121C8] name 'TYBDealSetInfo'):
        //   Recog = TYBDealSetInfo[+0xC] ? word[[+0xC]+0x26] : 0   (sub_712BE4)
        //   0x6F75E7 `66 BA 5E 11 mov dx,0x115E` -> call [ebx+0x250], no body.
        // Dispatched from the relation/gild CM cluster arm 0x6DBB3A (same
        // dispatcher as CM 4629). srv_AppearTimes 4446=160.
        // DEFERRED (BLOCKED-D6): [+0x192C] TYBDealSetInfo is not modelled in C#;
        // NativeYbDealPurchaseStateMachine is host-driven and dormant.
        public const int SM_YBDEAL_SET_NOTIFY = 4446;

        // Native mail protocol backed by gamedata.mailitem/attachitem/money_order.
        public const int CM_FETCH_MAIL_LIST = 4460;
        public const int SM_FETCH_MAIL_LIST = 4460;
        public const int CM_FETCH_MAIL_INFO = 4461;
        public const int SM_FETCH_MAIL_INFO = 4461;
        public const int CM_FETCH_ATTACH = 4462;
        public const int SM_FETCH_ATTACH = 4462;
        public const int CM_DEL_MAIL = 4463;
        public const int SM_DEL_MAIL = 4463;
        public const int SM_MAIL_INFO = 4464;
        public const int CM_SYSTEM_NEWMAIL = 4464;
        public const int CM_FETCH_ATTACH_OFFTM = 4468;
        public const int SM_FETCH_ATTACH_OFFTM = 4468;
        // Native slave-list name notify. Recog=Param=Tag=Series=0, sMsg=[obj+0x106].
        // JOIN  4469: 0x6F7883 66 BA 75 11 then [obj+0x250].
        //   MakeSlave sub_6CB070 @0x6CB357, MakeSlaveEx sub_6BFC20 @0x6BFD02,
        //   MagTamming sub_6ED2A4 @0x6ED528. srv_AppearTimes 261804.
        // LEAVE 4470: 0x6F78EB 66 BA 76 11, same frame. srv_AppearTimes 123532.
        public const int SM_SLAVE_JOIN = 4469;
        public const int SM_SLAVE_LEAVE = 4470;
        public const int CM_CLEAR_ALLMAIL = 4495;
        public const int SM_CLEAR_ALLMAIL = 4495;

        // Native stall protocol and gamedata tables.
        public const int CM_QUERY_STALL = 4418;
        public const int SM_QUERY_STALL = 4418;
        public const int CM_SET_STALL_TIMELV = 4419;
        public const int SM_SET_STALL_TIMELV = 4419;
        public const int CM_SET_STALL_NAME = 4420;
        public const int SM_SET_STALL_NAME = 4420;
        public const int CM_ADD_STALLITEM = 4421;
        public const int SM_ADD_STALLITEM = 4421;
        public const int CM_DEL_STALLITEM = 4422;
        public const int SM_DEL_STALLITEM = 4422;
        public const int CM_CANCEL_STALL = 4423;
        public const int SM_CANCEL_STALL = 4423;
        public const int CM_START_STALL = 4424;
        public const int SM_START_STALL = 4424;
        public const int CM_PAUSE_STALL = 4425;
        public const int SM_PAUSE_STALL = 4425;
        public const int CM_BUY_STALLITEM = 4426;
        public const int SM_BUY_STALLITEM = 4426;
        public const int SM_UPT_DEL_STALLITEM = 4427;
        public const int SM_UPT_ADD_STALLITEM = 4428;
        public const int SM_UPT_OTHER_DEL_STALLITEM = 4429;
        public const int CM_MESSAGE_STALL = 4467;
        public const int SM_MESSAGE_STALL = 4467;
        public const int CM_QUERY_STALL_STATUS = 4481;
        public const int SM_QUERY_STALL_STATUS = 4481;

        // Native voice channel protocol.
        public const int CM_CHANNEL_CREATE = 4447;
        public const int SM_CHANNEL_CREATE = 4447;
        public const int CM_CHANNEL_ENTER = 4448;
        public const int SM_CHANNEL_ENTER = 4448;
        public const int CM_CHANNEL_EXIT = 4449;
        public const int SM_CHANNEL_EXIT = 4449;
        public const int CM_CHANNEL_CHANGE_MODE = 4450;
        public const int SM_CHANNEL_CHANGE_MODE = 4450;
        public const int CM_CHANNEL_CHANGE_MUTE = 4451;
        public const int SM_CHANNEL_CHANGE_MUTE = 4451;
        public const int CM_CHANNEL_KICK_OUT = 4452;
        public const int SM_CHANNEL_KICK_OUT = 4452;
        public const int CM_QUERY_CHANNEL_LIST = 4453;
        public const int SM_SEND_CHANNEL_LIST = 4453;
        public const int CM_QUERY_CHANNEL_MEMBERS = 4454;
        public const int SM_SEND_CHANNEL_MEMBERS = 4454;

        // Native Corps/Gild protocol used by the white-pig G2.5 client.
        public const int CM_PLAYER_GILD = 4500;
        public const int SM_PLAYER_GILD = 4500;
        public const int CM_PLAYER_CORPS = 4501;
        public const int SM_PLAYER_CORPS = 4501;
        public const int CM_CORPS_LIST = 4520;
        public const int SM_CORPS_LIST = 4520;
        public const int CM_CORPS_QUERY_JOIN = 4521;
        public const int SM_CORPS_QUERY_JOIN = 4521;
        public const int CM_CORPS_REQUEST_JOIN = 4522;
        public const int SM_CORPS_REQUEST_JOIN = 4522;
        public const int CM_CORPS_CANCEL_JOIN = 4523;
        public const int SM_CORPS_CANCEL_JOIN = 4523;
        public const int CM_CORPS_CREATE = 4524;
        public const int SM_CORPS_CREATE = 4524;
        public const int CM_CORPS_MEMBER_LIST = 4525;
        public const int SM_CORPS_MEMBER_LIST = 4525;
        public const int CM_CORPS_SET_MEMBER_TITLE = 4526;
        public const int SM_CORPS_SET_MEMBER_TITLE = 4526;
        public const int CM_CORPS_DISMISS_MEMBER = 4527;
        public const int SM_CORPS_DISMISS_MEMBER = 4527;
        public const int CM_CORPS_TRANSFER_CAPTAIN = 4528;
        public const int SM_CORPS_TRANSFER_CAPTAIN = 4528;
        public const int CM_CORPS_APPOINT_VICE_CAPTAIN = 4529;
        public const int SM_CORPS_APPOINT_VICE_CAPTAIN = 4529;
        public const int CM_CORPS_STEPDOWN = 4530;
        public const int SM_CORPS_STEPDOWN = 4530;
        public const int CM_CORPS_GET_RECRUIT_CONDITION = 4531;
        public const int SM_CORPS_GET_RECRUIT_CONDITION = 4531;
        public const int CM_CORPS_SET_RECRUIT_CONDITION = 4532;
        public const int SM_CORPS_SET_RECRUIT_CONDITION = 4532;
        public const int CM_CORPS_DIRECT_ADD_MEMBER = 4533;
        public const int SM_CORPS_DIRECT_ADD_MEMBER = 4533;
        public const int CM_CORPS_QUERY_REQUESTS = 4534;
        public const int SM_CORPS_QUERY_REQUESTS = 4534;
        public const int CM_CORPS_ACCEPT_REQUEST = 4535;
        public const int SM_CORPS_ACCEPT_REQUEST = 4535;
        public const int CM_CORPS_REFUSE_REQUEST = 4536;
        public const int SM_CORPS_REFUSE_REQUEST = 4536;
        public const int CM_CORPS_QUERY_LOG = 4537;
        public const int SM_CORPS_QUERY_LOG = 4537;
        public const int CM_CORPS_EXIT = 4538;
        public const int SM_CORPS_EXIT = 4538;
        public const int CM_CORPS_NOTICE = 4539;
        public const int SM_CORPS_NOTICE = 4539;
        public const int CM_CORPS_DISMISS_VICE_CAPTAIN = 4540;
        public const int SM_CORPS_DISMISS_VICE_CAPTAIN = 4540;

        public const int CM_GILD_REQUEST_JOIN = 4560;
        public const int SM_GILD_REQUEST_JOIN = 4560;
        public const int CM_GILD_LIST = 4562;
        public const int SM_GILD_LIST = 4562;
        public const int CM_GILD_NOTICE = 4563;
        public const int SM_GILD_NOTICE = 4563;
        public const int CM_GILD_CREATE = 4564;
        public const int SM_GILD_CREATE = 4564;
        public const int CM_GILD_QUERY_CORPS = 4565;
        public const int SM_GILD_QUERY_CORPS = 4565;
        public const int CM_GILD_QUERY_PRESIDENT = 4566;
        public const int SM_GILD_QUERY_PRESIDENT = 4566;
        public const int CM_GILD_DISMISS_CORPS = 4567;
        public const int SM_GILD_DISMISS_CORPS = 4567;
        public const int CM_GILD_TRANSFER_PRESIDENT = 4568;
        public const int SM_GILD_TRANSFER_PRESIDENT = 4568;
        public const int CM_GILD_APPOINT_VICE_PRESIDENT = 4569;
        public const int SM_GILD_APPOINT_VICE_PRESIDENT = 4569;
        public const int CM_GILD_QUERY_REQUEST_JOIN_LIST = 4570;
        public const int SM_GILD_QUERY_REQUEST_JOIN_LIST = 4570;
        public const int CM_GILD_QUERY_REQUEST_UNION_LIST = 4571;
        public const int SM_GILD_QUERY_REQUEST_UNION_LIST = 4571;
        public const int CM_GILD_REFUSE_REQUEST = 4572;
        public const int SM_GILD_REFUSE_REQUEST = 4572;
        public const int CM_GILD_REQUEST_UNION = 4573;
        public const int SM_GILD_REQUEST_UNION = 4573;
        public const int CM_GILD_BREAK_UNION = 4574;
        public const int SM_GILD_BREAK_UNION = 4574;
        public const int CM_GILD_QUERY_UNION = 4575;
        public const int SM_GILD_QUERY_UNION = 4575;
        public const int CM_GILD_CONCERN_GILD_ID = 4576;
        public const int SM_GILD_CONCERN_GILD_ID = 4576;
        public const int CM_GILD_QUERY_CONCERN = 4577;
        public const int SM_GILD_QUERY_CONCERN = 4577;
        public const int CM_GILD_CANCLE_CONCERN = 4578;
        public const int SM_GILD_CANCLE_CONCERN = 4578;
        public const int CM_GILD_DECLARE_WAR = 4579;
        public const int SM_GILD_DECLARE_WAR = 4579;
        public const int CM_GILD_QUERY_HOSTILE = 4580;
        public const int SM_GILD_QUERY_HOSTILE = 4580;
        public const int CM_GILD_ENABLE_UNION = 4581;
        public const int SM_GILD_ENABLE_UNION = 4581;
        public const int CM_GILD_QUERY_LOG = 4582;
        public const int SM_GILD_QUERY_LOG = 4582;
        public const int CM_GILD_EXIT = 4583;
        public const int SM_GILD_EXIT = 4583;
        public const int CM_GILDMEMBER_LIST = 4584;
        public const int SM_GILDMEMBER_LIST = 4584;
        public const int CM_GILD_DECLARE_WAR_NAME = 4585;
        public const int SM_GILD_DECLARE_WAR_NAME = 4585;
        public const int CM_GILD_CONCERN_GILD_NAME = 4586;
        public const int SM_GILD_CONCERN_GILD_NAME = 4586;
        public const int CM_GILD_VICECAPTAIN_STEPDOWN = 4587;
        public const int SM_GILD_VICECAPTAIN_STEPDOWN = 4587;
        public const int CM_GILD_DISMISS_VICECAPTAIN = 4588;
        public const int SM_GILD_DISMISS_VICECAPTAIN = 4588;

        public const int CM_GILD_ACCEPT_REQUEST = 4611;
        public const int SM_GILD_ACCEPT_REQUEST = 4611;
        // Login dump of offline social-request notices. UserLogon @0x6B24EE
        // always calls sub_6F772C; even the empty-list arm (je 0x6F77EB) still
        // sends via [obj+0x254]: 0x6F7813 66 BA 04 12, Recog=Param=Tag=Series=0,
        // Len=count*17. srv_AppearTimes 50911.
        public const int SM_PENDING_NOTICE = 4612;
        public const int SM_PENDING_REQUEST = 4613;
        public const int SM_CLEAR_PENDING_REQUEST = 4615;
        public const int CM_FIND_CORPS_BYNAME = 4616;
        public const int SM_FIND_CORPS_BYNAME = 4616;
        public const int CM_FIND_GILD_BYNAME = 4617;
        public const int SM_FIND_GILD_BYNAME = 4617;
        public const int CM_GILD_CANCEL_JOIN = 4627;
        public const int SM_GILD_CANCEL_JOIN = 4627;
        public const int SM_REFRESH_SOCIAL_ROLE = 4628;
        public const int CM_REFRESH_CORPSINFO = 4631;
        public const int SM_REFRESH_CORPSINFO = 4631;
        public const int CM_REFRESH_GILDINFO = 4632;
        public const int SM_REFRESH_GILDINFO = 4632;
        public const int CM_CLICK_BACKHOME = 4633;
        public const int CM_V_POWERSTONE = 4644;
        public const int SM_V_POWERSTONE = 4644;

        // === Group Extended ===
        public const int CM_REPLY_GROUP_MESSAGE = 4412;
        public const int SM_NOTIFY_GROUP_MESSAGE = 4412;
        public const int CM_JOINGROUP = 4413;
        public const int CM_QUERY_NEARBYPLAYER = 4414;
        public const int CM_QUERY_NEARBYGROUP = 4415;
        public const int CM_QUERY_GROUP_MEMBERS = 4416;
        // 战神 CM 1089 (0x441) 组长广播：dispatch @0x6D970A。仅当 self 为组长时，
        // sub_727628 @0x727655 向本队每个存活成员单播 SM 965 (0x3C5)，
        // nRecog = 包里的 Recog ([msg+0])，其余 Param/Tag/Series=0、sMsg=""。
        // 965 在原始 SM 表落于 960-963/971 空隙、无既有语义名，按编号命名不臆造。
        public const int CM_1089 = 1089;
        public const int SM_965 = 965;

        // === Strengthen/Fusion System ===
        public const int CM_STRENGTHEN_EQUIP_QUEST = 4465;
        public const int CM_STRENGTHEN_EQUIP = 4466;
        public const int CM_UPDATE_CLOTHES = 4637;
        public const int SM_STRENGTHEN_EQUIP_QUEST = 4465;
        public const int SM_STRENGTHEN_EQUIP = 4466;
        public const int SM_UPDATE_CLOTHES = 4637;

        // === Shop / Misc ===
        public const int CM_REQSEESHOP = 1046;
        public const int CM_RENEWSEESHOP = 1047;
        public const int CM_DOSHOP = 1048;
        public const int CM_PILEUPITEM = 1115;
        public const int CM_SPLITITEM = 1116;
        public const int CM_QUERY_FOCUS_ITEM = 1271;
        public const int SM_ITEM_PILEUP_RESULT = 3322;
        // Native CM 3290 (handler 0x6DA34E) replies on SM 3289 via vtbl+0x254.
        // C# previously used 3290 as an SM ident (SM_QUERY_FOCUS_ITEM); that is
        // the opposite direction and is not this CM.
        public const int CM_3290 = 3290;
        public const int SM_3289 = 3289;
        public const int SM_QUERY_FOCUS_ITEM = 3290;

        // === Title / NPC / Item Commit ===
        public const int CM_QUERY_TITLE = 3202;
        public const int CM_QUERY_MAP_NPC = 4610;
        public const int SM_QUERY_MAP_NPC = 4610;
        // Native CM 4629 handler 0x6DBB70 -> 0x6F7C40. Same-ident reply via
        // vtbl+0x254 (0x6F7E81 66 BA 15 12 mov dx,0x1215).
        public const int CM_4629 = 4629;
        public const int SM_4629 = 4629;
        public const int CM_COMMIT_ITEM = 4634;
        public const int SM_COMMIT_ITEM = 4634;
        public const int SM_OPEN_COMMIT_ITEM = 4635;
        public const int SM_PLAYER_AUTHEN = 4636;

        // === 元宝寄售 (YB consignment) list queries ===
        // Jump table 0x6D8315 entries 52/53/56/57 (base ident 1200, 0x6D8300 `add eax,-1200`).
        // Each arm is `mov eax,[ebp-4] / call <thunk>`; the thunk takes self+0x106 (the char
        // name) and calls one method on the manager at [[0x7D6ABC]]. No packet field is read.
        //   1252 0x6DA685 -> 0x6E7E3C -> 0x632A14   1253 0x6DA692 -> 0x6E7E90 -> 0x632E7C
        //   1256 0x6DA6D5 -> 0x6E83AC -> 0x632BEC   1257 0x6DA6E2 -> 0x6E8400 -> 0x632D34
        public const int CM_YB_CONSIGN_INBOX = 1252;
        public const int CM_YB_CONSIGN_OUTBOX = 1253;
        public const int CM_YB_DEAL_BUY_HISTORY = 1256;
        public const int CM_YB_DEAL_SELL_HISTORY = 1257;
        // Reply idents. The manager passes sub_6E80CC a selector in ECX (0x47A/0x47B/0x480/0x481)
        // and 0x6E80DE-0x6E8129 translates it into these before the vtbl+0x254 send.
        public const int SM_YB_CONSIGN_INBOX = 3001;
        public const int SM_YB_CONSIGN_OUTBOX = 3002;
        public const int SM_YB_DEAL_BUY_HISTORY = 3005;
        public const int SM_YB_DEAL_SELL_HISTORY = 3006;

        public const int SM_NEEDPASSWORD = 8003;
        public const int SM_GETREGINFO = 8004;
        public const int DATA_BUFSIZE = 1024;
        public const int MAXMAGIC = 55;
        public const string sSTRING_GOLDNAME = "金币";
        public const short MAXLEVEL = short.MaxValue;
        public const int MAXCHANGELEVEL = 1000;
        public const int SLAVEMAXLEVEL = 50;
        public const int LOG_GAMEGOLD = 1;
        public const int LOG_GAMEPOINT = 2;
        
        
        
        public const int RC_PLAYOBJECT = 0;
        
        
        
        public const int RC_GUARD = 11;
        
        
        
        public const int RC_PEACENPC = 15;
        
        
        
        public const int RC_ANIMAL = 50;

        public const int RC_HEROOBJECT = 54;
        
        
        
        public const int RC_EXERCISE = 55;
        
        
        
        public const int RC_PLAYCLONE = 60;
        
        
        
        public const int RC_MONSTER = 80;
        
        
        
        public const int RC_NPC = 10;
        
        
        
        public const int RC_ARCHERGUARD = 112;
        
        
        
        public const int RC_135 = 135;
        
        
        
        public const int RC_136 = 136;
        
        
        
        public const int RC_153 = 153;
        public const int RM_TURN = 10001;
        public const int RM_WALK = 10002;
        public const int RM_HORSERUN = 50003;
        public const int RM_RUN = 10003;
        public const int RM_HIT = 10004;
        public const int RM_BIGHIT = 10006;
        public const int RM_HEAVYHIT = 10005;
        public const int RM_SPELL = 10007;
        public const int RM_SPELL2 = 10009;
        public const int RM_MOVEFAIL = 10010;
        public const int RM_LONGHIT = 10011;
        public const int RM_WIDEHIT = 10012;
        public const int RM_FIREHIT = 10014;
        public const int RM_CRSHIT = 10037;
        public const int RM_DEATH = 10021;
        public const int RM_SWORD_HIT = 10023;
        public const int RM_SKELETON = 10024;
        public const int RM_LOGON = 10050;
        public const int RM_ABILITY = 10051;
        public const int RM_HEALTHSPELLCHANGED = 10052;
        public const int RM_DAYCHANGING = 10053;
        // Internal queue tag, never a wire ident. The only immediate load of 10054
        // (0x2746) in CODE is 0x6B99F3 `66 B9 46 27 mov cx,0x2746` feeding
        // `0x6B99F9 call 0x765E68` -- the record-allocating ENQUEUE helper, which
        // contains no [+0x250]/[+0x254] send-slot call. The wire packet is emitted by
        // the RM handler: dispatcher 0x6B3F08 `jmp [eax*4+0x6B3F0F]` routes tag 10054
        // to arm 0x6B4DED, which builds a 24-byte body and sends
        // 0x6B4E3A `66 BA B2 04 mov dx,0x4B2` = 1202 = SM_GETDIAMNUM_EXT.
        public const int RM_LINGFU_CHANGED = 10054;
        public const int RM_USERMOVE = 10056;
        public const int RM_NATIVE_CLEAROBJECTS = 10117;
        public const int RM_NATIVE_CHANGEMAP = 10118;
        public const int RM_10101 = 10101;
        public const int RM_WEIGHTCHANGED = 10115;
        public const int RM_FEATURECHANGED = 10116;
        public const int RM_BUTCH = 10119;
        public const int RM_MAGICFIRE = 10120;
        public const int RM_MAGICFIREFAIL = 10121;
        public const int RM_SENDMYMAGIC = 10122;
        public const int RM_USERAZSETUP = 25008; // 安卓手机客户端UI设置
        public const int RM_MAGIC_LVEXP = 10123;
        public const int RM_DURACHANGE = 10125;
        public const int RM_MASTERRELATION = 10126;
        public const int RM_MERCHANTDLGCLOSE = 10127;
        public const int RM_SENDGOODSLIST = 10128;
        public const int RM_SENDUSERSELL = 10129;
        public const int RM_SENDBUYPRICE = 10130;
        public const int RM_USERSELLITEM_OK = 10131;
        public const int RM_USERSELLITEM_FAIL = 10132;
        public const int RM_BUYITEM_SUCCESS = 10133;
        public const int RM_BUYITEM_FAIL = 10134;
        public const int RM_SENDDETAILGOODSLIST = 10135;
        public const int RM_GOLDCHANGED = 10136;
        public const int RM_CHANGELIGHT = 10137;
        public const int RM_LAMPCHANGEDURA = 10138;
        public const int RM_CHARSTATUSCHANGED = 10139;
        public const int RM_GROUPCANCEL = 10140;
        public const int RM_SENDUSERREPAIR = 10141;
        public const int RM_SENDUSERSREPAIR = 10152;
        public const int RM_SENDREPAIRCOST = 10142;
        public const int RM_USERREPAIRITEM_OK = 10143;
        public const int RM_USERREPAIRITEM_FAIL = 10144;
        public const int RM_USERSTORAGEITEM = 10146;
        public const int RM_USERSAVEITEM = 10160;
        public const int RM_LEFTTIME = RM_USERSAVEITEM;
        public const int RM_USERGETBACKITEM = 10147;
        public const int RM_SENDDELITEMLIST = 10148;
        public const int RM_USERMAKEDRUGITEMLIST = 10149;
        public const int RM_MAKEDRUG_SUCCESS = 10150;
        public const int RM_MAKEDRUG_FAIL = 10151;
        public const int RM_ALIVE = 10153;
        public const int RM_10155 = 10155;
        public const int RM_GLORYFEALTY = 10158;
        public const int RM_NATIVE_MAGIC_EFFECT = 10177;
        public const int RM_DIGUP = 10200;
        public const int RM_DIGDOWN = 10201;
        public const int RM_FLYAXE = 10202;
        public const int RM_LIGHTING = 10204;
        public const int RM_10205 = 10205;
        public const int RM_CHANGEGUILDNAME = 10301;
        public const int RM_SUBABILITY = 10302;
        public const int RM_BUILDGUILD_OK = 10303;
        public const int RM_BUILDGUILD_FAIL = 10304;
        public const int RM_DONATE_OK = 10305;
        public const int RM_MENU_OK = 10309;
        public const int RM_RECONNECTION = 10332;
        public const int RM_HIDEEVENT = 10333;
        public const int RM_SHOWEVENT = 10334;
        public const int RM_10401 = 10401;
        public const int RM_OPENHEALTH = 10410;
        public const int RM_CLOSEHEALTH = 10411;
        
        
        
        public const int RM_BREAKWEAPON = 10413;
        public const int RM_10414 = 10414;
        public const int RM_CHANGEFACE = 10415;
        public const int RM_PASSWORD = 10416;
        public const int RM_PLAYDICE = 10500;
        public const int RM_HERO_UNIONSTATUS = 10611;
        public const int RM_PHYSICAL_ATT = 10612;
        public const int RM_NATIVE_UNION_EFFECT = 10612;
        public const int RM_NATIVE_EXP_CONTINUE = 10625;
        public const int RM_NATIVE_REVIVE_MESSAGE = 12308;
        public const int RM_NATIVE_MOOTEBO_CONTINUE = 12309;

        /// <summary>
        /// 登录状态同步 (native RM 0x3010 = 12304). UserLogon (sub_6B1D64) queues it at
        /// 0x6B2358; THeroAct.Logon (sub_687D70) queues the same RM at 0x688180.
        /// Both use sub_765E68 with edx=eax=Self and six zero params, so it is
        /// processed once on the next player/hero message-loop tick after login.
        /// The Operate loop routes it through the secondary dispatcher sub_743AD8
        /// (`0x6B6247 call 0x743AD8`); case 0x3010 (`0x743B24 sub eax,0x75F / je 0x743BF3`)
        /// runs `0x743BF7 call [edx+0x204]`. Player VMT+0x204 is sub_6E9A98;
        /// the four hero VMTs use the distinct sub_69057C cluster.
        /// Player sub_6E9A98 sends 3324, 1264, 3554, then optional 3556.
        /// Hero sub_69057C sends 3324, then optional 4367.
        /// Kept numerically equal to native because it neighbours RM_NATIVE_MOOTEBO_CONTINUE
        /// (12309); RM_* stays process-local and never reaches the wire (REPLICATION_RULES §1.4).
        /// </summary>
        public const int RM_NATIVE_LOGON_STATE_SYNC = 12304;

        public const int RM_HEAR = 11001;

        /// <summary>
        /// 彩色文字 speech. Native forwards it as RM ident 10033 (0x2731,
        /// 0x6C9471) whose handler 0x6B4B3C emits client ident 105; this port
        /// keeps the RM numbering local to the 110xx say block it belongs to,
        /// because what has to match on the wire is <see cref="SM_COLORHEAR"/>,
        /// not the internal forward code. Unlike RM_HEAR this ident is NOT
        /// filtered by the block-public-chat bit -- see TPlayObject.NativeColorSay.
        /// </summary>
        public const int RM_COLORHEAR = 11010;

        public const int RM_WHISPER = 11002;
        public const int RM_CRY = 11003;
        public const int RM_SYSMESSAGE = 11004;
        public const int RM_CATTLE_SYSMESSAGE = 10105;
        public const int RM_GROUPMESSAGE = 11005;
        public const int RM_SYSMESSAGE2 = 11006;
        public const int RM_GUILDMESSAGE = 11007;
        public const int RM_SYSMESSAGE3 = 11008;
        public const int RM_MERCHANTSAY = 11009;
        public const int RM_ZEN_BEE = 8020;
        public const int RM_DELAYMAGIC = 8021;
        public const int RM_STRUCK = 10020;
        public const int RM_MAGSTRUCK_MINE = 8030;
        public const int RM_MAGHEALING = 8034;
        public const int RM_POISON = 8037;
        public const int RM_DOOPENHEALTH = 8040;
        public const int RM_SPACEMOVE_FIRE2 = 10335;
        public const int RM_DELAYPUSHED = 8043;
        public const int RM_MAGSTRUCK = 8044;
        public const int RM_TRANSPARENT = 8045;
        public const int RM_DOOROPEN = 8046;
        public const int RM_DOORCLOSE = 8047;
        public const int RM_DISAPPEAR = 10022;
        public const int RM_SPACEMOVE_FIRE = 10330;
        public const int RM_SENDUSEITEMS = 8074;
        public const int RM_WINEXP = 10044;
        public const int RM_ADJUST_BONUS = 8078;
        public const int RM_ITEMSHOW = 8082;
        public const int RM_GAMEGOLDCHANGED = 8084;
        public const int RM_ITEMHIDE = 8085;
        public const int RM_LEVELUP = 10045;
        public const int RM_CHANGENAMECOLOR = 10046;
        public const int RM_PUSH = 10013;
        public const int RM_CLEAROBJECTS = 8097;
        public const int RM_CHANGEMAP = 8098;
        public const int RM_SPACEMOVE_SHOW2 = 10336;
        public const int RM_SPACEMOVE_SHOW = 10331;
        public const int RM_USERNAME = 10043;
        public const int RM_MYSTATUS = 8102;
        public const int RM_STRUCK_MAG = 10027;
        /// <summary>
        /// The trap/fire-point damage carrier. Three TMapEvent subclasses emit it
        /// through the immediate send helper sub_765E68:
        /// TBTFireBurnEvent.ApplyTo @0x717BA8 `66 B9 2C 27 mov cx,0x272C`,
        /// TOnceDamageTrapEvent.ApplyTo @0x717F46 (same bytes),
        /// TFireDragonPoint.ApplyTo @0x718B9F (same bytes).
        /// <para>
        /// The player pump has NO arm for it: base cluster @0x6B3EF8
        /// `add eax,0xFFFFD8F0` then `jmp [0x6B3F0F + eax*4]`, and slot 28
        /// (10028) holds 0x6B6241 — the same address every out-of-range ident
        /// jumps to (`0x6B3F02 ja 0x6B6241`). 0x6B6241 forwards to sub_743AD8.
        /// So the armour roll runs and the number is handed to the fallback,
        /// not to an HP mutation. Replicated as-is: do not "fix" it into damage.
        /// </para>
        /// </summary>
        public const int RM_10028 = 10028;
        /// <summary>
        /// TOnceDamageTrapEvent.ApplyTo @0x717F60 `66 BA 05 29 mov dx,0x2905`,
        /// dispatched through the enqueue-broadcast slot VMT+0xD8 with
        /// nParam3 = 0x20 (@0x717F54 `6A 20`).
        /// </summary>
        public const int RM_10501 = 10501;
        public const int RM_RUSH = 10015;
        public const int RM_RUSHKUNG = 10016;
        public const int RM_PASSWORDSTATUS = 8106;
        public const int RM_POWERHIT = 10008;
        public const int RM_41 = 9041;
        public const int RM_TWINHIT = 10038;
        public const int RM_43 = 9043;
        public const int RM_MOVEMESSAGE = 10099;

        // === Combat / Visual RM_ constants (战神 client) ===
        public const int RM_SLAVE_BORN = 15000;      // Slave monster spawned -> SM_SLAVE_BORN
        public const int RM_SLAVE_VANISH = 15001;    // Slave monster despawned -> SM_SLAVE_VANISH
        public const int RM_FIREON = 15005;          // Fire hit mode ON -> SM_FIREON
        public const int RM_SWORDHIT_ON = 15006;     // Sword hit mode ON -> SM_SWORDHIT_ON
        public const int RM_LNGHITONOFF = 15007;     // Long hit mode toggle -> SM_LNGHITONOFF
        public const int RM_WIDEHITONOFF = 15008;    // Wide hit mode toggle -> SM_WIDEHITONOFF
        public const int RM_NATIVE_HORSE_CALL_STOP = 15013;
        public const int RM_SHANGMA_OK = 15014;
        public const int RM_RUN3 = 15015;
        public const int RM_NATIVE_XIAMA_OK = 15314;
        public const int RM_NATIVE_INVITE_HORSE = 15317;
        public const int RM_NATIVE_SHANGMA_OK2 = 15318;
        public const int RM_NATIVE_XIAMA_2 = 15319;
        /// <summary>
        /// In-process label for native ident 0x3043, the delayed self-message
        /// magic 68 posts at 0x6EC896 (`66 B9 43 30` / sub_766060) and picks up
        /// in TPlayObject.Operate at the 0x6B4391 table, slot 63 of base 0x3004
        /// (0x6B437C `add eax,0xFFFFCFFC`), arm 0x6B6097 -> sub_6EC8E8.
        /// Not a wire ident.
        /// </summary>
        public const int RM_NATIVE_CHARGE_LAND = 15320;
        /// <summary>Carrier for <see cref="SM_NATIVE_CHARGE_MOVE"/>.</summary>
        public const int RM_NATIVE_CHARGE_MOVE = 15321;
        /// <summary>Carrier for <see cref="SM_NATIVE_BLINK_MOVE"/>.</summary>
        public const int RM_NATIVE_BLINK_MOVE = 15322;
        /// <summary>
        /// In-process label for native ident 0x3042, broadcast by magic 261 at
        /// 0x773D0A through VMT+0xD8 = sub_6DC590 and picked up in
        /// TPlayObject.Operate at the 0x6B4391 table, slot 62 of base 0x3004,
        /// arm 0x6B6065. Not a wire ident; the arm answers with SM_DISAPPEAR.
        /// </summary>
        public const int RM_NATIVE_STEALTH_VANISH = 15323;
        public const int RM_WWJATTACK = 10017;
        public const int RM_WSJATTACK = 10018;
        public const int RM_WTJATTACK = 10019;

        // === ISM (Inter-Server Message) Constants ===
        // C# extensions (not in native 202..257 range):
        public const int ISM_GROUPSERVERHEART = 100;     // C# extension - server heartbeat
        public const int ISM_USERSERVERCHANGE = 200;     // C# extension - user server change
        public const int ISM_USERLOGON = 201;            // C# extension - user logon
        public const int ISM_CS_USERLOGOUT = 198;        // C# extension - cross-server user logout (was wrongly on native 202)
        public const int ISM_CS_RELOADGUILD = 199;       // C# extension - reload guild (was wrongly on native 207)
        public const int ISM_CS_SERVERSWITCH = 258;      // C# extension - switchWord broadcast (was wrongly on native 207)

        // Native ProcessOthGsMsg range (202..257) - 27 REAL / 29 SINK:
        public const int ISM_ANTICHEAT_PENALTY = 202;    // REAL - Anti-cheat penalty (EA 0x657208, calls sub_658384)
        public const int ISM_WHISPER = 203;              // REAL - Whisper message
        public const int ISM_SYSOPMSG = 204;             // SINK - Sysop message (C# extension handler)
        public const int ISM_ADDGUILD = 205;             // SINK - Add guild (C# extension handler)
        public const int ISM_DELGUILD = 206;             // SINK - Delete guild (C# extension handler)
        public const int ISM_SINGLEQUOTE_SCAN = 207;     // REAL - Single-quote scan (EA 0x657230, calls sub_658114)
        public const int ISM_GUILDMSG = 208;             // SINK - Guild message (C# extension handler)
        public const int ISM_CHATPROHIBITION = 209;      // REAL - Chat prohibition
        public const int ISM_CHATPROHIBITIONCANCEL = 210;// REAL - Chat prohibition cancel
        public const int ISM_CHANGECASTLEOWNER = 211;    // REAL - Change castle owner
        public const int ISM_RELOADCASTLEINFO = 212;     // REAL - Reload castle info
        public const int ISM_RELOADADMIN = 213;          // REAL - Reload admin
        public const int ISM_GLOBAL_MODE_SET = 214;      // REAL - sub_6579B0 写全局 [[0x7D6010]]=1/2/3
        public const int ISM_FRIEND_DELETE = 215;        // SINK - Friend delete (C# extension handler)
        public const int ISM_DIVORCE = 216;              // REAL - Divorce
        public const int ISM_MENTOR_STUDENT_1 = 217;     // REAL - Mentor/student handler 1 (EA 0x6572A4, calls sub_657CF0)
        public const int ISM_MENTOR_STUDENT_2 = 218;     // REAL - Mentor/student handler 2 (EA 0x6572B4, calls sub_657AC0)
        public const int ISM_TEXT_RELAY3 = 219;          // REAL - sub_6581A4 三段式文本转发
        public const int ISM_DEAD_LEG_220 = 220;         // REAL - sub_657E08，输出终点 sub_7138CC 是空桩
        public const int ISM_GM_NOTICE = 221;            // REAL - sub_6575D8 GMLevel([+0x675])>=3 通知
        public const int ISM_CHANGESERVERRECIEVEOK = 222;// REAL - Change server receive OK
        public const int ISM_RELOADCHATLOG = 223;        // SINK - Reload chat log (C# extension handler)
        public const int ISM_MENTOR_REPUTATION = 224;    // REAL - sub_6574B4 [师父+0x4F0] += n（声望）
        public const int ISM_MARKETCLOSE = 225;          // SINK - Market close (C# extension handler)
        public const int ISM_MENTOR_GRADUATE = 226;      // REAL - sub_657888 徒弟出师（sub_6C5EC8 的跨服镜像）
        public const int ISM_PLAYER_NOTICE = 227;        // REAL - sub_657670 给指定玩家发通知
        public const int ISM_MENTOR_RECHARGE_REWARD = 228; // REAL - sub_657BCC 师徒充值奖励
        public const int ISM_RELOADGUILDAGIT = 229;      // SINK - Reload guild agit (C# extension handler)
        public const int ISM_LM_WHISPER = 230;           // SINK - Lover manager whisper (C# extension handler)
        public const int ISM_GMWHISPER = 231;            // SINK - GM whisper (C# extension handler)
        public const int ISM_LM_LOGIN = 232;             // SINK - Lover manager login (C# extension handler)
        public const int ISM_LM_LOGOUT = 233;            // SINK - Lover manager logout (C# extension handler)
        public const int ISM_REQUEST_RECALL = 234;       // SINK - Request recall (C# extension handler)
        public const int ISM_RECALL = 235;               // SINK - Recall (C# extension handler)
        public const int ISM_LM_LOGIN_REPLY = 236;       // SINK - Lover manager login reply (C# extension handler)
        public const int ISM_LM_KILLED_MSG = 237;        // SINK - Lover manager killed message (C# extension handler)
        public const int ISM_REQUEST_LOVERRECALL = 238;  // SINK - Request lover recall (C# extension handler)
        public const int ISM_STANDARDTICKREQ = 239;      // SINK - Standard tick request (C# extension handler)
        public const int ISM_SECT_INVITE = 240;          // REAL - sub_657F3C 宗派邀请
        public const int ISM_CREDITCARD_CLEARALL = 241;  // REAL - Credit card clear all (EA 0x657354)
        public const int ISM_GRUOPMESSAGE = 242;         // SINK - Group message (C# extension handler)
        public const int ISM_CREDITCARD_CLEARMONTHLY = 243; // REAL - Credit card clear monthly
        // 244-246: SINK (no native handlers)
        public const int ISM_IDENT_247 = 247;            // REAL - Unknown handler (EA 0x657378, calls sub_65805C)
        // 248: SINK
        public const int ISM_SETNICKLF = 249;            // REAL - Set nick lin fu
        // 250: SINK
        public const int ISM_GLORYLOG_FLUSH = 251;       // REAL - Glory log flush
        // 252-256: SINK (no native handlers)
        public const int ISM_MAKE_CATTLE_CRAZY = 257;    // REAL - Make cattle crazy

        // Obsolete constants (SGRP fixes - semantic mismatches):
        [Obsolete("SGRP-25: Native 202 is ISM_ANTICHEAT_PENALTY, not user logout. Use ISM_ANTICHEAT_PENALTY.")]
        public const int ISM_USERLOGOUT = 202;
        [Obsolete("SGRP-44: Native 207 is ISM_SINGLEQUOTE_SCAN, not reload guild. Use ISM_SINGLEQUOTE_SCAN.")]
        public const int ISM_RELOADGUILD = 207;
        [Obsolete("SGRP-44: Native 207 is ISM_SINGLEQUOTE_SCAN, not server switch. Use ISM_SINGLEQUOTE_SCAN.")]
        public const int ISM_SERVERSWITCH = 207;
        [Obsolete("SGRP-31: Native 241 is ISM_CREDITCARD_CLEARALL, not guild war. Use ISM_CREDITCARD_CLEARALL.")]
        public const int ISM_GUILDWAR = 241;
        [Obsolete("SGRP-26: Native 217 is ISM_MENTOR_STUDENT_1, not friend close. Use ISM_MENTOR_STUDENT_1.")]
        public const int ISM_FRIEND_CLOSE = 217;
        [Obsolete("SGRP-26: Native 218 is ISM_MENTOR_STUDENT_2, not friend result. Use ISM_MENTOR_STUDENT_2.")]
        public const int ISM_FRIEND_RESULT = 218;

        // mirror2: ProcessOthGsMsg 全表 dump（索引表 0x657160 / 地址表 0x657198，
        // 基数 202、跨度 202..257、27 REAL / 29 SINK）后逐个 handler 反汇编所得。
        // 详见 docs/mirror_crossserver_20260814.md。
        [Obsolete("SGRP: Native 214 is ISM_GLOBAL_MODE_SET (sub_6579B0 writes [[0x7D6010]]), not friend info.")]
        public const int ISM_FRIEND_INFO = 214;
        [Obsolete("SGRP: Native 219 is ISM_TEXT_RELAY3 (sub_6581A4), not tag send.")]
        public const int ISM_TAG_SEND = 219;
        [Obsolete("SGRP: Native 220 is ISM_DEAD_LEG_220 (sub_657E08, output sink sub_7138CC is a stub), not tag result.")]
        public const int ISM_TAG_RESULT = 220;
        [Obsolete("SGRP: Native 221 is ISM_GM_NOTICE (sub_6575D8, gated on GMLevel>=3), not user info.")]
        public const int ISM_USER_INFO = 221;
        [Obsolete("SGRP: Native 224 is ISM_MENTOR_REPUTATION (sub_6574B4 adds to [master+0x4F0]), not market open.")]
        public const int ISM_MARKETOPEN = 224;
        [Obsolete("SGRP: Native 226 is ISM_MENTOR_GRADUATE (sub_657888), not lover-manager delete.")]
        public const int ISM_LM_DELETE = 226;
        [Obsolete("SGRP: Native 227 is ISM_PLAYER_NOTICE (sub_657670), not reload make-item list.")]
        public const int ISM_RELOADMAKEITEMLIST = 227;
        [Obsolete("SGRP: Native 228 is ISM_MENTOR_RECHARGE_REWARD (sub_657BCC), not guild member recall.")]
        public const int ISM_GUILDMEMBER_RECALL = 228;
        [Obsolete("SGRP: Native 240 is ISM_SECT_INVITE (sub_657F3C), not standard tick.")]
        public const int ISM_STANDARDTICK = 240;

        public const int LA_UNDEAD = 1;

        // === CM missing Q3 (dispatcher sub_6D7D68 @0x6D805C reads word[record+4]) ===
        // The ascending-sorted native-arm/no-C#-case gap is 99 idents, quartered
        // 25/25/24/25 by cm-2's authoritative tooling. This third quarter (items
        // 51..74, idents 3180..4124) is restored in TPlayObject.NativeCmProtocol_Q3.cs.
        // Every ident below resolves to a REAL tree leaf, never the shared exit
        // 0x6DBC2C. Values verified against flat_image.bin.
        public const int CM_3180 = 0x0C6C;   // leaf 0x6DA405 -> worker 0x6E3280
        public const int CM_3190 = 0x0C76;   // leaf 0x6DA5AE -> worker 0x6E590C
        public const int CM_3191 = 0x0C77;   // leaf 0x6DA5C0 -> worker 0x6E5BA8
        public const int CM_3208 = 0x0C88;   // leaf 0x6DA54C -> worker 0x6EA5E0
        public const int CM_3209 = 0x0C89;   // leaf 0x6DA56D -> worker 0x6EA858
        public const int CM_3282 = 0x0CD2;   // leaf 0x6DA600 -> worker 0x6E64BC
        public const int CM_3283 = 0x0CD3;   // leaf 0x6DA626 -> worker 0x6E67B0
        public const int CM_3284 = 0x0CD4;   // leaf 0x6DA650 -> worker 0x6E6EA4
        public const int CM_3285 = 0x0CD5;   // leaf 0x6DA638 -> worker 0x6E6DE8
        public const int CM_3286 = 0x0CD6;   // leaf 0x6DA65D -> worker 0x6E6B54
        public const int CM_3287 = 0x0CD7;   // leaf 0x6DA895 -> worker 0x6E8734
        public const int CM_3288 = 0x0CD8;   // leaf 0x6DA8C4 -> worker 0x6E8820
        public const int CM_3294 = 0x0CDE;   // leaf 0x6DA613 -> worker 0x6EB190
        public const int CM_3295 = 0x0CDF;   // leaf 0x6DAA99 -> worker 0x6EB8E4
        public const int CM_3306 = 0x0CEA;   // leaf 0x6DAB39 -> worker 0x6EFD54
        public const int CM_3307 = 0x0CEB;   // leaf 0x6DABEA -> worker 0x6CBD78
        public const int CM_3340 = 0x0D0C;   // leaf 0x6DAC30 -> worker 0x79E78C
        public const int CM_3344 = 0x0D10;   // leaf 0x6DADD6 -> worker 0x6EC5D8
        public const int CM_3410 = 0x0D52;   // leaf 0x6DAED9 -> worker 0x6EBE50
        public const int CM_3503 = 0x0DAF;   // leaf 0x6DAF44 -> worker 0x6EF970
        public const int CM_4102 = 0x1006;   // leaf 0x6DABFC -> worker 0x6B7BCC
        public const int CM_4105 = 0x1009;   // leaf 0x6DA005 -> workers 0x7742C0/0x6BCE2C/0x6EE174
        public const int CM_4123 = 0x101B;   // leaf 0x6DAE32 -> worker 0x6BF908
        public const int CM_4124 = 0x101C;   // leaf 0x6DAE53 -> worker 0x6BFA88
        // Reply idents emitted by the evaluable pre-gate legs restored in Q3.
        public const int SM_1727 = 0x06BF;   // CM 3180 short-body status reply (all-zero body)
        public const int SM_4035 = 0x0FC3;   // CM 4123/4124 soul-wash invalid-target reply
        // === end CM missing Q3 ===

        public static ClientPacket MakeDefaultMsg(int msg, int Recog, int param, int tag, int series)
        {
            var result = new ClientPacket();
            result.Ident = (ushort)msg;
            result.Param = (ushort)param;
            result.Tag = (ushort)tag;
            result.Series = (ushort)series;
            result.Recog = Recog;
            return result;
        }

        public static int MakeMonsterFeature(byte btRaceImg, byte btWeapon, ushort wAppr)
        {
            return HUtil32.MakeLong(HUtil32.MakeWord(btRaceImg, btWeapon), wAppr);
        }

        public static int MakeHumanFeature(byte btRaceImg, byte btDress, byte btWeapon, byte btHair)
        {
            return HUtil32.MakeLong(HUtil32.MakeWord(btRaceImg, btWeapon), HUtil32.MakeWord(btHair, btDress));
        }

        // === SM missing-ident batch 2/4 (ident 666..1256, second quarter) ===========
        // Down-wire idents whose native send points were reversed byte-for-byte from
        // flat_image.bin (ImageBase 0x400000) for TBaseObject.SmIdent_Sm2.cs. Generic
        // SM_<value> names are used where the native message carries no established
        // semantic name, to avoid inventing meaning. Idents 917/924/925/965 already
        // have SM_ constants above and are reused there; 950/1108 are fail-closed
        // (unresolved body / global-manager dependency) and get no builder.
        // Values that collide with an existing CM_ constant of the same number are
        // intentional: the wire number is shared across the up/down directions.
        public const int SM_689 = 689;
        public const int SM_951 = 951;
        public const int SM_959 = 959;
        public const int SM_966 = 966;
        public const int SM_1107 = 1107;
        public const int SM_1109 = 1109;
        public const int SM_1201 = 1201;
        public const int SM_1233 = 1233;
        public const int SM_1250 = 1250;
        public const int SM_1251 = 1251;
        public const int SM_1252 = 1252;
        public const int SM_1253 = 1253;
        public const int SM_1254 = 1254;
        public const int SM_1255 = 1255;
        public const int SM_1256 = 1256;
        // ===================== SM missing-ident batch 1/4 (ascending first quarter) =====================
        // Native server->client (SM) idents that fire through a send slot ([obj+0x250] end sMsg /
        // [obj+0x254] end (Buf,Len) / [vmt+0xE0] / cx-wrapper 0x6BCE54) in flat_image.bin
        // (ImageBase 0x400000) yet had NO C# constant of any prefix. Census:
        // staging/_sm1_work/classes.txt (140 idents); this block is the lowest 35 by value.
        // Send builders (frame + body decoded from the image) live in
        // GameSvr/Actors/TBaseObject.SmIdent_Sm1.cs. Idents whose BODY could not be proven from the
        // image are registered here but marked BLOCKED and have no builder (fail-closed).
        // SM_965 (0x3C5) already exists earlier in this file and is reused by the builder.
        public const int SM_35 = 35;      // 0x023  RM-dispatch arm mov dx @0x6B48DE -> call [obj+0x250] @0x6B48E7 (empty body)
        public const int SM_37 = 37;      // 0x025  RM-dispatch arm mov dx @0x6B473C -> call [obj+0x250] @0x6B4745 (empty body)
        public const int SM_56 = 56;      // 0x038  map marker list: Param 0 clear, 1 record (24 bytes), 2 complete
        public const int SM_66 = 66;      // 0x042  RM-dispatch arm mov dx @0x6B5E5E -> call [obj+0x254] @0x6B5E67 (Buf=nil Len=0, empty body)
        public const int SM_70 = 70;      // 0x046  RM-dispatch arm mov dx @0x6B5D8C -> call [obj+0x250] @0x6B5D95 (empty body)
        public const int SM_71 = 71;      // 0x047  RM-dispatch arm mov dx @0x6B5DC0 -> call [obj+0x250] @0x6B5DC9 (empty body)
        public const int SM_72 = 72;      // 0x048  RM-dispatch arm mov dx @0x6B5DF4 -> call [obj+0x250] @0x6B5DFD (empty body)
        public const int SM_73 = 73;      // 0x049  RM-dispatch arm mov dx @0x6B5E28 -> call [obj+0x250] @0x6B5E31 (empty body)
        public const int SM_108 = 108;    // 0x06C  member-list broadcast via wrapper 0x705954, sMsg = source text truncated to 80 bytes @0x705FC6
        public const int SM_539 = 539;    // 0x21B  by-name target send mov dx @0x638A0C -> call [obj+0x250] @0x638A12 (empty body)
        public const int SM_543 = 543;    // 0x21F  fixed Recog=-6 mov dx @0x654BC5 -> call [obj+0x250] @0x654BCD (empty body)
        public const int SM_546 = 546;    // 0x222  fixed Recog=-2 mov dx @0x6E0B95 -> call [obj+0x250] @0x6E0B9D (empty body)
        public const int SM_551 = 551;    // 0x227  Param=2 mov dx @0x786606 -> call [obj+0x250] @0x786610 (empty body)
        public const int SM_554 = 554;    // 0x22A  BLOCKED: virtual send [vmt+0xE0] @0x656432, 6-arg signature + record +6/+8/+0xA semantics + Param=word[rec+6]+0x4C offset unproven
        public const int SM_1257 = 1257;  // 0x4E9  Recog=<self-derived> mov dx @0x6F10C0 -> call [obj+0x250] @0x6F10C8 (empty body)
        public const int SM_1258 = 1258;  // 0x4EA  Param=flag(0/1), Recog=<self-derived> mov dx @0x6F159E -> call [obj+0x250] @0x6F15A6 (empty body)
        public const int SM_1259 = 1259;  // 0x4EB  fixed Recog=-1 mov dx @0x6F114E -> call [obj+0x250] @0x6F1156 (empty body)
        public const int SM_1260 = 1260;  // 0x4EC  fixed Recog=-1 mov dx @0x6F11A6 -> call [obj+0x250] @0x6F11AE (empty body)
        public const int SM_1261 = 1261;  // 0x4ED  fixed Recog=-1 mov dx @0x6F0E4E -> call [obj+0x250] @0x6F0E56 (empty body)
        public const int SM_1262 = 1262;  // 0x4EE  fixed Recog=-1 mov dx @0x6F0FC2 -> call [obj+0x250] @0x6F0FCA (empty body)
        public const int SM_1263 = 1263;  // 0x4EF  Param=5, Recog=<arg> mov dx @0x6F11F9 -> call [obj+0x250] @0x6F1201 (empty body)

        // === SM missing batch 3 (ascending #36..#70 of classes.txt CLASS(c)) ===========
        // Native server->client (SM) idents that fire through a real send slot
        // ([obj+0x250] end sMsg / [obj+0x254] end (Buf,Len)) in flat_image.bin
        // (ImageBase 0x400000) yet had NO C# constant of any prefix. Send builders
        // (frame + body decoded byte-for-byte with capstone from each send site) live
        // in GameSvr/Actors/TBaseObject.SmIdent_Sm3.cs. Three idents whose body is a
        // local, runtime-composed variable-length record buffer that cannot be
        // evaluated at the send slot are registered here but marked BLOCKED and have
        // NO builder (fail-closed): SM_1729, SM_2850, SM_2956.
        public const int SM_1264 = 1264; // 0x4F0  [obj+0x250] empty; Recog=0 Param=1 @0x6F0A73
        public const int SM_1265 = 1265; // 0x4F1  [obj+0x250] empty; Recog=ecx(arg) Param/Tag=word args @0x6F1794
        public const int SM_1726 = 1726; // 0x6BE  [obj+0x250] empty; Recog=edi(runtime) @0x6E3273
        public const int SM_1729 = 1729; // 0x6C1  BLOCKED: [obj+0x254] Buf=&local[ebp-0xFC] Len=0xE0; 8x28-byte records built by loop @0x613788; body not resolvable at slot @0x613925
        public const int SM_1730 = 1730; // 0x6C2  [obj+0x250] empty; Recog=edx(runtime) @0x6E39BC
        public const int SM_1731 = 1731; // 0x6C3  [obj+0x250] empty; Recog=esi(runtime) @0x6E3A0D
        public const int SM_1732 = 1732; // 0x6C4  [obj+0x250] empty; all 0 @0x614AE8
        public const int SM_1733 = 1733; // 0x6C5  [obj+0x250] empty; Param=byte[self+0xF2] @0x6149C7
        public const int SM_1734 = 1734; // 0x6C6  [obj+0x250] empty; Param=byte[self+ebx+0xEC] @0x6145F0
        public const int SM_1735 = 1735; // 0x6C7  [obj+0x250] empty; all 0 @0x61487F
        public const int SM_1736 = 1736; // 0x6C8  [obj+0x250] empty; Param=byte[self+0xF3] @0x6144E4
        public const int SM_1737 = 1737; // 0x6C9  [obj+0x250] empty; Recog/Param/Tag/Series=byte[self+0xEC..0xEF] @0x61478D
        public const int SM_1738 = 1738; // 0x6CA  [obj+0x250] empty; all 0 @0x6152EE
        public const int SM_2812 = 2812; // 0xAFC  [obj+0x250] sMsg text; Recog/Param/Tag=args @0x645320
        public const int SM_2813 = 2813; // 0xAFD  [obj+0x250] empty; RM arm Recog=BaseObject @0x6B5D19
        public const int SM_2815 = 2815; // 0xAFF  [obj+0x250] sMsg text(local); all-0 frame @0x6D4ED7
        // === MessageBoard subsystem ===
        // CM 1280 自身/近身对象回显的回包（leaf 0x6DA8F3 → 0x6E9208 / path2 0x76C9D4）。
        // [obj+0x254] SendSocket：Buf=[命中对象+0x554] Len=0x1C，Recog=Recog，all-0 frame
        // @0x6DA925(self/hero) / 0x6DA99A(path2)。body 0x1C(28B) 字段块 [obj+0x554] 未建模，
        // 故其忠实处理器（TPlayObject.MessageBoard.cs）对命中路径 fail-closed，不臆造 body。
        public const int SM_3291 = 3291; // 0xCDB  [obj+0x254] Buf=[obj+0x554] Len=0x1C; Recog=Recog all-0 frame @0x6DA925 (CM 1280 自身/近身回显)
        public const int SM_2830 = 2830; // 0xB0E  [obj+0x254] Buf=[rec+0x10] Len=word[rec+0x14] forward; RM arm @0x6B555D
        public const int SM_2843 = 2843; // 0xB1B  [obj+0x250] empty; Recog=6 @0x6DE6FA
        public const int SM_2850 = 2850; // 0xB22  BLOCKED: [obj+0x254] Buf=&local[ebp-4] Len=Count*20 dyn-array built by 0x5F4D4C; body not resolvable at slot @0x6D30B7
        public const int SM_2865 = 2865; // 0xB31  [obj+0x250] sMsg text(local); Recog=self @0x6E1D39
        public const int SM_2878 = 2878; // 0xB3E  [obj+0x250] sMsg text(local); Recog=id @0x624AC6
        public const int SM_2880 = 2880; // 0xB40  [obj+0x250] empty; Recog=[ebp-8](runtime) @0x6E598B
        public const int SM_2881 = 2881; // 0xB41  [obj+0x250] empty; Param=ebx(runtime) @0x6E5E10
        public const int SM_2885 = 2885; // 0xB45  [obj+0x254] 20-byte struct body (5 dwords, layout proven from fill code) @0x744EF1
        public const int SM_2896 = 2896; // 0xB50  [obj+0x254] Buf=[rec+0x10] Len=word[rec+0x14] forward; RM arm @0x6B5F8E
        public const int SM_2897 = 2897; // 0xB51  [obj+0x254] Buf=[rec+0x10] Len=word[rec+0x14] forward; RM arm @0x6B5FC8
        public const int SM_2898 = 2898; // 0xB52  [obj+0x250] empty; RM arm Recog=BaseObject @0x6B5FED
        public const int SM_2951 = 2951; // 0xB87  [obj+0x250] empty; Recog=self Param/Tag=self fields @0x6E5376
        public const int SM_2952 = 2952; // 0xB88  [obj+0x250] empty; Recog=self Param=word local @0x6E5567
        public const int SM_2956 = 2956; // 0xB8C  BLOCKED: [obj+0x254] Buf=&local[ebp-0x488] Len=Count*24 record array built by loop @0x6E6A65; body not resolvable at slot @0x6E6AED
        public const int SM_2957 = 2957; // 0xB8D  [obj+0x250] empty; all 0 @0x6E6EE7
        public const int SM_2958 = 2958; // 0xB8E  [obj+0x250] empty; Param=1 @0x6E6CF6
        public const int SM_2960 = 2960; // 0xB90  [obj+0x250] sMsg text=[rec+0x10]; RM arm Recog=BaseObject @0x6B5ECE
        public const int SM_2968 = 2968; // 0xB98  [obj+0x250] empty; RM arm Recog=BaseObject Param=nParam1 @0x6B5F18
        // === CM missing Q1 (idents 1054..1260) — lowest 25 of the 99-item CM dispatch gap.
        // These are the wIdent selectors for TryHandleNativeCmQ1 (see
        // GameSvr/Players/TPlayObject.NativeCmProtocol_Q1.cs). Every value is the CM opcode
        // read as word[record+4] by sub_6D7D68 @0x6D805C; none had a prior CM_/same-value
        // constant, so each is defined once here. All are fail-closed (see
        // GameSvr/Services/NativeCmQ1FailClosed.cs for per-ident image evidence). ===
        public const int CM_1054 = 1054;   // 商城/mall submit, leaf 0x6D942F
        public const int CM_1055 = 1055;   // 商城/mall submit, leaf 0x6D9492
        public const int CM_1056 = 1056;   // 商城/mall submit, leaf 0x6D953A
        public const int CM_1057 = 1057;   // 商城/mall submit, leaf 0x6D9547
        public const int CM_1059 = 1059;   // 限时活动确认, leaf 0x6D9554
        public const int CM_1061 = 1061;   // 技能石复制, leaf 0x6D9579
        public const int CM_1068 = 1068;   // 装备密码锁输入, leaf 0x6D959B
        public const int CM_1080 = 1080;   // 强化/物品表, leaf 0x6D95D6
        public const int CM_1084 = 1084;   // 装备密码锁计时, leaf 0x6D95C9
        public const int CM_1090 = 1090;   // 答题/广播, leaf 0x6D9732
        public const int CM_1200 = 1200;   // 答题/广播, leaf 0x6DA21F
        public const int CM_1210 = 1210;   // 摆摊交易, leaf 0x6DA418
        public const int CM_1211 = 1211;   // 摆摊交易, leaf 0x6DA45D
        public const int CM_1212 = 1212;   // 摆摊交易, leaf 0x6DA49B
        public const int CM_1213 = 1213;   // 摆摊交易, leaf 0x6DA4BF
        public const int CM_1214 = 1214;   // 摆摊交易, leaf 0x6DA529
        public const int CM_1217 = 1217;   // 广播提交, leaf 0x6DA372
        public const int CM_1248 = 1248;   // 碎片拼合, leaf 0x6DA58E
        public const int CM_1250 = 1250;   // 任务发布板, leaf 0x6DA5A1
        public const int CM_1251 = 1251;   // 元宝寄售, leaf 0x6DA66A
        public const int CM_1254 = 1254;   // 元宝寄售(成交), leaf 0x6DA69F
        public const int CM_1255 = 1255;   // 元宝寄售, leaf 0x6DA6B1
        public const int CM_1258 = 1258;   // 元宝寄售, leaf 0x6DA6C3
        public const int CM_1259 = 1259;   // 元宝寄售(开启提交), leaf 0x6DA6EF
        public const int CM_1260 = 1260;   // 设置元宝交易金额, leaf 0x6DA6FC
        // === CM missing Q2 (ascending 26..50 of the CM missing set, ident 1265..3179) ===
        // Every ident below owns a real arm in 战神's CM dispatcher sub_6D7D68
        // (selector tree rooted at 0x6D805C, shared exit label 0x6DBC2C); none of
        // them carries a symbolic name in the image, so the handler (leaf) VA and
        // the worker VA it tail-calls are recorded inline. Wire record roles the
        // dispatcher keeps at [ebp-0x34]: [msg+0]=Recog->nParam1, [msg+6]=Param->
        // nParam2, [msg+8]=Tag->nParam3, [msg+0xA]=Series->wParam, body->sMsg,
        // (total-0xC)->nBodyLen. See docs/cm_q2_missing_impl_20260813.md.
        public const int CM_1265 = 1265;  // 0x6DA710 -> 0x6E8564  元宝交易设置
        public const int CM_1280 = 1280;  // 0x6DA8F3 -> 0x6E9208  自身对象回显
        public const int CM_1291 = 1291;  // 0x6DA3CA -> 0x69059C  英雄灵珠
        public const int CM_1300 = 1300;  // 0x6DAA17 -> 0x63D980  分身点击NPC
        public const int CM_1301 = 1301;  // 0x6DAA72 -> 0x63DC98  分身执行NPC过程
        public const int CM_1316 = 1316;  // 0x6DAACF -> 0x746908  英雄生肖镶嵌
        public const int CM_1320 = 1320;  // 0x6DAB6A -> 0x765E68  分身会话请求
        public const int CM_1350 = 1350;  // 0x6DAC8E -> 0x6F09C4  元宝寄售·写
        public const int CM_1351 = 1351;  // 0x6DACA7 -> 0x6F0A98  元宝寄售·写
        public const int CM_1352 = 1352;  // 0x6DACD0 -> 0x6F0B84  元宝寄售·上架
        public const int CM_1353 = 1353;  // 0x6DACE4 -> 0x6F0E0C  元宝寄售·写
        public const int CM_1354 = 1354;  // 0x6DACF6 -> 0x6F0E64  元宝寄售·写
        public const int CM_1355 = 1355;  // 0x6DAD08 -> 0x6F0EBC  元宝寄售·写
        public const int CM_1356 = 1356;  // 0x6DAD21 -> 0x6F0F28  元宝寄售·写
        public const int CM_1357 = 1357;  // 0x6DAD33 -> 0x6F0F80  元宝寄售·写
        public const int CM_1358 = 1358;  // 0x6DAD45 -> 0x6F0FD8  元宝寄售·写
        public const int CM_1359 = 1359;  // 0x6DAD57 -> 0x6F1028  元宝寄售·取回(cl=1)
        public const int CM_1360 = 1360;  // 0x6DAD6B -> 0x6F1028  元宝寄售·取回(cl=0)
        public const int CM_1361 = 1361;  // 0x6DAD7F -> 0x6F110C  元宝寄售·写
        public const int CM_1362 = 1362;  // 0x6DAD91 -> 0x6F1164  元宝寄售·写
        public const int CM_1363 = 1363;  // 0x6DADA3 -> 0x6F11BC  元宝寄售·写
        public const int CM_1364 = 1364;  // 0x6DADB5 -> 0x6F120C  元宝寄售·写
        public const int CM_1376 = 1376;  // 0x6DAFF3 -> 0x6F2E44  坐骑马牌
        public const int CM_2815 = 2815;  // 0x6D9B52 -> 0x6D4E4C  消息板/relay
        public const int CM_3179 = 3179;  // 0x6DA3F3 -> 0x6E320C  商人物品字节查询
        // === end CM missing Q2 ===

        // === HorseToken subsystem ===
        // SM 0x50A: reply built by the CM 1376 (坐骑马牌) worker 0x6F2E44 at
        // 0x6F2E8C `mov dx,0x50A` through [player+0x250] SendDefMessage; Recog is
        // [item+0x18] (ClientItemID, 0x6F2E89), wParam is the request Param
        // (0x6F2E82 push edi), Tag/Series 0, empty body. No prior C# SM constant
        // held 0x50A (CM_YANHUA_TEXT reuses 1290 in the CM direction).
        public const int SM_HORSETOKEN_SETMOUNT = 0x50A; // 1290

        // === SM missing batch 5 (rank 106-140) ===========================================
        // Highest 35 by value of the 140 class-(c) native wire-SM idents
        // (staging/_sm1_work/classes.txt) that fire through a real send slot in
        // flat_image.bin (ImageBase 0x400000) yet had NO C# constant of any prefix.
        // Send builders (frame + body decoded byte-for-byte with capstone from each
        // send site) live in GameSvr/Actors/TBaseObject.SmIdent_Sm5.cs. Send-slot
        // conventions (reverified against the earlier batches' exemplars and the two
        // member-broadcast wrappers 0x705954 [0x250] / 0x7059D0 [0x254]):
        //   [obj+0x250] SendDefMessage: push Param,Tag,Series,sMsg ; ecx=Recog dx=ident
        //   [obj+0x254] SendSocket:     push Param,Tag,Series,Buf,Len ; ecx=Recog dx=ident
        // Eight idents whose body cannot be evaluated at the send slot (locally
        // composed record/struct buffers with uninitialised ShortString padding, an
        // opaque per-record serializer, or a non-standard virtual slot) are registered
        // here but marked BLOCKED and have NO builder (fail-closed):
        //   SM_4363, SM_4441, SM_4442, SM_4443, SM_4612, SM_4626, SM_4646, SM_4647.
        public const int SM_4349 = 4349; // 0x10FD [obj+0x250] empty; RM arm Recog=nParam1 Param=wParam Tag=LoWord(nParam2) Series=HiWord(nParam2) @0x6B5215
        public const int SM_4350 = 4350; // 0x10FE [obj+0x250] empty; RM arm (same 5-tuple as 4349) @0x68980D
        public const int SM_4351 = 4351; // 0x10FF [obj+0x250] empty; Recog=arg(eax), all-0; gated target!=nil/not-ghost/[t+0x73]==0 @0x647F38
        public const int SM_4352 = 4352; // 0x1100 [obj+0x250] empty; Recog=self, all-0 @0x6E616A
        public const int SM_4361 = 4361; // 0x1109 [obj+0x250] empty; Recog=arg(eax), all-0 (sibling of 4348/4360 helper family) @0x64013C
        public const int SM_4363 = 4363; // 0x110B BLOCKED: send via [vmt+0xE0] non-standard 6-arg slot (unproven signature, cf SM_554); Buf=[rec+0x10] Len=word[rec+0x14] + extra push 1 @0x767160
        public const int SM_4407 = 4407; // 0x1137 [obj+0x250] sMsg text=[rec+0x10]; RM arm Recog=wParam[rec+2], Param/Tag/Series=0 @0x6B60F2
        public const int SM_4408 = 4408; // 0x1138 [obj+0x250] empty; Recog=esi(0x7487A8 result), all-0 (flag==0 arm) @0x6F3897
        public const int SM_4409 = 4409; // 0x1139 [obj+0x250] empty; Recog=esi(0x748A18 result), all-0 (flag==0 arm) @0x6F393A
        public const int SM_4410 = 4410; // 0x113A [obj+0x250] empty; Recog=esi(0x7487A8 result), all-0 (flag!=0 arm) @0x6F387D
        public const int SM_4411 = 4411; // 0x113B [obj+0x250] empty; Recog=esi(0x748A18 result), all-0 (flag!=0 arm) @0x6F3920
        public const int SM_4441 = 4441; // 0x1159 BLOCKED: [obj+0x254] 36-byte composed struct (name1 SS[15]@0x00, word[src+0x18]@0x10, byte[src+0x1A]@0x12, name2 SS[15]@0x13 from by-name lookup, flag@0x23); ShortString tail padding uninit @0x6FF4D9
        public const int SM_4442 = 4442; // 0x115A BLOCKED: [obj+0x254] 22-byte composed struct (name SS[15]@0x00, word[src+0x18]@0x10, byte[src+0x1A]@0x12, byte[src+0x1B]@0x13, online flag@0x14, pad@0x15); SS padding + pad uninit @0x6FFE30
        public const int SM_4443 = 4443; // 0x115B BLOCKED: [obj+0x254] 20-byte composed struct (name SS[15]@0x00, word[src+0x18]@0x10, online flag@0x12, pad@0x13); SS padding + pad uninit @0x700918
        public const int SM_4444 = 4444; // 0x115C [obj+0x250] sMsg text=char-name([tgt+0x106]); Recog=0 Param=byte[rec+4] (per-target loop) @0x6FE929
        public const int SM_4445 = 4445; // 0x115D [obj+0x250] sMsg text=char-name([tgt+0x106]); Recog=0 Param=byte[rec+4] (per-target loop) @0x6FE865
        public const int SM_4446 = 4446; // 0x115E [obj+0x250] empty; Recog=LoWord(0x712BE4 result), all-0; gated [self+0x192C]!=0 @0x6F75EF
        public const int SM_4455 = 4455; // 0x1167 [obj+0x250] sMsg text=char-name; Recog=0 Param=(stat&0x7F) Tag=(stat&0x7F) (per-nearby loop) @0x6A89E0
        public const int SM_4456 = 4456; // 0x1168 [obj+0x250] sMsg text=char-name; Recog=0 Param=(stat&0x7F) Tag=byte @0x6A8AAB
        public const int SM_4457 = 4457; // 0x1169 [obj+0x250] empty; Recog=0 Param=byte(arg dl) @0x6A8C9F
        public const int SM_4458 = 4458; // 0x116A [obj+0x250] sMsg text=char-name; Recog=0 Param=byte(arg cl) @0x6A8D22
        public const int SM_4459 = 4459; // 0x116B [obj+0x250] sMsg text=char-name; Recog=0 Param=byte(arg cl) @0x6A8DC2
        public const int SM_4469 = 4469; // 0x1175 [obj+0x250] sMsg text=char-name([arg+0x106]); all-0 frame @0x6F788B
        public const int SM_4470 = 4470; // 0x1176 [obj+0x250] sMsg text=char-name([arg+0x106]); all-0 frame @0x6F78F3
        public const int SM_4480 = 4480; // 0x1180 member-broadcast wrapper 0x705954; sMsg text = "与"+<name>+"行会的行会战结束"; all-0 frame @0x7068AF
        public const int SM_4496 = 4496; // 0x1190 [obj+0x250] empty; Recog=esi(runtime, -1 default / 0x4177C0 result), all-0 @0x6FAD1B
        public const int SM_4499 = 4499; // 0x1193 [obj+0x250] sMsg text=esi(string arg); Recog=edi(int arg), all-0 @0x6FBD25
        public const int SM_4612 = 4612; // 0x1204 BLOCKED: [obj+0x254] count*17 record array (byte + SS[15] name) copied by loop from unmapped source dyn-array; SS padding uninit; Len=[src+8]*17 @0x6F781C
        public const int SM_4614 = 4614; // 0x1206 member-broadcast wrapper 0x7059D0 ([0x254] variant); body=8 bytes (two dwords); Param=0 Tag=0 Series=1 Recog=0 @0x70214D
        public const int SM_4626 = 4626; // 0x1212 BLOCKED: [obj+0x254] count*0x40 record array filled by opaque serializer 0x7060B8; Buf=[ebp-0x1C] Len=[ebp-0x14]*0x40 @0x6AE363
        public const int SM_4638 = 4638; // 0x121E [obj+0x250] empty; Recog=0 all-0; sent to edx target!=nil @0x64E832
        public const int SM_4646 = 4646; // 0x1226 BLOCKED: [obj+0x254] Delphi dyn-array (elem filled by 0x69C57C+0x403260); Buf=[ebp-4] Len=[self+0x658] element-count (byte-length unresolved) @0x6FBC4C
        public const int SM_4647 = 4647; // 0x1227 BLOCKED: [obj+0x254] 24-byte record via 0x69C514 (SS[20]@0x00..0x14, gap@0x15, word@0x16); gap uninit @0x6FB7FF
        public const int SM_4649 = 4649; // 0x1229 [obj+0x250] empty; Recog=esi(0/1 from 0x69C47C), all-0 @0x6FBB5F
        public const int SM_4650 = 4650; // 0x122A [obj+0x250] empty; Recog=[ebp-4](runtime), all-0 @0x6FB610

        // === Inlay subsystem ===
        // 神珠/宝玉 镶嵌 reply idents. The numeric SM ident equals the CM ident for
        // all four: the workers 0x6F37EC / 0x6F38A8 answer via [vmt+0x250]
        // (SendDefMessage) with `mov dx,0x1138..0x113B` and ECX = the result code.

        // === Qiankun subsystem ===
        // 乾坤包/鸿福袋 CM idents. Dispatch-tree leaves rooted at selector 0x6D805C
        // (dispatcher sub_6D7D68). ImageBase 0x400000; file offset = VA - 0x400000.
        // Handler arm -> worker (三件套证据见 TPlayObject.Qiankun.cs 头注释):
        //   CM 3282 0x6DA600 -> 0x6E64BC  乾坤包列表查询/明细 (fail-closed: 依赖运行期配置)
        //   CM 3283 0x6DA626 -> 0x6E67B0  鸿福袋开启 (fail-closed: 配置管理器 [0x7D64B8] 未移植)
        //   CM 3284 0x6DA650 -> 0x6E6EA4  乾坤包选择列表重置 -> SM 2957(全零)
        //   CM 3285 0x6DA638 -> 0x6E6DE8  乾坤包·荣耀点更换 (config 恒 null -> 静默返回)
        //   CM 3286 0x6DA65D -> 0x6E6B54  乾坤包·领取奖励 (列表恒空 -> reset -> SM 2957)
        //   CM 3287 0x6DA895 -> 0x6E8734  附近玩家展示物品 RM 0x3004 (独立特性, fail-closed)
        //   CM 3288 0x6DA8C4 -> 0x6E8820  自身物品 RM 0x3005 (独立特性, fail-closed)

        // === ItemTransfer subsystem ===
        // CM 4215（邻域对象交互，leaf 0x6DAFCA -> worker 0x6E8684）与 CM 4218（"物品转移
        // 到本人"，leaf 0x6DB00C -> worker 0x6F3104）的镜像常量。两者的 CM ident 常量
        // (CM_4215 / CM_4218) 已在上方 CM 尾段清单里定义，此处不重复。
        //
        // SM 4117 (0x1015)：CM 4215 唯一回包 ident。全镜像仅 worker 0x6E8684 内三处
        // `66 BA 15 10 mov dx,0x1015`（0x6E86D0 / 0x6E86EF / 0x6E871F）发送，别无第二
        // 发送点，故其客户端语义无法从镜像推导；回包体又取自邻居对象未建模的
        // word[+0x608]，因此忠实实现里对该回包 fail-closed（扣留，绝不上线臆造字节）。
        public const int SM_4117 = 4117; // 0x1015  CM4215 邻域交互唯一回包（withheld）

        // CM 4218 的 1500ms 冷却门阈值：0x6F3118 `sub edx,[self+0xA6C]` /
        // 0x6F311E `cmp edx,0x5DC` / 0x6F3124 `jbe`（无符号 <=）-> 静默丢弃。
        public const int ItemTransferSelfThrottleMs = 0x5DC; // 1500
        // === SM missing batch 4 (rank 71-105 + 顺延 tail 106-140) =====================
        // Server->client idents that native M2Server (flat_image.bin, ImageBase
        // 0x400000) sends through a real [obj+0x250]/[obj+0x254] slot but had no C#
        // constant of any prefix. Census: staging/_sm1_work/reconcile.txt (140 class-c
        // idents); this batch is the second-highest quarter (rank 71-105) plus the
        // ascending 顺延 back-fill after skipping already-handled entries (sm-A / sm-1 /
        // sm-2 / TimedAbility 3554-3555 / NativeScriptUiOpen 4331/4339/4340/4348/4351/
        // 4361 / NativeYbCredit 3009 / slave-list 4469-4470 / social 4612 / SM_4034 /
        // m_sm_d relation+YB 4441-4446). Builders decoded byte-for-byte live in
        // GameSvr/Actors/TBaseObject.SmIdent_Sm4.cs. Idents whose BODY/FRAME is not
        // evaluable at a mapped send slot are BLOCKED (fail-closed, no builder).
        //   -- built (empty body unless noted) --
        public const int SM_2969 = 2969;  // 0xB99  RM-forward -> [obj+0x250] @0x6B5F3D
        public const int SM_2970 = 2970;  // 0xB9A  RM-forward -> [obj+0x250] @0x6B5F65 (0x254 serializer variant @0x6EB41C = gap)
        public const int SM_4038 = 4038;  // 0xFC6  flag notify -> [obj+0x250] @0x746D3B/@0x746D56
        public const int SM_4070 = 4070;  // 0xFE6  -> [obj+0x250] @0x649072
        public const int SM_4205 = 4205;  // 0x106D SMS-auth reply -> [obj+0x250] (4 sites @0x654C3E..@0x6F023A)
        public const int SM_4206 = 4206;  // 0x106E -> [obj+0x250] @0x6F0496 (Recog 0)/@0x6F04F7 (Recog -1)
        //   -- fail-closed (BLOCKED, no builder; body/frame not evaluable at a mapped slot) --
        public const int SM_3412 = 3412;  // 0xD54  BLOCKED: mov dx @0x6EE22C -> call [obj+0xE0] @0x6EE234 (non-slot virtual)
        public const int SM_4032 = 4032;  // 0xFC0  BLOCKED: [obj+0x254] @0x746D18, Buf/Len = [[0x7D6014]] 43-byte table record (undefined)
        public const int SM_4033 = 4033;  // 0xFC1  BLOCKED: [obj+0x254] @0x747362, 32-byte state-0x36 record from [self+0x5A8] (unmapped)
        public const int SM_4037 = 4037;  // 0xFC5  BLOCKED: [obj+0x254] @0x6B71ED, 24-byte body [self+0x60C]+[self+0x5A8] (unmapped)

        // === TaskBoard subsystem ===
        // Task-dispatch board refresh reply. Worker sub_699B68 answers via [player+0x254]
        // with Recog=Param=Tag=Series=0 and a fixed 873-byte (0x369) body.
        public const int SM_TASKBOARD_REFRESH = 3452; // 0xD7C

        // === NewbieQuest/PageList subsystem ===
        // Reply idents pinned off the two workers reversed for this subsystem.
        // SM_4496 (0x1190): CM 4496 新手任务 worker sub_6FAC8C @0x6FAD13
        //   `66 BA 90 11 mov dx,0x1190` before call [vmt+0x250]. Recog carries the
        //   int returned by the FreshmanTaskCommand @Main script; that script entry
        //   on the task-board admin object [[0x7D5D20]] (TTaskAdmin) is not modelled,
        //   so nothing is sent (fail-closed) — the constant is documentation only.
        // SM_4626 (0x1212): CM 4626 分页列表查询 worker sub_6AE260 @0x6AE35D
        //   `66 BA 12 12 mov dx,0x1212` before call [vmt+0x254]. It is the paged
        //   RECRUITING-corps list: TCorpsManager[+0x28] (sub_5EC0D8 mirrors the
        //   master [+0x24] list, keeping only corps whose member count < 30 —
        //   predicate sub_705690 `cmp [members+8],0x1E / setge`). Same 64-byte
        //   TCorps description record + pagination frame as CM_CORPS_LIST/4520.

        // === SoulWash subsystem ===
        // 祈福神佑袋 / 灵佑点 (native strings 0x74705C "灵气石", 0x747834 "点灵佑点!",
        // 0x747080 "神佑祈福收取"). CM 4126/4127/4128 (workers 0x6BF75C /
        // 0x747CF4+0x74730C / 0x6B7184). CM_4126/4127/4128 constants and SM_4034
        // (0xFC2, the CM 4126 reply, Tag carries the 0/1/2/3 result code) are declared
        // above and reused. Only these two down-wire replies were missing:
        //   SM_4033 (0xFC1) — 0x74730C sends via [vmt+0x254] a 32-byte body
        //     {int cur[+0x5A4]; int base[+0x5A0]; int cap[+0x59C]; word[10] slots[+0x5A8]},
        //     Tag = ([+0x178] == 0x36 ? 1 : 0), Recog=Param=Series=0.
        //   SM_4037 (0xFC5) — 0x6B7184 sends via [vmt+0x254] a 24-byte body
        //     {int [T+0x60C]; byte[20] [T+0x5A8]}, Recog=Param=Tag=Series=0.

        // === RewardList subsystem ===
        // Reply idents for the 金刚石抽奖领奖 / 藏宝图合成 subsystem (CM 4646..4650,
        // native dispatch leaves 0x6DBBEB..0x6DBC18). Each reply ident equals its CM
        // ident on the wire (SM 0x1226/0x1227/0x1229/0x122A), which is how 战神's own
        // senders emit them. Kept as explicit SM_ aliases for call-site clarity.
        public const int SM_REWARDLIST = 4646;       // 0x1226 worker 0x6FBB90 -> [obj+0x254] list body
        public const int SM_REWARDPRECHECK = 4647;   // 0x1227 worker 0x6FB6FC -> [obj+0x254] 24-byte drawn-reward record
        public const int SM_REWARDCLAIMITEM = 4649;  // 0x1229 worker 0x6FBB28 -> [obj+0x250] empty, Recog=deleted?0:1
        public const int SM_TREASUREMAPSYNTH = 4650; // 0x122A worker 0x6FB51C -> [obj+0x250] empty, Recog=synth result
    }
}
