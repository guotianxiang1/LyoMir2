using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private int _nativeDiamondFoundrySelector;

        internal int NativeDiamondFoundrySelector =>
            _nativeDiamondFoundrySelector;

        internal void ShowNativeDiamondFoundryList(NormNpc npc,
            NativeDiamondFoundry foundry)
        {
            if (npc == null) return;
            var selectedFoundry = foundry ?? NativeDiamondFoundry.Unavailable;
            SendNativeDiamondFoundryDialog(npc,
                selectedFoundry.FoundryListDialog,
                selectedFoundry.FoundryListDialogGbkBytes);
        }

        internal void SelectNativeDiamondFoundryRecipe(NormNpc npc,
            NativeDiamondFoundry foundry, int zeroBasedRecipeIndex)
        {
            if (npc == null || foundry == null ||
                !foundry.TryBuildSelectionDialogGbk(zeroBasedRecipeIndex,
                    out var selector, out var rawDialog))
                return;

            _nativeDiamondFoundrySelector = selector;
            foundry.TryBuildSelectionDialog(zeroBasedRecipeIndex,
                out _, out var dialog);
            SendNativeDiamondFoundryDialog(npc,
                dialog, rawDialog);
        }

        private void SendNativeDiamondFoundryDialog(NormNpc npc, string dialog,
            ReadOnlyMemory<byte> rawDialog)
        {
            m_NPC = npc;
            var npcName = HUtil32.GbkEncoding.GetBytes(npc.m_sCharName ?? "");
            var rawPayload = new byte[npcName.Length + 1 + rawDialog.Length];
            npcName.CopyTo(rawPayload, 0);
            rawPayload[npcName.Length] = (byte)'/';
            rawDialog.Span.CopyTo(rawPayload.AsSpan(npcName.Length + 1));
            SendMsg(npc, Grobal2.RM_MERCHANTSAY, 0, 0, 0, 0,
                npc.m_sCharName + '/' + dialog, rawPayload);
        }
    }
}
