using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using SystemModule.Packet;

namespace GameSvr.Services
{
    public enum NativeType1PersistenceAckKind
    {
        None,
        HeroSave,
        PlayState
    }

    public enum NativeType1PersistenceAckDisposition
    {
        InvalidFrame,
        IgnoredStateWord,
        Processed
    }

    public readonly struct NativeType1CorrelationKey :
        IEquatable<NativeType1CorrelationKey>
    {
        public NativeType1CorrelationKey(int param1, int param2)
        {
            Param1 = param1;
            Param2 = param2;
        }

        public int Param1 { get; }
        public int Param2 { get; }

        public bool Equals(NativeType1CorrelationKey other) =>
            Param1 == other.Param1 && Param2 == other.Param2;

        public override bool Equals(object obj) =>
            obj is NativeType1CorrelationKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Param1, Param2);

        public static bool operator ==(NativeType1CorrelationKey left,
            NativeType1CorrelationKey right) => left.Equals(right);

        public static bool operator !=(NativeType1CorrelationKey left,
            NativeType1CorrelationKey right) => !left.Equals(right);
    }

    public readonly struct NativeType1PersistenceAck
    {
        internal NativeType1PersistenceAck(ushort command, ushort stateWord,
            NativeType1PersistenceAckKind kind,
            NativeType1CorrelationKey correlationKey)
        {
            Command = command;
            StateWord = stateWord;
            Kind = kind;
            CorrelationKey = correlationKey;
        }

        public ushort Command { get; }
        public ushort StateWord { get; }
        public NativeType1PersistenceAckKind Kind { get; }
        public NativeType1CorrelationKey CorrelationKey { get; }
        public int Param1 => CorrelationKey.Param1;
        public int Param2 => CorrelationKey.Param2;
    }

    public static class NativeType1PersistenceAckCodec
    {
        public const ushort HeroSaveCommand = 0x013C;
        public const ushort PlayStateCommand = 0x013D;
        public const int HeaderSize = 0x48;

        public static bool TryDecode(LegacyDbServerFrame frame,
            out NativeType1PersistenceAck acknowledgement)
        {
            acknowledgement = default;
            if (frame == null || frame.Type != 1
                || frame.Payload == null
                || frame.Payload.Length < HeaderSize)
                return false;

            var payload = frame.Payload.AsSpan();
            var command = BinaryPrimitives.ReadUInt16LittleEndian(payload);
            var kind = command switch
            {
                HeroSaveCommand => NativeType1PersistenceAckKind.HeroSave,
                PlayStateCommand => NativeType1PersistenceAckKind.PlayState,
                _ => NativeType1PersistenceAckKind.None
            };
            if (kind == NativeType1PersistenceAckKind.None)
                return false;

            acknowledgement = new NativeType1PersistenceAck(
                command,
                BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(2, 2)),
                kind,
                new NativeType1CorrelationKey(
                    BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(8, 4)),
                    BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(12, 4))));
            return true;
        }
    }

    public sealed class NativeType1PersistencePending
    {
        internal NativeType1PersistencePending(
            NativeType1CorrelationKey sessionKey,
            NativeType1CorrelationKey saveKey,
            bool heroSaveRequired)
        {
            SessionKey = sessionKey;
            SaveKey = saveKey;
            HeroSaveRequired = heroSaveRequired;
            CompletionFlags = heroSaveRequired
                ? (byte)0
                : NativeType1PersistenceCompletionHandler.HeroSaveCompletionFlag;
        }

        public NativeType1CorrelationKey SessionKey { get; }
        public NativeType1CorrelationKey SaveKey { get; }
        public bool HeroSaveRequired { get; }
        public byte CompletionFlags { get; private set; }

        public bool HeroSaveCompleted =>
            (CompletionFlags
             & NativeType1PersistenceCompletionHandler.HeroSaveCompletionFlag) != 0;

        public bool PlayStateCompleted =>
            (CompletionFlags
             & NativeType1PersistenceCompletionHandler.PlayStateCompletionFlag) != 0;

        public bool IsReady =>
            (CompletionFlags
             & NativeType1PersistenceCompletionHandler.RequiredCompletionFlags)
            == NativeType1PersistenceCompletionHandler.RequiredCompletionFlags;

        internal bool MarkCompletion(byte flag)
        {
            var previous = CompletionFlags;
            CompletionFlags |= flag;
            return previous != CompletionFlags;
        }

        internal void ClearCompletionFlags()
        {
            CompletionFlags = 0;
        }
    }

    public readonly struct NativeType1PersistenceAckResult
    {
        internal NativeType1PersistenceAckResult(
            NativeType1PersistenceAckDisposition disposition,
            NativeType1PersistenceAckKind kind,
            int matchedCount, int changedCount)
        {
            Disposition = disposition;
            Kind = kind;
            MatchedCount = matchedCount;
            ChangedCount = changedCount;
        }

        public NativeType1PersistenceAckDisposition Disposition { get; }
        public NativeType1PersistenceAckKind Kind { get; }
        public int MatchedCount { get; }
        public int ChangedCount { get; }
    }

    /// <summary>
    /// Independent state for native Type1 0x013C/0x013D acknowledgements.
    /// Registration is intentionally explicit: the current runtime has no
    /// 0x019E request owner and ordinary hero saves use zero correlation data.
    /// </summary>
    public sealed class NativeType1PersistenceCompletionHandler
    {
        public const byte HeroSaveCompletionFlag = 0x04;
        public const byte PlayStateCompletionFlag = 0x08;
        public const byte RequiredCompletionFlags = 0x0F;

        private readonly List<NativeType1PersistencePending> _pending = new();

        public int Count => _pending.Count;

        public bool TryRegister(NativeType1CorrelationKey sessionKey,
            NativeType1CorrelationKey saveKey, bool heroSaveRequired,
            out NativeType1PersistencePending pending)
        {
            foreach (var candidate in _pending)
            {
                if (candidate.SessionKey != sessionKey)
                    continue;
                pending = null;
                return false;
            }

            pending = new NativeType1PersistencePending(
                sessionKey, saveKey, heroSaveRequired);
            _pending.Add(pending);
            return true;
        }

        public bool Remove(NativeType1PersistencePending pending) =>
            pending != null && _pending.Remove(pending);

        public IReadOnlyList<NativeType1PersistencePending> Snapshot() =>
            _pending.ToArray();

        public void Clear()
        {
            _pending.Clear();
        }

        public NativeType1PersistenceAckResult Consume(
            LegacyDbServerFrame frame)
        {
            if (!NativeType1PersistenceAckCodec.TryDecode(
                    frame, out var acknowledgement))
                return new NativeType1PersistenceAckResult(
                    NativeType1PersistenceAckDisposition.InvalidFrame,
                    NativeType1PersistenceAckKind.None, 0, 0);

            if (acknowledgement.Kind
                == NativeType1PersistenceAckKind.PlayState
                && acknowledgement.StateWord != 1)
                return new NativeType1PersistenceAckResult(
                    NativeType1PersistenceAckDisposition.IgnoredStateWord,
                    acknowledgement.Kind, 0, 0);

            var matchedCount = 0;
            var changedCount = 0;
            if (acknowledgement.Kind
                == NativeType1PersistenceAckKind.HeroSave)
            {
                foreach (var pending in _pending)
                {
                    if (pending.SaveKey != acknowledgement.CorrelationKey)
                        continue;
                    matchedCount++;
                    if (pending.MarkCompletion(HeroSaveCompletionFlag))
                        changedCount++;
                }
            }
            else
            {
                foreach (var pending in _pending)
                {
                    if (pending.SessionKey != acknowledgement.CorrelationKey)
                        continue;
                    matchedCount = 1;
                    if (pending.MarkCompletion(PlayStateCompletionFlag))
                        changedCount = 1;
                    break;
                }
            }

            return new NativeType1PersistenceAckResult(
                NativeType1PersistenceAckDisposition.Processed,
                acknowledgement.Kind, matchedCount, changedCount);
        }
    }
}
