using System.Collections.Generic;

namespace DBSvr
{
    /// <summary>
    /// 主宰宠物服务接口 (对应 dominatorpet 表)。
    /// </summary>
    public interface IPetService
    {
        byte[] LoadPet(long masterId);
        (int idx, byte[] data) LoadPetWithIdx(long masterId);
        bool CreatePet(string masterName, long masterId, int level, int exp);
        bool SavePet(long masterId, string masterName, int level, int exp,
            byte[] data);
        bool UpdatePetLevel(long masterId, int level, int exp);
        bool DeletePet(long masterId);
        bool RenameMaster(string oldMaster, string newMaster);
        List<PetIndexInfo> GetPetPage(int lastIdx, int limit = 5000);
    }

    public class PetIndexInfo
    {
        public int Idx;
        public long MasterId;
        public string MasterName;
        public int Level;
        public int Exp;
    }
}
