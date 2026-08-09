using System;
using System.Collections.Generic;
using GameSvr.Services;
using SystemModule;

// Dormant-model compat check for NativeGildQueryPresidentModel.cs — asserts that CM_GILD_QUERY_PRESIDENT
// (4566 / 0x11D6) is a native NO-OP (recognized by the gild dispatcher, no case, default: break) and
// that the C# absence of a handler is faithful. Evidence: staging/gild_query_president_4566_20260801.md.
//
// Single generic assertion helper (no overloaded local Equal).

int checks = 0;

void Equal<T>(T actual, T expected, string label)
{
    checks++;
    if (!EqualityComparer<T>.Default.Equals(actual, expected))
    {
        Console.Error.WriteLine($"FAIL: {label}: expected <{expected}>, got <{actual}>");
        Environment.Exit(1);
    }
}

// The model's idents match the real protocol constants (both CM and SM are 4566).
Equal(NativeGildQueryPresidentModel.CmIdent, Grobal2.CM_GILD_QUERY_PRESIDENT,
    "model CM ident == Grobal2.CM_GILD_QUERY_PRESIDENT");
Equal(NativeGildQueryPresidentModel.SmIdent, Grobal2.SM_GILD_QUERY_PRESIDENT,
    "model SM ident == Grobal2.SM_GILD_QUERY_PRESIDENT");
Equal(NativeGildQueryPresidentModel.CmIdent, 0x11D6, "CM ident is 0x11D6");

// 4566 reaches the gild dispatcher window (wIdent > 0x116F, <= 0x122B) but has no leaf case.
Equal(NativeGildQueryPresidentModel.IsWithinDispatcherWindow(0x11D6), true,
    "0x11D6 is within the gild dispatcher window");
Equal(NativeGildQueryPresidentModel.IsWithinDispatcherWindow(0x116F), false,
    "guard boundary: 0x116F is NOT > 0x116F");
Equal(NativeGildQueryPresidentModel.IsWithinDispatcherWindow(0x1170), true,
    "0x1170 is the first in-window ident");
Equal(NativeGildQueryPresidentModel.IsWithinDispatcherWindow(0x122C), false,
    "0x122C is above the highest case");

Equal(NativeGildQueryPresidentModel.IsHandledLeaf(0x11D6), false,
    "0x11D6 (4566) has NO leaf case");
// Immediate neighbors ARE handled -> proves 4566 reaches this switch and is an isolated gap.
Equal(NativeGildQueryPresidentModel.IsHandledLeaf(0x11D5), true, "0x11D5 (4565) is handled");
Equal(NativeGildQueryPresidentModel.IsHandledLeaf(0x11D7), true, "0x11D7 (4567) is handled");
// A couple of known gild leaves from the reversed switch, for good measure.
Equal(NativeGildQueryPresidentModel.IsHandledLeaf(0x11E0), true, "0x11E0 (4576 add-concern) is handled");
Equal(NativeGildQueryPresidentModel.IsHandledLeaf(0x11E9), true, "0x11E9 (4585 declare-war-name) is handled");

// The native outcome for 4566 is UnhandledNoOp: no reply, no request-body read.
Equal(NativeGildQueryPresidentModel.ClassifyQueryPresident(),
    NativeGildDispatchOutcome.UnhandledNoOp, "4566 -> UnhandledNoOp (default: break)");
Equal(NativeGildQueryPresidentModel.NativeSendsReply, false, "4566 native sends no reply");
Equal(NativeGildQueryPresidentModel.NativeReadsRequestBody, false, "4566 native reads no request body");

// Because the native path is a no-op, the C# no-handler is faithful.
Equal(NativeGildQueryPresidentModel.IsFaithfulNoHandler(), true,
    "C# absence of a 4566 handler is faithful (native no-op)");

Console.WriteLine(
    $"PASS NativeGildQueryPresidentCompatCheck: {checks} checks " +
    "(CM_GILD_QUERY_PRESIDENT 4566 = native no-op via gild dispatcher default; C# no-handler is faithful)");
return 0;
