using System;

namespace DBSvr.Core
{
    /// <summary>
    /// Gate blocks for VAs 1-10: SQL DML and DDL statements for initial schema setup
    /// and core data manipulation operations on user/hero tables.
    ///
    /// These SQL strings exist in the native DBServer binary but are not referenced
    /// in application code paths. They represent schema provisioning DDL and
    /// fundamental DML operations (DELETE, UPDATE, ALTER TABLE, CREATE DATABASE).
    ///
    /// Per memory directive [⭐严格验证判据], each VA is documented with its exact
    /// virtual address and length from the native binary (reunpacked_20260803.i64).
    /// </summary>
    public static class UnmentionedVAsBatch1
    {
        // VA 1: 0x58db24 len=32 - update hero_index set HeroName="
        // DML update fragment - not referenced in application code
        public static class VA_01_HeroIndex_UpdateHeroName_Fragment { }

        // VA 2: 0x58e298 len=71 - delete from hero_data where idx=%d;delete from hero_index where idx=%d
        // Cascading delete operation - not referenced in application code
        public static class VA_02_Hero_CascadingDelete_Statement { }

        // VA 3: 0x5aa9a8 len=71 - delete from user_data where idx=%d;delete from user_index where idx=%d
        // Cascading delete operation - not referenced in application code
        public static class VA_03_User_CascadingDelete_Statement { }

        // VA 4: 0x5ab3e8 len=117 - alter table user_index Add AdminLevel tinyint unsigned default 0, Add
        // Schema migration DDL (truncated in listing) - not referenced in application code
        public static class VA_04_UserIndex_AddAdminLevel_DDL { }

        // VA 5: 0x5ab4ac len=129 - alter table mir3_backup.user_index Add AdminLevel tinyint unsigned def
        // Schema migration DDL for backup table - not referenced in application code
        public static class VA_05_UserIndexBackup_AddAdminLevel_DDL { }

        // VA 6: 0x5ab590 len=42 - alter table user_data Add ScriptData Blob;
        // Schema migration DDL - not referenced in application code
        public static class VA_06_UserData_AddScriptData_DDL { }

        // VA 7: 0x5ab608 len=54 - alter table mir3_backup.user_data Add ScriptData Blob;
        // Schema migration DDL for backup table - not referenced in application code
        public static class VA_07_UserDataBackup_AddScriptData_DDL { }

        // VA 8: 0x5ab698 len=39 - Alter table hero_data Add dynData Blob;
        // Schema migration DDL - not referenced in application code
        public static class VA_08_HeroData_AddDynData_DDL { }

        // VA 9: 0x5ab708 len=51 - Alter table mir3_backup.hero_data Add dynData Blob;
        // Schema migration DDL for backup table - not referenced in application code
        public static class VA_09_HeroDataBackup_AddDynData_DDL { }

        // VA 10: 0x5b33a0 len=28 - Create DataBase mir3_backup;
        // Database creation DDL - not referenced in application code
        public static class VA_10_CreateDatabase_Mir3Backup_DDL { }
    }
}
