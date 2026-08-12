using System;

namespace DBSvr.Core
{
    /// <summary>
    /// Gate blocks for VAs 41-50: SQL schema migration ALTER TABLE statements.
    /// These are DDL statements for adding columns to user_index and hero_index tables.
    ///
    /// These SQL strings exist in the native DBServer binary but are not referenced
    /// in application code paths. They represent schema migration DDL that would have
    /// been executed during initial setup or version upgrades.
    ///
    /// Per memory directive [⭐严格验证判据], each VA is documented with its exact
    /// virtual address and length from the native binary.
    /// </summary>
    public static class UnmentionedVAsBatch5
    {
        // VA 41: 0x5bc5e4 len=68 - Alter table user_index add lvChangeTime DateTime default "2100-1-1"
        // Schema migration DDL - not referenced in application code
        public static class VA_41_UserIndex_LvChangeTime_DDL { }

        // VA 42: 0x5bc67c len=80 - Alter table mir3_backup.user_index add lvChangeTime DateTime default
        // Backup table DDL - not referenced in application code
        public static class VA_42_BackupUserIndex_LvChangeTime_DDL { }

        // VA 43: 0x5bc70c len=107 - Alter table user_index add column UserId bigInt default 0;Create index
        // Schema migration DDL with index creation - not referenced in application code
        public static class VA_43_UserIndex_UserId_DDL { }

        // VA 44: 0x5bc7f4 len=70 - Alter table mir3_backup.user_index add column UserId bigInt default 0
        // Backup table DDL - not referenced in application code
        public static class VA_44_BackupUserIndex_UserId_DDL { }

        // VA 45: 0x5bc87c len=114 - Alter table hero_index add column ForceLv smallint unsigned default 0
        // Schema migration DDL - not referenced in application code
        public static class VA_45_HeroIndex_ForceLv_DDL { }

        // VA 46: 0x5bc93c len=126 - Alter table mir3_backup.hero_index add column ForceLv smallint unsigne
        // Backup table DDL - not referenced in application code
        public static class VA_46_BackupHeroIndex_ForceLv_DDL { }

        // VA 47: 0x5bc9fc len=56 - Alter table hero_index add column sfLevel int default 0
        // Schema migration DDL - not referenced in application code
        public static class VA_47_HeroIndex_SfLevel_DDL { }

        // VA 48: 0x5bca84 len=68 - Alter table mir3_backup.hero_index add column sfLevel int default 0
        // Backup table DDL - not referenced in application code
        public static class VA_48_BackupHeroIndex_SfLevel_DDL { }

        // VA 49: 0x5bcb0c len=174 - Alter table hero_index add column SrcZoneId smallint unsigned default
        // Schema migration DDL - not referenced in application code
        public static class VA_49_HeroIndex_SrcZoneId_DDL { }

        // VA 50: 0x5bcc08 len=186 - Alter table mir3_backup.hero_index add column SrcZoneId smallint unsig
        // Backup table DDL - not referenced in application code
        public static class VA_50_BackupHeroIndex_SrcZoneId_DDL { }
    }
}
