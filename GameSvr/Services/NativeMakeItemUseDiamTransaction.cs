using SystemModule;

namespace GameSvr
{
    public enum NativeMakeItemUseDiamOutcome
    {
        FeatureDisabled,
        RecipeMissing,
        PlayerStateRejected,
        MaterialOrChanceFailed,
        DiamondInsufficient,
        ItemCreateFailed,
        BagFull,
        Success
    }

    /// <summary>
    /// Runtime operations needed by the original MakeItemUseDiam procedure.
    /// Material and diamond consumers own their native per-item type-10 logs,
    /// deletion/durability notifications, disposal, and weight refreshes.
    /// Neither consumer may roll back a previously committed recipe slot.
    /// </summary>
    public interface INativeMakeItemUseDiamHost
    {
        int FoundrySelector { get; set; }

        bool IsServerSwitchBitSet(int byteOffset, byte mask);

        /// <summary>
        /// Implements native sub_6C7D88. An inactive equipment-confirmation
        /// lock returns true. An active lock sends internal 10035 with the
        /// supplied action (or action 8 after 180 seconds), maintains the
        /// challenge-token lifecycle, and returns false. This is not a plain
        /// gameplay-state Boolean and must share the native confirmation
        /// state used by CM 1068/1084 and the asynchronous security callback.
        /// </summary>
        bool TryEnterPlayerState(int mode, int rejectionInternalIdent);

        bool TryConsumeMaterialSlot(NativeDiamondFoundry.Material material);
        int NextNativeRandom(int exclusiveUpperBound);
        bool TryTakePhysicalDiamondWithoutCapitalRefresh(ushort amount);
        void SendInternalRefresh(int ident);

        /// <summary>
        /// Resolves the standard definition using recipe.ItemNameGbkBytes
        /// (the native 15-byte short-string identity, including an odd-byte
        /// truncation) and creates the corresponding native runtime item class
        /// with a fresh MakeIndex, full standard durability, and zero additional
        /// attributes. Returns null if either definition lookup or runtime-class
        /// construction fails.
        /// </summary>
        object CreateStandardItem(NativeDiamondFoundry.Recipe recipe);
        bool HasBagCapacity(int requestedCount, int maximumCount);
        void InsertBagItem(object item);
        void SendAddItem(object item, int clientIdent);
        void RefreshBagWeight();
        void DisposeCreatedItem(object item);

        void SendSuccessSystemMessage(NativeMakeItemUseDiamMessage message,
            byte foregroundColor, byte backgroundColor);
        void WriteFoundrySuccessLog(int type, string category,
            NativeDiamondFoundry.Recipe recipe, ushort diamondCost,
            string trailingValue);
        void WriteItemGainLog(int type, NativeDiamondFoundry.Recipe recipe,
            object item, int count, string reason);
        void SendMerchantDialog(NativeMakeItemUseDiamMessage message,
            int clientIdent);
    }

    public sealed class NativeMakeItemUseDiamMessage
    {
        private readonly byte[] _gbkBytes;

        internal NativeMakeItemUseDiamMessage(byte[] gbkBytes)
        {
            _gbkBytes = gbkBytes ?? Array.Empty<byte>();
            Text = HUtil32.GbkEncoding.GetString(_gbkBytes);
        }

        public string Text { get; }
        public ReadOnlyMemory<byte> GbkBytes => _gbkBytes;
    }

    /// <summary>
    /// Exact dependency-injected execution order for the native NPC procedure.
    /// It intentionally remains unwired until the shared player-confirmation
    /// state behind native sub_6C7D88 has an equivalent runtime adapter.
    /// </summary>
    public static class NativeMakeItemUseDiamTransaction
    {
        public const int ServerSwitchByteOffset = 0;
        public const byte ServerSwitchMask = 0x08;
        public const int PlayerStateMode = 1;
        public const int PlayerStateRejectInternalIdent = 10035;
        public const int CapitalRefreshInternalIdent = 10054;
        public const int AddItemClientIdent = 200;
        public const int MerchantSayClientIdent = 643;
        public const int MaximumBagItems = 48;
        public const int FoundryLogType = 34;
        public const int ItemGainLogType = 9;
        public const string FoundryLogCategory = "金刚宝石";
        public const string ItemGainLogReason = "实物锻造";
        public const string DefaultMessage =
            "实物锻造功能暂时关闭，请留意最新官方主页新闻。";
        public const string MaterialOrChanceFailureMessage =
            "很遗憾锻造失败，除金刚石外其他物品均被消耗";
        public const string DiamondInsufficientMessage = "金刚石不足";
        public const string SuccessPrefix = "恭喜：你的 ";
        public const string SuccessSuffix = " 锻造成功";
        public const string ExitCommand = " \\ \\ \\ <离开/@exit>";

        public static NativeMakeItemUseDiamOutcome Execute(
            NativeDiamondFoundry foundry, INativeMakeItemUseDiamHost host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));

            var recipe = ResolveRecipe(foundry, host.FoundrySelector);
            host.FoundrySelector = 0;

            if (!host.IsServerSwitchBitSet(ServerSwitchByteOffset,
                    ServerSwitchMask))
            {
                return Finish(host, NativeMakeItemUseDiamOutcome.FeatureDisabled,
                    BuildText(DefaultMessage));
            }

            if (recipe == null)
            {
                return Finish(host, NativeMakeItemUseDiamOutcome.RecipeMissing,
                    BuildText(DefaultMessage));
            }

            if (!host.TryEnterPlayerState(PlayerStateMode,
                    PlayerStateRejectInternalIdent))
            {
                return Finish(host,
                    NativeMakeItemUseDiamOutcome.PlayerStateRejected,
                    BuildText(DefaultMessage));
            }

            var materialsSucceeded = true;
            for (var index = 0;
                 index < NativeDiamondFoundry.MaximumMaterials &&
                 index < recipe.Materials.Count;
                 index++)
            {
                var material = recipe.Materials[index];
                if (material.ItemNameGbkBytes.IsEmpty) break;
                if (!host.TryConsumeMaterialSlot(material))
                    materialsSucceeded = false;
            }

            if (!materialsSucceeded ||
                host.NextNativeRandom(100) > recipe.SuccessRate)
            {
                return Finish(host,
                    NativeMakeItemUseDiamOutcome.MaterialOrChanceFailed,
                    BuildText(MaterialOrChanceFailureMessage));
            }

            var diamondTaken = false;
            if (recipe.DiamondCost > 0)
            {
                diamondTaken =
                    host.TryTakePhysicalDiamondWithoutCapitalRefresh(
                        recipe.DiamondCost);
                host.SendInternalRefresh(CapitalRefreshInternalIdent);
            }

            if (!diamondTaken)
            {
                return Finish(host,
                    NativeMakeItemUseDiamOutcome.DiamondInsufficient,
                    BuildText(DiamondInsufficientMessage));
            }

            host.SendInternalRefresh(CapitalRefreshInternalIdent);
            var item = host.CreateStandardItem(recipe);
            if (item == null)
            {
                return Finish(host,
                    NativeMakeItemUseDiamOutcome.ItemCreateFailed,
                    BuildText(DefaultMessage));
            }

            if (!host.HasBagCapacity(1, MaximumBagItems))
            {
                host.DisposeCreatedItem(item);
                return Finish(host, NativeMakeItemUseDiamOutcome.BagFull,
                    BuildText(DefaultMessage));
            }

            host.InsertBagItem(item);
            host.SendAddItem(item, AddItemClientIdent);
            host.RefreshBagWeight();

            var success = BuildSuccessText(recipe);
            host.SendSuccessSystemMessage(success, 0xFF, 0x38);
            host.WriteFoundrySuccessLog(FoundryLogType, FoundryLogCategory,
                recipe, recipe.DiamondCost, string.Empty);
            host.WriteItemGainLog(ItemGainLogType, recipe, item, 1,
                ItemGainLogReason);
            return Finish(host, NativeMakeItemUseDiamOutcome.Success, success);
        }

        private static NativeDiamondFoundry.Recipe ResolveRecipe(
            NativeDiamondFoundry foundry, int oneBasedSelector)
        {
            var zeroBased = unchecked(oneBasedSelector - 1);
            return foundry != null &&
                   (uint)zeroBased < (uint)foundry.Recipes.Count
                ? foundry.Recipes[zeroBased]
                : null;
        }

        private static NativeMakeItemUseDiamOutcome Finish(
            INativeMakeItemUseDiamHost host,
            NativeMakeItemUseDiamOutcome outcome,
            NativeMakeItemUseDiamMessage message)
        {
            host.SendMerchantDialog(AppendExit(message),
                MerchantSayClientIdent);
            return outcome;
        }

        private static NativeMakeItemUseDiamMessage BuildSuccessText(
            NativeDiamondFoundry.Recipe recipe)
        {
            var bytes = new List<byte>();
            bytes.AddRange(HUtil32.GbkEncoding.GetBytes(SuccessPrefix));
            bytes.AddRange(recipe.ItemNameGbkBytes.ToArray());
            bytes.AddRange(HUtil32.GbkEncoding.GetBytes(SuccessSuffix));
            return new NativeMakeItemUseDiamMessage(bytes.ToArray());
        }

        private static NativeMakeItemUseDiamMessage BuildText(string text)
        {
            return new NativeMakeItemUseDiamMessage(
                HUtil32.GbkEncoding.GetBytes(text ?? string.Empty));
        }

        private static NativeMakeItemUseDiamMessage AppendExit(
            NativeMakeItemUseDiamMessage message)
        {
            var bytes = new List<byte>(message.GbkBytes.Length +
                HUtil32.GbkEncoding.GetByteCount(ExitCommand));
            bytes.AddRange(message.GbkBytes.ToArray());
            bytes.AddRange(HUtil32.GbkEncoding.GetBytes(ExitCommand));
            return new NativeMakeItemUseDiamMessage(bytes.ToArray());
        }
    }
}
