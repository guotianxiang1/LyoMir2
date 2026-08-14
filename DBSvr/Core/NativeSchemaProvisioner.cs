using System;
using System.Data;
using MySql.Data.MySqlClient;

namespace DBSvr.Core
{
    /// <summary>
    /// DORMANT: 原版战神 DBServer 全量 schema 供给器。
    /// 未经生产库执行过。仅用于全新部署的初始化。
    ///
    /// 默认关闭（ProvisionAll(enabled: false)）。不会在启动时自动修改任何表结构。
    /// 整表 UPDATE（UserId/HeroId 回填）不在本文件，在 NativeUserIdBackfillService.cs。
    ///
    /// MySQL 5.1.55 兼容性说明：
    ///   - CREATE USER ... IF NOT EXISTS 是语法错误，不使用。
    ///   - ALTER TABLE ... ADD COLUMN IF NOT EXISTS 是语法错误，不使用。
    ///   - 列迁移改用 SHOW COLUMNS LIKE 先探测，空结果集时才执行 ALTER TABLE ADD，与原版一致。
    ///
    /// 数据丢失风险：
    ///   - CREATE TABLE IF NOT EXISTS：无风险（表存在时跳过）
    ///   - CREATE DATABASE IF NOT EXISTS：无风险
    ///   - ALTER TABLE ADD COLUMN（有 SHOW COLUMNS 前置探测）：无风险（列存在时跳过）
    ///   - CREATE TABLE ... LIKE（备份克隆）：无风险
    ///   - 整表 UPDATE：不在本文件
    /// </summary>
    public class NativeSchemaProvisioner
    {
        private readonly string _conn;

        public NativeSchemaProvisioner(string connectionString)
        {
            _conn = connectionString;
        }

        /// <summary>
        /// DORMANT — 默认 enabled=false，调用不执行任何 DDL。
        /// 未经生产库执行过。全新部署时才传入 true。
        /// </summary>
        public void ProvisionAll(bool enabled = false)
        {
            if (!enabled) return;
            ProvisionDatabases();
            ProvisionTables();
            ProvisionBackupClones();
            RunColumnMigrations();
        }

        // ─── CREATE DATABASE (4 条) ───────────────────────────────────────
        private void ProvisionDatabases()
        {
            // 0x5BAD98 len=39 rc=-1
            Exec("Create database if not exists gamedata;");
            // 0x5BF7E8 len=36 rc=-1
            Exec("CREATE DATABASE IF NOT EXISTS Guild;");
            // 0x5C0F90 len=38 rc=-1
            Exec("CREATE DATABASE IF NOT EXISTS gamelog;");
            // 0x5C0FC0 len=38 rc=-1
            Exec("CREATE DATABASE IF NOT EXISTS itemlog;");
        }

        // ─── CREATE TABLE — 29 实际表定义 ────────────────────────────────
        private void ProvisionTables()
        {
            // 0x5BAF04 len=1417 rc=-1  mir3.user_index
            Exec("Create table if not exists user_index (idx int unsigned not null AUTO_INCREMENT PRIMARY KEY,PTID Char(20) binary not null,LoginID Char(15) binary not null,ChrName Char(15) binary not null UNIQUE,IsDelete Bool default 0,IsSelect Bool default 0,job tinyint unsigned default 0,sex tinyint unsigned default 0,level smallint unsigned default 0,Exp int unsigned default 0,PlatinaChrLv tinyint unsigned default 0,HeroCardLv tinyint unsigned default 0,ApprenticeNum int  unsigned default 0,GuardNum int unsigned default 0,DarePoint int default 0,CreateDate DateTime default \"2000-01-01\",ModifyDate DateTime default \"2000-01-01\",AdminLevel tinyint unsigned default 0,ForceLv smallint unsigned default 0,ForceExp int unsigned default 0,FightPoints int unsigned default 0,sfLevel int default 0,SrcZoneId smallint unsigned default 0, SrcGroupId smallint unsigned default 0, SrcCharName Char(15) binary default \"\",DesZoneId smallint unsigned default 0,DesGroupId smallint unsigned default 0,UserId bigInt default 0,IsTransLock smallint default 0,TransferModal smallint default 0,lvChangeTime DateTime default \"2100-1-1\",Index UserId_index (UserId),Index PTID_index(PTID, ChrName, job, level, Exp),Index Date_index(ModifyDate, Job, Level, Exp),Index Level_index(Level,ChrName),index level_exp_sort(job, level, Exp),index DarePonit_Sort(DarePoint, level),Index Admin_Level_Sort(AdminLevel, level, exp),index Job_index(Job,ChrName));");
            // 0x5BB498 len=205 rc=-1  mir3.user_data
            Exec("Create table if not exists user_data (Idx Int unsigned not null PRIMARY KEY,ChrName Char(15) binary not null UNIQUE,Status TinyInt unsigned default 0,Data Blob, ScriptData Blob, Index Name_Index(ChrName));");
            // 0x5BB570 len=955 rc=-1  mir3.hero_index
            Exec("Create table if not exists hero_index (idx INT unsigned not null AUTO_INCREMENT PRIMARY KEY,HeroName Char(15) binary not null UNIQUE, MasterName Char(15) binary not null,IsDelete Bool default 0,HeroType tinyint Unsigned default 1,job tinyint unsigned default 0,sex tinyint unsigned default 0,Level smallint unsigned default 0,Exp int unsigned default 0,Consignation tinyint Unsigned default 0,CreateDate DateTime default \"2000-01-01\",ModifyDate DateTime default \"2000-01-01\",ForceLv smallint unsigned default 0,ForceExp int unsigned default 0,sfLevel int default 0,SrcZoneId smallint unsigned default 0, SrcGroupId smallint unsigned default 0, SrcHeroName Char(15) binary default \"\",heroId bigint default 0,index heroId_index (heroId),index hero_index (HeroName),index Master_index (MasterName, HeroName, IsDelete),index Data_index(ModifyDate, Level),index Level_index(Level,HeroName),index level_exp_sort(job, level, Exp),index Job_index(Job, HeroName));");
            // 0x5BB934 len=203 rc=-1  mir3.hero_data
            Exec("Create table if not exists hero_data (Idx Int unsigned not null PRIMARY KEY,HeroName Char(15) binary not null UNIQUE, Status TinyInt unsigned default 0,Data Blob,dynData Blob,Index Name_Index(HeroName));");
            // 0x5BBA08 len=322 rc=-1  mir3.awardplayers
            Exec("Create table if not exists awardplayers(Idx int auto_increment Primary key, PTID char(20) binary not null Unique, HumName Char(14) binary not null default \"\", Level smallint unsigned default 0,job tinyint unsigned default 0,sex tinyint unsigned default 0,Status tinyint unsigned default 0, Index PTID_Index(PTID, Status));");
            // 0x5BBB54 len=253 rc=-1  mir3.HallOfFame
            Exec("Create table if not exists HallOfFame(Idx int not null PRIMARY KEY auto_increment,Rank int default 0,ZoneName char(20) binary not null default \"\",GroupName char(20) binary not null default \"\", CharName char(15) binary not null default \"\",CharData blob);");
            // 0x5BBC5C len=358 rc=-1  mir3.dominatorpet
            Exec("Create table if not exists dominatorpet (Idx int auto_increment Primary key,MasterId bigInt not null unique,MasterName Char(15) binary not null,Level int(5) not null, Exp int(10) unsigned not null, Data Blob, CreateDate DateTime default \"2000-01-01\",ModifyDate DateTime default \"2000-01-01\",index MasterId_Index(MasterId),index MasterName_Index(MasterName));");
            // 0x5BD3FC len=848 rc=-1  mir3.humanmagic
            Exec("Create table if not exists humanmagic (MagName Char(15) binary not null Unique,MagicIdx smallint unsigned default 0 Primary key,EffectType tinyint unsigned default 0,Effect tinyint unsigned default 0,Spell tinyint unsigned default 0,Power tinyint unsigned default 0,MaxPower tinyint unsigned default 0,DefSpell tinyint unsigned default 0,DefPower tinyint unsigned default 0,DefMaxPower tinyint unsigned default 0,job tinyint unsigned default 0,NeedLv1 tinyint unsigned default 0,NeedLv2 tinyint unsigned default 0,NeedLv3 tinyint unsigned default 0,NeedLv4 tinyint unsigned default 0,NeedLv5 tinyint unsigned default 0,LvTrain1 integer default 0,LvTrain2 integer default 0,LvTrain3 integer default 0,LvTrain4 integer default 0,LvTrain5 integer default 0,ColdMilSec integer default 0,SpellMilSec integer default 0,Delay smallint unsigned default 0);");
            // 0x5BD758 len=788 rc=-1  mir3.heromagic
            Exec("Create table if not exists heromagic (MagName Char(15) binary not null Unique,MagicIdx smallint unsigned default 0 Primary key,EffectType tinyint unsigned default 0,Effect tinyint unsigned default 0,Spell tinyint unsigned default 0,Power tinyint unsigned default 0,MaxPower tinyint unsigned default 0,DefSpell tinyint unsigned default 0,DefPower tinyint unsigned default 0,DefMaxPower tinyint unsigned default 0,job tinyint unsigned default 0,NeedLv1 tinyint unsigned default 0,NeedLv2 tinyint unsigned default 0,NeedLv3 tinyint unsigned default 0,NeedLv4 tinyint unsigned default 0,NeedLv5 tinyint unsigned default 0,LvTrain1 integer default 0,LvTrain2 integer default 0,LvTrain3 integer default 0,LvTrain4 integer default 0,LvTrain5 integer default 0,Delay smallint unsigned default 0);");
            // 0x5BDA78 len=861 rc=-1  mir3.monster
            Exec("Create table if not exists monster (MonName Char(15) binary not null Unique,Race tinyint unsigned default 0,RaceImg tinyint unsigned default 0,Undead tinyint unsigned default 0,CoolEye tinyint unsigned default 0,Appr smallint unsigned default 0,Level smallint unsigned default 0,Exp integer default 0,HP integer default 0,MP integer default 0,Ac smallint unsigned default 0,MAc smallint unsigned default 0,Dc smallint unsigned default 0,DcMax smallint unsigned default 0,Mc smallint unsigned default 0,Sc smallint unsigned default 0,Speed smallint unsigned default 0,Hit smallint unsigned default 0,WalkSpd smallint unsigned default 0,AttackSpd smallint unsigned default 0,ForceValue integer unsigned default 0,ForceLevel tinyint unsigned default 0,Speciality integer default 0,SuperForceExp integer unsigned default 0,SuperForceLv smallint unsigned default 0);");
            // 0x5BDF50 len=1121 rc=-1  mir3.stditems
            Exec("Create table if not exists stditems (idx int auto_increment Primary key,AllowFlag smallint unsigned default 0,iname Char(15) binary not null Unique,Stdmode tinyint unsigned default 0,shape tinyint unsigned default 0,source tinyint default 0,OutLook tinyint default 0,Looks smallint unsigned default 0,weight smallint unsigned default 0,DuraMax smallint unsigned default 0,anicount smallint unsigned default 0,NeedConf smallint unsigned default 0,AC smallint unsigned default 0,MaxAc smallint unsigned default 0,MAC smallint unsigned default 0,MaxMAC smallint unsigned default 0,DC smallint unsigned default 0,MaxDC smallint unsigned default 0,MC smallint unsigned default 0,MaxMC smallint unsigned default 0,SC smallint unsigned default 0,MaxSc smallint unsigned default 0,need tinyint unsigned default 0,NeedLevel smallint unsigned default 0,AntiqueLv tinyInt default 0,wParam1 smallint unsigned default 0,wParam2 smallint unsigned default 0,intParam int default 0,itemScore smallint default 0,SuitEquipType smallint default 0,price integer default 0,ItemConf smallint(6) unsigned default 0,itemLevel int(11) default 0);");
            // 0x5BE3BC len=680 rc=-1  mir3.AntiqueItems
            Exec("Create table if not exists AntiqueItems(Idx int AUTO_INCREMENT PRIMARY KEY,AntiqueName char(15) not NULL UNIQUE,baseItemName char(15) not NULL,antiqueLv tinyInt not NULL,maxAntiqueLv tinyInt not NULL,mysteryCnt tinyInt not NULL,maxMysteryCnt tinyInt not NULL,abilName1 char(15)  default \"\",abilVal1  tinyInt   default 0,abilName2 char(15)  default \"\",abilVal2  tinyInt   default 0,abilName3 char(15)  default \"\",abilVal3  tinyInt   default 0,abilName4 char(15)  default \"\",abilVal4  tinyInt   default 0,specAbil1 char(15)  default \"\",specAbil2 char(15)  default \"\",specAbil3 char(15)  default \"\",specAbil4 char(15)  default \"\",steellv tinyInt default 0,veinslv tinyInt default 0);");
            // 0x5BE670 len=1037 rc=-1  mir3.fieldhero
            Exec("Create table if not exists fieldhero (name Char(15) binary not null Unique,BossLevel tinyint unsigned default 0,sex tinyint unsigned default 0,job tinyint unsigned default 0,Lvl smallint unsigned default 0,BodyLuck tinyint unsigned default 0,AddHitPoint tinyint unsigned default 0,Exp int unsigned default 0,DrinkDrug smallint unsigned default 0,Dress Char(14) binary not null,DressScatter int,Weapon char(14) binary not null,WeaponScatter int,Medal char(14) binary not null,MedalScatter int,Necklace char(14) binary not null,NecklaceScatter int,Helmet char(14) binary not null,HelmetScatter int,ArmringL char(14) binary not null,ArmringLScatter int,ArmringR char(14) binary not null,ArmringRScatter int,RingL char(14) binary not null,RingLScatter int,RingR char(14) binary not null,RingRScatter int,Bujuk char(14) binary not null,BujukScatter int,Belt char(14) binary not null,BeltScatter int,Boots char(14) binary not null,BootsScatter int,Charm char(14) binary not null,CharmScatter int,Mask char(14) binary not null,MaskScatter int);");
            // 0x5BEA88 len=931 rc=-1  mir3.forcemagic
            Exec("Create table if not exists forcemagic(ForceID int auto_increment Primary key,magkind tinyint unsigned default 0, MagicIdx smallint unsigned default 0,name Char(14) binary not null Unique,Effect tinyint unsigned default 0, Job tinyint unsigned default 0, Spell tinyint unsigned default 0,DefSpell tinyint unsigned default 0, Power smallint unsigned default 0, DefPower smallint unsigned default 0,PowerParam tinyint unsigned default 0, LastLv smallint unsigned default 0, NeedL1 smallint unsigned default 0,L1Train integer default 0,L1NeedStone integer default 0,NeedL2 smallint unsigned default 0,L2Train integer default 0,L2NeedStone integer default 0,NeedL3 smallint unsigned default 0,L3Train integer default 0, L3NeedStone integer default 0, NeedL4 smallint unsigned default 0,L4Train integer default 0, L4NeedStone integer default 0,NeedL5 smallint unsigned default 0,L5Train integer default 0, L5NeedStone integer default 0);");
            // 0x5BEE34 len=359 rc=-1  gamedata.ZongpaiBase
            Exec("Create table if not exists gamedata.ZongpaiBase ( Idx int auto_increment Primary key, MasterName Char(15) binary not null, MasterLevel smallint unsigned default 0, StudentExp int unsigned default 0, MasterExp int unsigned default 0, UpdateTime DateTime not null,Notice blob, unique key MasterName_Index(MasterName),Index Order_Index(MasterLevel, StudentExp));");
            // 0x5BEFA4 len=277 rc=-1  gamedata.ZongpaiRole
            Exec("Create table if not exists gamedata.ZongpaiRole (Idx int auto_increment Primary key, MasterName varchar(15) binary not null, RoleName varchar(20) binary not null, RolePrivilege int unsigned default 0, MaxMemberNum int default 0,unique key RoleName_Index(MasterName, RoleName));");
            // 0x5BF0C4 len=289 rc=-1  gamedata.ZongpaiMember
            Exec("Create table if not exists gamedata.ZongpaiMember (Idx int auto_increment Primary key, MasterName varchar(15) binary not null, MemberName varchar(15) binary not null, RoleName varchar(20) binary not null, unique key MemberName_Index(MemberName),Index RoleName_Index(MasterName, RoleName));");
            // 0x5BF1F0 len=1326 rc=-1  gamedata.mirparams
            Exec("CREATE TABLE IF NOT EXISTS gamedata.mirparams (idx int unsigned NOT NULL Primary Key auto_increment,ParamNo int NOT NULL unique,ParamName varchar(20) binary,g1 int(10) default -1,g2 int(10) default -1,g3 int(10) default -1,g4 int(10) default -1,g5 int(10) default -1,g6 int(10) default -1,g7 int(10) default -1,g8 int(10) default -1,g9 int(10) default -1,g10 int(10) default -1,g11 int(10) default -1,g12 int(10) default -1,g13 int(10) default -1,g14 int(10) default -1,g15 int(10) default -1,g16 int(10) default -1,g17 int(10) default -1,g18 int(10) default -1,g19 int(10) default -1,g20 int(10) default -1,g21 int(10) default -1,g22 int(10) default -1,g23 int(10) default -1,g24 int(10) default -1,g25 int(10) default -1,g26 int(10) default -1,g27 int(10) default -1,g28 int(10) default -1,g29 int(10) default -1,g30 int(10) default -1,g31 int(10) default -1,g32 int(10) default -1,g33 int(10) default -1,g34 int(10) default -1,g35 int(10) default -1,g36 int(10) default -1,g37 int(10) default -1,g38 int(10) default -1,g39 int(10) default -1,g40 int(10) default -1,g41 int(10) default -1,g42 int(10) default -1,g43 int(10) default -1,g44 int(10) default -1,g45 int(10) default -1,g46 int(10) default -1,g47 int(10) default -1,g48 int(10) default -1,g49 int(10) default -1,g50 int(10) default -1,Index ParamNo_Idx(ParamNo));");
            // 0x5BF728 len=151 rc=-1  mir3.user_storage
            Exec("Create table if not exists user_storage ( Idx int auto_increment Primary key, PTID Char(20) binary not null Unique, Data blob, index PTID_Index(PTID));");
            // 0x5BF818 len=782 rc=-1  guild.Castle
            Exec("CREATE TABLE IF NOT EXISTS Guild.Castle(  Guid    int unsigned NOT NULL default 0,  name    char(64) binary NOT NULL default '',  TotalGold int unsigned NOT NULL default 0,  TodayIncome int unsigned NOT NULL default 0,  WineCount int unsigned NOT NULL default 0,  OwnGuild  char(32) binary default '',  IncomeToday datetime NOT NULL default '0000-00-00 00:00:00',  changeDate datetime NOT NULL default '0000-00-00 00:00:00',  WarDate datetime NOT NULL default '0000-00-00 00:00:00',  Data blob,  ExtValue1  int unsigned NOT NULL default 0,  ExtValue2 int unsigned NOT NULL default 0,  ExtValue3 int unsigned NOT NULL default 0,  ExtValue4 int unsigned NOT NULL default 0,  ExtValue5 int unsigned NOT NULL default 0,  ExtValue6 int unsigned NOT NULL default 0,  PRIMARY KEY  (Guid));");
            // 0x5BFB30 len=947 rc=-1  guild.guild_list
            Exec("CREATE TABLE IF NOT EXISTS Guild.guild_list (  Idx int unsigned NOT NULL auto_increment,  Gname char(32) binary NOT NULL default '',  MaxUser int unsigned NOT NULL default '0',  GLevel int unsigned NOT NULL default '0',  CurExp int unsigned NOT NULL default '0',  UserAddExp int unsigned NOT NULL default '0',  Water int unsigned NOT NULL default '0',  StarPoint int unsigned NOT NULL default '0',  StarGrantTime int unsigned NOT NULL default '0',  GuildFlag tinyint(1) unsigned NOT NULL default '0',  StarOwner  char(15) binary default '',  CreateTime datetime NOT NULL default '0000-00-00 00:00:00',  Notice blob,  ExtValue1  int unsigned NOT NULL default '0',  ExtValue2 int unsigned NOT NULL default '0',  ExtValue3 int unsigned NOT NULL default '0',  ExtValue4 int unsigned NOT NULL default '0',  ExtValue5 int unsigned NOT NULL default '0',  ExtValue6 int unsigned NOT NULL default '0',  PRIMARY KEY  (Idx),  UNIQUE KEY gname_index (Gname));");
            // 0x5BFEEC len=462 rc=-1  guild.guild_rank
            Exec("CREATE TABLE IF NOT EXISTS Guild.guild_rank (  Idx int unsigned NOT NULL auto_increment,  Gname char(32) binary NOT NULL default '',  RankID int unsigned NOT NULL default '0',  RankName char(16) binary NOT NULL default '0',  MaxUser int unsigned NOT NULL default '0',  CreateTime datetime NOT NULL default '0000-00-00 00:00:00',  PRIMARY KEY  (Idx),  UNIQUE KEY rankid_index (Gname,RankID),  UNIQUE KEY rankname_index (Gname,RankName),  KEY gname_index (Gname));");
            // 0x5C00C4 len=690 rc=-1  guild.guild_user
            Exec("CREATE TABLE IF NOT EXISTS Guild.guild_user (  Idx int unsigned NOT NULL auto_increment,  Gname char(32) binary NOT NULL default '',  CharName char(16) binary NOT NULL default '',  RankID int unsigned NOT NULL default 0,  ConferRight int unsigned NOT NULL default 0,  Contribution int unsigned NOT NULL default 0,  JoinDate datetime NOT NULL default '0000-00-00 00:00:00',  job tinyint unsigned default 0,  sex tinyint unsigned default 0,  level smallint unsigned default 0,  modifydate datetime NOT NULL default '0000-00-00 00:00:00',  sfLevel smallint unsigned default 0,  PRIMARY KEY  (Idx),  UNIQUE KEY user_index (CharName),  KEY rankid_index (Gname,RankID),  KEY gname_index (Gname));");
            // 0x5C0380 len=545 rc=-1  guild.guild_relation
            Exec("CREATE TABLE IF NOT EXISTS Guild.guild_relation(  Idx int unsigned NOT NULL auto_increment,  SrcGname char(32) binary NOT NULL default '',  DstGname char(32) binary NOT NULL default '',  Relationid tinyint(3) unsigned NOT NULL default '0',  ExtValue1 int unsigned NOT NULL default '0',  ExtValue2 int unsigned NOT NULL default '0',  CreateTime datetime NOT NULL default '0000-00-00 00:00:00',  PRIMARY KEY  (Idx),  UNIQUE KEY pair_index (SrcGname,DstGname,Relationid),  KEY src_index (SrcGname,RelationID),  KEY dst_index (DstGname,RelationID));");
            // 0x5C05AC len=331 rc=-1  guild.guild_log
            Exec("CREATE TABLE IF NOT EXISTS Guild.guild_log (  Idx int unsigned NOT NULL auto_increment,  Gname char(32) binary NOT NULL default '',  LogType int unsigned NOT NULL default '0',  MsgText char(200) binary NOT NULL default '',  CreateTime datetime NOT NULL default '0000-00-00 00:00:00',  PRIMARY KEY  (Idx),  KEY gname_index (Gname));");
            // 0x5C07F4 len=764 rc=-1  mir3.superForce
            Exec("CREATE TABLE IF NOT EXISTS mir3.superForce ( Level int not null primary key, NeedExp int not null, AC1 int not null, MaxAc1 int not null, MAC1 int not null, MaxMAC1 int not null, MainPower1 int not null, MaxMainPower1 int not null, AC2 int not null, MaxAC2 int not null, MAC2 int not null, MaxMAC2 int not null, MainPower2 int not null, MaxMainPower2 int not null, AC3 int not null, MaxAC3 int not null, MAC3 int not null, MaxMAC3 int not null, MainPower3 int not null, MaxMainPower3 int not null, AC4 int not null, MaxAC4 int not null, MAC4 int not null, MaxMAC4 int not null, MainPower4 int not null, MaxMainPower4 int not null, AC5 int not null, MaxAC5 int not null, MAC5 int not null, MaxMAC5 int not null, MainPower5 int not null, MaxMainPower5 int not null);");
            // 0x5C0AFC len=494 rc=-1  mir3.superSkill
            Exec("CREATE TABLE IF NOT EXISTS mir3.superSkill ( SkillId int not null Primary Key, SkillName char(20) not null, baseParam int not null, levelParam int not null, upItemParam int not null, needLv1 int not null, needLv2 int not null, needLv3 int not null, needLv4 int not null, needLv5 int not null, needLv6 int not null, needLv7 int not null, needLv8 int not null, needLv9 int not null, effectType int not null, effect1 int not null, effect2 int not null, effect3 int not null, effect4 int not null);");
            // 0x5C0CF4 len=421 rc=-1  gamedata.TransferAreaScoreSendRecord
            Exec("Create table if not exists gamedata.TransferAreaScoreSendRecord ( Idx int auto_increment Primary key, TimeStamp DateTime not null,CharName Char(15) binary not null, ZoneId smallint unsigned default 0, GroupId smallint unsigned default 0, ScoreType smallint unsigned default 0, Score smallint unsigned default 0, State smallint unsigned default 0, unique key Record_Index(TimeStamp, CharName, ZoneId, GroupId, ScoreType));");
            // 0x5C0EA4 len=226 rc=-1  gamedata.TransferAreaScore
            Exec("Create table if not exists gamedata.TransferAreaScore ( Idx int auto_increment Primary key, CharName Char(15) binary not null, Score1 int default 0, Score2 int default 0, Score3 int default 0, Unique Key Char_Index(CharName));");
        }

        // ─── CREATE TABLE ... LIKE — 6 备份克隆 ──────────────────────────
        private void ProvisionBackupClones()
        {
            // 0x5B341C len=57 rc=-1
            Exec("Create Table mir3_backup.hero_index like mir3.hero_index;");
            // 0x5B350C len=55 rc=-1
            Exec("Create Table mir3_backup.hero_data like mir3.hero_data;");
            // 0x5B3638 len=61 rc=-1
            Exec("Create Table mir3_backup.user_storage like mir3.user_storage;");
            // 0x5B3734 len=61 rc=-1
            Exec("Create Table mir3_backup.dominatorpet like mir3.dominatorpet;");
            // 0x5B382C len=57 rc=-1
            Exec("Create Table mir3_backup.user_index like mir3.user_index;");
            // 0x5B391C len=55 rc=-1
            Exec("Create Table mir3_backup.user_data like mir3.user_data;");
            // 0x5B354C len=51 rc=-1  独立 MAX_ROWS 调整（无需探测，幂等表选项）
            Exec("Alter Table mir3_backup.hero_data Max_ROWS=20000000000;");
            // 0x5B395C len=55 rc=-1  独立 MAX_ROWS 调整（无需探测，幂等表选项）
            Exec("Alter Table mir3_backup.user_data Max_ROWS=20000000000;");
        }

        // ─── 列迁移（SHOW COLUMNS LIKE 探测 + ALTER TABLE ADD） ──────────
        // 原版在 MySQL 5.1.55 上不支持 ADD COLUMN IF NOT EXISTS，
        // 故按原版「先探测再加列」模式实现（xref 来自 0x5AB200 和 VMP 虚拟化块）。
        private void RunColumnMigrations()
        {
            MigrateUserDataScriptData();
            MigrateHeroDataDynData();
            MigrateUserIndexAdminLevel();
            MigrateUserIndexForceLv();
            MigrateUserIndexSfLevel();
            MigrateUserIndexSrcZoneId();
            MigrateUserIndexDesZoneId();
            MigrateUserIndexIsTransLock();
            MigrateUserIndexTransferModal();
            MigrateUserIndexLvChangeTime();
            MigrateUserIndexUserId();
            MigrateHeroIndexForceLv();
            MigrateHeroIndexSfLevel();
            MigrateHeroIndexSrcZoneId();
            MigrateHeroIndexHeroId();
            MigrateHeroIndexLvChangeTime();
            MigrateMonsterColumns();
            MigrateGuildUserSfLevel();
            // BLOCKED: forcemagic.L5NeedStone / forcemagic.LastLv 的 ALTER TABLE 是
            // 运行时拼接字符串（0x5B3E9C / 0x5C19D8 只保留了 "Alter Table " 前缀片段），
            // 无法从静态字面量还原完整 SQL，故跳过。
        }

        // ── user_data: ScriptData ─────────────────────────────────────────
        private void MigrateUserDataScriptData()
        {
            // 0x5AB538 len=44 rc=-1: Show Fields From user_data like "ScriptData"
            if (!ProbeColumn("Show Fields From user_data like \"ScriptData\""))
                // 0x5AB590 len=42 rc=-1
                Exec("alter table user_data Add ScriptData Blob;");
            // 0x5AB5C4 len=56 rc=-1
            if (!ProbeColumn("Show Fields From mir3_backup.user_data like \"ScriptData\""))
                // 0x5AB608 len=54 rc=-1
                Exec("alter table mir3_backup.user_data Add ScriptData Blob;");
        }

        // ── hero_data: dynData ────────────────────────────────────────────
        private void MigrateHeroDataDynData()
        {
            // 0x5AB648 len=42 rc=-1: Show Fields From hero_data like "dynData";
            if (!ProbeColumn("Show Fields From hero_data like \"dynData\";"))
                // 0x5AB698 len=39 rc=-1
                Exec("Alter table hero_data Add dynData Blob;");
            // 0x5AB6C8 len=54 rc=-1
            if (!ProbeColumn("Show Fields From mir3_backup.hero_data like \"dynData\";"))
                // 0x5AB708 len=51 rc=-1
                Exec("Alter table mir3_backup.hero_data Add dynData Blob;");
            MigrateHeroDataNameLayout();
        }

        /// <summary>
        /// C#-ONLY 世代门：原生 hero_data 无 layout 列；DDL 与一次性迁移见
        /// docs/dbsvr_hero_name_layout_migration_20260814.sql（运维执行，启动不自动 swap）。
        /// </summary>
        private void MigrateHeroDataNameLayout()
        {
            if (!ProbeColumn("Show Fields From hero_data like \"NameLayout\";"))
                Exec("ALTER TABLE hero_data ADD COLUMN NameLayout TINYINT NOT NULL DEFAULT 0 "
                     + "COMMENT '0=unknown 1=csharp-swapped 2=native-correct';");
            if (!ProbeColumn("Show Fields From mir3_backup.hero_data like \"NameLayout\";"))
                Exec("ALTER TABLE mir3_backup.hero_data ADD COLUMN NameLayout TINYINT NOT NULL DEFAULT 0;");
        }

        // ── user_index: AdminLevel ────────────────────────────────────────
        private void MigrateUserIndexAdminLevel()
        {
            // 0x5AB390 len=45 rc=-1: Show Fields From user_index like "AdminLevel"
            if (!ProbeColumn("Show Fields From user_index like \"AdminLevel\""))
                // 0x5AB3E8 len=117 rc=-1
                Exec("alter table user_index Add AdminLevel tinyint unsigned default 0, Add Index Admin_Level_Sort(AdminLevel, level, exp);");
            // 0x5AB468 len=57 rc=-1
            if (!ProbeColumn("Show Fields From mir3_backup.user_index like \"AdminLevel\""))
                // 0x5AB4AC len=129 rc=-1
                Exec("alter table mir3_backup.user_index Add AdminLevel tinyint unsigned default 0, Add Index Admin_Level_Sort(AdminLevel, level, exp);");
        }

        // ── user_index: ForceLv / ForceExp / FightPoints ──────────────────
        private void MigrateUserIndexForceLv()
        {
            // 0x5BBDCC len=44 rc=-1: show columns from user_index like "ForceLv";
            if (!ProbeColumn("show columns from user_index like \"ForceLv\";"))
                // 0x5BBE04 len=161 rc=-1
                Exec("Alter table user_index add column ForceLv smallint unsigned default 0, add column ForceExp int unsigned default 0, add column FightPoints int unsigned default 0;");
            // 0x5BBEB0 len=56 rc=-1
            if (!ProbeColumn("show columns from mir3_backup.user_index like \"ForceLv\";"))
                // 0x5BBEF4 len=173 rc=-1
                Exec("Alter table mir3_backup.user_index add column ForceLv smallint unsigned default 0, add column ForceExp int unsigned default 0, add column FightPoints int unsigned default 0;");
        }

        // ── user_index: sfLevel ───────────────────────────────────────────
        private void MigrateUserIndexSfLevel()
        {
            // 0x5BBFAC len=44 rc=-1: show columns from user_index like "sfLevel";
            if (!ProbeColumn("show columns from user_index like \"sfLevel\";"))
                // 0x5BBFE4 len=56 rc=-1
                Exec("Alter table user_index add column sfLevel int default 0;");
            // 0x5BC028 len=56 rc=-1
            if (!ProbeColumn("show columns from mir3_backup.user_index like \"sfLevel\";"))
                // 0x5BC06C len=68 rc=-1
                Exec("Alter table mir3_backup.user_index add column sfLevel int default 0;");
        }

        // ── user_index: SrcZoneId / SrcGroupId / SrcCharName ─────────────
        private void MigrateUserIndexSrcZoneId()
        {
            // 0x5BC0BC len=46 rc=-1: show columns from user_index like "SrcZoneId";
            if (!ProbeColumn("show columns from user_index like \"SrcZoneId\";"))
                // 0x5BC0F4 len=174 rc=-1
                Exec("Alter table user_index add column SrcZoneId smallint unsigned default 0, add column SrcGroupId smallint unsigned default 0, add column SrcCharName Char(15) binary default \"\";");
            // 0x5BC1AC len=58 rc=-1
            if (!ProbeColumn("show columns from mir3_backup.user_index like \"SrcZoneId\";"))
                // 0x5BC1F0 len=186 rc=-1
                Exec("Alter table mir3_backup.user_index add column SrcZoneId smallint unsigned default 0, add column SrcGroupId smallint unsigned default 0, add column SrcCharName Char(15) binary default \"\";");
        }

        // ── user_index: DesZoneId / DesGroupId ───────────────────────────
        private void MigrateUserIndexDesZoneId()
        {
            // 0x5BC2B4 len=46 rc=-1: show columns from user_index like "DesZoneId";
            if (!ProbeColumn("show columns from user_index like \"DesZoneId\";"))
                // 0x5BC2EC len=122 rc=-1
                Exec("Alter table user_index add column DesZoneId smallint unsigned default 0,add column DesGroupId smallint unsigned default 0;");
        }

        // ── user_index: IsTransLock ───────────────────────────────────────
        private void MigrateUserIndexIsTransLock()
        {
            // 0x5BC370 len=48 rc=-1: show columns from user_index like "IsTransLock";
            if (!ProbeColumn("show columns from user_index like \"IsTransLock\";"))
                // 0x5BC3AC len=58 rc=-1
                Exec("Alter table user_index add IsTransLock smallint default 0;");
            // 0x5BC3F0 len=60 rc=-1
            if (!ProbeColumn("show columns from mir3_backup.user_index like \"IsTransLock\";"))
                // 0x5BC438 len=70 rc=-1
                Exec("Alter table mir3_backup.user_index add IsTransLock smallInt default 0;");
        }

        // ── user_index: TransferModal ─────────────────────────────────────
        private void MigrateUserIndexTransferModal()
        {
            // 0x5BC488 len=50 rc=-1: show columns from user_index like "TransferModal";
            if (!ProbeColumn("show columns from user_index like \"TransferModal\";"))
                // 0x5BC4C4 len=60 rc=-1
                Exec("Alter table user_index add TransferModal smallint default 0;");
            // 0x5BC50C len=62 rc=-1
            if (!ProbeColumn("show columns from mir3_backup.user_index like \"TransferModal\";"))
                // 0x5BC554 len=72 rc=-1
                Exec("Alter table mir3_backup.user_index add TransferModal smallInt default 0;");
        }

        // ── user_index: lvChangeTime ──────────────────────────────────────
        private void MigrateUserIndexLvChangeTime()
        {
            // 0x5BC5A8 len=49 rc=-1: show columns from user_index like "lvChangeTime";
            if (!ProbeColumn("show columns from user_index like \"lvChangeTime\";"))
                // 0x5BC5E4 len=68 rc=-1
                Exec("Alter table user_index add lvChangeTime DateTime default \"2100-1-1\";");
            // 0x5BC634 len=61 rc=-1
            if (!ProbeColumn("show columns from mir3_backup.user_index like \"lvChangeTime\";"))
                // 0x5BC67C len=80 rc=-1
                Exec("Alter table mir3_backup.user_index add lvChangeTime DateTime default \"2100-1-1\";");
        }

        // ── user_index: UserId (含 CREATE INDEX) ──────────────────────────
        private void MigrateUserIndexUserId()
        {
            // 0x5BC6D8 len=43 rc=-1: show columns from user_index like "UserId";
            if (!ProbeColumn("show columns from user_index like \"UserId\";"))
                // 0x5BC70C len=107 rc=-1
                // 注意：原版字面量把 ALTER 和 CREATE INDEX 合并在同一字符串，分号分隔
                Exec("Alter table user_index add column UserId bigInt default 0;Create index userId_index on user_index (userId);");
            // 0x5BC7B4 len=55 rc=-1
            if (!ProbeColumn("show columns from mir3_backup.user_index like \"UserId\";"))
                // 0x5BC7F4 len=70 rc=-1
                Exec("Alter table mir3_backup.user_index add column UserId bigInt default 0;");
        }

        // ── hero_index: ForceLv / ForceExp ────────────────────────────────
        private void MigrateHeroIndexForceLv()
        {
            // 0x5BC844 len=44 rc=-1: show columns from hero_index like "ForceLv";
            if (!ProbeColumn("show columns from hero_index like \"ForceLv\";"))
                // 0x5BC87C len=114 rc=-1
                Exec("Alter table hero_index add column ForceLv smallint unsigned default 0, add column ForceExp int unsigned default 0;");
            // 0x5BC8F8 len=56 rc=-1
            if (!ProbeColumn("show columns from mir3_backup.hero_index like \"ForceLv\";"))
                // 0x5BC93C len=126 rc=-1
                Exec("Alter table mir3_backup.hero_index add column ForceLv smallint unsigned default 0, add column ForceExp int unsigned default 0;");
        }

        // ── hero_index: sfLevel ───────────────────────────────────────────
        private void MigrateHeroIndexSfLevel()
        {
            // 0x5BC9C4 len=44 rc=-1: show columns from hero_index like "sfLevel";
            if (!ProbeColumn("show columns from hero_index like \"sfLevel\";"))
                // 0x5BC9FC len=56 rc=-1
                Exec("Alter table hero_index add column sfLevel int default 0;");
            // 0x5BCA40 len=56 rc=-1
            if (!ProbeColumn("show columns from mir3_backup.hero_index like \"sfLevel\";"))
                // 0x5BCA84 len=68 rc=-1
                Exec("Alter table mir3_backup.hero_index add column sfLevel int default 0;");
        }

        // ── hero_index: SrcZoneId / SrcGroupId / SrcHeroName ─────────────
        private void MigrateHeroIndexSrcZoneId()
        {
            // 0x5BCAD4 len=46 rc=-1: show columns from hero_index like "SrcZoneId";
            if (!ProbeColumn("show columns from hero_index like \"SrcZoneId\";"))
                // 0x5BCB0C len=174 rc=-1
                Exec("Alter table hero_index add column SrcZoneId smallint unsigned default 0, add column SrcGroupId smallint unsigned default 0, add column SrcHeroName Char(15) binary default \"\";");
            // 0x5BCBC4 len=58 rc=-1
            if (!ProbeColumn("show columns from mir3_backup.hero_index like \"SrcZoneId\";"))
                // 0x5BCC08 len=186 rc=-1
                Exec("Alter table mir3_backup.hero_index add column SrcZoneId smallint unsigned default 0, add column SrcGroupId smallint unsigned default 0, add column SrcHeroName Char(15) binary default \"\";");
        }

        // ── hero_index: HeroId (含 CREATE INDEX) ─────────────────────────
        private void MigrateHeroIndexHeroId()
        {
            // 0x5BCCCC len=43 rc=-1: show columns from hero_index like "HeroId";
            if (!ProbeColumn("show columns from hero_index like \"HeroId\";"))
                // 0x5BCD00 len=107 rc=-1
                Exec("Alter table hero_index add column HeroId bigInt default 0;Create index HeroId_index on Hero_index (HeroId);");
            // 0x5BCDA8 len=55 rc=-1
            if (!ProbeColumn("show columns from mir3_backup.Hero_index like \"HeroId\";"))
                // 0x5BCDE8 len=70 rc=-1
                Exec("Alter table mir3_backup.Hero_index add column HeroId bigInt default 0;");
        }

        // ── hero_index: lvChangeTime ──────────────────────────────────────
        private void MigrateHeroIndexLvChangeTime()
        {
            // 0x5BCE38 len=49 rc=-1: show columns from Hero_index like "lvChangeTime";
            if (!ProbeColumn("show columns from Hero_index like \"lvChangeTime\";"))
                // 0x5BCE74 len=68 rc=-1
                Exec("Alter table Hero_index add lvChangeTime DateTime default \"2100-1-1\";");
            // 0x5BCEC4 len=61 rc=-1
            if (!ProbeColumn("show columns from mir3_backup.Hero_index like \"lvChangeTime\";"))
                // 0x5BCF0C len=80 rc=-1
                Exec("Alter table mir3_backup.Hero_index add lvChangeTime DateTime default \"2100-1-1\";");
        }

        // ── monster: JobFastness / JobFastnessVal / SuperPower ────────────
        private void MigrateMonsterColumns()
        {
            // 0x5BDDE0 len=45 rc=-1: show columns from monster like "JobFastness";
            if (!ProbeColumn("show columns from monster like \"JobFastness\";"))
                // 0x5BDE18 len=54 rc=-1
                Exec("Alter table monster add JobFastness integer default 0;");
            // 0x5BDE58 len=48 rc=-1: show columns from monster like "JobFastnessVal";
            if (!ProbeColumn("show columns from monster like \"JobFastnessVal\";"))
                // 0x5BDE94 len=57 rc=-1
                Exec("Alter table monster add JobFastnessVal integer default 0;");
            // 0x5BDED8 len=44 rc=-1: show columns from monster like "SuperPower";
            if (!ProbeColumn("show columns from monster like \"SuperPower\";"))
                // 0x5BDF10 len=53 rc=-1
                Exec("Alter table monster add SuperPower integer default 0;");
        }

        // ── guild.guild_user: sfLevel ─────────────────────────────────────
        private void MigrateGuildUserSfLevel()
        {
            // 0x5C0768 len=50 rc=-1: show columns from Guild.guild_user like "sfLevel";
            if (!ProbeColumn("show columns from Guild.guild_user like \"sfLevel\";"))
                // 0x5C07A4 len=69 rc=-1
                Exec("Alter table Guild.guild_user add sfLevel smallint unsigned default 0;");
        }

        // ─── helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// SHOW COLUMNS / SHOW FIELDS 探测：返回 true 表示列已存在，false 表示不存在。
        /// </summary>
        private bool ProbeColumn(string showSql)
        {
            using var conn = new MySqlConnection(_conn);
            conn.Open();
            using var cmd = new MySqlCommand(showSql, conn);
            using var reader = cmd.ExecuteReader();
            return reader.HasRows;
        }

        /// <summary>
        /// 执行单条 DDL 语句（或以分号分隔的两条）。忽略返回值。
        /// </summary>
        private void Exec(string sql)
        {
            using var conn = new MySqlConnection(_conn);
            conn.Open();
            // 部分原版字面量把两条语句写在同一字符串（分号分隔），需开启多语句模式。
            // 连接串须含 AllowUserVariables=true;Allow User Variables=true 或直接追加。
            foreach (var stmt in sql.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var s = stmt.Trim();
                if (s.Length == 0) continue;
                using var cmd = new MySqlCommand(s, conn);
                cmd.ExecuteNonQuery();
            }
        }
    }
}



