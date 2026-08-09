using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using SystemModule;
using SystemModule.Packet;

namespace GameSvr.Services
{
    internal enum NativeHeroAuxiliaryResponseDisposition
    {
        InvalidFrame,
        PlayerNotFound,
        ConsignedListSent,
        ConsignedListEmptySent,
        RestoreConsignedDialogSent,
        RestoreConsignedIgnored,
        BuildThreeSlotSent
    }

    // Owns the Type1 005D, 005E, and 0070 response paths selected by the
    // original M2 dispatch table. DBService deliberately remains the router.
    internal static class NativeHeroAuxiliaryResponseClient
    {
        private const int HeaderSize = NativeHeroDbFrameCodec.MessageHeaderSize;
        private const int MasterNameOffset = 37;
        private const int MasterNameCapacity = 15;
        private const int BuildThreeSlotMasterNameOffset = 16;
        private const int BuildThreeSlotMasterNameCapacity = 20;

        internal static NativeHeroAuxiliaryResponseDisposition ProcessResponse(
            LegacyDbServerFrame frame)
        {
            return ProcessResponse(frame, FindOnlinePlayer, SendPacket,
                owner => HeroDataService.RequestLoad(owner, 0, 0));
        }

        internal static NativeHeroAuxiliaryResponseDisposition ProcessResponse(
            LegacyDbServerFrame frame, Func<string, TPlayObject> findPlayer,
            Action<TPlayObject, ClientPacket, byte[]> sendPacket,
            Action<TPlayObject> requestDefaultHero)
        {
            if (!TryGetPayload(frame, out var payload, out var command)
                || findPlayer == null || sendPacket == null)
                return NativeHeroAuxiliaryResponseDisposition.InvalidFrame;

            switch (command)
            {
                case NativeHeroDbFrameCodec.ConsignedListResponseCommand:
                    return ProcessConsignedList(payload, findPlayer, sendPacket);

                case NativeHeroDbFrameCodec.RestoreConsignedResponseCommand:
                    return ProcessRestoreConsigned(payload, findPlayer, sendPacket);

                case NativeHeroDbFrameCodec.BuildThreeSlotResponseCommand:
                    return ProcessBuildThreeSlot(payload, findPlayer, sendPacket,
                        requestDefaultHero);

                default:
                    return NativeHeroAuxiliaryResponseDisposition.InvalidFrame;
            }
        }

        internal static TPlayObject FindOnlinePlayer(string masterName)
        {
            return FindOnlinePlayer(M2Share.UserEngine?.GetPlayerList(),
                masterName);
        }

        internal static TPlayObject FindOnlinePlayer(
            IEnumerable<TPlayObject> players, string masterName)
        {
            if (players == null || string.IsNullOrEmpty(masterName))
                return null;

            foreach (var player in players)
            {
                if (player == null || !string.Equals(player.m_sCharName,
                        masterName, StringComparison.OrdinalIgnoreCase))
                    continue;

                // The original name table returns its first match. Its caller
                // then rejects that object when it is ghosted or not ReadyRun.
                return player.m_boGhost || !player.m_boReadyRun
                    ? null
                    : player;
            }
            return null;
        }

        private static NativeHeroAuxiliaryResponseDisposition ProcessConsignedList(
            byte[] payload, Func<string, TPlayObject> findPlayer,
            Action<TPlayObject, ClientPacket, byte[]> sendPacket)
        {
            if (!TryReadShortString(payload, MasterNameOffset,
                    MasterNameCapacity, out var masterName))
                return NativeHeroAuxiliaryResponseDisposition.InvalidFrame;

            var owner = findPlayer(masterName);
            if (owner == null)
                return NativeHeroAuxiliaryResponseDisposition.PlayerNotFound;

            var count = BinaryPrimitives.ReadUInt16LittleEndian(
                payload.AsSpan(2, 2));
            var bodyLength = payload.Length - HeaderSize;
            if (count is >= 1 and <= 3
                && bodyLength == count * NativeHeroDbFrameCodec.ConsignedListEntrySize)
            {
                var body = payload.AsSpan(HeaderSize, bodyLength).ToArray();
                sendPacket(owner, Grobal2.MakeDefaultMsg(
                        Grobal2.SM_HEROLISTINFO, owner.ObjectId, 0, 0, count),
                    body);
                return NativeHeroAuxiliaryResponseDisposition.ConsignedListSent;
            }

            // sub_654064 still emits SM_HEROLISTINFO after an invalid count or
            // tail length, but zeroes all header fields after Recog.
            sendPacket(owner, Grobal2.MakeDefaultMsg(Grobal2.SM_HEROLISTINFO,
                owner.ObjectId, 0, 0, 0), Array.Empty<byte>());
            return NativeHeroAuxiliaryResponseDisposition.ConsignedListEmptySent;
        }

        private static NativeHeroAuxiliaryResponseDisposition ProcessRestoreConsigned(
            byte[] payload, Func<string, TPlayObject> findPlayer,
            Action<TPlayObject, ClientPacket, byte[]> sendPacket)
        {
            if (!TryReadShortString(payload, MasterNameOffset,
                    MasterNameCapacity, out var masterName))
                return NativeHeroAuxiliaryResponseDisposition.InvalidFrame;

            var owner = findPlayer(masterName);
            if (owner == null)
                return NativeHeroAuxiliaryResponseDisposition.PlayerNotFound;

            var result = BinaryPrimitives.ReadUInt16LittleEndian(
                payload.AsSpan(2, 2));
            switch (result)
            {
                case 0:
                    SendRestoreDialog(owner, RestoreFailureDialog, sendPacket);
                    return NativeHeroAuxiliaryResponseDisposition
                        .RestoreConsignedDialogSent;

                case 1:
                    ApplyRestoreConsignedState(owner,
                        BinaryPrimitives.ReadInt32LittleEndian(
                            payload.AsSpan(4, 4)));
                    SendRestoreDialog(owner, RestoreSuccessDialog, sendPacket);
                    return NativeHeroAuxiliaryResponseDisposition
                        .RestoreConsignedDialogSent;

                case 2:
                    SendRestoreDialog(owner, RestoreHasHeroDialog, sendPacket);
                    return NativeHeroAuxiliaryResponseDisposition
                        .RestoreConsignedDialogSent;

                default:
                    // sub_655330 has no default action after the lookup.
                    return NativeHeroAuxiliaryResponseDisposition
                        .RestoreConsignedIgnored;
            }
        }

        private static NativeHeroAuxiliaryResponseDisposition ProcessBuildThreeSlot(
            byte[] payload, Func<string, TPlayObject> findPlayer,
            Action<TPlayObject, ClientPacket, byte[]> sendPacket,
            Action<TPlayObject> requestDefaultHero)
        {
            if (!TryReadShortString(payload, BuildThreeSlotMasterNameOffset,
                    BuildThreeSlotMasterNameCapacity, out var masterName))
                return NativeHeroAuxiliaryResponseDisposition.InvalidFrame;

            var owner = findPlayer(masterName);
            if (owner == null)
                return NativeHeroAuxiliaryResponseDisposition.PlayerNotFound;

            var result = BinaryPrimitives.ReadUInt16LittleEndian(
                payload.AsSpan(2, 2));
            if (result == 1)
            {
                ApplyBuildThreeSlotState(owner);
                requestDefaultHero?.Invoke(owner);
            }

            // sub_65545C does not restrict the result range or inspect +4.
            sendPacket(owner, Grobal2.MakeDefaultMsg(Grobal2.SM_SECHERO_EST,
                0, 0, 0, result), Array.Empty<byte>());
            return NativeHeroAuxiliaryResponseDisposition.BuildThreeSlotSent;
        }

        internal static void ApplyRestoreConsignedState(TPlayObject owner,
            int heroType)
        {
            if (owner == null) return;
            if (heroType == 1)
            {
                owner.m_btNativeHeroState |= 0x01;
                owner.m_btNativeHeroState &= 0xFB;
            }
            else if (heroType == 2)
            {
                owner.m_btNativeHeroState |= 0x02;
                owner.m_btNativeHeroState &= 0xF7;
            }
            else
            {
                return;
            }

            // The native byte is part of the player record. ScriptData[0x52]
            // is the managed representation that must remain synchronized.
            _ = owner.PersistNativeHeroState();
        }

        internal static void ApplyBuildThreeSlotState(TPlayObject owner)
        {
            if (owner == null) return;
            owner.m_btNativeHeroState |= 0x03;
            owner.m_btNativeHeroState &= 0xF3;
            _ = owner.PersistNativeHeroState();
        }

        private static void SendRestoreDialog(TPlayObject owner, string dialog,
            Action<TPlayObject, ClientPacket, byte[]> sendPacket)
        {
            var npc = owner.m_NPC;
            if (npc != null)
            {
                var body = HUtil32.GbkEncoding.GetBytes(
                    (npc.m_sCharName ?? string.Empty) + "/" + dialog);
                sendPacket(owner, Grobal2.MakeDefaultMsg(
                    Grobal2.SM_MERCHANTSAY, npc.ObjectId, 0, 0, 1), body);
                return;
            }

            var fallbackBody = HUtil32.GbkEncoding.GetBytes("NPC/" + dialog);
            sendPacket(owner, Grobal2.MakeDefaultMsg(Grobal2.SM_MERCHANTSAY,
                0, 0, 0, 0), fallbackBody);
        }

        private static void SendPacket(TPlayObject owner, ClientPacket packet,
            byte[] body)
        {
            owner.SendSocket(packet, body ?? Array.Empty<byte>());
        }

        private static bool TryGetPayload(LegacyDbServerFrame frame,
            out byte[] payload, out ushort command)
        {
            payload = null;
            command = 0;
            if (frame == null || frame.Type != 1 || frame.Payload == null
                              || frame.Payload.Length < HeaderSize)
                return false;

            payload = frame.Payload;
            command = BinaryPrimitives.ReadUInt16LittleEndian(payload);
            return true;
        }

        private static bool TryReadShortString(byte[] payload, int offset,
            int capacity, out string value)
        {
            value = null;
            if (payload == null || offset < 0 || capacity < 0
                || offset >= payload.Length)
                return false;

            var length = payload[offset];
            if (length > capacity || offset + 1 + length > payload.Length)
                return false;

            try
            {
                value = HUtil32.GbkEncoding.GetString(payload, offset + 1,
                    length);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        // Extracted from M2Server_unpacked_fixed.exe at 006553F4/408/438.
        internal const string RestoreFailureDialog = "取回失败。";
        internal const string RestoreSuccessDialog =
            "你的战斗伙伴又可以与你一起并肩作战了。";
        internal const string RestoreHasHeroDialog =
            "尚有一个英雄在您身边，您不能取回。";
    }
}
