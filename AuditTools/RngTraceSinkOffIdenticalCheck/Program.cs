using SystemModule;

// RandSeed cutover (task #78) — step 2 guard: the RngTraceSink must be OFF by
// default (facade byte-identical), and ON it must record every live draw with the
// returned value, contiguous ordinals, and the ambient owner tag.

try
{
    var rng = RandomNumber.GetInstance();

    // 1. Default OFF => byte-identical facade: draws still happen, NOTHING recorded.
    Assert(!RngTraceSink.Enabled, "sink must default OFF");
    RngTraceSink.Reset();
    for (int i = 0; i < 50; i++)
    {
        int a = rng.Random(100);           Assert(a >= 0 && a < 100, "Random(100) range");
        int b = rng.Random(5, 9);          Assert(b >= 5 && b < 9, "Random(min,max) range");
        int c = rng.GetRandomNumber(5, 9); Assert(c >= 5 && c <= 9, "GetRandomNumber inclusive");
        int d = rng.Random();              Assert(d >= 0, "Random() advance");
    }
    Assert(RngTraceSink.Log.Count == 0, "OFF must record nothing (byte-identical)");

    // 2. ON => every draw recorded; Result == the returned value; ordinals contiguous; owner captured.
    RngTraceSink.Reset();
    RngTraceSink.CurrentOwner = "D1/Test";
    RngTraceSink.Enabled = true;

    int r0 = rng.Random(1000);         AssertLast(RngTraceApi.Random, 1000, 0, r0, "Random(value)");
    int r1 = rng.Random(3, 7);         AssertLast(RngTraceApi.RandomMinMax, 3, 7, r1, "Random(min,max)");
    int r2 = rng.GetRandomNumber(3, 7);AssertLast(RngTraceApi.GetRandomNumber, 3, 7, r2, "GetRandomNumber");
    int r3 = rng.Random();             AssertLast(RngTraceApi.ParamlessAdvance, 0, 0, r3, "Random() advance");

    Assert(RngTraceSink.Log.Count == 4, "ON must record every draw");
    for (int i = 0; i < RngTraceSink.Log.Count; i++)
    {
        Assert(RngTraceSink.Log[i].Ordinal == i, "ordinals contiguous");
        Assert(RngTraceSink.Log[i].Owner == "D1/Test", "owner tag captured");
        Assert(RngTraceSink.Log[i].SeedBefore == 0 && RngTraceSink.Log[i].SeedAfter == 0,
            "seed 0 pre-swap (.NET facade; real seed after step-6 DelphiRandom swap)");
    }

    // 3. OFF again => no new records (clean re-disable).
    long before = RngTraceSink.Count;
    RngTraceSink.Enabled = false;
    for (int i = 0; i < 20; i++) rng.Random(50);
    Assert(RngTraceSink.Count == before, "re-disabled must record nothing");

    Console.WriteLine(
        "PASS RngTraceSinkOffIdenticalCheck off=byte-identical(0-records) on=4-apis-recorded " +
        "result==returned ordinals=contiguous owner=captured seed=0-pre-swap redisable=clean (EXIT 0)");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"RngTraceSinkOffIdenticalCheck FAIL: {ex.Message}");
    return 1;
}

static void Assert(bool cond, string msg)
{
    if (!cond) throw new Exception(msg);
}

static void AssertLast(RngTraceApi api, long a0, long a1, long result, string tag)
{
    var last = RngTraceSink.Log[^1];
    Assert(last.Api == api, tag + ": api");
    Assert(last.Arg0 == a0, tag + ": arg0");
    Assert(last.Arg1 == a1, tag + ": arg1");
    Assert(last.Result == result, tag + ": result==returned");
}
