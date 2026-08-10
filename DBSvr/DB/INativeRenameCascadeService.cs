namespace DBSvr
{
    /// <summary>
    /// 角色改名的三库级联（原版 fn_5A923C）。
    ///
    /// 触发链（全部已证，每级只有 1 个 E8 调用点、全文件 0 个 dword 引用，
    /// 故不在任何 VMT/派发表里 ⇒ opcode 0xFB0 是唯一入口）：
    ///   [入向 opcode 0xFB0 (4016)]
    ///     -> fn_5CDF6C  外层 switch byte[Self+8]==5（跳表 6 项，idx5 -> 0x5CE307）
    ///                   内层两级表：idx = word[msg+4] - 0xFAC
    ///                              -> grp = byte[0x5CE345+idx]（30 项）
    ///                              -> dword[0x5CE363+grp*4]（9 项）
    ///                   0xFB0 -> idx=0x04 -> grp=5 -> 0x5CE404
    ///     -> 0x5CE41E   call fn_5CD2EC    宿主（校验层）
    ///     -> 0x5CD3AF   call fn_5A8DDC    改名两级入口（主档）
    ///     -> 0x5A912F   call fn_5A923C    本接口对应的三库级联
    ///
    /// ⚠️ 顺序不可颠倒：主档（user_index / user_data）必须**先**写且失败即中断，
    /// 那是原版唯一的安全性来源（0x5A8F4A test al,al / 0x5A8F4C je 0x5A9162 ⇒
    /// 返回 -1 且**一条级联都不执行**）。级联本身是 fire-and-forget。
    /// </summary>
    public interface INativeRenameCascadeService
    {
        /// <summary>
        /// 主档改名（原版 fn_5A8DDC 自身在 0x5A8F3F 入队的那一条，前序逆向报告漏了它）。
        /// 由四段常量拼成、一次提交两条语句：
        ///   0x5A91D0 `Update user_index set ChrName="` (len 31)
        ///   0x5A91F8 `" where Idx=`                   (len 12)
        ///   0x5A9210 `;Update user_data set ChrName="` (len 31)
        ///   0x5A9238 `;`                              (len 1)
        /// ⇒ Update user_index set ChrName="新" where Idx=N;
        ///    Update user_data  set ChrName="新" where Idx=N;
        /// 注意：**按数值 Idx 筛，不按名字**；且这两条**没有** ignore。
        /// 前置门 0x5A8EE7 `cmp dword [eax+0xc],0` / `jle` ⇒ Idx &lt;= 0 直接返回 -1。
        /// </summary>
        /// <returns>true = 主档写成功，调用方才可以继续级联；false = 必须中断。</returns>
        bool RenameMasterRecords(int idx, string newName);

        /// <summary>
        /// 22 条级联 UPDATE（打 19 张表，3 张表各被打 2 次），逐条 fire-and-forget。
        /// 15 个 `show tables ... like` 门只作开关：门关闭 ⇒ 跳过该块且**不报错**。
        /// 全部是 `Update ignore`（主档 2 条没有）——ignore 让新名已存在时的唯一键冲突
        /// 从报错降级为跳过该行，受影响行数与残留数据都不同，必须保留。
        /// </summary>
        /// <returns>实际执行成功的 UPDATE 条数，仅用于日志；原版调用方无条件置成功。</returns>
        int RenameCascade(string oldName, string newName);
    }
}
