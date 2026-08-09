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
        public bool CreateMaster(string masterName, int masterLevel)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            using var cmd = new MySqlCommand(
                "INSERT INTO gamedata.ZongpaiBase(MasterName, MasterLevel, StudentExp, UpdateTime) VALUES(@n,@l,0,NOW())", conn);
            cmd.Parameters.Add(LegacyGbkText.Parameter("@n", masterName));
            cmd.Parameters.AddWithValue("@l", masterLevel);
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

        public bool UpdateMasterExp(string masterName, int exp)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            using var cmd = new MySqlCommand("UPDATE gamedata.ZongpaiBase SET MasterExp=@e, UpdateTime=NOW() WHERE MasterName=@n", conn);
            cmd.Parameters.AddWithValue("@e", exp);
            cmd.Parameters.Add(LegacyGbkText.Parameter("@n", masterName));
            return cmd.ExecuteNonQuery() > 0;
        }

        public bool UpdateStudentExp(string masterName, int exp)
        {
            using var conn = OpenConn();
            if (conn == null) return false;
            using var cmd = new MySqlCommand("UPDATE gamedata.ZongpaiBase SET StudentExp=@e, UpdateTime=NOW() WHERE MasterName=@n", conn);
            cmd.Parameters.AddWithValue("@e", exp);
            cmd.Parameters.Add(LegacyGbkText.Parameter("@n", masterName));
            return cmd.ExecuteNonQuery() > 0;
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
