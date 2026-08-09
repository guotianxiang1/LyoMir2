using SystemModule;

namespace GameSvr
{
    public partial class NormNpc
    {
        protected bool TryGotoPascalLabel(TPlayObject playObject, string label)
        {
            // Do NOT pre-null the current-NPC binding. Native's current-NPC field is
            // player+0xCD8, and an exhaustive immediate scan of the flat image for the
            // 0x00000CD8 displacement finds exactly THREE writes and NO write of zero
            // anywhere: the property setter sub_63DFAC (0x63DFAF `mov [edx+0xCD8],eax`) and
            // two direct stores in the NPC-click handler sub_6B8B28 (0x6B8BA7, 0x6B8C48),
            // both placed AFTER the script/talk call succeeds. There is no clear on label
            // entry, on move, on death or on relog; instead each reader re-validates
            // (nil / identity / same-map [obj+0x128] / within 15 tiles via sub_7743E0, e.g.
            // 0x6C3E1A..0x6C3E4D). Nulling here dropped a binding native keeps whenever a
            // label resolved to no handler. PasScriptHost.cs writes m_NPC on success only,
            // matching 0x6B8BA7 / 0x6B8C48.
            if (M2Share.PasEngine == null)
            {
                if (IsMainLabel(label))
                    SendNpcFallbackDialog(playObject, "NPC脚本引擎未初始化。");
                return false;
            }

            var beforeDialogSeq = playObject?.MerchantDialogSeq ?? 0;
            if (M2Share.PasEngine.TryCallNpcLabel(this, label, playObject,
                    out _, out var scriptFound))
            {
                if (IsMainLabel(label) && playObject != null && playObject.MerchantDialogSeq == beforeDialogSeq)
                    SendNpcFallbackDialog(playObject, "NPC脚本没有返回界面，请检查脚本执行日志。");
                return true;
            }

            if (IsMainLabel(label))
                SendNpcFallbackDialog(playObject, scriptFound
                    ? "NPC脚本缺少 @main 过程，请检查 Pascal 脚本。"
                    : "NPC脚本未找到，请检查 Pascal 脚本映射与 PsNpcscripts。");
            return false;
        }

        internal bool TryCallPascalCallback(TPlayObject playObject, string callbackName,
            params PasEngine.PasValue[] args)
        {
            if (M2Share.PasEngine == null || string.IsNullOrWhiteSpace(callbackName))
                return false;

            return M2Share.PasEngine.TryCallNpcProcedure(this,
                new[] { "_" + callbackName, callbackName }, playObject,
                out _, args);
        }

        private static bool IsMainLabel(string label)
        {
            return string.Equals(label, "@main", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(label, "main", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(label, "_main", StringComparison.OrdinalIgnoreCase);
        }

        private void SendNpcFallbackDialog(TPlayObject playObject, string message)
        {
            // 原版在 NPC 脚本引擎失败/未初始化/未返回界面时对玩家【静默】——不发任何 RM_MERCHANTSAY
            // 气泡。诊断信息仍在服务端脚本执行日志(PasEngine)里。此前的 C# 气泡是原版没有的调试便利，
            // 按 1:1 去掉（保留方法与调用点，仅静默，避免改动上游控制流）。
            _ = playObject;
            _ = message;
        }

        public void GotoLable(TPlayObject playObject, string label, bool extendedJump)
        {
            _ = extendedJump;
            TryGotoPascalLabel(playObject, label);
        }

        public void GotoLable_GiveItem(TPlayObject playObject, string itemName, int itemCount)
        {
            if (playObject == null || itemCount <= 0) return;
            if (string.Equals(itemName, Grobal2.sSTRING_GOLDNAME, StringComparison.OrdinalIgnoreCase))
            {
                playObject.IncGold(itemCount);
                playObject.GoldChanged();
                if (M2Share.g_boGameLogGold)
                {
                    M2Share.AddGameDataLog('9' + "\t" + playObject.m_sMapName + "\t" +
                        playObject.m_nCurrX + "\t" + playObject.m_nCurrY + "\t" +
                        playObject.m_sCharName + "\t" + Grobal2.sSTRING_GOLDNAME + "\t" +
                        itemCount + "\t1\t" + m_sCharName);
                }
                return;
            }

            if (M2Share.UserEngine.GetStdItemIdx(itemName) <= 0) return;
            itemCount = Math.Clamp(itemCount, 1, 50);
            for (var i = 0; i < itemCount; i++)
            {
                var userItem = new TUserItem();
                if (!M2Share.UserEngine.CopyToUserItemFromName(itemName, ref userItem))
                {
                    Dispose(userItem);
                    continue;
                }

                var stdItem = M2Share.UserEngine.GetStdItem(userItem.wIndex);
                if (stdItem?.NeedIdentify == 1)
                {
                    M2Share.AddGameDataLog('9' + "\t" + playObject.m_sMapName + "\t" +
                        playObject.m_nCurrX + "\t" + playObject.m_nCurrY + "\t" +
                        playObject.m_sCharName + "\t" + itemName + "\t" +
                        userItem.MakeIndex + "\t1\t" + m_sCharName);
                }

                if (playObject.IsEnoughBag())
                {
                    playObject.m_ItemList.Add(userItem);
                    playObject.SendAddItem(userItem);
                }
                else
                {
                    playObject.DropItemDown(userItem, 3, false, playObject, null);
                    Dispose(userItem);
                }
            }
        }
    }
}
