using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private string _lastNativeMapDescriptionMapName = string.Empty;
        private bool _lastNativeMapDescriptionHadRecords;

        internal IReadOnlyList<NativeMapDescriptionFrame>
            BuildNativeMapDescriptionFrames()
        {
            var frames = new List<NativeMapDescriptionFrame>();
            if (m_PEnvir == null)
                return frames;

            var description = m_PEnvir.ResolveNativeMapDescription(
                m_nCurrX, m_nCurrY);
            if (!string.Equals(_lastMapDescription, description,
                    StringComparison.Ordinal))
            {
                _lastMapDescription = description;
                frames.Add(NativeMapDescriptionFrame.Text(
                    Grobal2.MakeDefaultMsg(Grobal2.SM_MAPDESCRIPTION,
                        1, 0, 0, 0), description));
            }

            var mapName = m_PEnvir.sMapName ?? string.Empty;
            if (string.Equals(_lastNativeMapDescriptionMapName, mapName,
                    StringComparison.Ordinal))
            {
                return frames;
            }

            if (_lastNativeMapDescriptionHadRecords)
            {
                frames.Add(NativeMapDescriptionFrame.Binary(
                    Grobal2.MakeDefaultMsg(Grobal2.SM_56,
                        0, 0, 0, 0), Array.Empty<byte>()));
            }

            var tableKey = m_PEnvir.ResolveNativeMapDescription(0, 0);
            var records = M2Share.MapManager?.GetNativeMapDescriptionRecords(
                tableKey) ?? Array.Empty<byte[]>();
            for (var index = 0; index < records.Count; index++)
            {
                frames.Add(NativeMapDescriptionFrame.Binary(
                    Grobal2.MakeDefaultMsg(Grobal2.SM_56,
                        0, 1, 0, 0), records[index]));
            }
            if (records.Count > 0)
            {
                frames.Add(NativeMapDescriptionFrame.Binary(
                    Grobal2.MakeDefaultMsg(Grobal2.SM_56,
                        0, 2, 0, 0), Array.Empty<byte>()));
            }

            _lastNativeMapDescriptionMapName = mapName;
            _lastNativeMapDescriptionHadRecords = records.Count > 0;
            return frames;
        }
    }

    internal readonly struct NativeMapDescriptionFrame
    {
        private NativeMapDescriptionFrame(ClientPacket header, string textBody,
            byte[] binaryBody, bool isBinary)
        {
            Header = header;
            TextBody = textBody;
            BinaryBody = binaryBody;
            IsBinary = isBinary;
        }

        internal ClientPacket Header { get; }
        internal string TextBody { get; }
        internal byte[] BinaryBody { get; }
        internal bool IsBinary { get; }

        internal static NativeMapDescriptionFrame Text(ClientPacket header,
            string body) => new(header, body ?? string.Empty, null, false);

        internal static NativeMapDescriptionFrame Binary(ClientPacket header,
            byte[] body) => new(header, null, body ?? Array.Empty<byte>(), true);
    }
}
