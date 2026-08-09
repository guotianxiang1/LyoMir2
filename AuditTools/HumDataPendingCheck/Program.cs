using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using DBSvr.Core;
using GameSvr;
using SystemModule;
using SystemModule.Packet;

namespace HumDataPendingCheck
{
    internal static class Program
    {
        private static readonly Type ServiceType = typeof(HumDataService);
        private static readonly FieldInfo PendingLoadsField = RequireField("PendingLoads");
        private static readonly FieldInfo CachedLoadsField = RequireField("CachedLoads");
        private static readonly Encoding Gbk = CreateGbk();
        private static readonly List<string> Failures = new();

        public static async Task<int> Main()
        {
            await Run("early response is retained", TestEarlyResponse);
            await Run("normal response completes waiter", TestNormalResponse);
            await Run("response keys are case-insensitive", TestCaseInsensitiveResponse);
            await Run("unrelated response remains isolated", TestUnrelatedResponseIsolation);
            await Run("duplicate response keeps first packet", TestDuplicateResponse);
            await Run("timeout removes pending request", TestTimeoutCleanup);
            await Run("expired cached response is rejected", TestExpiredCachedResponse);
            await Run("concurrent responses route by key", TestConcurrentResponses);
            await Run("disconnect releases waiters and clears cache", TestDisconnectCleanup);

            HumDataService.NotifyDisconnected();
            if (PendingCount() != 0 || CachedCount() != 0)
                Failures.Add($"FAIL final cleanup: pending={PendingCount()}, cached={CachedCount()}");

            if (Failures.Count != 0)
            {
                Console.Error.WriteLine(string.Join(Environment.NewLine, Failures));
                return 1;
            }

            Console.WriteLine("HumData pending-response regression checks passed (9/9)." );
            return 0;
        }

        private static async Task Run(string name, Func<Task> test)
        {
            HumDataService.NotifyDisconnected();
            try
            {
                await test();
                Console.WriteLine("PASS " + name);
            }
            catch (Exception ex)
            {
                Failures.Add("FAIL " + name + ": " + ex.Message);
            }
            finally
            {
                HumDataService.NotifyDisconnected();
            }
        }

        private static Task TestEarlyResponse()
        {
            HumDataService.AddNativeLoadFrame(Frame("early", "hero", 11));
            Equal(1, CachedCount(), "early cache count");

            var result = Load("early", "hero");
            Check(result.Success, "early response was not accepted");
            Equal((ushort)11, result.Record.Data.Abil.Level, "early response level");
            Equal(0, CachedCount(), "early response was not consumed");
            return Task.CompletedTask;
        }

        private static async Task TestNormalResponse()
        {
            var loadTask = StartLoad("normal", "hero");
            await WaitForCount(PendingCount, 1, "normal pending registration");

            HumDataService.AddNativeLoadFrame(Frame("normal", "hero", 12));
            var result = await loadTask.WaitAsync(TimeSpan.FromSeconds(2));
            Check(result.Success, "normal response was not accepted");
            Equal((ushort)12, result.Record.Data.Abil.Level, "normal response level");
            Equal(0, PendingCount(), "normal pending cleanup");
        }

        private static async Task TestCaseInsensitiveResponse()
        {
            var loadTask = StartLoad("mixedaccount", "mixedhero");
            await WaitForCount(PendingCount, 1, "case-insensitive pending registration");

            HumDataService.AddNativeLoadFrame(Frame("MixedAccount", "MixedHero", 13));
            var result = await loadTask.WaitAsync(TimeSpan.FromSeconds(2));
            Check(result.Success, "case-insensitive response was not accepted");
            Equal((ushort)13, result.Record.Data.Abil.Level,
                "case-insensitive response level");
        }

        private static async Task TestUnrelatedResponseIsolation()
        {
            var firstTask = StartLoad("account-a", "hero-a");
            await WaitForCount(PendingCount, 1, "isolated pending registration");

            HumDataService.AddNativeLoadFrame(Frame("account-b", "hero-b", 22));
            await Task.Delay(50);
            Check(!firstTask.IsCompleted, "unrelated response completed the wrong waiter");
            Equal(1, PendingCount(), "unrelated response pending count");
            Equal(1, CachedCount(), "unrelated response cache count");

            HumDataService.AddNativeLoadFrame(Frame("account-a", "hero-a", 21));
            var first = await firstTask.WaitAsync(TimeSpan.FromSeconds(2));
            var second = Load("account-b", "hero-b");
            Check(first.Success && second.Success, "isolated responses were not accepted");
            Equal((ushort)21, first.Record.Data.Abil.Level, "first isolated level");
            Equal((ushort)22, second.Record.Data.Abil.Level, "second isolated level");
        }

        private static async Task TestDuplicateResponse()
        {
            var completion = InsertPending("duplicate", "hero");
            HumDataService.AddNativeLoadFrame(Frame("duplicate", "hero", 31));
            HumDataService.AddNativeLoadFrame(Frame("duplicate", "hero", 32));

            var actual = await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Equal((ushort)31, actual.HumanRecord.Data.Abil.Level,
                "duplicate response replaced first packet");
            Equal(1, PendingCount(), "manual duplicate pending registration");
        }

        private static Task TestTimeoutCleanup()
        {
            var stopwatch = Stopwatch.StartNew();
            var result = Load("timeout", "hero");
            stopwatch.Stop();

            Check(!result.Success, "empty request unexpectedly completed");
            Check(stopwatch.ElapsedMilliseconds >= 4_500,
                $"timeout returned too early ({stopwatch.ElapsedMilliseconds} ms)");
            Check(stopwatch.ElapsedMilliseconds < 7_500,
                $"timeout returned too late ({stopwatch.ElapsedMilliseconds} ms)");
            Equal(0, PendingCount(), "timeout pending cleanup");
            return Task.CompletedTask;
        }

        private static async Task TestExpiredCachedResponse()
        {
            HumDataService.AddNativeLoadFrame(Frame("expired", "hero", 41));
            SetOnlyCachedReceivedAt(Environment.TickCount64 - 31_000);

            var loadTask = StartLoad("expired", "hero");
            await WaitForCount(PendingCount, 1, "expired response pending registration");
            Equal(0, CachedCount(), "expired response cache removal");

            HumDataService.NotifyDisconnected();
            var result = await loadTask.WaitAsync(TimeSpan.FromSeconds(2));
            Check(!result.Success, "expired cached response was accepted");
        }

        private static async Task TestConcurrentResponses()
        {
            const int count = 16;
            var tasks = Enumerable.Range(0, count)
                .Select(index => StartLoad($"parallel-{index}", $"hero-{index}"))
                .ToArray();
            await WaitForCount(PendingCount, count, "concurrent pending registration",
                TimeSpan.FromSeconds(4));

            Parallel.For(0, count, index =>
                HumDataService.AddNativeLoadFrame(Frame($"parallel-{index}",
                    $"hero-{index}", (ushort)(100 + index))));

            var results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(3));
            for (var index = 0; index < count; index++)
            {
                Check(results[index].Success, $"concurrent response {index} failed");
                Equal((ushort)(100 + index), results[index].Record.Data.Abil.Level,
                    $"concurrent response {index} level");
            }
            Equal(0, PendingCount(), "concurrent pending cleanup");
        }

        private static async Task TestDisconnectCleanup()
        {
            var loadTask = StartLoad("disconnect", "waiter");
            await WaitForCount(PendingCount, 1, "disconnect pending registration");
            HumDataService.AddNativeLoadFrame(Frame("disconnect", "cached", 61));
            Equal(1, CachedCount(), "disconnect precondition cache count");

            HumDataService.NotifyDisconnected();
            var result = await loadTask.WaitAsync(TimeSpan.FromSeconds(2));
            Check(!result.Success, "disconnect completed waiter as a successful load");
            Equal(0, PendingCount(), "disconnect pending cleanup");
            Equal(0, CachedCount(), "disconnect cache cleanup");
        }

        private static Task<LoadResult> StartLoad(string account, string character)
        {
            return Task.Factory.StartNew(() => Load(account, character),
                CancellationToken.None, TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        private static LoadResult Load(string account, string character)
        {
            var record = new THumDataInfo();
            var success = HumDataService.LoadHumRcdFromDB(account, character,
                "127.0.0.1", ref record, 1, out var nativeLoad);
            return new LoadResult(success, record, nativeLoad);
        }

        private static LegacyDbServerFrame Frame(string account, string character,
            ushort level)
        {
            var raw = new byte[NativeHumanDataCodec.DataRecordSize];
            WriteShortString(raw, 0x0000, 15, character);
            WriteShortString(raw, 0x0010, 15, "3");
            WriteShortString(raw, 0x0020, 20, account);
            BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(0x3C, 2), level);
            raw[0x3E] = 1;
            raw[0x3F] = 1;
            raw[0x40] = 2;

            var payload = new byte[NativeHumanDbCodec.ScriptDataOffset];
            BinaryPrimitives.WriteUInt16LittleEndian(payload,
                NativeHumanDbCodec.LoadCommand);
            WriteShortString(payload, NativeHumanDbCodec.AccountOffset, 20, account);
            WriteShortString(payload, NativeHumanDbCodec.CharacterOffset, 15,
                character);
            raw.CopyTo(payload, NativeHumanDbCodec.NativeDataOffset);
            return new LegacyDbServerFrame(1, 0, payload);
        }

        private static void WriteShortString(byte[] destination, int offset,
            int capacity, string value)
        {
            var bytes = Gbk.GetBytes(value);
            Check(bytes.Length <= capacity,
                $"fixture value '{value}' exceeds SS{capacity}");
            destination[offset] = (byte)bytes.Length;
            bytes.CopyTo(destination, offset + 1);
        }

        private static TaskCompletionSource<NativeHumanLoadData> InsertPending(
            string account, string character)
        {
            var dictionary = PendingLoadsField.GetValue(null)
                ?? throw new InvalidOperationException("PendingLoads is null");
            var pendingType = ServiceType.GetNestedType("PendingLoad",
                    BindingFlags.NonPublic)
                ?? throw new MissingMemberException(ServiceType.FullName, "PendingLoad");
            var pending = Activator.CreateInstance(pendingType, nonPublic: true)
                ?? throw new InvalidOperationException("PendingLoad construction failed");
            var completion = (TaskCompletionSource<NativeHumanLoadData>)(
                pendingType.GetProperty("Completion")?.GetValue(pending)
                ?? throw new MissingMemberException(pendingType.FullName, "Completion"));
            var tryAdd = dictionary.GetType().GetMethod("TryAdd")
                ?? throw new MissingMethodException(dictionary.GetType().FullName, "TryAdd");
            var key = account + '\0' + character;
            Check((bool)tryAdd.Invoke(dictionary, new[] { key, pending })!,
                "manual pending registration failed");
            return completion;
        }

        private static void SetOnlyCachedReceivedAt(long receivedAt)
        {
            var dictionary = CachedLoadsField.GetValue(null)
                ?? throw new InvalidOperationException("CachedLoads is null");
            var values = (System.Collections.IEnumerable)(dictionary.GetType()
                    .GetProperty("Values")?.GetValue(dictionary)
                ?? throw new MissingMemberException(dictionary.GetType().FullName,
                    "Values"));
            var entries = values.Cast<object>().ToArray();
            Equal(1, entries.Length, "cached entry count before expiration");
            var field = entries[0].GetType().GetField("ReceivedAt")
                ?? throw new MissingFieldException(entries[0].GetType().FullName,
                    "ReceivedAt");
            field.SetValue(entries[0], receivedAt);
        }

        private static int PendingCount() => DictionaryCount(PendingLoadsField);

        private static int CachedCount() => DictionaryCount(CachedLoadsField);

        private static int DictionaryCount(FieldInfo field)
        {
            var dictionary = field.GetValue(null);
            return (int)(dictionary?.GetType().GetProperty("Count")
                ?.GetValue(dictionary) ?? -1);
        }

        private static async Task WaitForCount(Func<int> getCount, int expected,
            string message, TimeSpan? timeout = null)
        {
            var deadline = Environment.TickCount64
                           + (long)(timeout ?? TimeSpan.FromSeconds(2)).TotalMilliseconds;
            while (getCount() != expected && Environment.TickCount64 < deadline)
                await Task.Delay(5);
            Equal(expected, getCount(), message);
        }

        private static FieldInfo RequireField(string name) =>
            ServiceType.GetField(name, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingFieldException(ServiceType.FullName, name);

        private static Encoding CreateGbk()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(936);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Equal<T>(T expected, T actual, string message)
            where T : notnull
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException(
                    $"{message}: expected {expected}, actual {actual}");
        }

        private readonly record struct LoadResult(bool Success, THumDataInfo Record,
            NativeHumanLoadData NativeLoad);
    }
}

namespace GameSvr
{
    public sealed class GameSvrConfig
    {
        public int nLoadDBErrorCount;
        public int nLoadDBCount;
        public int nSaveDBCount;
    }

    public sealed class TestDataServer
    {
        public bool Connected { get; set; }

        public bool SendNativeFrame(byte[] wire) => false;
    }

    public static class M2Share
    {
        public static TestDataServer DataServer;
        public static GameSvrConfig g_Config = new();

        public static void ErrorMessage(string message)
        {
        }
    }
}
