using System;
using GameSvr.PasEngine;
using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// 任务发布板 (task-dispatch board) subsystem — CM 4150 / 4151 / 4417 / 4651.
    ///
    /// The native board is the singleton at <c>[[0x7D5D20]]</c> (Delphi class, VMT
    /// 0x72868C). Every field/offset below is proven from flat_image.bin (base
    /// 0x400000); nothing here is inferred:
    ///
    ///   [board+0x10]  TSTDScript  — created by the reload path sub_6997BC from class
    ///                 [0x728640] (ctor 0x7295B0), loading
    ///                 "&lt;envir&gt;\PsMapQuest\TaskDispatch.pas" and running its
    ///                 "OnInitialize" label. This is the object CM 4150/4151 query.
    ///                 Its vmt+0x48 (sub_733B98) is TSTDScript.CallFunc: it sets the
    ///                 script context vars This_DB / This_Item / This_Player, looks the
    ///                 function up by name in [self+0x24] and returns a Variant.
    ///                 The C# port of that engine is M2Share.PasEngine; the exact
    ///                 equivalent call is TryCallTaskDispatchProcedure(player, name, …).
    ///   [board+0x20]  word cmpCost       (InitTaskDispatchInfo arg1; setter sub_699E84)
    ///   [board+0x22]  word dispatchCnt   (arg3)
    ///   [board+0x24]  word acceptCnt     (arg2)
    ///   [board+0x26]  word bronzeCost    (arg4)
    ///   [board+0x28]  word silverCost    (arg5)
    ///   [board+0x2A]  word goldCost      (arg6)
    ///   [board+0x2C]  TSTDScript  — created by sub_699FE0 from the same class, loading
    ///                 "&lt;envir&gt;\PsMapQuest\HelperQuest.pas" (the board @Main script
    ///                 object). CM 4417/4651 drive this one.
    ///
    /// InitTaskDispatchInfo is a NATIVE script API (its handler is the sub_699E84 setter
    /// that writes the 6 words into the board). The C# bridge for it (PasApiBridge
    /// "inittaskdispatchinfo") stores the same six integers into
    /// g_Config.GlobalVal group 100, index 1..6, in argument order — so this port reads
    /// the config back from there. The offset→arg mapping is proven from the setter and
    /// matches the bridge's documented storage order.
    /// </summary>
    public partial class TPlayObject
    {
        // Body layout of SM 3452 (0xD7C), built by worker sub_699B68 and sent through
        // [player+0x254] with Recog=Param=Tag=Series=0. Total 0x369 = 873 bytes.
        private const int TaskBoardBodyLen = 0x369;
        private const int TaskBoardPrizeTrueOff = 0x00B;   // GetTaskPrizeDesc(i, True)  ×3, stride 0x65
        private const int TaskBoardPrizeFalseOff = 0x13A;  // GetTaskPrizeDesc(i, False) ×3, stride 0x65
        private const int TaskBoardAcceptDescOff = 0x269;  // GetTaskAcceptDesc(), ShortString[255]
        private const int TaskBoardPrizeStride = 0x65;     // ShortString[100] = 1 len byte + 100 chars
        private const int TaskBoardPrizeMax = 100;
        private const int TaskBoardAcceptDescMax = 255;

        /// <summary>
        /// Dispatch hook owned by this subsystem; wired in from
        /// TryHandleNativeCmTailProtocol (single marked call). Returns true iff it
        /// consumed the ident, so the shared switch never double-handles these CMs.
        /// </summary>
        private bool TryHandleTaskBoardCm(TProcessMessage processMessage)
        {
            switch (processMessage.wIdent)
            {
                case Grobal2.CM_4150:
                    NativeTaskBoardRefresh();
                    return true;
                case Grobal2.CM_4151:
                    // leaf 0x6DAF5E: Recog=[rec+0]=nParam1, Param=word[rec+6]=nParam2,
                    // Tag=word[rec+8]=nParam3 (selector).
                    NativeTaskBoardAction(processMessage.nParam1, processMessage.nParam2,
                        processMessage.nParam3);
                    return true;
                case Grobal2.CM_4417:
                    NativeTaskBoardScriptCommand();
                    return true;
                case Grobal2.CM_4651:
                    NativeTaskBoardTextCommand();
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// CM 4150, leaf 0x6DAF51 → thunk 0x6F2924 (loads [[0x7D5D20]] into EAX, Self
        /// into EDX) → worker 0x699B68.
        ///
        /// The worker fills a 0x369-byte buffer and always answers SM 3452 through
        /// [player+0x254] with Recog=Param=Tag=Series=0. Fields, in image order:
        ///   +0x000 byte  = Boolean(TaskDispatch.GetTaskDispatchCnt())   (no args)
        ///   +0x001 byte  = LOBYTE board[0x22] = dispatchCnt
        ///   +0x002 byte  = Boolean(TaskDispatch.GetTaskAcceptCnt())     (no args)
        ///   +0x003 byte  = LOBYTE board[0x24] = acceptCnt
        ///   +0x004 byte  = LOBYTE board[0x20] = cmpCost
        ///   +0x005 word  = board[0x26] = bronzeCost
        ///   +0x007 word  = board[0x28] = silverCost
        ///   +0x009 word  = board[0x2A] = goldCost
        ///   +0x00B ShortString[100] ×3 = GetTaskPrizeDesc(i, True)  for i=1..3
        ///   +0x13A ShortString[100] ×3 = GetTaskPrizeDesc(i, False) for i=1..3
        ///   +0x269 ShortString[255]    = GetTaskAcceptDesc()
        ///
        /// vmt+0x48 truncates each prize entry through sub_4039E4 (ShortString[100]) and
        /// converts the last via sub_41A65C (VarToStr → ShortString[255]). When a script
        /// function is absent the native VarClears the result (Boolean→false / string→"")
        /// and still sends, so a PasEngine miss here yields the same zero/empty field
        /// rather than a guess. The packet is sent unconditionally, exactly as native.
        /// </summary>
        private void NativeTaskBoardRefresh()
        {
            var body = new byte[TaskBoardBodyLen];

            // +0x00 / +0x02 — Boolean(count) from the TaskDispatch script.
            if (TryCallTaskDispatchFunc("GetTaskDispatchCnt", out var dispatchCntVar))
                body[0x00] = (byte)(dispatchCntVar.AsBool() ? 1 : 0);
            if (TryCallTaskDispatchFunc("GetTaskAcceptCnt", out var acceptCntVar))
                body[0x02] = (byte)(acceptCntVar.AsBool() ? 1 : 0);

            // board[0x20..0x2A] config (InitTaskDispatchInfo → GlobalVal group 100).
            body[0x04] = (byte)ReadTaskBoardConfig(1);          // [0x20] cmpCost
            body[0x03] = (byte)ReadTaskBoardConfig(2);          // [0x24] acceptCnt
            body[0x01] = (byte)ReadTaskBoardConfig(3);          // [0x22] dispatchCnt
            WriteWord(body, 0x05, ReadTaskBoardConfig(4));      // [0x26] bronzeCost
            WriteWord(body, 0x07, ReadTaskBoardConfig(5));      // [0x28] silverCost
            WriteWord(body, 0x09, ReadTaskBoardConfig(6));      // [0x2A] goldCost

            for (var i = 1; i <= 3; i++)
            {
                if (TryCallTaskDispatchFunc("GetTaskPrizeDesc", out var prize,
                        PasValue.FromInt(i), PasValue.FromBool(true)))
                {
                    WriteShortString(body, TaskBoardPrizeTrueOff + (i - 1) * TaskBoardPrizeStride,
                        TaskBoardPrizeMax, prize.AsString());
                }
            }

            for (var i = 1; i <= 3; i++)
            {
                if (TryCallTaskDispatchFunc("GetTaskPrizeDesc", out var prize,
                        PasValue.FromInt(i), PasValue.FromBool(false)))
                {
                    WriteShortString(body, TaskBoardPrizeFalseOff + (i - 1) * TaskBoardPrizeStride,
                        TaskBoardPrizeMax, prize.AsString());
                }
            }

            if (TryCallTaskDispatchFunc("GetTaskAcceptDesc", out var acceptDesc))
            {
                WriteShortString(body, TaskBoardAcceptDescOff, TaskBoardAcceptDescMax,
                    acceptDesc.AsString());
            }

            // [player+0x254] carries the raw body to the gate (native sub_6D7BF8 →
            // sub_5F7554, no server-side encode); C# SendSocket(ClientPacket, byte[]) is
            // the same raw path into GateManager.
            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_TASKBOARD_REFRESH, 0, 0, 0, 0);
            SendSocket(m_DefMsg, body);
        }

        /// <summary>
        /// CM 4151, leaf 0x6DAF5E → worker 0x6999D4. The worker switches on Tag and calls
        /// TaskDispatch.CallFunc (vmt+0x48, This_Player=Self) with integer args; it emits
        /// no packet itself (any reply is raised from inside the script proc):
        ///   Tag==1 → DoTaskDispatch(Recog, Param)
        ///   Tag==2 → DoTaskAccept(Recog)
        ///   Tag==3 → DoTaskComplete(Recog)
        ///   else   → nothing
        /// Recog/Param are marshalled as varInteger (sub_41AFE4).
        /// </summary>
        private void NativeTaskBoardAction(int recog, int param, int tag)
        {
            switch (tag)
            {
                case 1:
                    TryCallTaskDispatchFunc("DoTaskDispatch", out _,
                        PasValue.FromInt(recog), PasValue.FromInt(param));
                    break;
                case 2:
                    TryCallTaskDispatchFunc("DoTaskAccept", out _, PasValue.FromInt(recog));
                    break;
                case 3:
                    TryCallTaskDispatchFunc("DoTaskComplete", out _, PasValue.FromInt(recog));
                    break;
                // Any other Tag falls straight through to the epilogue in native — silent.
            }
        }

        /// <summary>
        /// CM 4417, leaf 0x6DB1BF → worker 0x699EB4. The worker only runs when
        /// board[0x2C] (the HelperQuest.pas @Main script object) is non-null, then invokes
        /// its TSTDScript.vmt+0x44 (sub_733D84) with (Self, 0, "@Main"), i.e. it enters the
        /// board's @Main dialog. That board @Main script object and the player↔script
        /// interaction it drives are not modelled in this port, so the command is
        /// fail-closed rather than guessed.
        /// </summary>
        private void NativeTaskBoardScriptCommand()
        {
            NativeCmTailFailClosed.Drop(Grobal2.CM_4417, m_sCharName);
        }

        /// <summary>
        /// CM 4651, leaf 0x6DB1D8 → worker 0x6FC054. The worker copies the packet body
        /// text and, only when board[0x2C] (HelperQuest.pas) is loaded, routes it through
        /// the player script-interaction method sub_6B8CC4 (which drives the
        /// [player+0x18B4] script host bound to [player+0xCD8]). That interaction state
        /// machine is not modelled here, so the text command is fail-closed.
        /// </summary>
        private void NativeTaskBoardTextCommand()
        {
            NativeCmTailFailClosed.Drop(Grobal2.CM_4651, m_sCharName);
        }

        /// <summary>
        /// TSTDScript.CallFunc equivalent for the board's TaskDispatch.pas script object.
        /// Native sets This_DB / This_Item = 0 and This_Player = Self for these calls; the
        /// PasEngine bridge sets the player context and runs PsMapQuest/TaskDispatch.pas.
        /// </summary>
        private bool TryCallTaskDispatchFunc(string funcName, out PasValue result,
            params PasValue[] args)
        {
            result = PasValue.Nil;
            var host = M2Share.PasEngine;
            if (host == null)
                return false;
            return host.TryCallTaskDispatchProcedure(this, funcName, out result, args);
        }

        /// <summary>
        /// Reads board[0x20..0x2A] config back from where the InitTaskDispatchInfo bridge
        /// stored it: g_Config.GlobalVal, group 100, index 1..6 (flat 100*100+index),
        /// matching PasApiBridge.SetGlobalVar/GetGlobalVar. Returns 0 when unset — the
        /// same value an uninitialised native board would carry.
        /// </summary>
        private static int ReadTaskBoardConfig(int index)
        {
            var config = M2Share.g_Config;
            if (config?.GlobalVal == null)
                return 0;
            var flat = 100 * 100 + index;
            if (flat < 0 || flat >= config.GlobalVal.Length)
                return 0;
            return config.GlobalVal[flat];
        }

        private static void WriteWord(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        /// <summary>
        /// Writes a Delphi ShortString field: one length byte capped at
        /// <paramref name="maxLen"/> followed by up to maxLen content bytes (GBK, via
        /// HUtil32.GetBytes), matching sub_4039E4 / sub_41A65C. The trailing bytes of the
        /// field stay zero, as the buffer is zero-initialised.
        /// </summary>
        private static void WriteShortString(byte[] buffer, int offset, int maxLen, string value)
        {
            var bytes = HUtil32.GetBytes(value ?? string.Empty);
            var n = bytes.Length;
            if (n > maxLen)
                n = maxLen;
            buffer[offset] = (byte)n;
            if (n > 0)
                Array.Copy(bytes, 0, buffer, offset + 1, n);
        }
    }
}
