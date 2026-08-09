using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        /// <summary>
        /// Runs the native 金刚石实物锻造 (MakeItemUseDiam / sub_64DF3C) transaction for this player
        /// against the loaded <paramref name="foundry"/>, using the recipe the player selected through
        /// the @DiaPeif_N foundry dialog. Consumption-safe by construction: every material slot and the
        /// physical diamond are taken through the atomic native bag-take (TryTakeNativeBagItem) BEFORE
        /// the single output item is created, and a full bag disposes the freshly-made item without
        /// refunding anything — identical to the native partial-commit contract (materials/diamond can
        /// be lost, but exactly one item is produced and never duplicated). Invoked from the PAS
        /// 'makeitemusediam' dispatch (the coordinator wires PasApiBridge to call this).
        /// </summary>
        internal NativeMakeItemUseDiamOutcome ExecuteNativeDiamondForge(NormNpc npc,
            NativeDiamondFoundry foundry)
        {
            var host = new NativeDiamondForgeHost(this, npc);
            return NativeMakeItemUseDiamTransaction.Execute(
                foundry ?? NativeDiamondFoundry.Unavailable, host);
        }

        /// <summary>
        /// Concrete <see cref="INativeMakeItemUseDiamHost"/> bound to one player + foundry NPC. Nested
        /// inside TPlayObject so it can reach the private native bag-take / foundry-selector primitives
        /// without widening TPlayObject's public surface.
        /// </summary>
        private sealed class NativeDiamondForgeHost : INativeMakeItemUseDiamHost
        {
            private readonly TPlayObject _player;
            private readonly NormNpc _npc;

            internal NativeDiamondForgeHost(TPlayObject player, NormNpc npc)
            {
                _player = player;
                _npc = npc;
            }

            public int FoundrySelector
            {
                get => _player._nativeDiamondFoundrySelector;
                set => _player._nativeDiamondFoundrySelector = value;
            }

            public bool IsServerSwitchBitSet(int byteOffset, byte mask) =>
                M2Share.ServerSwitches.IsBitSet(byteOffset, mask);

            // Native sub_6C7D88(player, 1): returns true unless the equipment-secret confirmation lock
            // [player+0x711] is active. That lock, its CM 1068/1084 handlers, SM_LOCKEQUIP/689, the
            // asynchronous security callback, and the 16-entry held-equipment escrow do NOT exist in
            // this C# server (verified: no +0x711 field, no CM 1068/1084, no SM_LOCKEQUIP handler). The
            // confirm-pending flag is therefore permanently 0, so the native gate is unconditionally
            // satisfied here. This does NOT open a dupe: the gate only blocked operations while a
            // player's equipment sat in unlock-escrow — a state this server never enters — and the
            // forge's own conservation is enforced by consume-before-produce in the transaction.
            public bool TryEnterPlayerState(int mode, int rejectionInternalIdent) => true;

            // One native material slot: an atomic take-by-name from the bag. Independently atomic (a
            // failed slot neither rolls back earlier slots nor stops later ones) and emits the native
            // type-10 log, delete/durability notifications, disposal and weight refresh internally.
            public bool TryConsumeMaterialSlot(NativeDiamondFoundry.Material material) =>
                _player.TryTakeNativeBagItem(material.ItemName, material.Count);

            public int NextNativeRandom(int exclusiveUpperBound) =>
                M2Share.RandomNumber.Random(exclusiveUpperBound);

            // Physical 金刚石 bag take WITHOUT the capital refresh (the transaction issues the refresh
            // separately via SendInternalRefresh, mirroring the native ordering).
            public bool TryTakePhysicalDiamondWithoutCapitalRefresh(ushort amount) =>
                _player.TryTakeNativeBagItem("金刚石", amount);

            public void SendInternalRefresh(int ident)
            {
                // Native internal 10054 = capital refresh. The 金刚石 count is derived from bag-item
                // Dura, so recompute the cache after a take and push the capital snapshot to the client.
                _player.m_nNativeDiamondCache = _player.GetNativeDiamondCount();
                _player.SendNativeCapitalInfo();
            }

            public object CreateStandardItem(NativeDiamondFoundry.Recipe recipe)
            {
                TUserItem item = null;
                if (!M2Share.UserEngine.CopyToUserItemFromName(recipe.ItemName, ref item) ||
                    item == null)
                    return null;
                return item;
            }

            public bool HasBagCapacity(int requestedCount, int maximumCount) =>
                _player.m_ItemList.Count + requestedCount <= maximumCount;

            public void InsertBagItem(object item) =>
                _player.m_ItemList.Add((TUserItem)item);

            public void SendAddItem(object item, int clientIdent) =>
                _player.SendAddItem((TUserItem)item);

            public void RefreshBagWeight() => _player.WeightChanged();

            public void DisposeCreatedItem(object item) =>
                _player.Dispose((TUserItem)item);

            public void SendSuccessSystemMessage(NativeMakeItemUseDiamMessage message,
                byte foregroundColor, byte backgroundColor) =>
                _player.SendMsg(_player, Grobal2.RM_SYSMESSAGE, 0,
                    foregroundColor, backgroundColor, 0, message.Text);

            public void WriteFoundrySuccessLog(int type, string category,
                NativeDiamondFoundry.Recipe recipe, ushort diamondCost, string trailingValue) =>
                M2Share.AddGameDataLog(string.Join('\t', type, _player.m_sMapName,
                    _player.m_nCurrX, _player.m_nCurrY, _player.m_sCharName, category,
                    recipe.ItemName, diamondCost, trailingValue ?? string.Empty));

            public void WriteItemGainLog(int type, NativeDiamondFoundry.Recipe recipe,
                object item, int count, string reason) =>
                M2Share.AddGameDataLog(string.Join('\t', type, _player.m_sMapName,
                    _player.m_nCurrX, _player.m_nCurrY, _player.m_sCharName, recipe.ItemName,
                    unchecked((uint)((TUserItem)item).MakeIndex), count, reason));

            public void SendMerchantDialog(NativeMakeItemUseDiamMessage message, int clientIdent)
            {
                if (_npc == null) return;
                _player.SendNativeDiamondFoundryDialog(_npc, message.Text, message.GbkBytes);
            }
        }
    }
}
