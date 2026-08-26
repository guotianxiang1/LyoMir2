using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using DBSvr;

namespace DBSvr.Core
{
    public static class NativeRelationLogProtocol
    {
        public const ushort Command = 0x0040;
        private static readonly byte[] Separator =
            System.Text.Encoding.ASCII.GetBytes("@$&#$");
        private static readonly byte[] End =
            System.Text.Encoding.ASCII.GetBytes("#$@#&");
        private static readonly byte[] NewCharacterPrefix =
            System.Text.Encoding.ASCII.GetBytes("3@$&#$");
        private static readonly byte[] Zero =
            System.Text.Encoding.ASCII.GetBytes("0");
        private static readonly byte[] One =
            System.Text.Encoding.ASCII.GetBytes("1");

        // Native UserSoc+0x24/+0x28 are copied from the 21-byte ShortString
        // slots at LoginGate auth payload+0x0C/+0x36 (0x5CEE76/0x5CE87E).
        // Keep both as raw bytes: decoding and re-encoding would corrupt an
        // AnsiString containing non-ASCII account/session data.
        public const int AuthField24Offset = 12;
        public const int AuthField28Offset = 54;
        public const int AuthField24Capacity = 21;
        public const int AuthField28Capacity = 21;

        public static bool TryReadAuthField24(byte[] rawPayload,
            out byte[] value)
        {
            return TryReadAuthField(rawPayload, AuthField24Offset,
                AuthField24Capacity, out value);
        }

        public static bool TryReadAuthField28(byte[] rawPayload,
            out byte[] value)
        {
            return TryReadAuthField(rawPayload, AuthField28Offset,
                AuthField28Capacity, out value);
        }

        private static bool TryReadAuthField(byte[] rawPayload, int offset,
            int capacity, out byte[] value)
        {
            value = Array.Empty<byte>();
            if (rawPayload == null || offset < 0
                || rawPayload.Length < offset + capacity)
                return false;

            var field = rawPayload.AsSpan(offset, capacity);
            var length = field.IndexOf((byte)0);
            // Native 0x404E68 uses a bounded `repne scasb` and returns an
            // empty AnsiString when the 21-byte slot has no terminator.  Do
            // not copy an unterminated authentication window into the log.
            if (length < 0) length = 0;
            value = field.Slice(0, length).ToArray();
            return true;
        }

        /// <summary>
        /// Builds the successful-NewChr relation record emitted by native
        /// 0x590A54.  field24 and field28 deliberately retain their raw
        /// session-offset names until the writer for Self+0x28 is proven.
        /// </summary>
        public static byte[] BuildNewCharacterMessage(byte[] field24,
            byte[] field28, byte sex, byte[] characterName)
        {
            field24 ??= Array.Empty<byte>();
            field28 ??= Array.Empty<byte>();
            characterName ??= Array.Empty<byte>();

            using var output = new MemoryStream();
            output.Write(NewCharacterPrefix);
            output.Write(characterName);
            output.Write(Separator);
            output.Write(field28);
            output.Write(Separator);
            output.Write(Zero);
            output.Write(Separator);
            output.Write(field24);
            output.Write(Separator);
            output.Write(sex == 0 ? Zero : One);
            output.Write(End);
            return output.ToArray();
        }

        public static List<byte[]> BuildMessages(NativeType2Message request,
            Func<byte[], byte[]> ownerResolver,
            Func<DateTime> nowProvider = null)
        {
            var result = new List<byte[]>();
            if (request == null || request.Command != Command
                || ownerResolver == null)
                return result;
            var subcommand = unchecked((byte)request.Word2);
            var body = request.Suffix ?? Array.Empty<byte>();
            var expected = subcommand <= 3 ? 0x20
                         : subcommand <= 7 ? 0x54 : -1;
            if (expected < 0 || body.Length != expected) return result;

            if (subcommand == 5)
            {
                if (!TryRead(body, 4, 31, out var onlyX)) return result;
                Add(result, "11", onlyX);
                return result;
            }

            byte[] a;
            byte[] b;
            if (subcommand <= 3)
            {
                if (!TryRead(body, 0, 15, out a)
                    || !TryRead(body, 0x10, 15, out b))
                    return result;
            }
            else
            {
                if (!TryRead(body, 0x44, 15, out a)) return result;
                b = Array.Empty<byte>();
            }
            var ownerA = ownerResolver(a) ?? Array.Empty<byte>();
            var ownerB = ownerResolver(b) ?? Array.Empty<byte>();
            switch (subcommand)
            {
                case 0:
                    if (ownerA.Length == 0 || ownerB.Length == 0) return result;
                    Add(result, "1", "16", a, ownerA, "1", b, ownerB, "1");
                    Add(result, "1", "8", b, ownerB, "1", a, ownerA, "1");
                    break;
                case 1:
                    Add(result, "1", "4", a, ownerA, "1", b, ownerB, "1");
                    Add(result, "1", "2", b, ownerB, "1", a, ownerA, "1");
                    break;
                case 2:
                    Add(result, "2", "16", b, ownerB, "1", a, ownerA, "1");
                    Add(result, "2", "8", a, ownerA, "1", b, ownerB, "1");
                    break;
                case 3:
                    Add(result, "2", "4", a, ownerA, "1", b, ownerB, "1");
                    Add(result, "2", "2", b, ownerB, "1", a, ownerA, "1");
                    break;
                case 4:
                    if (ownerA.Length == 0) return result;
                    if (!TryRead(body, 4, 31, out var x)
                        || !TryRead(body, 0x24, 31, out var y))
                        return result;
                    var date = System.Text.Encoding.ASCII.GetBytes(
                        (nowProvider ?? (() => DateTime.Now))().ToString(
                            "yyyyMMdd", CultureInfo.InvariantCulture));
                    Add(result, "6", date, x, "5000", a, ownerA, "1",
                        Array.Empty<byte>());
                    Add(result, "7", x, a, ownerA, "1", y);
                    Add(result, "9", x, "1", a, ownerA, "1");
                    break;
                case 6:
                    if (!TryRead(body, 4, 31, out var x6)
                        || !TryRead(body, 0x24, 31, out var y6)
                        || ownerA.Length == 0)
                        return result;
                    Add(result, "7", x6, a, ownerA, "1", y6);
                    break;
                case 7:
                    if (!TryRead(body, 4, 31, out var x7)
                        || ownerA.Length == 0)
                        return result;
                    Add(result, "8", x7, a, ownerA, "1");
                    break;
            }
            return result;
        }

        private static void Add(List<byte[]> result, params object[] parts)
        {
            using var output = new MemoryStream();
            var first = true;
            foreach (var part in parts)
            {
                var bytes = part switch
                {
                    byte[] value => value,
                    string text => System.Text.Encoding.ASCII.GetBytes(text),
                    _ => Array.Empty<byte>()
                };
                if (!first) output.Write(Separator);
                output.Write(bytes);
                first = false;
            }
            output.Write(End);
            result.Add(output.ToArray());
        }

        private static bool TryRead(byte[] body, int offset, int capacity,
            out byte[] value)
        {
            value = null;
            if (body == null || offset < 0 || offset >= body.Length) return false;
            var length = body[offset];
            if (length > capacity || offset + 1 + length > body.Length)
                return false;
            value = body.AsSpan(offset + 1, length).ToArray();
            return true;
        }
    }

    public sealed class NativeRelationLogService
    {
        private readonly IPlayRecordService _records;
        private readonly string _directory;
        private readonly string _fileName;
        private readonly object _sync = new();
        private readonly Queue<byte[]> _pending = new();
        private readonly AutoResetEvent _wake = new(false);
        private Thread _worker;
        private bool _stopping;

        public NativeRelationLogService(IPlayRecordService records,
            ConfigManager config)
        {
            _records = records ?? throw new ArgumentNullException(nameof(records));
            if (config == null) throw new ArgumentNullException(nameof(config));
            var gameType = config.ReadInteger("TimeProtect", "GameType", 4);
            _directory = Path.Combine(AppContext.BaseDirectory,
                "relation", "log");
            _fileName = Path.Combine(_directory,
                $"{gameType}-{DBShare.nZoneIdx}-{DBShare.nGroupIdx}.urt");
        }

        public void Start()
        {
            lock (_sync)
            {
                if (_worker?.IsAlive == true) return;
                Directory.CreateDirectory(_directory);
                _stopping = false;
                _worker = new Thread(Consume)
                {
                    IsBackground = true,
                    Name = "DBSvr relation log"
                };
                _worker.Start();
            }
        }

        public void Stop()
        {
            Thread worker;
            lock (_sync)
            {
                _stopping = true;
                worker = _worker;
                _wake.Set();
            }
            if (worker?.IsAlive == true && Thread.CurrentThread != worker)
                worker.Join();
            lock (_sync)
                if (ReferenceEquals(_worker, worker)) _worker = null;
        }

        public bool Process(NativeType2Message request)
        {
            var messages = NativeRelationLogProtocol.BuildMessages(request,
                name =>
                {
                    return _records.TryGetNativeCharacterByName(name,
                        out var record)
                        ? record.PTIDBytes ?? Array.Empty<byte>()
                        : Array.Empty<byte>();
                });
            if (messages.Count == 0) return true;
            lock (_sync)
                foreach (var message in messages) _pending.Enqueue(message);
            return true;
        }

        /// <summary>
        /// Queues the relation record emitted by native 0x590A54 after a
        /// successful Native77 4012.  The caller supplies the proven raw
        /// Self+0x24, Self+0x28, sex and character-name fields.
        /// </summary>
        public bool EnqueueNewCharacter(byte[] field24, byte[] field28,
            byte sex, byte[] characterName)
        {
            var message = NativeRelationLogProtocol.BuildNewCharacterMessage(
                field24, field28, sex, characterName);
            lock (_sync)
            {
                _pending.Enqueue(message);
                _wake.Set();
            }
            return true;
        }

        public bool ProcessMasterReset(byte[] masterName, byte[] studentName)
            => ProcessMasterRelation(0, masterName, studentName);

        public bool ProcessMasterClear(byte[] masterName, byte[] studentName)
            => ProcessMasterRelation(2, masterName, studentName);

        private bool ProcessMasterRelation(ushort subcommand,
            byte[] masterName, byte[] studentName)
        {
            masterName ??= Array.Empty<byte>();
            studentName ??= Array.Empty<byte>();
            if (masterName.Length > 15 || studentName.Length > 15)
                return false;

            var body = new byte[0x20];
            WriteShortString(body, 0, masterName);
            WriteShortString(body, 0x10, studentName);
            return Process(new NativeType2Message
            {
                Command = NativeRelationLogProtocol.Command,
                Word2 = subcommand,
                Suffix = body
            });
        }

        private static void WriteShortString(byte[] destination, int offset,
            byte[] value)
        {
            destination[offset] = (byte)value.Length;
            value.CopyTo(destination, offset + 1);
        }

        private void Consume()
        {
            while (true)
            {
                List<byte[]> batch = null;
                lock (_sync)
                {
                    if (_pending.Count > 0)
                    {
                        batch = new List<byte[]>(_pending);
                        _pending.Clear();
                    }
                    else if (_stopping) return;
                }
                if (batch != null)
                    foreach (var message in batch)
                    {
                        WriteWithRetry(message);
                        if (!Volatile.Read(ref _stopping)) _wake.WaitOne(5);
                    }
                if (_stopping) continue;
                if (batch == null || batch.Count == 0) _wake.WaitOne(5);
            }
        }

        private void WriteWithRetry(byte[] message)
        {
            for (var retry = 0; retry < 10; retry++)
            {
                try
                {
                    using var stream = new FileStream(_fileName,
                        FileMode.OpenOrCreate, FileAccess.Write,
                        FileShare.ReadWrite);
                    stream.Seek(0, SeekOrigin.End);
                    stream.Write(message, 0, message.Length);
                    stream.Flush();
                    return;
                }
                catch (Exception ex) when (ex is IOException
                                           || ex is UnauthorizedAccessException)
                {
                    if (retry == 9)
                    {
                        DBShare.MainOutMessage(
                            "[Error]: TRelationLog.SaveToFile " + ex.Message);
                        return;
                    }
                    if (!Volatile.Read(ref _stopping)) Thread.Sleep(5);
                }
            }
        }
    }
}
