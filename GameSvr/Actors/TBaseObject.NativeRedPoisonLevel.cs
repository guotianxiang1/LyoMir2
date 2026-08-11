namespace GameSvr
{
    public partial class TBaseObject
    {
        /// <summary>
        /// 红毒(native state <c>0x1E</c> = 30)的 <b>level</b>。
        /// <para>
        /// 与既有的 <c>m_btGreenPoisoningPoint</c> 完全对称：绿毒把"每跳伤害"存成
        /// 一个普通字段，红毒把"放大档位"存成一个普通字段。两者的位与时长权威都在
        /// legacy 槽 <c>m_wStatusTimeArr[]</c>，这里只补原版记录里的 level 一项。
        /// </para>
        /// </summary>
        private byte m_btRedPoisoningLevel;

        /// <summary>
        /// POIS-12 的读取端：红毒伤害放大档位所需的 level。
        /// <para>
        /// 原版把档位建立在状态记录的 level 上，不是位上(<c>sub_767A20</c>)：
        /// <c>0x767AA5 call 0x773BEC</c> 取 level → <c>0x767AAA cmp eax,4</c> →
        /// 命中走 <c>0x767AB5 fmul dword[0x767B3C]</c> = <b>1.25</b>，
        /// 否则走 <c>0x767ACA fld xword[0x767B40]</c> = <b>1.2</b>。
        /// 两个常量都按字节读出核对过(<c>00 00 A0 3F</c> / <c>9A 99 … FF 3F</c>)。
        /// </para>
        /// <para>
        /// 为什么要有这个回退：<c>MakePosion</c> 只写 legacy 槽，从不建 timed-ability
        /// 节点，所以 <c>TryGetNativeTimedAbilityValue(30)</c> 对红毒恒为
        /// <c>false</c>/<c>0</c>。实测(探针 4 条腿)：
        /// <c>MakePosion(POISON_DAMAGEARMOR,60,pt=4)</c> 之后
        /// <c>HasNativeActiveState(30)</c> 已经是 <c>true</c>，可是放大后是
        /// <b>1200</b> 而不是 1250 —— 即 ×1.2 一直在生效，<b>只有 ×1.25 档打不到</b>。
        /// (旧注释说"整档永不触发"是错的，已就地更正。)
        /// </para>
        /// <para>
        /// 不改成"给 30 建 timed-ability 节点"的理由：那条路会走
        /// <c>CanAddNativeTimedAbility</c>(30 在 <c>IsBlockedByNativeState16</c>
        /// 名单里会被否决)，还会发 3555 客户端帧、到期时触发能力重算并
        /// <c>ClearNativeActiveState(30)</c>。原版红毒不走 timed-ability 客户端协议，
        /// 凭空造这些帧与重算是新的背离；而给 30 造第二个"能否施加"的权威更糟。
        /// </para>
        /// </summary>
        private int GetNativeRedPoisonLevel()
        {
            return TryGetNativeTimedAbilityValue(NativeMagicState30,
                out int value)
                ? value
                : m_btRedPoisoningLevel;
        }

        /// <summary>
        /// POIS-12 的写入端，由 <c>MakePosion</c> 在写完 legacy 槽后调用。
        /// <para>
        /// <paramref name="level"/> 就是原版 <c>AddState</c> 之前 <c>push</c> 的那个
        /// level 参数(<c>0x680ACC push 3</c> / <c>0x680AE0 push 0</c> /
        /// <c>0x666D42 push 1</c> 都在同一位置)。无条件覆盖，与紧邻的
        /// <c>m_btGreenPoisoningPoint = (byte)nPoint;</c> 同一写法——续毒时机的
        /// 取舍权威在上方已有的"时长取较大者"分支，这里不重复发明第二套判断。
        /// </para>
        /// </summary>
        private void RecordNativeRedPoisonLevel(int level)
        {
            m_btRedPoisoningLevel = unchecked((byte)level);
        }
    }
}
