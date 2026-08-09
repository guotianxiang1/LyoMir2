using ProtoBuf;
using System;
using System.IO;

namespace SystemModule
{
    public class ProtoBufDecoder
    {
        public static byte[] Serialize<T>(T model)
        {
            try
            {
                PrepareForTransport(model);
                try
                {
                    using var ms = new MemoryStream();
                    Serializer.Serialize<T>(ms, model);
                    byte[] result = new byte[ms.Length];
                    ms.Position = 0;
                    ms.Read(result, 0, result.Length);
                    return result;
                }
                finally
                {
                    RestoreAfterTransport(model);
                }
            }
            catch
            {
                return null;
            }
        }

        public static T DeSerialize<T>(byte[] msg)
        {
            try
            {
                using var ms = new MemoryStream(msg);
                ms.Position = 0;
                var result = Serializer.Deserialize<T>(ms);
                RestoreAfterTransport(result);
                return result;
            }
            catch
            {
                return default(T);
            }
        }

        private static void PrepareForTransport<T>(T model)
        {
            switch (model)
            {
                case THumDataInfo human:
                    human.PrepareForTransport();
                    break;
                case SaveHumDataPacket save:
                    save.HumDataInfo?.PrepareForTransport();
                    break;
                case LoadHumanRcdResponsePacket load:
                    load.HumDataInfo?.PrepareForTransport();
                    break;
            }
        }

        private static void RestoreAfterTransport<T>(T model)
        {
            switch (model)
            {
                case THumDataInfo human:
                    human.RestoreAfterTransport();
                    break;
                case SaveHumDataPacket save:
                    save.HumDataInfo?.RestoreAfterTransport();
                    break;
                case LoadHumanRcdResponsePacket load:
                    load.HumDataInfo?.RestoreAfterTransport();
                    break;
            }
        }
    }
}
