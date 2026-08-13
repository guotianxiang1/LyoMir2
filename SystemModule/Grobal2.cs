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
        public const int ET_MINE = 2;
        public const int ET_PILESTONES = 3;
        public const int ET_HOLYCURTAIN = 4;
        public const int ET_FIRE = 5;
        public const int ET_SCULPEICE = 6;
        public const int ET_YANHUA_TEXT = 23;
        public const int RCC_MERCHANT = 50;
        public const int RCC_GUARD = 12;
        public const int RCC_USERHUMAN = 0;
        public const int CM_QUERYUSERSTATE = 82;
        public const int CM_QUERYUSERNAME = 80;
        public const int CM_QUERYBAGITEMS = 81;
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
        public const int CM_TAKEONITEM = 1003;
        public const int CM_TAKEOFFITEM = 1004;
        public const int CM_1005 = 1005;
        public const int CM_EAT = 1006;
        public const int CM_QUEST_ORDER = 1060;
        public const int CM_1069 = 1069;
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
        public const int CM_OPENGUILDDLG = 1035;
        public const int CM_GUILDHOME = 1036;
        public const int CM_GUILDMEMBERLIST = 1037;
        public const int CM_GUILDADDMEMBER = 1038;
        public const int CM_GUILDDELMEMBER = 1039;
        public const int CM_GUILDUPDATENOTICE = 1040;
        public const int CM_MERCHANT_QUERY = 1110;
        public const int CM_GUILDUPDATERANKINFO = 1041;
        public const int CM_SPEEDHACKUSER = 1042;
        public const int CM_ADJUST_BONUS = 1043;
        public const int CM_GUILDALLY = 1044;
        public const int CM_GUILDBREAKALLY = 1045;
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
        public const int SM_41 = 4;
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
        // 战神 attack-mode notify ident = 0x221 (545), NOT 213.
        // All three native senders load dx=0x221 and go through VMT+0x250 (SendDefMessage):
        //   @0x6B210C  UserLogon sub_6B1AA0  (mov cl,[esi+0xAED] = MyAttackMode; mov dx,0x221)
        //   @0x6F2D33  sub_6F2D10 SetAttackMode
        //   @0x623A2E  ChangeAttackMode GM handler inside sub_622820
        // Native's 213 (0xD5) exists but never reaches VMT+0x250 — it goes to
        // sub_5F701C / VMT+0x254, i.e. a different message entirely. The request
        // ident CM_ATTACKMODE is also 545: 战神 deliberately echoes the request id.
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
        public const int SM_OPENGUILDDLG = 753;
        public const int SM_OPENGUILDDLG_FAIL = 754;
        public const int SM_SENDGUILDMEMBERLIST = 756;
        public const int SM_GUILDADDMEMBER_OK = 757;
        public const int SM_GUILDADDMEMBER_FAIL = 758;
        public const int SM_GUILDDELMEMBER_OK = 759;
        public const int SM_GUILDDELMEMBER_FAIL = 760;
        public const int SM_GUILDRANKUPDATE_FAIL = 761;
        public const int SM_BUILDGUILD_OK = 762;
        public const int SM_BUILDGUILD_FAIL = 763;
        public const int SM_DONATE_OK = 764;
        public const int SM_DONATE_FAIL = 765;
        public const int SM_MENU_OK = 767;
        public const int SM_GUILDMAKEALLY_OK = 768;
        public const int SM_GUILDMAKEALLY_FAIL = 769;
        public const int SM_GUILDBREAKALLY_OK = 770;
        public const int SM_GUILDBREAKALLY_FAIL = 771;
        public const int SM_DLGMSG = 772;
        public const int SM_BUILDHERO = 773;
        public const int SM_HEROLISTINFO = 971;
        public const int SM_SPACEMOVE_HIDE = 800;
        public const int SM_SPACEMOVE_SHOW = 801;
        public const int SM_RECONNECT = 802;
        public const int SM_GHOST = 803;
        public const int SM_SHOWEVENT = 804;
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

        // Mobile protocol constants (from 战神 capture)
        public const int SM_MOBILE_SURROUNDING = 0x3A;   // 58 - entity position sync
        public const int SM_MOBILE_STATUSCHANGE = 0x1C;   // 28 - entity appear/disappear
        public const int SM_MOBILE_ITEMS = 0x3E;          // 62 - backpack/equipment
        public const int SM_MOBILE_NPCDIALOG = 0x4A;      // 74 - NPC dialog HTML

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
        public const int SM_COMMON_INFORMATION = 2821; // Safe zone entry/exit notification
        public const int SM_SAFE_ZONE_INFO = 4230;     // Map safe zone information
        public const int SM_MAPINFO_EX = 796;         // Extended map info (after SM_NEWMAP)
        public const int SM_BIGMONMAGIC = 797;        // Monster magic effect
        public const int SM_NPCWALK = 798;            // NPC walk animation
        public const int SM_FIREON = 779;             // Fire hit mode ON
        public const int SM_SWORDHIT_ON = 2819;       // Sword hit mode ON
        // 战神 thrusting (刺杀剑术) toggle notify = ident 624 (0x270), param1 = 1.
        // Native UserLogon @0x6B225B: gated on [obj+0xA8]!=0 && [obj+0x94]==0, latches
        // [obj+0x94]=1, then `push 1; push 0; push 0; push 0; xor ecx,ecx;
        // mov dx,0x270; call [ebx+0x250]` (SendDefMessage) — recog=0, nParam=1.
        public const int SM_THRUSTING = 624;

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
        public const int CM_FIND_CORPS_BYNAME = 4616;
        public const int SM_FIND_CORPS_BYNAME = 4616;
        public const int CM_FIND_GILD_BYNAME = 4617;
        public const int SM_FIND_GILD_BYNAME = 4617;
        public const int CM_GILD_CANCEL_JOIN = 4627;
        public const int SM_GILD_CANCEL_JOIN = 4627;
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
        public const int SM_QUERY_FOCUS_ITEM = 3290;

        // === Title / NPC / Item Commit ===
        public const int CM_QUERY_TITLE = 3202;
        public const int CM_QUERY_MAP_NPC = 4610;
        public const int SM_QUERY_MAP_NPC = 4610;
        public const int CM_COMMIT_ITEM = 4634;
        public const int SM_COMMIT_ITEM = 4634;
        public const int SM_OPEN_COMMIT_ITEM = 4635;
        public const int SM_PLAYER_AUTHEN = 4636;

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
        public const int SM_LINGFU_CHANGED = 10054;
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
        public const int RM_DONATE_FAIL = 10306;
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
        public const int RM_NATIVE_MOOTEBO_CONTINUE = 12309;
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
        public const int RM_WWJATTACK = 10017;
        public const int RM_WSJATTACK = 10018;
        public const int RM_WTJATTACK = 10019;

        // === ISM (Inter-Server Message) Constants ===
        // C# extensions (not in native 202..257 range):
        public const int ISM_GROUPSERVERHEART = 100;     // C# extension - server heartbeat
        public const int ISM_USERSERVERCHANGE = 200;     // C# extension - user server change
        public const int ISM_USERLOGON = 201;            // C# extension - user logon

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
        public const int ISM_FRIEND_INFO = 214;          // REAL - Friend info
        public const int ISM_FRIEND_DELETE = 215;        // SINK - Friend delete (C# extension handler)
        public const int ISM_DIVORCE = 216;              // REAL - Divorce
        public const int ISM_MENTOR_STUDENT_1 = 217;     // REAL - Mentor/student handler 1 (EA 0x6572A4, calls sub_657CF0)
        public const int ISM_MENTOR_STUDENT_2 = 218;     // REAL - Mentor/student handler 2 (EA 0x6572B4, calls sub_657AC0)
        public const int ISM_TAG_SEND = 219;             // REAL - Tag send
        public const int ISM_TAG_RESULT = 220;           // REAL - Tag result
        public const int ISM_USER_INFO = 221;            // REAL - User info
        public const int ISM_CHANGESERVERRECIEVEOK = 222;// REAL - Change server receive OK
        public const int ISM_RELOADCHATLOG = 223;        // SINK - Reload chat log (C# extension handler)
        public const int ISM_MARKETOPEN = 224;           // REAL - Market open
        public const int ISM_MARKETCLOSE = 225;          // SINK - Market close (C# extension handler)
        public const int ISM_LM_DELETE = 226;            // REAL - Lover manager delete
        public const int ISM_RELOADMAKEITEMLIST = 227;   // REAL - Reload make item list
        public const int ISM_GUILDMEMBER_RECALL = 228;   // REAL - Guild member recall
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
        public const int ISM_STANDARDTICK = 240;         // REAL - Standard tick
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

        public const int LA_UNDEAD = 1;

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
    }
}
