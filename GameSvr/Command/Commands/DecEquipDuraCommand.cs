using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM command @DecEquipDura (idx549, perm4).
    /// 原版契约: "@DecEquipDura &lt;数值&gt;" -> sub_6F3324(self, value)。value = Str_ToInt(p0)；
    /// 遍历【发令 GM 本人】0..15 号装备位，占用的装备把耐久(item+0x26 Dura WORD)直接写为 value；
    /// 只作用于自己，无 SysMsg、无跨服、不选目标玩家、不做客户端推送(原版核心不 recalc/不通知)。
    /// (此前的 C# 存根按 "人物名称 装备位置 数值" 的祖传形态 fail-closed，与原版契约发散。)
    /// 证据: GameSvr/Services/NativeGmSkillEquipCommands.cs (DecEquipDura idx549 case@0x00623BDE;
    ///       ItemDuraOffset=0x26; "For slot 0..15: item=GetUseItems(self,slot); if item!=null item.Dura=value; no SysMsg")。
    /// 偏差(已标注供复核): 完全未提供参数时提前返回，避免裸命令把全身装备耐久静默写 0 的走火；
    ///       给了参数时严格按原版把每个占用装备位的耐久写为该值。
    /// </summary>
    [GameCommand("DecEquipDura", "减少装备持久", "数值", 4)]
    public class DecEquipDuraCommand : BaseCommond
    {
        [DefaultCommand]
        public void DecEquipDura(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null || @Params.Length < 1 || string.IsNullOrEmpty(@Params[0]))
            {
                return;
            }
            var nDura = (ushort)HUtil32.Str_ToInt(@Params[0], 0);
            var useItems = PlayObject.m_UseItems;
            if (useItems == null)
            {
                return;
            }
            // 原版遍历 0..15 号装备位；C# 玩家 m_UseItems 长度即 HUMAN_EQUIPPED_ITEM_COUNT(16)。
            // 空装备位在 C# 中表示为 wIndex<=0(与 null 等价)，对应原版的 "item == null" 跳过。
            for (var i = 0; i < useItems.Length; i++)
            {
                if (useItems[i] != null && useItems[i].wIndex > 0)
                {
                    useItems[i].Dura = nDura;
                }
            }
        }
    }
}
