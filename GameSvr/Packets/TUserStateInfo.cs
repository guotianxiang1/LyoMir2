using SystemModule;

namespace GameSvr
{
    public class TUserStateInfo : Packets
    {
        public int Feature;
        public string UserName;
        public string GuildName;
        public string GuildRankName;
        public ushort NameColor;
        public TClientItem[] UseItems;

        public TUserStateInfo()
        {
            UseItems = new TClientItem[13];
        }

        protected override void ReadPacket(BinaryReader reader)
        {
            Feature = reader.ReadInt32();

            var nameLen = reader.ReadByte();
            var nameBuff = reader.ReadBytes(32);
            UserName = HUtil32.GbkEncoding.GetString(nameBuff, 0, nameLen);

            NameColor = reader.ReadUInt16();

            var guildLen = reader.ReadByte();
            var guildBuff = reader.ReadBytes(32);
            GuildName = HUtil32.GbkEncoding.GetString(guildBuff, 0, guildLen);

            var rankLen = reader.ReadByte();
            var rankBuff = reader.ReadBytes(32);
            GuildRankName = HUtil32.GbkEncoding.GetString(rankBuff, 0, rankLen);

            for (var i = 0; i < UseItems.Length; i++)
            {
                UseItems[i] = Packets.ToPacket<TClientItem>(reader.ReadBytes(76));
            }
        }

        protected override void WritePacket(BinaryWriter writer)
        {
            writer.Write(Feature);

            var StrLen = 0;
            var NameBuff = HUtil32.StringToByteAry(UserName, out StrLen);
            NameBuff[0] = (byte)StrLen;
            Array.Resize(ref NameBuff, 33);  // length(1) + Name[32]
            writer.Write(NameBuff, 0, NameBuff.Length);

            writer.Write(NameColor);

            NameBuff = HUtil32.StringToByteAry(GuildName, out StrLen);
            NameBuff[0] = (byte)StrLen;
            Array.Resize(ref NameBuff, 33);  // length(1) + GuildName[32]
            writer.Write(NameBuff, 0, NameBuff.Length);

            NameBuff = HUtil32.StringToByteAry(GuildRankName, out StrLen);
            NameBuff[0] = (byte)StrLen;
            Array.Resize(ref NameBuff, 33);  // length(1) + GuildRankName[32]
            writer.Write(NameBuff, 0, NameBuff.Length);

            for (var i = 0; i < UseItems.Length; i++)
            {
                writer.Write(UseItems[i].GetBuffer());
            }
        }
    }
}