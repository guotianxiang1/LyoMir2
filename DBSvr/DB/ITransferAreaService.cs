using System;
using System.Collections.Generic;

namespace DBSvr
{
    /// <summary>
    /// 跨服转区/转分服务接口 (对应 gamedata.TransferAreaScore 表)。
    /// </summary>
    public interface ITransferAreaService
    {
        /// <summary>查询角色转分</summary>
        Dictionary<string, int> GetScores(string charName);

        /// <summary>扣分</summary>
        bool DeductScore(string charName, string scoreField, int amount);
        bool TryDeductNativeScore(byte[] characterName, ushort scoreType,
            ushort amount);

        /// <summary>插入或累加 (ON DUPLICATE KEY UPDATE)</summary>
        bool UpsertScore(string charName, int score1, int score2, int score3);

        /// <summary>插入发送记录</summary>
        bool InsertSendRecord(string timeStamp, string charName, int zoneId, int groupId, int scoreType, int score, int state);

        /// <summary>
        /// 更新发送记录状态。scoreType 是唯一键 Record_Index 的第 5 列
        /// (DDL 0x5C0CF4)，省略它会覆盖同名其它 ScoreType 的兄弟行。
        /// </summary>
        bool UpdateSendRecordState(string timeStamp, string charName, int zoneId, int groupId, int scoreType, int state);

        /// <summary>清理7天过期记录</summary>
        int CleanExpiredRecords(int days = 7);

        /// <summary>
        /// 读取待发送记录（State=1），按 TimeStamp 升序（原版 0x595714）。
        /// 原版 0x595430 函数定期轮询，读取 State=1 记录填充 0x27 字节结构体推入列表。
        /// </summary>
        List<TransferAreaSendRecord> GetPendingSendRecords();

        /// <summary>改名级联</summary>
        bool RenameChar(string oldName, string newName);
    }

    /// <summary>
    /// TransferAreaScoreSendRecord 记录（原版 0x595430 的 0x27 字节结构体）。
    /// </summary>
    public class TransferAreaSendRecord
    {
        public DateTime TimeStamp { get; set; }
        public string CharName { get; set; }
        public ushort ZoneId { get; set; }
        public ushort GroupId { get; set; }
        public ushort ScoreType { get; set; }
        public ushort Score { get; set; }
        public ushort State { get; set; }
    }
}
