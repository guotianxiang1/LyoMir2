using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace SystemModule.Packet
{
    /// <summary>
    /// Dormant decoder and pure cache decision for native YBDB response 1102.
    /// No matching request Ident is proven. This type performs no transport,
    /// cache, player, database, gateway, or client mutation.
    /// </summary>
    public static class YbDbGlobalShopHotProtocol
    {
        public const ushort ResponseIdent = 1102;
        public const int PayloadSize = 900;
        public const int RecordSize = YbDbGlobalShopItemProtocol.RecordSize;
        public const int RecordCount = 5;
        public const ushort NativeHotPage = 10;
        public const ushort DownstreamClientMessageIdent = 815;
        public const string ConsolePrefix = "==> 加载商城热销榜:";

        public static bool TryDecodeResponse(YbDbLegacy77Frame frame,
            out Response response, out string error)
        {
            response = null;
            error = string.Empty;
            if (frame == null)
            {
                error = "global-shop hot response frame is null";
                return false;
            }
            if (frame.Ident != ResponseIdent)
            {
                error = $"global-shop hot response Ident must be {ResponseIdent}";
                return false;
            }

            var payload = frame.Payload ?? Array.Empty<byte>();
            if (payload.Length != PayloadSize)
            {
                error = $"global-shop hot response payload must be " +
                        $"{PayloadSize} bytes";
                return false;
            }

            var records = new YbDbGlobalShopItemProtocol.ItemRecord[RecordCount];
            var activePrefixCount = RecordCount;
            for (var index = 0; index < RecordCount; index++)
            {
                var raw = payload.AsSpan(index * RecordSize, RecordSize);
                records[index] =
                    YbDbGlobalShopItemProtocol.DecodeDeclaredRecord(raw);
                if (activePrefixCount == RecordCount
                    && raw[YbDbGlobalShopItemProtocol.NameOffset] == 0)
                    activePrefixCount = index;
            }

            response = new Response(frame.QueryId, frame.Param,
                activePrefixCount, (byte[])payload.Clone(), records);
            return true;
        }

        public static bool TryEvaluateCache(Response response,
            IReadOnlyList<StandardItemResolution> resolutions,
            out CacheDecision decision, out string error)
        {
            decision = null;
            error = string.Empty;
            if (response == null)
            {
                error = "global-shop hot response is null";
                return false;
            }
            if (resolutions == null
                || resolutions.Count != response.ActivePrefixCount)
            {
                error = "global-shop hot standard-item resolution count mismatch";
                return false;
            }

            var patched = (byte[])response.RawPayload.Clone();
            for (var index = 0; index < response.ActivePrefixCount; index++)
            {
                var recordOffset = index * RecordSize;
                var resolution = resolutions[index];
                if (resolution.Found)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        patched.AsSpan(recordOffset +
                                       YbDbGlobalShopItemProtocol.LooksOffset,
                            sizeof(ushort)), resolution.CanonicalLooks);
                }

                BinaryPrimitives.WriteUInt16LittleEndian(
                    patched.AsSpan(recordOffset +
                                   YbDbGlobalShopItemProtocol.CategoryOffset,
                        sizeof(ushort)), NativeHotPage);
            }

            decision = new CacheDecision(patched,
                response.ActivePrefixCount,
                ConsolePrefix + response.ActivePrefixCount);
            return true;
        }

        public readonly struct StandardItemResolution
        {
            public StandardItemResolution(bool found, ushort canonicalLooks)
            {
                Found = found;
                CanonicalLooks = canonicalLooks;
            }

            public bool Found { get; }
            public ushort CanonicalLooks { get; }
        }

        public sealed class Response
        {
            internal Response(int ignoredQueryId, int ignoredHeaderParam,
                int activePrefixCount, byte[] rawPayload,
                YbDbGlobalShopItemProtocol.ItemRecord[] records)
            {
                IgnoredQueryId = ignoredQueryId;
                IgnoredHeaderParam = ignoredHeaderParam;
                ActivePrefixCount = activePrefixCount;
                RawPayload = rawPayload;
                Records = records;
            }

            public int IgnoredQueryId { get; }
            public int IgnoredHeaderParam { get; }
            public int ActivePrefixCount { get; }
            public byte[] RawPayload { get; }
            public YbDbGlobalShopItemProtocol.ItemRecord[] Records { get; }
            public bool UsesRoleOrSessionRouting => false;
            public bool CreatesPendingRequest => false;
        }

        public sealed class CacheDecision
        {
            internal CacheDecision(byte[] patchedPayload,
                int loadedRecordCount, string consoleMessage)
            {
                PatchedPayload = patchedPayload;
                LoadedRecordCount = loadedRecordCount;
                ConsoleMessage = consoleMessage;
            }

            public byte[] PatchedPayload { get; }
            public int LoadedRecordCount { get; }
            public string ConsoleMessage { get; }
            public bool AllocateCacheIfMissing => true;
            public bool OverwriteEntireCache => true;
            public bool CreatesSpecialItemCompanions => false;
            public bool EmitsCompletionBroadcast => false;
            public bool SendsClient815Directly => false;
            public bool SendsAck => false;
            public bool MutatesPlayerAccountInventoryOrDatabase => false;
            public bool WritesBusinessGameLog => false;
            public bool MutatesRuntime => false;
        }
    }
}
