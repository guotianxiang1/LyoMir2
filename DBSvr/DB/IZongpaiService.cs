using System.Collections.Generic;

namespace DBSvr
{
    /// <summary>
    /// 师徒系统服务接口 (对应 gamedata.ZongpaiBase / ZongpaiMember / ZongpaiRole)。
    /// </summary>
    public interface IZongpaiService
    {
        // === 师父 ===
        bool CreateMaster(string masterName, int masterLevel);
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
