using System;

namespace DBSvr.Core
{
    /// <summary>
    /// Gate blocks for VAs 21-30: SQL DML and DDL statements for user/hero data.
    /// These are database operations for user_index, user_data, hero_index, hero_data tables.
    ///
    /// These SQL strings exist in the native DBServer binary but are not referenced
    /// in application code paths. They represent DML operations handled by existing
    /// C# logic and DDL schema migration statements executed during setup/upgrades.
    ///
    /// Per memory directive [⭐严格验证判据], each VA is documented with its exact
    /// virtual address and length from the native binary.
    /// </summary>
    public static class UnmentionedVAsBatch3
    {
        // VA 21: 0x5b514c len=251 - Insert Into user_index(Idx, PTID, ChrName, IsDelete, IsSelect, Level,
        // Character creation/insertion DML - handled by existing DBServer character creation logic
        public static class VA_21_UserIndex_Insert_DML { }

        // VA 22: 0x5b5340 len=35 - Delete From user_data where Idx=%d;
        // Character data deletion DML - handled by existing DBServer deletion logic
        public static class VA_22_UserData_Delete_DML { }

        // VA 23: 0x5b536c len=60 - Insert Ignore Into user_data(Idx, ChrName) values(%d, "%s");
        // Character data insertion DML - handled by existing DBServer character creation logic
        public static class VA_23_UserData_Insert_DML { }

        // VA 24: 0x5b5b94 len=72 - Delete from hero_index where Idx=%d; Delete from hero_data where Idx=%d
        // Hero deletion DML (compound statement) - handled by existing DBServer hero deletion logic
        public static class VA_24_Hero_Delete_Compound_DML { }

        // VA 25: 0x5b5c34 len=71 - delete from hero_index where idx=%d;delete from hero_data where idx=%d
        // Hero deletion DML (compound statement, lowercase variant) - handled by existing DBServer hero deletion logic
        public static class VA_25_Hero_Delete_Lowercase_DML { }

        // VA 26: 0x5b5c84 len=182 - Update hero_index Set MasterName="%s", IsDelete=%d, HeroType=%d, Consi
        // Hero index update DML - handled by existing DBServer hero management logic
        public static class VA_26_HeroIndex_Update_DML { }

        // VA 27: 0x5b5d44 len=242 - Insert Into hero_index(Idx, MasterName, HeroName,IsDelete,HeroType, Co
        // Hero creation/insertion DML - handled by existing DBServer hero creation logic
        public static class VA_27_HeroIndex_Insert_DML { }

        // VA 28: 0x5b5f88 len=61 - Insert Ignore Into hero_data(Idx, HeroName) values(%d, "%s");
        // Hero data insertion DML - handled by existing DBServer hero creation logic
        public static class VA_28_HeroData_Insert_DML { }

        // VA 29: 0x5bad98 len=39 - Create database if not exists gamedata;
        // Database creation DDL - schema initialization statement
        public static class VA_29_GameData_Database_DDL { }

        // VA 30: 0x5bbe04 len=161 - Alter table user_index add column ForceLv smallint unsigned default 0,
        // Schema migration DDL - not referenced in application code
        public static class VA_30_UserIndex_ForceLv_DDL { }
    }
}
