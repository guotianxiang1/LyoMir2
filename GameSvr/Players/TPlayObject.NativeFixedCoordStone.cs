using System;
using System.Buffers.Binary;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// 定位石 (TFixedCoordStone) position-recall.
    ///
    /// Two halves, both absent from C# before this file:
    ///   (A) SETTER   — CM opcode 3420 (0xD5C) records the current map + X/Y.
    ///                  M2Server.exe dispatch 0x6D873F `sub eax,0xD5C` /
    ///                  0x6D8745 `je 0x6DADE3`; case body 0x6DADE3 calls the
    ///                  setter sub_6E9BAC at 0x6DAE1B.
    ///   (B) CONSUMER — TFixedCoordStone VMT slot +0x18 (pointer at 0x7827D4,
    ///                  VMT 0x7827BC) = sub_78A014. Teleports back.
    ///
    /// Runtime fields (native TPlayer, instSize 0x1948 so they live on the
    /// player, not the shared base):
    ///   obj+0x18F8  ShortString[15]  saved map name   (1 len byte + 15 data)
    ///   obj+0x1908  word             saved X
    ///   obj+0x190A  word             saved Y
    /// Save record: rec[0x5AC] / rec[0x5BC] / rec[0x5BE]. The shared DTO codec
    /// models none of them, but TryEncode starts from NativeData.Clone(), so a
    /// patch here reaches the wire verbatim -- the same mechanism
    /// TPlayObject.NativeUnmappedScalars.cs uses. DBSvr is NOT touched.
    /// </summary>
    public partial class TPlayObject
    {
        /// <summary>
        /// Native ShortString capacity, from the setter's literal
        /// 0x6E9CB1 `mov cl,0x0F` handed to the ShortString assign 0x4039E4.
        /// </summary>
        internal const int NativeFixedCoordNameCapacity = 0x0F;

        // rec[0x5AC] = ShortString[15] (16 bytes: len + 15), rec[0x5BC]/[0x5BE] = word X/Y.
        internal const int NativeFixedCoordMapOffset = 0x05AC;
        internal const int NativeFixedCoordXOffset = 0x05BC;
        internal const int NativeFixedCoordYOffset = 0x05BE;

        private const int NativeFixedCoordMinimumLength =
            NativeFixedCoordYOffset + sizeof(ushort);

        /// <summary>
        /// 0x6E9CB3 -> 0x4039E4 truncates at cl and does NOT zero-fill, so the
        /// authoritative in-RAM form is the raw 16-byte ShortString frame. Keeping
        /// the bytes (rather than a string) is what makes the save byte-exact:
        /// residual tail bytes past the length byte survive untouched, exactly as
        /// they do natively.
        /// </summary>
        private byte[] m_NativeFixedCoordName;

        internal short m_nNativeFixedCoordX;
        internal short m_nNativeFixedCoordY;

        /// <summary>
        /// Native emptiness test is `cmp byte ptr [esi+0x18F8],0` (consumer
        /// 0x78A082, login replay 0x6B23E3) -- it reads the ShortString LENGTH
        /// byte, not the first character.
        /// </summary>
        internal bool HasNativeFixedCoord()
        {
            return m_NativeFixedCoordName != null
                   && m_NativeFixedCoordName.Length > 0
                   && m_NativeFixedCoordName[0] != 0;
        }

        /// <summary>
        /// Decode the ShortString frame to text. GBK (codepage 936), because the
        /// stored bytes are whatever the map name was on the wire.
        /// </summary>
        internal string GetNativeFixedCoordMapName()
        {
            if (!HasNativeFixedCoord()) return string.Empty;
            var len = m_NativeFixedCoordName[0];
            if (len > NativeFixedCoordNameCapacity)
                len = NativeFixedCoordNameCapacity;
            if (len > m_NativeFixedCoordName.Length - 1)
                len = (byte)(m_NativeFixedCoordName.Length - 1);
            return len == 0
                ? string.Empty
                : HUtil32.GbkEncoding.GetString(m_NativeFixedCoordName, 1, len);
        }

        /// <summary>
        /// Byte-faithful port of the two-stage native conversion:
        /// 0x6E9CA0 sub_4057AC widens the map name into a 255-cap ShortString,
        /// then 0x6E9CB3 sub_4039E4 copies it into the 15-byte field, truncating
        /// at cl=0x0F with NO zero-fill of the unused tail.
        /// </summary>
        private void StoreNativeFixedCoordName(string mapName)
        {
            m_NativeFixedCoordName ??=
                new byte[NativeFixedCoordNameCapacity + 1];

            var raw = string.IsNullOrEmpty(mapName)
                ? Array.Empty<byte>()
                : HUtil32.GbkEncoding.GetBytes(mapName);
            var len = raw.Length > NativeFixedCoordNameCapacity
                ? NativeFixedCoordNameCapacity
                : raw.Length;

            m_NativeFixedCoordName[0] = (byte)len;
            // Copy exactly len bytes and leave the remainder as-is: sub_4039E4
            // does not clear it, so zero-filling here would byte-diff the save.
            if (len > 0) Array.Copy(raw, 0, m_NativeFixedCoordName, 1, len);
        }

        // ------------------------------------------------------------------
        // Shared mount gate. Native repeats this idiom verbatim in the setter's
        // CM case body (0x6DADE3-0x6DAE0D) and at the head of the consumer
        // (0x78A032-0x78A055):
        //     bit 0x33 set AND [obj+0x3C0] != 0  -> reject
        //     bit 0x34 set                       -> reject
        // Being on a SOLO mount with no partner is allowed. [obj+0x3C0] is the
        // two-seat mount partner pointer (written 0x6EEAA7, tested 0x6EE854,
        // ported as m_NativeHorsePartner -- cf. TPlayObject.NativeHorsePair.cs:101).
        // Both rejects are SILENT natively.
        // ------------------------------------------------------------------
        private bool IsNativeFixedCoordMountBlocked()
        {
            if (HasNativeActiveState(NativeHorseMountedState) &&
                m_NativeHorsePartner != null)
            {
                return true;
            }
            return HasNativeActiveState(NativeHorseBlockedState);
        }

        /// <summary>
        /// SETTER — port of sub_6E9BAC, reached from CM 3420.
        /// The caller supplies the bag match key: native reads the dword at
        /// msg[+0x00] (`mov eax,[ebp-0x34]; mov edx,[eax]` at 0x6DAE13-0x6DAE16),
        /// i.e. the wire Recog, and sub_73CF08 matches it against item+0x18
        /// (== TUserItem.MakeIndex) while walking the bag TList at obj+0x508.
        /// </summary>
        internal void ClientSetFixedCoord(int makeIndex)
        {
            // 0x6DADE3 / 0x6DAE01 -- silent.
            if (IsNativeFixedCoordMountBlocked()) return;

            // 0x6E9BD9 `cmp dword ptr [ebx+0x128],0` -> no map.
            if (m_PEnvir == null)
            {
                SendNativeFixedCoordMapRefusal();
                return;
            }

            // 0x6E9C00 `cmp byte ptr [esi+0x67],0`。map+0x67 由两个 token 各自
            // 在两个池里置位：NORECALL(0x7751BB/0x776382, str 0x775D38/0x776C68)
            // 以及复合 LimitItemMove(0x775A5C/0x7769E8, str 0x775FB4/0x776F2C，
            // 同时置 +0x67/+0x68/+0x6B/+0x6C)。故此门 = boNORECALL ∨ boLIMITITEMMOVE，
            // 缺 boLIMITITEMMOVE 会让 LimitItemMove 图上原本 setter 就拒绝(且不记录
            // 定点)的用例变成"记录定点+入队、再由 consumer 静默拦截"，既漏发
            // 0x6E9C18 的拒绝消息又多留了定点态。
            // 0x6E9C12 then rejects when the map name is listed in the
            // blacklist file; IndexOf returns -1 when absent (0x42812B).
            if (m_PEnvir.Flag.boNORECALL ||
                m_PEnvir.Flag.boLIMITITEMMOVE ||
                M2Share.IsNativeFixedCoordBannedMap(m_PEnvir.sMapName))
            {
                SendNativeFixedCoordMapRefusal();
                return;
            }

            // 0x6E9C37 bag scan, then the Delphi `is` test against
            // TFixedCoordStone (0x6E9C4A sub_404828 with VMT 0x7827BC) and the
            // definition gate StdMode==1 / Shape==0x23 (0x6E9C6D / 0x6E9C77).
            // All of these reject SILENTLY.
            var item = FindNativeFixedCoordStone(makeIndex, out var stdItem);
            if (item == null || stdItem == null) return;

            // 0x6E9C81 `cmp word ptr [esi+0x26],0` / jbe -> the count==0 leg
            // (0x6E9D27) skips the write entirely and only cleans up. C#
            // represents the native count as Dura.
            if (item.Dura <= 0) return;

            // ---- 0x6E9CAB..0x6E9CCD: the write ----
            StoreNativeFixedCoordName(m_PEnvir.sMapName);
            m_nNativeFixedCoordX = m_nCurrX;   // 0x6E9CB8 -> 0x6E9CBF
            m_nNativeFixedCoordY = m_nCurrY;   // 0x6E9CC6 -> 0x6E9CCD

            // 0x6E9CFE `mov cx,0x3026` -> sub_765E68.
            SendNativeFixedCoordAck();

            // 0x6E9D0B `dec word ptr [esi+0x26]` then the inventory refresh
            // through VMT+0x260 -> 0x6D7974 -> VMT+0x250 with dx=0x281
            // (== SM_BAGITEMDURACHG, 641).
            //
            // The Recog argument is `[esi+0x18]` (0x6D7974 takes it in ecx, fed
            // from 0x6E9D1A `mov edx,[ebp-4]` / the item pointer chain), and
            // esi is the item found by sub_73CF08 at 0x6E9C37. item+0x18 is the
            // session-local CLIENT id, not the server MakeIndex (item+0x20, the
            // getter at 0x78455C) -- every other C# send on this protocol passes
            // EnsureClientItemId, so passing MakeIndex here would hand the client
            // an id it has never seen and the row would not update.
            item.Dura--;
            SendDefMessage(Grobal2.SM_BAGITEMDURACHG,
                EnsureClientItemId(item), item.Dura, item.DuraMax, 0, string.Empty);

            // 0x6E9D27: when the stack is exhausted native removes it from the
            // bag TList (0x73D140), writes a type-0xB item log, tells the client
            // via dx=0x27B (SM_DELITEM, 635), frees the record and recomputes the
            // bag (0x73CEE4). Note there is NO message on this leg -- no vmt+0xD4
            // call and no colour word anywhere between 0x6E9D2E and 0x6E9D82.
            if (item.Dura <= 0)
            {
                m_ItemList.Remove(item);

                // 0x6E9D37..0x6E9D5B: sub_768BE0 with dx=0xB.
                //   6E9D37  mov eax,[esi+0x20]        ; MakeIndex (getter 0x78455C)
                //   6E9D3A  push eax
                //   6E9D3B  push 1
                //   6E9D3D  push 0x6E9DE4             ; AnsiString len=1, "0"
                //   6E9D4A  call sub_784568           ; the item's display name
                //   6E9D55  mov dx,0xB                ; the log TYPE
                // sub_768BE0 then prepends the player's map name (obj+0x106), X
                // (obj+0x12C) and Y (obj+0x130) and hands the row to the same
                // channel WritePileItemLog uses for its 0x44/0x45/0x46 rows.
                WriteFixedCoordStoneExhaustionLog(item);

                SendDefMessage(Grobal2.SM_DELITEM,
                    EnsureClientItemId(item), 0, 0, 0, string.Empty);
                Dispose(item);
                WeightChanged();
            }
        }

        /// <summary>
        /// The type-0xB item log 战神 writes when the last 传送石 charge is spent
        /// (<c>0x6E9D5B call sub_768BE0</c> with <c>dx=0xB</c>). Column order and
        /// the tab join follow the sibling 0x44/0x45/0x46 rows, whose builder is
        /// the same <c>sub_768BE0</c>; the trailing literal is <c>0x6E9DE4</c>, an
        /// AnsiString of length 1 whose single character is <c>'0'</c> — decoded
        /// from the length dword at 0x6E9DE0, not assumed.
        /// </summary>
        private void WriteFixedCoordStoneExhaustionLog(TUserItem item)
        {
            var stdItem = item == null || M2Share.UserEngine == null
                ? null
                : M2Share.UserEngine.GetStdItem(item.wIndex);
            if (stdItem == null) return;

            M2Share.AddGameDataLog(string.Join('\t', 0x0B, m_sMapName, m_nCurrX,
                m_nCurrY, m_sCharName, stdItem.Name, item.MakeIndex, 1, "0"));
        }

        /// <summary>
        /// Port of the bag scan sub_73CF08 plus the two type gates that always
        /// follow it in the setter. Returns null on every native reject leg.
        /// </summary>
        private TUserItem FindNativeFixedCoordStone(int makeIndex,
            out GoodItem stdItem)
        {
            stdItem = null;
            if (m_ItemList == null) return null;

            // sub_73CF08: linear walk of obj+0x508 comparing the dword at
            // item+0x18. First match wins (0x73CF45 stores then breaks).
            TUserItem found = null;
            for (var i = 0; i < m_ItemList.Count; i++)
            {
                var candidate = m_ItemList[i];
                if (candidate != null && candidate.MakeIndex == makeIndex)
                {
                    found = candidate;
                    break;
                }
            }
            if (found == null) return null;

            var definition = M2Share.UserEngine.GetStdItem(found.wIndex);
            if (definition == null) return null;

            // 0x6E9C4A `is TFixedCoordStone`, then 0x6E9C6D StdMode==1 and
            // 0x6E9C77 Shape==0x23. NativeItemFactory is the C# port of the
            // Delphi class factory, so the class-name test subsumes the `is`.
            if (NativeItemFactory.GetClassName(definition) != "TFixedCoordStone")
                return null;
            if (definition.StdMode != 1 || definition.Shape != 0x23) return null;

            stdItem = definition;
            return found;
        }

        /// <summary>
        /// 「对不起，该地图上无法使用该道具」 -- str@0x006E9DBC (setter) and
        /// str@0x0078A15C (consumer), both len 30 GBK, sent with cx=0x38FF
        /// through VMT+0xD4. That wrapper (0x73C8F4) emits ident 0x2774 with the
        /// colour word split as FColor=0xFF / BColor=0x38, which is exactly the
        /// raw RM_SYSMESSAGE shape used elsewhere in this codebase. SysMsg()
        /// cannot reproduce it -- it maps enums through config.
        /// </summary>
        private void SendNativeFixedCoordMapRefusal()
        {
            SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, 0xFF, 0x38, 0,
                "对不起，该地图上无法使用该道具");
        }

        /// <summary>
        /// SM 3420. Two hops, and the field order changes between them - reading only the
        /// enqueue is what shifted this packet by one slot.
        /// <para>
        /// Hop 1, enqueue tag 12326. The six pushes at 0x6E9CD4-0x6E9CFC feed sub_765E68,
        /// whose stores (0x765E99-0x765EB0) name the slots:
        /// [ebp+0x1C]-&gt;rec+2 = wParam = 0, [ebp+0x18]-&gt;rec+4 = nParam1 = 0,
        /// [ebp+0x14]-&gt;rec+8 = nParam2 = X, [ebp+0x10]-&gt;rec+0xC = nParam3 = Y,
        /// [ebp+0x0C] = sMsg = map name (rec+0x10 data, rec+0x14 = Length+1).
        /// The login replay at 0x6B23EC-0x6B2414 repeats them byte-identically.
        /// </para>
        /// <para>
        /// Hop 2, the RM handler at 0x6B6036 rebuilds the wire frame in a DIFFERENT order:
        /// 0x6B6036 <c>mov ax,[ebx+2] / push eax</c> = Param &lt;- wParam,
        /// 0x6B603C <c>mov eax,[ebx+8] / push eax</c> = Tag &lt;- nParam2,
        /// 0x6B6040 <c>mov ax,[ebx+0xC] / push eax</c> = Series &lt;- nParam3,
        /// 0x6B6045/0x6B6049 push Buf and Len, 0x6B604E <c>mov ecx,[ebx+4]</c> = nRecog &lt;- nParam1,
        /// 0x6B6051 <c>66 BA 5C 0D mov dx,0xD5C</c>, 0x6B605A <c>call [ebx+0x254]</c>.
        /// </para>
        /// So on the wire it is Recog=0, Param=0, Tag=X, Series=Y - not Param=X, Tag=Y.
        /// </summary>
        internal void SendNativeFixedCoordAck()
        {
            SendDefMessage(Grobal2.SM_FIXEDCOORD, 0, 0,
                m_nNativeFixedCoordX, m_nNativeFixedCoordY,
                GetNativeFixedCoordMapName());
        }

        /// <summary>
        /// Login replay. Native UserLogon (fn~0x6B1AA0) re-pushes 0x3026 at
        /// 0x6B23E3 when `cmp byte ptr [esi+0x18F8],0` is non-zero, AFTER the
        /// social-relink call sub_6CCE40 (0x6B21CF). Without this the client
        /// renders no marker after a relog even though the record is correct.
        /// </summary>
        internal void ReplayNativeFixedCoordOnLogon()
        {
            if (!HasNativeFixedCoord()) return;
            SendNativeFixedCoordAck();
        }

        /// <summary>
        /// CONSUMER — port of sub_78A014 (TFixedCoordStone VMT slot +0x18).
        ///
        /// Return contract: native ends with `cmp word ptr [eax+0x26],0 / sete bl`
        /// (0x78A120), so it returns True when the stack is EXHAUSTED, not when
        /// the use succeeded -- the same idiom as the TRndFlyStone sibling
        /// (0x786EBE). The shared consume tail sub_6B8380 gates on that byte at
        /// 0x6B85DF and does the removal itself, so this method must NOT free the
        /// item. In C# the equivalent tail is ClientUseItems, which for a
        /// non-pile item (StdMode 1) removes it wholesale on a true return --
        /// matching native.
        /// </summary>
        private bool UseNativeFixedCoordStone(TUserItem item)
        {
            // 0x78A032-0x78A055 -- identical mount gate, silent.
            if (IsNativeFixedCoordMountBlocked()) return false;

            // 0x78A05D `cmp byte ptr [eax+0x6C],0`. Map flag +0x6C is set only by
            // the composite LimitItemMove token (parser 0x775A47 -> 0x775A68 sets
            // +0x67/+0x68/+0x6B/+0x6C together), ported as boLIMITITEMMOVE and
            // mapped identically at GameSvr/Maps/Maps.cs:307-313. Silent.
            if (m_PEnvir == null || m_PEnvir.Flag.boLIMITITEMMOVE) return false;

            // 0x78A075 item count, 0x78A082 saved-name length byte. Both silent.
            if (item == null || item.Dura <= 0) return false;
            if (!HasNativeFixedCoord()) return false;

            // 0x78A08F: player has no map -> the ONE message this function sends.
            if (m_PEnvir == null)
            {
                SendNativeFixedCoordMapRefusal();
                return false;
            }

            // 0x78A0B0 sub_696228 existence pre-check; the result is discarded
            // (only tested) because sub_6BE4D0 re-resolves the name internally.
            // 0x78A0B7: when the map is gone native falls straight to the tail --
            // NO message, NO fallback map. Deliberately silent.
            var mapName = GetNativeFixedCoordMapName();
            if (string.IsNullOrEmpty(mapName)) return false;
            if (M2Share.MapManager.FindMap(mapName) == null) return false;

            // 0x78A0DB sub_6DEF8C(Self, mapName, X=ecx, Y=[ebp+8]). Verified from
            // the prologue (ret 4 = one stack arg) plus two independent call sites
            // (0x78A0DB and 0x62818B) agreeing on the register assignment.
            // Zero X or Y is a native sentinel for "random spot on that map"
            // (0x6DEFB0/0x6DEFB5 -> sub_768C7C), which BaseObjectMove reproduces.
            BaseObjectMove(mapName, m_nNativeFixedCoordX, m_nNativeFixedCoordY);

            // 0x78A0E3 `dec word ptr [eax+0x26]` happens UNCONDITIONALLY after
            // the warp returns -- even when the inner walkability test rejected
            // the tile and emitted 「该地点不可达」. Preserved on purpose.
            item.Dura--;
            // 0x78A0FB `mov edx,[eax+0x18]` -- the Recog is the item's CLIENT id,
            // not the server MakeIndex (item+0x20, getter 0x78455C). Same field
            // the setter's send uses at 0x6E9D1A.
            SendDefMessage(Grobal2.SM_BAGITEMDURACHG,
                EnsureClientItemId(item), item.Dura, item.DuraMax, 0, string.Empty);

            // 0x78A120 `sete bl`: True == stack exhausted, let the tail remove it.
            return item.Dura <= 0;
        }

        // ------------------------------------------------------------------
        // Persistence. The offsets already survive a save round-trip because
        // TryEncode clones NativeData and only overwrites modeled slots, but
        // TryDecode never reads them -- so without an explicit restore the
        // fields would be zeroed on every login.
        // ------------------------------------------------------------------

        /// <summary>Load direction: read straight out of the native record.</summary>
        internal void RestoreNativeFixedCoord()
        {
            var raw = m_NativeHumanData;
            if (raw == null || raw.Length < NativeFixedCoordMinimumLength)
            {
                m_NativeFixedCoordName = null;
                m_nNativeFixedCoordX = 0;
                m_nNativeFixedCoordY = 0;
                return;
            }

            m_NativeFixedCoordName = new byte[NativeFixedCoordNameCapacity + 1];
            Array.Copy(raw, NativeFixedCoordMapOffset, m_NativeFixedCoordName, 0,
                NativeFixedCoordNameCapacity + 1);
            m_nNativeFixedCoordX = BinaryPrimitives.ReadInt16LittleEndian(
                raw.AsSpan(NativeFixedCoordXOffset, sizeof(short)));
            m_nNativeFixedCoordY = BinaryPrimitives.ReadInt16LittleEndian(
                raw.AsSpan(NativeFixedCoordYOffset, sizeof(short)));
        }

        /// <summary>
        /// Save direction. The whole 16-byte ShortString frame is written back,
        /// including the bytes past the length byte, so a record produced here is
        /// byte-identical to one produced by the non-zero-filling sub_4039E4.
        /// </summary>
        internal bool PersistNativeFixedCoord()
        {
            var raw = m_NativeHumanData;
            if (raw == null || raw.Length < NativeFixedCoordMinimumLength)
            {
                return !HasNativeFixedCoord()
                       && m_nNativeFixedCoordX == 0
                       && m_nNativeFixedCoordY == 0;
            }

            if (m_NativeFixedCoordName != null)
            {
                var count = Math.Min(NativeFixedCoordNameCapacity + 1,
                    m_NativeFixedCoordName.Length);
                Array.Copy(m_NativeFixedCoordName, 0, raw,
                    NativeFixedCoordMapOffset, count);
            }
            BinaryPrimitives.WriteInt16LittleEndian(
                raw.AsSpan(NativeFixedCoordXOffset, sizeof(short)),
                m_nNativeFixedCoordX);
            BinaryPrimitives.WriteInt16LittleEndian(
                raw.AsSpan(NativeFixedCoordYOffset, sizeof(short)),
                m_nNativeFixedCoordY);
            return true;
        }
    }
}
