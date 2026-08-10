using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using DBSvr.Core;

namespace DBSvr
{
    /// <summary>
    /// 师徒系统 MySQL 实现 (gamedata.ZongpaiBase / ZongpaiMember / ZongpaiRole)。
    /// </summary>
    public class MySqlZongpaiService : IZongpaiService
    {
        /// <summary>
        /// 原版模板 0x592FD4 (len 101)：
        ///   insert into ZongpaiBase(MasterName, MasterLevel, StudentExp, UpdateTime)
        ///   values("%s", %d, %u, Now());
        /// TVarRec 自 [ebp-0x30] 起、每槽 8 字节，ecx=2（高位索引，共 3 元素）：
        ///   0x592ED8  slot0 = [ebp-0x08]           type 0x0B(string) -> %s MasterName
        ///   0x592EDF  movzx eax, word [ebp-0x0a]   ★16 位零扩展
        ///   0x592EE3  slot1 = eax                  type 0x00(int)    -> %d MasterLevel
        ///   0x592EEA  slot2 = [ebp+0x08]           type 0x00(int)    -> %u StudentExp
        /// 调用点 0x594213 给出各值的 tail 来源：
        ///   0x5941EE  mov eax,[eax+0x50] / 0x5941F1 push eax   ; tail+0x50 dword -> StudentExp
        ///   0x5941FB  add edx,0x35 / 0x5941FE call 0x404E5C    ; tail+0x35 Str   -> MasterName
        ///   0x59420C  mov cx, word ptr [eax+0x4c]              ; tail+0x4C WORD  -> MasterLevel
        ///
        /// ⚠️ 修正两处背离：此前 MasterLevel 传的是 tail+0x50，且 StudentExp 在 SQL 里
        /// 硬写 0 —— 两个字段一个错位、一个丢失。MasterLevel 取 tail+0x4C 的**低 16 位**
        /// （原版 movzx word，DDL 亦为 `MasterLevel smallint unsigned`）；
        /// StudentExp 取 tail+0x50（DDL `StudentExp int unsigned`，故按无符号写）。
        /// </summary>
        public bool CreateMaster(string masterName, int masterLevel, uint studentExp)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            using var cmd = new MySqlCommand(
                "INSERT INTO gamedata.ZongpaiBase(MasterName, MasterLevel, StudentExp, UpdateTime) VALUES(@n,@l,@e,NOW())", conn);
            cmd.Parameters.Add(LegacyGbkText.Parameter("@n", masterName));
            // 原版是 movzx word ⇒ 只有低 16 位进 SQL，且 DDL 是 smallint unsigned。
            cmd.Parameters.AddWithValue("@l", (ushort)masterLevel);
            cmd.Parameters.AddWithValue("@e", studentExp);
            cmd.ExecuteNonQuery();
            return true;
        }

        public bool UpdateMasterLevel(string masterName, int level)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            using var cmd = new MySqlCommand("UPDATE gamedata.ZongpaiBase SET MasterLevel=@l WHERE MasterName=@n", conn);
            cmd.Parameters.AddWithValue("@l", level);
            cmd.Parameters.Add(LegacyGbkText.Parameter("@n", masterName));
            return cmd.ExecuteNonQuery() > 0;
        }

        // ===== 四个经验原语。原版是 read-modify-write 的内存表操作，
        // 落库只是把结果写回，所以这里用事务包住读改写以保证原子性。
        // ⚠️ 此前这两个方法是**绝对赋值**（SET StudentExp=@e），与原版的
        // 饱和累加 / 扣减语义完全不同 —— 客户端送的是增量，绝对赋值会把
        // 玩家的总经验直接覆盖成一次的增量值。

        /// <summary>
        /// 原版 helper 0x591C8C。饱和累加 StudentExp，返回实际发放量。
        /// 上限 0xFFB43480；已满或增量 &lt;= 0 时返 0 且不写库。
        /// </summary>
        public uint AddStudentExpSaturating(string masterName, uint amount)
            => AddSaturating(masterName, amount, "StudentExp");

        /// <summary>
        /// 原版 helper 0x591D28。结构与 0x591C8C 同构，字段换成 MasterExp。
        /// </summary>
        public uint AddMasterExpSaturating(string masterName, uint amount)
            => AddSaturating(masterName, amount, "MasterExp");

        /// <summary>
        /// 原版 helper 0x591CF8。扣减 StudentExp；不足则拒绝（不部分扣）。
        /// </summary>
        public bool SubtractStudentExp(string masterName, uint amount)
            => Subtract(masterName, amount, "StudentExp");

        /// <summary>
        /// 原版 helper 0x591D94。扣减 MasterExp；不足则拒绝。
        /// </summary>
        public bool SubtractMasterExp(string masterName, uint amount)
            => Subtract(masterName, amount, "MasterExp");

        /// <summary>
        /// 饱和累加的共用实现。<paramref name="column"/> 只取自本文件的两个字面量，
        /// 不是外部输入，故可插值。
        /// </summary>
        private static uint AddSaturating(string masterName, uint amount, string column)
        {
            // 0x591CA9 cmp amount,0 / jbe -> 增量为 0 直接返 0，连查询都不做。
            if (amount == 0) return 0;

            using var conn = OpenConn();
            if (conn == null) return 0;
            using var tx = conn.BeginTransaction();
            try
            {
                using var read = new MySqlCommand(
                    $"SELECT {column} FROM gamedata.ZongpaiBase "
                    + "WHERE MasterName=@n FOR UPDATE", conn, tx);
                read.Parameters.Add(LegacyGbkText.Parameter("@n", masterName));
                var scalar = read.ExecuteScalar();
                // 0x59359D/0x5935A0：找不到记录 -> 什么都不做。
                if (scalar == null || scalar == DBNull.Value) { tx.Rollback(); return 0; }

                var current = Convert.ToUInt32(scalar);

                // 0x591CA0 cmp [master+X], 0xFFB43480 / jae -> 已满，返 0 不写库。
                if (current >= IZongpaiService.ExperienceCap) { tx.Rollback(); return 0; }

                // 0x591CB5 add / 0x591CC1 cmp / jae -> 溢出检测（无符号回绕）。
                var sum = unchecked(current + amount);
                uint granted;
                uint stored;
                if (sum < current || sum > IZongpaiService.ExperienceCap)
                {
                    // 0x591CC6..0x591CD7：封顶，实发量 = cap - old。
                    granted = IZongpaiService.ExperienceCap - current;
                    stored = IZongpaiService.ExperienceCap;
                }
                else
                {
                    // 0x591CE6/0x591CEC：正常写入新总额，返回增量原值。
                    granted = amount;
                    stored = sum;
                }

                // 调用方（sub 6 @0x5935B4）在实发量 <= 0 时不发 SQL；
                // 这里 granted 必 > 0（current < cap 已保证），故一定落库。
                using var write = new MySqlCommand(
                    $"UPDATE gamedata.ZongpaiBase SET {column}=@e, UpdateTime=NOW() "
                    + "WHERE MasterName=@n", conn, tx);
                write.Parameters.AddWithValue("@e", stored);
                write.Parameters.Add(LegacyGbkText.Parameter("@n", masterName));
                write.ExecuteNonQuery();
                tx.Commit();
                return granted;
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                return 0;
            }
        }

        /// <summary>
        /// 扣减的共用实现。原版 0x591D0E/0x591DAA 的 <c>ja</c> 是**严格大于**才拒，
        /// 即 amount == current 时允许扣到 0。
        /// </summary>
        private static bool Subtract(string masterName, uint amount, string column)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            using var tx = conn.BeginTransaction();
            try
            {
                using var read = new MySqlCommand(
                    $"SELECT {column} FROM gamedata.ZongpaiBase "
                    + "WHERE MasterName=@n FOR UPDATE", conn, tx);
                read.Parameters.Add(LegacyGbkText.Parameter("@n", masterName));
                var scalar = read.ExecuteScalar();
                if (scalar == null || scalar == DBNull.Value) { tx.Rollback(); return false; }

                var current = Convert.ToUInt32(scalar);

                // 0x591D0E cmp amount,[master+X] / 0x591D11 ja -> 不足则拒绝。
                // 注意是 ja（严格大于）：amount == current 可以扣。
                if (amount > current) { tx.Rollback(); return false; }

                using var write = new MySqlCommand(
                    $"UPDATE gamedata.ZongpaiBase SET {column}=@e, UpdateTime=NOW() "
                    + "WHERE MasterName=@n", conn, tx);
                write.Parameters.AddWithValue("@e", current - amount);
                write.Parameters.Add(LegacyGbkText.Parameter("@n", masterName));
                write.ExecuteNonQuery();
                tx.Commit();
                return true;
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                return false;
            }
        }

        /// <summary>
        /// 该师门当前成员行数；查询失败返回 -1（调用方必须据此放弃删除，fail-closed）。
        /// 复刻原版门 0x591DC4：`call dword [edx+0x14]`(Count) / `dec eax` / `sete`
        /// ⇒ 成员数**恰为 1** 才允许解散。原版读的是内存表容器，这里用等价的行计数。
        /// </summary>
        public int CountMembers(string masterName)
        {
            using var conn = OpenConn();
            if (conn == null) return -1;
            try
            {
                using var cmd = new MySqlCommand(
                    "SELECT COUNT(*) FROM gamedata.ZongpaiMember WHERE MasterName=@n", conn);
                cmd.Parameters.Add(LegacyGbkText.Parameter("@n", masterName));
                var scalar = cmd.ExecuteScalar();
                return scalar == null ? -1 : Convert.ToInt32(scalar);
            }
            catch { return -1; }
        }

        public bool DeleteMaster(string masterName)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            using var tx = conn.BeginTransaction();
            try
            {
                using var c1 = new MySqlCommand("DELETE FROM gamedata.ZongpaiMember WHERE MasterName=@n", conn, tx);
                c1.Parameters.Add(LegacyGbkText.Parameter("@n", masterName));
                c1.ExecuteNonQuery();
                using var c2 = new MySqlCommand("DELETE FROM gamedata.ZongpaiBase WHERE MasterName=@n", conn, tx);
                c2.Parameters.Add(LegacyGbkText.Parameter("@n", masterName));
                c2.ExecuteNonQuery();
                tx.Commit(); return true;
            }
            catch { try { tx.Rollback(); } catch { } return false; }
        }

        public ZongpaiMasterInfo GetMaster(string masterName)
        {
            using var conn = OpenConn();
            if (conn == null) return null;
            using var cmd = new MySqlCommand(
                "SELECT Idx, MasterName, MasterLevel, StudentExp, MasterExp, Notice FROM gamedata.ZongpaiBase WHERE MasterName=@n", conn);
            cmd.Parameters.Add(LegacyGbkText.Parameter("@n", masterName));
            using var dr = cmd.ExecuteReader();
            if (dr.Read()) return new ZongpaiMasterInfo
            {
                Idx = dr.GetInt32(0), MasterName = LegacyGbkText.Read(dr, 1), MasterLevel = dr.GetInt32(2),
                StudentExp = dr.GetInt32(3), MasterExp = dr.GetInt32(4), Notice = dr["Notice"] as byte[]
            };
            return null;
        }

        public bool AddRole(string masterName, string roleName, int privilege, int maxMembers)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            using var cmd = new MySqlCommand(
                "INSERT IGNORE INTO gamedata.ZongpaiRole(MasterName, RoleName, RolePrivilege, MaxMemberNum) VALUES(@m,@r,@p,@x)", conn);
            cmd.Parameters.Add(LegacyGbkText.Parameter("@m", masterName));
            cmd.Parameters.Add(LegacyGbkText.Parameter("@r", roleName));
            cmd.Parameters.AddWithValue("@p", privilege); cmd.Parameters.AddWithValue("@x", maxMembers);
            cmd.ExecuteNonQuery(); return true;
        }

        public bool AddMember(string masterName, string memberName, string roleName)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            using var cmd = new MySqlCommand(
                "INSERT INTO gamedata.ZongpaiMember(MasterName, MemberName, RoleName) VALUES(@m,@n,@r)", conn);
            cmd.Parameters.Add(LegacyGbkText.Parameter("@m", masterName));
            cmd.Parameters.Add(LegacyGbkText.Parameter("@n", memberName));
            cmd.Parameters.Add(LegacyGbkText.Parameter("@r", roleName));
            cmd.ExecuteNonQuery(); return true;
        }

        public bool RemoveMember(string masterName, string memberName)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            using var cmd = new MySqlCommand(
                "DELETE FROM gamedata.ZongpaiMember WHERE MasterName=@m AND MemberName=@n", conn);
            cmd.Parameters.Add(LegacyGbkText.Parameter("@m", masterName));
            cmd.Parameters.Add(LegacyGbkText.Parameter("@n", memberName));
            return cmd.ExecuteNonQuery() > 0;
        }

        public bool UpdateMemberRole(string masterName, string memberName, string roleName)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            using var cmd = new MySqlCommand(
                "UPDATE gamedata.ZongpaiMember SET RoleName=@r WHERE MasterName=@m AND MemberName=@n", conn);
            cmd.Parameters.Add(LegacyGbkText.Parameter("@m", masterName));
            cmd.Parameters.Add(LegacyGbkText.Parameter("@n", memberName));
            cmd.Parameters.Add(LegacyGbkText.Parameter("@r", roleName));
            return cmd.ExecuteNonQuery() > 0;
        }

        public bool RenameMaster(string oldName, string newName)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            using var tx = conn.BeginTransaction();
            try
            {
                foreach (var sql in new[] { "UPDATE gamedata.ZongpaiBase SET MasterName=@n WHERE MasterName=@o",
                    "UPDATE gamedata.ZongpaiMember SET MasterName=@n WHERE MasterName=@o",
                    "UPDATE gamedata.ZongpaiRole SET MasterName=@n WHERE MasterName=@o" })
                {
                    using var c = new MySqlCommand(sql, conn, tx);
                    c.Parameters.Add(LegacyGbkText.Parameter("@n", newName));
                    c.Parameters.Add(LegacyGbkText.Parameter("@o", oldName));
                    c.ExecuteNonQuery();
                }
                tx.Commit(); return true;
            }
            catch { try { tx.Rollback(); } catch { } return false; }
        }

        public bool RenameMember(string masterName, string oldName, string newName)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            using var cmd = new MySqlCommand(
                "UPDATE gamedata.ZongpaiMember SET MemberName=@n WHERE MasterName=@m AND MemberName=@o", conn);
            cmd.Parameters.Add(LegacyGbkText.Parameter("@m", masterName));
            cmd.Parameters.Add(LegacyGbkText.Parameter("@n", newName));
            cmd.Parameters.Add(LegacyGbkText.Parameter("@o", oldName));
            return cmd.ExecuteNonQuery() > 0;
        }

        public List<ZongpaiMasterInfo> LoadAllMasters()
        {
            var list = new List<ZongpaiMasterInfo>();
            using var conn = OpenConn();
            if (conn == null) return list;
            using var cmd = new MySqlCommand("SELECT Idx, MasterName, MasterLevel, StudentExp, MasterExp, Notice FROM gamedata.ZongpaiBase ORDER BY Idx", conn);
            using var dr = cmd.ExecuteReader();
            while (dr.Read()) list.Add(new ZongpaiMasterInfo { Idx = dr.GetInt32(0), MasterName = LegacyGbkText.Read(dr, 1), MasterLevel = dr.GetInt32(2), StudentExp = dr.GetInt32(3), MasterExp = dr.GetInt32(4), Notice = dr["Notice"] as byte[] });
            return list;
        }

        public List<ZongpaiMemberInfo> LoadAllMembers()
        {
            var list = new List<ZongpaiMemberInfo>();
            using var conn = OpenConn();
            if (conn == null) return list;
            using var cmd = new MySqlCommand("SELECT Idx, MasterName, MemberName, RoleName FROM gamedata.ZongpaiMember ORDER BY Idx", conn);
            using var dr = cmd.ExecuteReader();
            while (dr.Read()) list.Add(new ZongpaiMemberInfo { Idx = dr.GetInt32(0), MasterName = LegacyGbkText.Read(dr, 1), MemberName = LegacyGbkText.Read(dr, 2), RoleName = LegacyGbkText.Read(dr, 3) });
            return list;
        }

        public List<ZongpaiRoleInfo> LoadAllRoles()
        {
            var list = new List<ZongpaiRoleInfo>();
            using var conn = OpenConn();
            if (conn == null) return list;
            using var cmd = new MySqlCommand("SELECT Idx, MasterName, RoleName, RolePrivilege, MaxMemberNum FROM gamedata.ZongpaiRole ORDER BY Idx", conn);
            using var dr = cmd.ExecuteReader();
            while (dr.Read()) list.Add(new ZongpaiRoleInfo { Idx = dr.GetInt32(0), MasterName = LegacyGbkText.Read(dr, 1), RoleName = LegacyGbkText.Read(dr, 2), RolePrivilege = dr.GetInt32(3), MaxMemberNum = dr.GetInt32(4) });
            return list;
        }

        private static MySqlConnection OpenConn()
        {
            try
            {
                var c = new MySqlConnection(DBShare.DBConnection);
                c.Open();
                using(var sc = new MySqlCommand("SET SESSION TRANSACTION ISOLATION LEVEL READ COMMITTED; SET SESSION wait_timeout=2073600", c))
                    sc.ExecuteNonQuery();
                return c;
            }
            catch { return null; }
        }
    }
}
