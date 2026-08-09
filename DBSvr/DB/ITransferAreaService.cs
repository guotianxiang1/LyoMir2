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

        /// <summary>更新发送记录状态</summary>
        bool UpdateSendRecordState(string timeStamp, string charName, int zoneId, int groupId, int state);

        /// <summary>清理7天过期记录</summary>
        int CleanExpiredRecords(int days = 7);

        /// <summary>改名级联</summary>
        bool RenameChar(string oldName, string newName);
    }
}
