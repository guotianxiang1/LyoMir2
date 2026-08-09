using SystemModule;
using SystemModule.Packet;

namespace GameSvr
{
    public partial class TPlayObject
    {
        // ===================================================================
        // 战神 sub_6C5EC8 -- the ONE routine that dissolves an apprenticeship
        // from the STUDENT's side.  Four call sites, all passing the mode in
        // edx (Delphi register convention, so `mode` is the 1st real argument):
        //
        //   0x6BDF1D  fn~0x6BDCA8  mode=1   reputation/声望 path (0x6BDF8C
        //                                   "声望值"), gated on [ebp-0xD] != 0
        //   0x6C5E36  fn~0x6C5E08  mode=0   GM path (sole caller 0x6252B6 is
        //                                   the @-command dispatcher sub_622820)
        //   0x6CB017  fn~0x6CAFF0  mode=0   PAS `NpcLeaveTec`
        //   0x6CCEBB  fn~0x6CCE40  mode=1   login reconciliation, remote
        //                                   graduation leg (level >= CHUSHI)
        //
        // mode is a tri-state on the native side but only two values are ever
        // passed: 0 == 自行离开师门 (the student walks out) and 1 == 顺利出师
        // (graduation).  The routine uses it three times:
        //   0x6C5F71 / 0x6C6039  message + subcommand selection
        //   0x6C6099  cmp [ebp-4], 1  -- the trailing self-notification leg
        //
        // Two structural facts the previous inline C# fragment missed:
        //   1. The OFFLINE-master branch is NOT a no-op.  0x6C5F37 `je 0x6C5FD0`
        //      falls into a leg that sends the 0x0152 frame (subcmd 1 or 4) so
        //      the original DBServer mutates the master's stored record.  The
        //      old code called GetPlayObject and simply did nothing on null,
        //      leaving the student pinned in the offline master's slot forever.
        //   2. The student-side teardown at 0x6C605A runs UNCONDITIONALLY --
        //      it is the fallthrough of every branch, including the two early
        //      bail-outs at 0x6C605A.  Only the 0x6C5EF1 entry gate can skip it.
        // ===================================================================

        /// <summary>obj+0xB95 -> record 0xDC (SAVE 0x6B1210). IsAStudent.</summary>
        internal const int NativeStudentFlagRecordOffset = 0x00DC;

        /// <summary>obj+0xB96 -> record 0xDF (SAVE 0x6B121C). Student order.</summary>
        internal const int NativeStudentOrderRecordOffset = 0x00DF;

        /// <summary>obj+0xB98 -> record 0xE1 (SAVE 0x6B1240).</summary>
        internal const int NativeStudentAuxRecordOffset = 0x00E1;

        /// <summary>
        /// The subcommand most recently handed to the 0x0152 emitter, or -1 if
        /// this player has never taken the offline-master leg.  Diagnostic only:
        /// the frame itself goes to DBServer, which an audit cannot observe, so
        /// this records WHICH branch of 0x6C6039 was taken.
        /// </summary>
        internal int LastNativeMasterRelationSubcommand = -1;

        /// <summary>
        /// 战神 sub_6C5EC8.  <paramref name="mode"/> is native's edx: 0 = the
        /// student leaves of their own accord, 1 = graduation.
        /// </summary>
        internal void NativeLeaveMaster(int mode)
        {
            // 0x6C5EF1  cmp byte [ebx+0xB95], 0 / 0x6C5EF8 je 0x6C60BB
            // The only gate that skips the teardown entirely.
            if (!m_boStudent)
            {
                return;
            }

            // 0x6C5EFE / 0x6C5F07  masterName := block slot obj+0xC58
            // 0x6C5F0C / 0x6C5F15  selfName   := obj+0x106
            // Native copies the ShortString slot out of the opaque social block;
            // our decoded mirror of that slot is m_sMasterName.
            var masterName = m_sMasterName ?? string.Empty;
            var selfName = m_sCharName ?? string.Empty;

            // 0x6C5F1A  cmp dword [ebp-8], 0 / 0x6C5F1E je 0x6C605A
            // An empty master name skips straight to the student-side teardown.
            if (masterName.Length != 0)
            {
                // 0x6C5F24..0x6C5F2E  master := UserEngine.GetPlayObject(name)
                var master = M2Share.UserEngine?.GetPlayObject(masterName);

                // 0x6C5F35 test esi,esi / 0x6C5F37 je 0x6C5FD0
                if (master != null)
                {
                    ApplyNativeLeaveMasterOnline(master, mode, selfName);
                }
                else
                {
                    ApplyNativeLeaveMasterOffline(masterName, mode, selfName);
                }
            }

            // ---- 0x6C605A: unconditional student-side teardown ----
            ApplyNativeLeaveMasterSelfTeardown(mode, masterName);
        }

        /// <summary>
        /// 战神 0x6C5F3D..0x6C5FCB -- the master is in memory, so mutate it
        /// directly and skip the DBServer round trip.
        /// </summary>
        private void ApplyNativeLeaveMasterOnline(TPlayObject master, int mode,
            string selfName)
        {
            // 0x6C5F45  call sub_6C614C(master, selfName, out slot)
            // 0x6C5F4A  test al,al / je 0x6C605A  -- name not found, bail
            var slot = FindNativeStudentSlot(master, selfName);
            if (slot < 0)
            {
                return;
            }

            // 0x6C5F52  mov eax,[ebp-0x14] / sub eax,5 / 0x6C5F58 jae 0x6C605A
            // Native needs this unsigned range check because sub_6C614C reports
            // the index through an OUT-PARAM ([ebp-0x14]) that is only assigned
            // on a hit -- on a miss it holds whatever the stack had.  The `test
            // al,al` above already filtered misses, so the check is belt-and-
            // braces there.  FindNativeStudentSlot below returns a VALUE that is
            // either -1 or 0..4 by construction, so re-testing the bound here
            // would be unreachable code; the `slot < 0` guard is the whole gate.
            //
            // 0x6C5F5E  dec byte [esi+0xB97]  -- storedCount--
            if (master.m_nStudentCount > 0)
            {
                master.m_nStudentCount--;
            }

            // 0x6C5F64/0x6C5F69  mov byte [esi+slot*16+0xC78], 0
            // Clears the ShortString LENGTH BYTE only; the stale name bytes stay
            // behind it, which is what keeps the save record byte-identical.
            master.ClearNativeStudentSlot(slot);
            if (master.m_sStudentNames != null
                && slot < master.m_sStudentNames.Length)
            {
                master.m_sStudentNames[slot] = string.Empty;
            }

            string sayMsg;
            if (mode == 0)
            {
                // 0x6C5F71 jne / 0x6C5F77..0x6C5F8C:
                //   "你的徒弟 " (0x6C60F4, len 9) + name + " 自行离开师门！"
                //   (0x6C6108, len 15).  NOTE: no 0xB91 latch, no 0xBF4 bump --
                //   walking out is not a graduation.
                sayMsg = "你的徒弟 " + selfName + " 自行离开师门！";
            }
            else
            {
                // 0x6C5F93  mov byte [esi+0xB91], 1   -- master latch
                master.m_boMaster = true;
                // 0x6C5F9A..0x6C5FAF:
                //   "你的徒弟 " + name + " 顺利出师！" (0x6C6120, len 11)
                sayMsg = "你的徒弟 " + selfName + " 顺利出师！";
                // 0x6C5FB4  inc dword [esi+0xBF4]  -- ApprenticeNum++
                master.BumpNativeApprenticeNum();
            }

            // 0x6C5FBA  mov cx, 0xFCFF / call [vmt+0xD4]  -- SysMsg notice.
            // cx packs as FColor = cx & 0xFF, BColor = cx >> 8 (see the
            // playernotice bridge in PasApiBridge), so 0xFCFF is the
            // FColor 0xFF / BColor 0xFC pair == MsgColor.Blue, NOT MsgColor.Red
            // (which is 0x38FF).  Same pair the sibling graduation notice sends
            // raw in TPlayObject.NativeSocialSlots.cs.
            master.SysMsg(sayMsg, MsgColor.Blue, MsgType.Hint);
        }

        /// <summary>
        /// 战神 0x6C5FD0..0x6C6055 -- the master is offline, so the record has
        /// to be mutated by DBServer via the 0x0152 frame (sub_6C53B8) with
        /// subcommand 1 (left) or 4 (graduated).
        ///
        /// Native ALSO builds a "&lt;master&gt;/&lt;self&gt;" string at
        /// 0x6C5FE0..0x6C6020 and hands it to sub_713890 with id 0xD9 (left) or
        /// 0xE2 (graduated) at 0x6C6034.  That leg is DEAD in this binary:
        /// sub_713890 marshals its arguments and tails into sub_7138CC, whose
        /// entire body is `push ebp / mov ebp,esp / pop ebp / ret 0xC` -- an
        /// empty stub.  So the string is built, passed down and discarded.  It is
        /// deliberately NOT ported: emitting anything here would be inventing
        /// behaviour the original server does not have.
        /// </summary>
        private void ApplyNativeLeaveMasterOffline(string masterName, int mode,
            string selfName)
        {
            // 0x6C6039  cmp [ebp-4],0 / jne -> si=4 else si=1
            var subcommand = mode == 0
                ? NativeMasterRelationFrameCodec.StudentLeftSubcommand
                : NativeMasterRelationFrameCodec.StudentGraduatedSubcommand;

            // 0x6C6049 push si / 0x6C604A push 0 / 0x6C604C mov cx,0x152
            // 0x6C6050 mov edx,[ebp-8] (masterName) / 0x6C6053 mov eax,ebx (self)
            // 0x6C6055 call sub_6C53B8
            //   payload: [0x00] cmd=0x152  [0x02] subcmd  [0x04] 0
            //            [0x10] account  = self obj+0xAF4, ShortString(20)
            //            [0x25] selfName = self obj+0x106, ShortString(15)
            //            [0x35] target   = masterName,     ShortString(15)
            LastNativeMasterRelationSubcommand = subcommand;
            SendNativeMasterRelationFrame(subcommand, selfName, masterName);
        }

        /// <summary>
        /// 战神 sub_6C53B8 -- wraps the 0x48-byte 0x0152 payload in the standard
        /// DBServer envelope (sub_71315B builds the 0x33AABB77 header) and posts
        /// it.  Native takes the destination-name argument in edx and the
        /// subcommand on the stack; the account and the acting character's own
        /// name come from the object (obj+0xAF4 / obj+0x106).
        ///
        /// Native has no failure path here -- it queues unconditionally.  We log
        /// and drop, because a C# GameSvr with no DBServer link has nowhere to
        /// put the frame; silently swallowing it would hide a desync.
        /// </summary>
        private void SendNativeMasterRelationFrame(ushort subcommand,
            string selfName, string targetName)
        {
            var dataServer = M2Share.DataServer;
            if (dataServer == null)
            {
                M2Share.ErrorMessage(
                    "[ShiMen] DataServer 未就绪，0x0152 子命令 "
                    + subcommand + " 未发送: " + selfName + "/" + targetName);
                return;
            }
            if (!NativeMasterRelationFrameCodec.TryEncode(subcommand,
                    m_sUserID, selfName, targetName, out var frame,
                    out var error))
            {
                M2Share.ErrorMessage(
                    "[ShiMen] 0x0152 子命令 " + subcommand + " 编码失败 "
                    + selfName + "/" + targetName + ": " + error);
                return;
            }
            var queryId = dataServer.NextQueryId();
            var outer = new ServerMessagePacket(
                NativeMasterRelationFrameCodec.RequestCommand, 0, 0, 0, 0);
            if (!dataServer.SendRawRequest(queryId, outer, frame))
            {
                M2Share.ErrorMessage(
                    "[ShiMen] DBServer未连接，0x0152 子命令 " + subcommand
                    + " 未发送: " + selfName + "/" + targetName);
            }
        }

        /// <summary>
        /// 战神 0x6C605A..0x6C60B4 -- runs on every path that got past the
        /// 0x6C5EF1 entry gate, master online or not, name present or not.
        /// </summary>
        private void ApplyNativeLeaveMasterSelfTeardown(int mode,
            string masterName)
        {
            // 0x6C605A  mov byte [ebx+0xB95], 0   -- boStudent := false
            m_boStudent = false;
            // 0x6C6061  mov byte [ebx+0xB96], 0   -- student order := 0
            m_btStudentOrder = 0;
            ClearNativeStudentScalarRecordBytes();
            // 0x6C6071  mov dword [ebx+0xCCC], 0  -- cached master pointer
            m_MasterHuman = null;
            m_MasterRequestTarget = null;
            m_dwMasterRequestTime = 0;

            // 0x6C6079  call sub_7685E0 -- refresh the name plate
            RefShowName();

            // 0x6C6084..0x6C6094  sub_765E68(cx=0x278E, masterName)
            // RM_MASTERRELATION (10126 == 0x278E) broadcast to the client.
            SendMsg(this, Grobal2.RM_MASTERRELATION, 0, 0, 0, 0, masterName);

            if (mode == 1)
            {
                // 0x6C6099 cmp [ebp-4],1 / 0x6C609D jne 0x6C60B4
                // 0x6C609F mov cx,0xFCFF / edx=0x6C6138 "恭喜：你成功出师！"
                // 0xFCFF == FColor 0xFF / BColor 0xFC == MsgColor.Blue.
                SysMsg("恭喜：你成功出师！", MsgColor.Blue, MsgType.Hint);
            }
            else
            {
                // 0x6C60B4  mov byte [ebx+0xC58], 0
                // Graduation deliberately LEAVES the master name slot intact;
                // only the walk-out path clears it.  Zeroing the length byte is
                // the whole clear -- the tail bytes are not scrubbed.
                m_sMasterName = string.Empty;
                ClearNativeSocialSlotLengthByte(NativeMasterSlotOffset);
            }
        }

        /// <summary>
        /// 战神 sub_6C614C: walks the five student slots of
        /// <paramref name="master"/> and returns the index whose name matches,
        /// or -1.  Native gates the whole walk on `cmp byte [esi+0xB97], 0 /
        /// jbe` (0x6C6173) -- an UNSIGNED test, so a stored count of zero skips
        /// the scan even when slots hold names.  The comparison at 0x6C6196 is
        /// sub_40BD78, Delphi's case-insensitive AnsiCompareText.
        /// </summary>
        private static int FindNativeStudentSlot(TPlayObject master,
            string studentName)
        {
            // 0x6C6173 / 0x6C617A
            if (master.m_nStudentCount <= 0)
            {
                return -1;
            }
            var names = master.m_sStudentNames;
            if (names == null)
            {
                return -1;
            }
            for (var i = 0; i < NativeStudentSlotCount && i < names.Length; i++)
            {
                // 0x6C617E..0x6C618C reads slot i (stride 16), 0x6C6196 compares.
                // A slot whose raw length byte is zero holds "" and cannot match
                // a real name, so the raw check is implicit here.
                if (string.Equals(names[i], studentName,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Zeroes the ShortString length byte of a social-block slot in the
        /// carried record blob (战神 `mov byte [ebx+0xNNN], 0`).  The tail is
        /// left untouched: M2 sub_4039E4 never zero-fills, so scrubbing would
        /// diverge from the original save bytes.
        /// </summary>
        private void ClearNativeSocialSlotLengthByte(int recordOffset)
        {
            var raw = m_NativeHumanData;
            if (raw == null || raw.Length <= recordOffset)
            {
                return;
            }
            raw[recordOffset] = 0;
        }

        /// <summary>
        /// Mirrors the 0x6C605A/0x6C6061/0x6C6068 stores into the carried record
        /// blob.  0xB95 and 0xB96 have DTO members (boStudent / btStudentOrder)
        /// and are re-encoded from those, but obj+0xB98 -> record 0xE1 does NOT:
        /// the codec clone-carries it, so without an explicit store the stale
        /// value would survive the teardown.
        /// </summary>
        private void ClearNativeStudentScalarRecordBytes()
        {
            var raw = m_NativeHumanData;
            if (raw == null)
            {
                return;
            }
            if (raw.Length > NativeStudentFlagRecordOffset)
            {
                raw[NativeStudentFlagRecordOffset] = 0;
            }
            if (raw.Length > NativeStudentOrderRecordOffset)
            {
                raw[NativeStudentOrderRecordOffset] = 0;
            }
            // 0x6C6068  mov byte [ebx+0xB98], 0
            if (raw.Length > NativeStudentAuxRecordOffset)
            {
                raw[NativeStudentAuxRecordOffset] = 0;
            }
        }
    }
}
