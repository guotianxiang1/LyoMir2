using System;

namespace DBSvr.Core
{
    /// <summary>
    /// Gate blocks for VAs 11-20: Database Backup SQL Statements.
    /// These SQL statements create and populate the mir3_backup database.
    ///
    /// All statements are ✓ IMPLEMENTED in BackupService.cs:HotBackupToMir3Backup().
    /// Native routine: 0x5B2FAB..0x5B3352, literals: 0x5B33A0..0x5B39B5.
    ///
    /// Per memory directive [⭐严格验证判据], each VA is documented with its exact
    /// virtual address and length from the native binary (reunpacked_20260803.i64).
    /// </summary>
    public static class UnmentionedVAsBatch2
    {
        // VA 11: 0x5b341c len=57 - Create Table mir3_backup.hero_index like mir3.hero_index;
        // ✓ IMPLEMENTED in BackupService.cs:64-66
        // Native exec site: 0x5B3053 (first in table creation loop)
        public static class VA_11_BackupHeroIndex_Create
        {
            // C# impl: BackupService.cs:64-66
            //   $"CREATE TABLE IF NOT EXISTS mir3_backup.{table} LIKE mir3.{table}"
            // Divergence: Added "IF NOT EXISTS" for idempotency (native relies on swallowed exceptions)
        }

        // VA 12: 0x5b350c len=55 - Create Table mir3_backup.hero_data like mir3.hero_data;
        // ✓ IMPLEMENTED in BackupService.cs:64-66
        // Same loop as VA_11, hero_data table (tables array line 46)
        public static class VA_12_BackupHeroData_Create { }

        // VA 13: 0x5b354c len=55 - Alter Table mir3_backup.hero_data Max_ROWS=20000000000;
        // ✓ IMPLEMENTED in BackupService.cs:73-75
        // Applied only to hero_data and user_data (needMaxRows=true)
        public static class VA_13_BackupHeroData_MaxRows
        {
            // C# impl: BackupService.cs:73-75
            //   $"ALTER TABLE mir3_backup.{table} MAX_ROWS=20000000000"
            // Purpose: Prevents "table is full" error during bulk insert on large tables
        }

        // VA 14: 0x5b3638 len=61 - Create Table mir3_backup.user_storage like mir3.user_storage;
        // ✓ IMPLEMENTED in BackupService.cs:64-66
        // user_storage table (tables array line 47)
        public static class VA_14_BackupUserStorage_Create { }

        // VA 15: 0x5b3734 len=61 - Create Table mir3_backup.dominatorpet like mir3.dominatorpet;
        // ✓ IMPLEMENTED in BackupService.cs:64-66
        // dominatorpet table (tables array line 48)
        public static class VA_15_BackupDominatorPet_Create { }

        // VA 16: 0x5b382c len=57 - Create Table mir3_backup.user_index like mir3.user_index;
        // ✓ IMPLEMENTED in BackupService.cs:64-66
        // user_index table (tables array line 49)
        public static class VA_16_BackupUserIndex_Create { }

        // VA 17: 0x5b3870 len=78 - Insert LOW_PRIORITY Into mir3_backup.user_index select * from mir3.user_index;
        // ✓ IMPLEMENTED in BackupService.cs:79-81
        // Native exec site: 0x5B3460 (bulk copy after table creation)
        public static class VA_17_BackupUserIndex_Copy
        {
            // C# impl: BackupService.cs:79-81
            //   $"INSERT LOW_PRIORITY INTO mir3_backup.{table} SELECT * FROM mir3.{table}"
            // LOW_PRIORITY: Non-blocking insert, allows concurrent reads during backup
        }

        // VA 18: 0x5b391c len=55 - Create Table mir3_backup.user_data like mir3.user_data;
        // ✓ IMPLEMENTED in BackupService.cs:64-66
        // user_data table (tables array line 50)
        public static class VA_18_BackupUserData_Create { }

        // VA 19: 0x5b395c len=55 - Alter Table mir3_backup.user_data Max_ROWS=20000000000;
        // ✓ IMPLEMENTED in BackupService.cs:73-75
        // user_data is the second large table requiring MAX_ROWS setting
        public static class VA_19_BackupUserData_MaxRows { }

        // VA 20: 0x5b399c len=76 - Insert LOW_PRIORITY Into mir3_backup.user_data select * from mir3.user_data;
        // ✓ IMPLEMENTED in BackupService.cs:79-81
        // user_data bulk copy, same pattern as VA_17
        public static class VA_20_BackupUserData_Copy { }
    }
}
