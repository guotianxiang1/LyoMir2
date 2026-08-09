using System.Collections.Generic;

namespace DBSvr
{
    /// <summary>
    /// 师徒系统服务接口 (对应 gamedata.ZongpaiBase / ZongpaiMember / ZongpaiRole)。
    /// </summary>
    public interface IZongpaiService
    {
        // === 师父 ===
        /// <summary>
        /// 原版 sub 2：模板 0x592FD4，三个 TVarRec 槽依次为
        /// MasterName(tail+0x35 Str) / MasterLevel(tail+0x4C **WORD**) / StudentExp(tail+0x50 dword)。
        /// 调用点 0x594213；`0x59420C mov cx, word ptr [eax+0x4c]` 是 level 只取 16 位的判据。
        /// </summary>
        bool CreateMaster(string masterName, int masterLevel, uint studentExp);
        bool UpdateMasterLevel(string masterName, int level);
        bool UpdateMasterExp(string masterName, int exp);
        bool UpdateStudentExp(string masterName, int exp);
        bool DeleteMaster(string masterName);
        ZongpaiMasterInfo GetMaster(string masterName);

        // === 角色 ===
        bool AddRole(string masterName, string roleName, int privilege, int maxMembers);

        // === 成员 ===
        bool AddMember(string masterName, string memberName, string roleName);
        bool RemoveMember(string masterName, string memberName);
        bool UpdateMemberRole(string masterName, string memberName, string roleName);

        /// <summary>
        /// 该师门当前成员行数。用于复刻原版 sub 13 (DeleteMaster) 的前置门：
        /// 0x591DC4 `call dword [edx+0x14]`(Count) / `dec eax` / `sete`
        /// ⇒ 仅当成员数**恰为 1** 才允许解散（0x593FB8 `test al,al` / `je 0x59400D`
        /// 不满足则连删都不删）。查询失败返回 -1，调用方据此不得执行删除。
        /// </summary>
        int CountMembers(string masterName);

        // === 改名 ===
        bool RenameMaster(string oldName, string newName);
        bool RenameMember(string masterName, string oldName, string newName);

        // === 查询 ===
        List<ZongpaiMasterInfo> LoadAllMasters();
        List<ZongpaiMemberInfo> LoadAllMembers();
        List<ZongpaiRoleInfo> LoadAllRoles();
    }

    public class ZongpaiMasterInfo
    {
        public int Idx;
        public string MasterName;
        public int MasterLevel;
        public int StudentExp;
        public int MasterExp;
        public byte[] Notice;
        public string UpdateTime;
    }

    public class ZongpaiMemberInfo
    {
        public int Idx;
        public string MasterName;
        public string MemberName;
        public string RoleName;
    }

    public class ZongpaiRoleInfo
    {
        public int Idx;
        public string MasterName;
        public string RoleName;
        public int RolePrivilege;
        public int MaxMemberNum;
    }
}
