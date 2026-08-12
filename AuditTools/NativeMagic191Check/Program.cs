using GameSvr;
using SystemModule;

// NativeMagic191Check — audit for magic id 191 (凝冰) handler
// Verifies:
//   1. Field m_dwMagic191FreezeDeadline exists on TPlayObject
//   2. Gates: nil target, ghost, dead, bodyState 0x10, bodyState 0x3E
//   3. Deadline gate: [obj+0x300] > now → reject
//   4. ColdTime gate: key 191 remaining != 0 → reject
//   5. Success path: state 0x3E applied (value=8, duration=300000ms), deadline written, cooldown armed
//   6. Re-activation after expiry works

class Program
{
    static void Main()
    {
        Console.WriteLine("=== NativeMagic191Check ===");
        int passCount = 0;
        int totalCount = 0;

        void Assert(bool condition, string label)
        {
            totalCount++;
            if (!condition)
            {
                Console.WriteLine($"FAIL: {label}");
                Environment.Exit(1);
            }
            passCount++;
        }

        // Test 1: Field m_dwMagic191FreezeDeadline zero-initialized
        var caster = new TPlayObject();
        Assert(caster.m_dwMagic191FreezeDeadline == 0, "field initialized to zero");

        // Test 2: nil target rejects (TBaseObject is the base type; TPlayObject is a TBaseObject)
        Assert(!caster.TryActivateNativeMagic191(null, 1000), "nil target rejected");

        // Test 3: ghost target rejects
        var ghostTarget = new TPlayObject();
        ghostTarget.m_boGhost = true;
        Assert(!caster.TryActivateNativeMagic191(ghostTarget, 2000), "ghost target rejected");

        // Test 4: dead target rejects
        var deadTarget = new TPlayObject();
        deadTarget.m_boDeath = true;
        Assert(!caster.TryActivateNativeMagic191(deadTarget, 3000), "dead target rejected");

        // Test 5: target with bodyState 0x10 SET rejects
        var frozenTarget = new TPlayObject();
        frozenTarget.NativeApplyBodyState(0x10, 10000, 1);
        Assert(!caster.TryActivateNativeMagic191(frozenTarget, 4000), "0x10 SET rejects");

        // Test 6: target with bodyState 0x3E SET rejects
        var already3E = new TPlayObject();
        already3E.NativeApplyBodyState(0x3E, 10000, 8);
        Assert(!caster.TryActivateNativeMagic191(already3E, 5000), "0x3E SET rejects");

        // Test 7: clean target, first activation at now=10000 succeeds
        var cleanTarget = new TPlayObject();
        Assert(caster.TryActivateNativeMagic191(cleanTarget, 10000), "first activation succeeds");

        // Test 8: state 0x3E is applied to target
        Assert(cleanTarget.HasNativeActiveState(0x3E), "state 0x3E applied to target");

        // Test 9: deadline = 10000 + 300000 = 310000
        Assert(caster.m_dwMagic191FreezeDeadline == 310000, "deadline written = 10000 + 300000");

        // Test 10: coldTime key 0xBF is armed (remaining should be 300000)
        Assert(caster.QueryNativeColdTime(0xBF) == 300000, "coldTime key 0xBF armed 300000ms");

        // Test 11: state node has value=8 and durationMs=300000
        var node = cleanTarget.m_pNativeBodyStateDurationHead;
        GameSvr.TBaseObject.NativeBodyStateDurationNode stateNode = null;
        while (node != null)
        {
            if (node.StateId == 0x3E)
            {
                stateNode = node;
                break;
            }
            node = node.Next;
        }
        Assert(stateNode != null, "0x3E state node found");
        Assert(stateNode.Value == 8, "state node value is 8");
        Assert(stateNode.DurationMs == 300000, "state node duration is 300000ms");

        // Test 12: second activation at same tick fails (deadline gate)
        var target2 = new TPlayObject();
        Assert(!caster.TryActivateNativeMagic191(target2, 10000), "deadline gate: same tick rejects");

        // Test 13: advancing to deadline-1 still rejects
        Assert(!caster.TryActivateNativeMagic191(target2, 309999), "deadline gate: deadline-1 rejects");

        // Test 14: at deadline exactly (remaining = 0), deadline gate passes; cold time still active
        // At now=310000: remaining = 310000 - 310000 = 0 → passes deadline gate
        // But coldTime was armed with 300000ms at now=10000, so it has remaining ~0 at 310000
        // ProcessNativeColdTimes(310000) will expire it
        caster.ProcessNativeColdTimes(310000);
        Assert(caster.QueryNativeColdTime(0xBF) == 0, "coldTime expired at now=310000");

        // Test 15: both gates passed → activation succeeds
        var target3 = new TPlayObject();
        Assert(caster.TryActivateNativeMagic191(target3, 310000), "second activation after both gates pass");

        // Test 16: new deadline = 310000 + 300000 = 610000
        Assert(caster.m_dwMagic191FreezeDeadline == 610000, "deadline updated on second activation");

        // Test 17: state 0x3E applied to target3
        Assert(target3.HasNativeActiveState(0x3E), "state applied to target3");

        // Test 18: coldTime re-armed
        Assert(caster.QueryNativeColdTime(0xBF) > 0, "coldTime re-armed after second activation");

        // Test 19: verify SKILL_191 constant matches native key
        Assert(SpellsDef.SKILL_191 == 191, "SKILL_191 == 191");
        Assert(TBaseObject.NativeMagic191ColdTimeKey == 0xBF, "cold time key = 0xBF = 191");
        Assert(TBaseObject.NativeMagic191Id == 191, "magic id constant = 191");
        Assert(TBaseObject.NativeMagic191DurationMilliseconds == 300000, "duration = 300000ms");
        Assert(TBaseObject.NativeMagic191StateId == 0x3E, "state id = 0x3E");
        Assert(TBaseObject.NativeMagic191StateValue == 8, "state value = 8");

        Console.WriteLine($"PASS: {passCount}/{totalCount} assertions");
    }
}
