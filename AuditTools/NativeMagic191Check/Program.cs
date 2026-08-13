using GameSvr;
using SystemModule;

// NativeMagic191Check — audit for magic id 191 (凝冰), sub_6EF340.
//
// Register roles are fixed by 0x6EF351 `mov ebx,ecx` (TARGET) and 0x6EF356
// `mov esi,eax` (CASTER); neither register is clobbered across the body, so
// every side effect below is attributable.
//
//   gates      target validity sub_767498 (0x6EF378), target state 0x10
//              (0x6EF385), target state 0x3E (0x6EF39A)
//   deadline   read  0x6EF3C0 `mov eax,[ebx+0x300]`   <- TARGET
//              write 0x6EF40C `mov [ebx+0x300],eax`   <- TARGET
//   cooldown   query 0x6EF3B8 [caster.vmt+0x1F4], arm 0x6EF429 [caster.vmt+0x1F0]
//   state      0x6EF3F2 push 0 / 0x6EF3F4 mov cx,8 / 0x6EF3F8 mov dl,0x3E,
//              through [target.vmt+0x1A8] = sub_76B478, which does
//              `movzx eax,di` + `imul ecx,eax,0x3E8` at 0x76B489/0x76B48C and
//              forwards its own stack argument as the node value.
//
// Three earlier expectations contradicted those bytes and are corrected here:
//   * the deadline was asserted on the caster; 0x6EF40C writes the target;
//   * the state duration was asserted as 300000 ms; the 8 at 0x6EF3F4 is
//     SECONDS and becomes 8000 ms. 300000 (0x493E0 at 0x6EF407) is the
//     target's protection window, a different quantity;
//   * the node value was asserted as 8; the value slot carries the 0 pushed at
//     0x6EF3F2, and the state lands on the obj+0xDC timed-ability list
//     (sub_7730D0), not on the separate NativeBodyStateDuration scaffolding.

class Program
{
    static int Main()
    {
        try
        {
            Diagnose("enter-main");
            Diagnose("prepare-runtime-config");
            PrepareRuntimeConfig();
            Diagnose("before-new-TPlayObject");
            _ = new TPlayObject();
            Diagnose("after-new-TPlayObject");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                "INCOMPLETE: TPlayObject construction/type-init failed before magic-191 assertions.");
            Console.Error.WriteLine(ex.ToString());
            return 2;
        }

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

        // Test 1: the deadline field is zero-initialised
        var caster = new TPlayObject();
        Assert(caster.m_dwMagic191FreezeDeadline == 0, "field initialized to zero");

        // Test 2: nil target rejects (sub_767498 leads with `test esi,esi / je`)
        Assert(!caster.TryActivateNativeMagic191(null, 1000), "nil target rejected");

        // Test 3: ghost target rejects (0x7674B0 cmp byte [target+0x73],0)
        var ghostTarget = new TPlayObject();
        ghostTarget.m_boGhost = true;
        Assert(!caster.TryActivateNativeMagic191(ghostTarget, 2000), "ghost target rejected");

        // Test 4: dead target rejects (0x7674A7 call sub_772DA8 = byte [target+0x74])
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

        // Test 8: state 0x3E is applied to the target
        Assert(cleanTarget.HasNativeActiveState(0x3E), "state 0x3E applied to target");

        // Test 9: the deadline lands on the TARGET, 10000 + 300000
        Assert(cleanTarget.m_dwMagic191FreezeDeadline == 310000,
            "target deadline written = 10000 + 300000 (0x6EF40C mov [ebx+0x300],eax)");

        // Test 9b: and NOT on the caster. This is the exact inversion the old
        // expectation encoded, so it is the assertion that fails first if the
        // deadline is ever moved back onto the caster.
        Assert(caster.m_dwMagic191FreezeDeadline == 0,
            "caster deadline untouched: ebx is the target at 0x6EF3C0 and 0x6EF40C");

        // Test 10: coldTime key 0xBF armed on the caster with sub_6EF4F0's default
        Assert(caster.QueryNativeColdTime(0xBF) == 300000, "coldTime key 0xBF armed 300000ms");

        // Test 11: the node is on the timed-ability list with value 0 and 8000 ms
        Assert(cleanTarget.GetNativeTimedAbilityValue(0x3E) == 0,
            "state node value is the 0 pushed at 0x6EF3F2, not the duration");
        Assert(cleanTarget.GetNativeTimedAbilityRemainingMilliseconds(0x3E) == 8000,
            "state node duration is 8 * 1000 (0x76B48C imul ecx,eax,0x3E8)");

        // Test 12: a second cast while the caster's cooldown is running is refused.
        // target2 has never been frozen, so its own protection window is zero and
        // the only gate that can fire is the cooldown at 0x6EF3F0.
        var target2 = new TPlayObject();
        Assert(!caster.TryActivateNativeMagic191(target2, 10000), "cooldown gate: same tick rejects");
        Assert(!target2.HasNativeActiveState(0x3E), "cooldown refusal applied no state");

        // Test 13: still refused just before the cooldown would elapse. Nothing has
        // ticked the table, so the remaining value is untouched, and 0x6EF3F0 is a
        // `jne`: any nonzero remainder refuses.
        Assert(!caster.TryActivateNativeMagic191(target2, 309999), "cooldown gate: deadline-1 rejects");

        // Test 14: the tick expires it
        caster.ProcessNativeColdTimes(310000);
        Assert(caster.QueryNativeColdTime(0xBF) == 0, "coldTime expired at now=310000");

        // Test 15: both gates open -> success
        var target3 = new TPlayObject();
        Assert(caster.TryActivateNativeMagic191(target3, 310000), "second activation after both gates pass");

        // Test 16: target3 carries the new deadline; the caster still carries none
        Assert(target3.m_dwMagic191FreezeDeadline == 610000, "target3 deadline = 310000 + 300000");
        Assert(caster.m_dwMagic191FreezeDeadline == 0, "caster deadline still untouched");
        Assert(cleanTarget.m_dwMagic191FreezeDeadline == 310000,
            "the first target's window is independent of the second's");

        // Test 17: state applied to target3
        Assert(target3.HasNativeActiveState(0x3E), "state applied to target3");

        // Test 18: coldTime re-armed
        Assert(caster.QueryNativeColdTime(0xBF) > 0, "coldTime re-armed after second activation");

        // Test 19: the protection window in isolation. Fresh casters have no
        // cooldown, so only [target+0x300] can refuse.
        var protectedTarget = new TPlayObject();
        protectedTarget.m_dwMagic191FreezeDeadline = 500000;
        Assert(!new TPlayObject().TryActivateNativeMagic191(protectedTarget, 499999),
            "protection window: one tick before expiry rejects");
        // 0x6EF3C9 `ja` is strict, so at the deadline itself the remainder is zero
        // and 0x6EF3D4 `jle` falls through to the cooldown test.
        Assert(new TPlayObject().TryActivateNativeMagic191(protectedTarget, 500000),
            "protection window: the boundary tick is open, the compare is strictly >");

        // Test 20: constants
        Assert(SpellsDef.SKILL_191 == 191, "SKILL_191 == 191");
        Assert(TBaseObject.NativeMagic191ColdTimeKey == 0xBF, "cold time key = 0xBF = 191");
        Assert(TBaseObject.NativeMagic191Id == 191, "magic id constant = 191");
        Assert(TBaseObject.NativeMagic191ProtectionMilliseconds == 300000,
            "protection window = 0x493E0 (0x6EF407)");
        Assert(TBaseObject.NativeMagic191StateId == 0x3E, "state id = 0x3E");
        Assert(TBaseObject.NativeMagic191StateSeconds == 8, "state duration argument = 8 seconds");
        Assert(TBaseObject.NativeMagic191StateDurationMilliseconds == 8000,
            "state duration = 8000ms");
        Assert(TBaseObject.NativeMagic191StateValue == 0, "state node value = 0");

        Console.WriteLine($"PASS: {passCount}/{totalCount} assertions");
        return 0;
    }

    static void Diagnose(string step)
    {
        Console.WriteLine("DIAG step=" + step);
        Console.Out.Flush();
        Console.Error.Flush();
    }

    // The fixture players are online, so every notice and every cooldown
    // notification reaches TPlayObject.SendSocket, which dereferences
    // M2Share.GateManager. The singleton has no gate registered, so
    // AddGateBuffer returns false and nothing leaves the process.
    static void PrepareRuntimeConfig()
    {
        var runtimeDirectory = AppContext.BaseDirectory;
        File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
            "[Server]" + Environment.NewLine);
        File.WriteAllText(Path.Combine(runtimeDirectory, "String.ini"),
            "[String]" + Environment.NewLine);
        File.WriteAllText(Path.Combine(runtimeDirectory, "Command.conf"),
            "[Command]" + Environment.NewLine);

        var shareDirectory = Path.Combine(Path.GetFullPath(
            Path.Combine(runtimeDirectory, "..")), "Share");
        Directory.CreateDirectory(shareDirectory);
        File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
            "[PlayerLevelExp]" + Environment.NewLine);
        File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
            "[Integer]" + Environment.NewLine);

        Diagnose("before-GateManager.Instance");
        M2Share.GateManager ??= GateManager.Instance;
        Diagnose("after-GateManager.Instance");
        // Every TBaseObject constructor ends with
        // M2Share.ObjectManager.RegisterConstructed(this) (TBaseObject.cs:903)
        // and only GameApp assigns that singleton in a real boot, so
        // `new TPlayObject()` threw NullReferenceException and this tool
        // reported INCOMPLETE with zero assertions executed.
        M2Share.ObjectManager ??= new ObjectManager();
        M2Share.ProcessMsgCriticalSection ??= new object();
        M2Share.LogMsgCriticalSection ??= new object();

        // TBaseObject's ctor ends in M2Share.ObjectManager.RegisterConstructed(this)
        // (TBaseObject.cs:903), so the singleton must exist before a real actor can be
        // built. Same minimal set the InProc harnesses boot: no engine threads, no network.
        M2Share.g_Config ??= new GameSvrConfig();
        M2Share.RandomNumber ??= RandomNumber.GetInstance();
        M2Share.ObjectManager ??= new ObjectManager();
    }
}
