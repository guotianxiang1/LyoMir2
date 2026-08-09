using SystemModule;

namespace GameSvr
{
    public class TOUserStateInfo : Packets
    {
        public int Feature;
        public string UserName;
        public string GuildName;
        public string GuildRankName;
        public short NameColor;
        public TOClientItem[] UseItems;

        public TOUserStateInfo()
        {
            UseItems = new TOClientItem[13];
        }

        protected override void ReadPacket(BinaryReader reader)
        {
            Feature = reader.ReadInt32();

            var nameLen = reader.ReadByte();
            var nameBuff = reader.ReadBytes(32);
            UserName = HUtil32.GbkEncoding.GetString(nameBuff, 0, nameLen);

            NameColor = reader.ReadInt16();

            var guildLen = reader.ReadByte();
            var guildBuff = reader.ReadBytes(32);
            GuildName = HUtil32.GbkEncoding.GetString(guildBuff, 0, guildLen);

            var rankLen = reader.ReadByte();
            var rankBuff = reader.ReadBytes(32);
            GuildRankName = HUtil32.GbkEncoding.GetString(rankBuff, 0, rankLen);

            for (var i = 0; i < UseItems.Length; i++)
            {
                UseItems[i] = Packets.ToPacket<TOClientItem>(reader.ReadBytes(68));
            }
        }

        protected override void WritePacket(BinaryWriter writer)
        {
            writer.Write(Feature);

            var StrLen = 0;
            var NameBuff = HUtil32.StringToByteAry(UserName ?? "", out StrLen);
            NameBuff[0] = (byte)StrLen;
            Array.Resize(ref NameBuff, 33);
            writer.Write(NameBuff, 0, NameBuff.Length);

            writer.Write(NameColor);

            NameBuff = HUtil32.StringToByteAry(GuildName ?? "", out StrLen);
            NameBuff[0] = (byte)StrLen;
            Array.Resize(ref NameBuff, 33);
            writer.Write(NameBuff, 0, NameBuff.Length);

            NameBuff = HUtil32.StringToByteAry(GuildRankName ?? "", out StrLen);
            NameBuff[0] = (byte)StrLen;
            Array.Resize(ref NameBuff, 33);
            writer.Write(NameBuff, 0, NameBuff.Length);

            for (var i = 0; i < UseItems.Length; i++)
            {
                if (UseItems[i] != null)
                {
                    writer.Write(UseItems[i].GetBuffer());
                }
                else
                {
                    writer.Write(new byte[68]); // TOClientItem size: TOStdItem(60) + MakeIndex(4) + Dura(2) + DuraMax(2)
                }
            }
        }
    }
}