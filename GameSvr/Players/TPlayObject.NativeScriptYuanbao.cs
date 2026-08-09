using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        internal void ScriptRequestNativeYuanbao(int amount, byte operation)
        {
            NativeYuanbaoRequest request = null;
            request = NativeYuanbaoRequest.CreateScript(
                GetCachedNativeUserId(), m_sUserID, m_sCharName, amount, operation,
                result => PrepareNativeScriptYuanbaoCompletion(request, result),
                result => CompleteNativeScriptYuanbao(request, result));
            NativeYuanbaoManager.Enqueue(request);
        }

        private void PrepareNativeScriptYuanbaoCompletion(
            NativeYuanbaoRequest request, NativeYuanbaoResult result)
        {
            request.SetScriptCallbackCharacterName(m_sCharName);
            if (result.ErrorCode != 0) return;
            m_nGameGold = result.Balance;
            RefreshNativeLingFu();
        }

        private void CompleteNativeScriptYuanbao(NativeYuanbaoRequest request,
            NativeYuanbaoResult result)
        {
            var add = request.Operation == NativeYuanbaoManager.AddOperation;
            if (result.ErrorCode != 0)
            {
                SysMsg((add ? "添加元宝失败 玩家:" : "扣除元宝失败 玩家:") +
                       m_sCharName + " 错误信息：" +
                       NativeYuanbaoManager.GetErrorText(result.ErrorCode),
                    MsgColor.Red, MsgType.Hint);
                return;
            }

            SysMsg((add ? "添加元宝成功 玩家:" : "扣除元宝成功 玩家:") +
                   m_sCharName + (add ? " 元宝增加 " : " 元宝减少 ") +
                   request.Amount,
                MsgColor.Green, MsgType.Hint);

            var characterName = HUtil32.GbkEncoding.GetString(
                request.CharacterNameBytes).TrimEnd('\0', ' ');
            var online = M2Share.UserEngine?.GetPlayObject(characterName);
            online?.SysMsg((add ? "NPC为您添加了" : "NPC扣除了您") +
                           request.Amount + "个元宝！",
                MsgColor.Green, MsgType.Hint);
        }
    }
}
