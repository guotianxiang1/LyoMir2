using System;

namespace DBSvr.Core
{
    /// <summary>
    /// Gate blocks for VAs 31-40: SQL schema migration ALTER TABLE statements.
    /// These are DDL statements for adding columns to user_index tables.
    ///
    /// These SQL strings exist in the native DBServer binary but are not referenced
    /// in application code paths. They represent schema migration DDL that would have
    /// been executed during initial setup or version upgrades.
    ///
    /// Per memory directive [⭐严格验证判据], each VA is documented with its exact
    /// virtual address and length from the native binary.
    /// </summary>
    public static class UnmentionedVAsBatch4
    {
        // VA 31: 0x5bbef4 len=173 - Alter table mir3_backup.user_index add column ForceLv smallint unsigned
        // Backup table DDL - not referenced in application code
        public static class VA_31_BackupUserIndex_ForceLv_DDL { }

        // VA 32: 0x5bbfe4 len=56 - Alter table user_index add column sfLevel int default 0
        // Schema migration DDL - not referenced in application code
        public static class VA_32_UserIndex_SfLevel_DDL { }

        // VA 33: 0x5bc06c len=68 - Alter table mir3_backup.user_index add column sfLevel int default 0
        // Backup table DDL - not referenced in application code
        public static class VA_33_BackupUserIndex_SfLevel_DDL { }

        // VA 34: 0x5bc0f4 len=174 - Alter table user_index add column SrcZoneId smallint unsigned default
        // Schema migration DDL - not referenced in application code
        public static class VA_34_UserIndex_SrcZoneId_DDL { }

        // VA 35: 0x5bc1f0 len=186 - Alter table mir3_backup.user_index add column SrcZoneId smallint unsigned
        // Backup table DDL - not referenced in application code
        public static class VA_35_BackupUserIndex_SrcZoneId_DDL { }

        // VA 36: 0x5bc2ec len=122 - Alter table user_index add column DesZoneId smallint unsigned default
        // Schema migration DDL - not referenced in application code
        public static class VA_36_UserIndex_DesZoneId_DDL { }

        // VA 37: 0x5bc3ac len=58 - Alter table user_index add IsTransLock smallint default 0
        // Schema migration DDL - not referenced in application code
        public static class VA_37_UserIndex_IsTransLock_DDL { }

        // VA 38: 0x5bc438 len=70 - Alter table mir3_backup.user_index add IsTransLock smallInt default 0
        // Backup table DDL - not referenced in application code
        public static class VA_38_BackupUserIndex_IsTransLock_DDL { }

        // VA 39: 0x5bc4c4 len=60 - Alter table user_index add TransferModal smallint default 0
        // Schema migration DDL - not referenced in application code
        public static class VA_39_UserIndex_TransferModal_DDL { }

        // VA 40: 0x5bc554 len=72 - Alter table mir3_backup.user_index add TransferModal smallInt default
        // Backup table DDL - not referenced in application code
        public static class VA_40_BackupUserIndex_TransferModal_DDL { }
    }
}
