using GameSvr.Services;

NativeGlobalBreakSettings.Reset();
AssertDefaults("explicit reset");

NativeGlobalBreakSettings.SetSlot(
    NativeGlobalBreakSettings.ProcBaseChanceIndex, int.MinValue);
NativeGlobalBreakSettings.SetSlot(
    NativeGlobalBreakSettings.MaxEquipmentBreakLevelIndex, int.MaxValue);
NativeGlobalBreakSettings.SetSlot(
    NativeGlobalBreakSettings.BreakLevelIndex, -1);
NativeGlobalBreakSettings.SetSlot(
    NativeGlobalBreakSettings.CrazyBreakLevelIndex, 65537);
Equal(int.MinValue, NativeGlobalBreakSettings.ProcBaseChance,
    "slot 1 full Int32 storage");
Equal(int.MaxValue, NativeGlobalBreakSettings.MaxEquipmentBreakLevel,
    "slot 2 full Int32 storage");
Equal(-1, NativeGlobalBreakSettings.BreakLevel,
    "slot 3 signed Int32 storage");
Equal(65537, NativeGlobalBreakSettings.CrazyBreakLevel,
    "slot 4 has no ushort truncation");

NativeGlobalBreakSettings.ResetAndLoad(null);
AssertDefaults("missing callback");

var order = new List<int>();
NativeGlobalBreakSettings.ResetAndLoad((int index, out int value) =>
{
    order.Add(index);
    value = index switch
    {
        1 => int.MinValue,
        2 => -1,
        3 => int.MaxValue,
        4 => 65537,
        _ => throw new InvalidOperationException()
    };
    return true;
});
SequenceEqual(new[] { 1, 2, 3, 4 }, order,
    "callback index order");
Equal(int.MinValue, NativeGlobalBreakSettings.ProcBaseChance,
    "callback slot 1");
Equal(-1, NativeGlobalBreakSettings.MaxEquipmentBreakLevel,
    "callback slot 2");
Equal(int.MaxValue, NativeGlobalBreakSettings.BreakLevel,
    "callback slot 3");
Equal(65537, NativeGlobalBreakSettings.CrazyBreakLevel,
    "callback slot 4");

NativeGlobalBreakSettings.ResetAndLoad((int index, out int value) =>
{
    value = index * 10;
    return index != NativeGlobalBreakSettings.MaxEquipmentBreakLevelIndex;
});
Equal(10, NativeGlobalBreakSettings.ProcBaseChance,
    "successful value before failed callback");
Equal(NativeGlobalBreakSettings.DefaultMaxEquipmentBreakLevel,
    NativeGlobalBreakSettings.MaxEquipmentBreakLevel,
    "false callback retains slot default");
Equal(30, NativeGlobalBreakSettings.BreakLevel,
    "false callback did not stop later slot 3");
Equal(40, NativeGlobalBreakSettings.CrazyBreakLevel,
    "false callback did not stop later slot 4");

order.Clear();
NativeGlobalBreakSettings.SetSlot(
    NativeGlobalBreakSettings.ProcBaseChanceIndex, 901);
NativeGlobalBreakSettings.SetSlot(
    NativeGlobalBreakSettings.MaxEquipmentBreakLevelIndex, 902);
NativeGlobalBreakSettings.SetSlot(
    NativeGlobalBreakSettings.BreakLevelIndex, 903);
NativeGlobalBreakSettings.SetSlot(
    NativeGlobalBreakSettings.CrazyBreakLevelIndex, 904);
NativeGlobalBreakSettings.ResetAndLoad((int index, out int value) =>
{
    order.Add(index);
    if (index == NativeGlobalBreakSettings.ProcBaseChanceIndex)
    {
        Equal(NativeGlobalBreakSettings.DefaultProcBaseChance,
            NativeGlobalBreakSettings.ProcBaseChance,
            "reset occurs before callback index 1");
        Equal(NativeGlobalBreakSettings.DefaultMaxEquipmentBreakLevel,
            NativeGlobalBreakSettings.MaxEquipmentBreakLevel,
            "all slots reset before callback index 1");
    }

    value = index * 100;
    if (index == NativeGlobalBreakSettings.MaxEquipmentBreakLevelIndex)
        throw new ApplicationException("expected per-index failure");
    return true;
});
SequenceEqual(new[] { 1, 2, 3, 4 }, order,
    "exception callback index order");
Equal(100, NativeGlobalBreakSettings.ProcBaseChance,
    "exception scenario slot 1");
Equal(NativeGlobalBreakSettings.DefaultMaxEquipmentBreakLevel,
    NativeGlobalBreakSettings.MaxEquipmentBreakLevel,
    "exception retains slot default");
Equal(300, NativeGlobalBreakSettings.BreakLevel,
    "exception did not stop slot 3");
Equal(400, NativeGlobalBreakSettings.CrazyBreakLevel,
    "exception did not stop slot 4");

for (var index = 1; index <= 4; index++)
    Equal(NativeGlobalBreakSettings.GetSlot(index),
        ReadProjectedSlot(index), $"slot {index} projection");

Throws<ArgumentOutOfRangeException>(() =>
    NativeGlobalBreakSettings.GetSlot(0), "invalid read index");
Throws<ArgumentOutOfRangeException>(() =>
    NativeGlobalBreakSettings.SetSlot(5, 1), "invalid write index");

Console.WriteLine("NativeGlobalBreakSettingsCompatCheck PASS");

static int ReadProjectedSlot(int index) => index switch
{
    1 => NativeGlobalBreakSettings.ProcBaseChance,
    2 => NativeGlobalBreakSettings.MaxEquipmentBreakLevel,
    3 => NativeGlobalBreakSettings.BreakLevel,
    4 => NativeGlobalBreakSettings.CrazyBreakLevel,
    _ => throw new ArgumentOutOfRangeException(nameof(index))
};

static void AssertDefaults(string scenario)
{
    Equal(15, NativeGlobalBreakSettings.ProcBaseChance,
        $"{scenario} slot 1 default");
    Equal(100, NativeGlobalBreakSettings.MaxEquipmentBreakLevel,
        $"{scenario} slot 2 default");
    Equal(0, NativeGlobalBreakSettings.BreakLevel,
        $"{scenario} slot 3 default");
    Equal(0, NativeGlobalBreakSettings.CrazyBreakLevel,
        $"{scenario} slot 4 default");
}

static void SequenceEqual<T>(IEnumerable<T> expected,
    IEnumerable<T> actual, string message)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException(message);
}

static void Throws<TException>(Action action, string message)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }
    throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"{message}: expected={expected} actual={actual}");
    }
}
