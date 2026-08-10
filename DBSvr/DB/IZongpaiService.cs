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

        /// <summary>
        /// sub 9 的等级同步（原版 worker 0x593944）。⚠️ 与 <see cref="UpdateMasterLevel"/>
        /// 的区别是它带**幂等短路**：等级与库里现值相同时不写库。
        ///   0x593A8F  mov ax, word [eax+0x3e]  ; 等级取自活体角色对象 +0x3E
        ///   0x593A96  cmp ax, word [edx+0x20]  ; edx = 0x30 字节栈上出参（非记录），
        ///                                        ; 其 +0x20 由 0x593A58 从 rec+0x10 拷入
        ///   0x593A9A  je 0x593AF5              ; ★相等 -> 不写库
        ///   0x593AA6  mov word [edx+0x20], ax  ; 不同才更新（写出参，供回包）
        /// 请求报文里的等级字段（tail+0x50）在整个 worker 内没有任何读取点，
        /// 所以调用方必须传活体等级，不能传请求里的值。
        /// 宽度是 word（原版 movzx），DDL 为 <c>MasterLevel smallint unsigned</c>。
        /// </summary>
        /// <returns>true = 已同步（含"值相同故无需写库"）；false = 记录不存在或写库失败。</returns>
        bool UpdateMasterLevelFromLive(string masterName, ushort liveLevel);

        /// <summary>
        /// 原版的经验上限，两个饱和累加器都用同一个立即数：
        /// 0x591CA0 / 0x591CC6 / 0x591CD7（StudentExp）与
        /// 0x591D3C / 0x591D62 / 0x591D73（MasterExp）均为 0xFFB43480 = 4290000000。
        /// </summary>
        const uint ExperienceCap = 0xFFB43480;

        /// <summary>
        /// 饱和累加 StudentExp（原版 helper \c 0x591C8C，被 sub 6 调用）。
        ///   0x591CA0  cmp [master+0x14], 0xFFB43480 / jae  -> 已满，返 0 且不落库
        ///   0x591CA9  cmp amount, 0 / jbe                  -> 增量 &lt;= 0，返 0 且不落库
        ///   0x591CB5  add / 0x591CC1 cmp / jae              -> 溢出检测
        ///   0x591CC6  封顶为 0xFFB43480，返回**实发量** cap - old
        ///   0x591CE6  正常路径写入新总额，返回增量原值
        /// 调用方 sub 6 在 0x5935B4 用 `cmp [ebp-0x18],0 / jbe` 判实发量，
        /// **实发量 &lt;= 0 时连 SQL 都不发**。
        /// </summary>
        /// <returns>实际发放量；0 表示未发放（调用方据此跳过 SQL）。</returns>
        uint AddStudentExpSaturating(string masterName, uint amount);

        /// <summary>
        /// 扣减 StudentExp（原版 helper \c 0x591CF8，被 sub 7 调用）。
        ///   0x591D0E  cmp amount, [master+0x14] / 0x591D11 ja -> 不足则**拒绝**返 false
        ///   0x591D19  sub [master+0x14], amount / 返 true
        /// ⚠️ 这不是累加器。规格把 sub 7 描述成「同时加师徒与师父经验」是错的 ——
        /// 它实际是**扣师徒经验、转换为师父经验**。
        /// </summary>
        bool SubtractStudentExp(string masterName, uint amount);

        /// <summary>
        /// 饱和累加 MasterExp（原版 helper \c 0x591D28，被 sub 7 的后半段调用）。
        /// 结构与 \c 0x591C8C 完全同构，只是字段换成 <c>[master+0x18]</c>：
        ///   0x591D3C cmp / jae、0x591D45 cmp amount,0 / jbe、
        ///   0x591D5D 溢出检测、0x591D73 封顶、0x591D82 正常写入。
        /// </summary>
        /// <returns>实际发放量；0 表示未发放。</returns>
        uint AddMasterExpSaturating(string masterName, uint amount);

        /// <summary>
        /// 扣减 MasterExp（原版 helper \c 0x591D94，被 sub 8 调用）。
        ///   0x591DAA  cmp amount, [master+0x18] / 0x591DAD ja -> 不足则拒绝
        ///   0x591DB5  sub [master+0x18], amount / 返 true
        /// ⚠️ sub 8 是**扣减**，不是累加。原注释「MasterExp=tail+0x4C」的绝对赋值是错的。
        /// </summary>
        bool SubtractMasterExp(string masterName, uint amount);
        bool DeleteMaster(string masterName);
        ZongpaiMasterInfo GetMaster(string masterName);

        /// <summary>
        /// sub 12 (ModifyNotice) 的落库。原版 worker 0x593D70 用的是**数据集流写**，
        /// 不是 UPDATE 语句：
        ///   0x593DDB `mov eax,0x593F04` / 0x593DE0 `call 0x40CF30`(Format) ⇒
        ///     `select idx, Notice from ZongpaiBase where MasterName = "%s";`
        ///     （0x593F04 longstr refcount=-1 len=60，逐字）
        ///   0x593DEB `call 0x592300` / 0x593DF0 `dec eax / jne` ⇒ 该查询必须**恰返回 1**
        ///   0x593E0B/0x593E16 `call 0x5655FC/0x5659E4` ⇒ 数据集 Edit
        ///   0x593E29 `call 0x556EE8`(edx=1) / 0x593E3A `call [vmt+0x214]` ⇒ 取 Notice
        ///     字段的 BLOB 写入流（cl=1 = 写模式）
        ///   0x593E4E `call dword [ebx+0x10]`(edx=tail 指针, ecx=tail 长度) ⇒ 原样写字节
        ///   0x593E61 `call [vmt+0x24C]` ⇒ Post
        /// 语义等价于 `UPDATE ZongpaiBase SET Notice=<blob> WHERE MasterName=...`，
        /// 且**不带 UpdateTime**（整个 worker 里没有 Now()/UpdateTime 的引用）。
        /// Notice 列类型是 <c>blob</c>（DDL 0x5BEE34），所以必须按**字节**写，
        /// 不能按字符串写 —— tail 里的 0x00 填充也是内容的一部分。
        /// </summary>
        /// <returns>true = 已落库；false = 记录不存在或写库失败（调用方据此不回包）。</returns>
        bool UpdateNotice(string masterName, byte[] notice);

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

        // ⚠️ These two MUST be unsigned. The original's saturating adders cap at
        // 0xFFB43480 (4,290,000,000), which is far above int.MaxValue
        // (2,147,483,647), and every comparison in those helpers is unsigned:
        //   00591CA0  81 78 14 80 34 b4 ff   cmp dword [eax+0x14],0xffb43480
        //   00591CA7  73 46                  jae   (unsigned!)
        //   00591CAD  76 40                  jbe   (unsigned!)
        //   00591CC4  73 1a                  jae   (unsigned overflow test)
        //   00591CD7  c7 40 14 80 34 b4 ff   mov dword [eax+0x14],0xffb43480
        // (StudentExp is rec+0x14 via 0x591C8C/0x591CF8; MasterExp is rec+0x18 via
        // 0x591D28/0x591D94 — same shape, same constant at 0x591D3F/0x591D63/
        // 0x591D76.) Declaring these `int` and reading them with GetInt32 made any
        // value above 2.1 billion either throw or silently wrap negative, which
        // then failed the unsigned cap test on the very next add.
        public uint StudentExp;
        public uint MasterExp;
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
