namespace GameSvr
{
    /// <summary>
    /// 落地半径的原生取值表。
    ///
    /// 半径最终都汇到同一个子程序 <c>sub_7688A0</c>（= <c>DropItemDown</c>）的 ecx：
    /// <code>
    /// 7688B4  8B D9              mov ebx,ecx        ; 序言把半径存进 ebx
    /// 768907  53                 push ebx           ; -&gt; sub_768688 的 [ebp+0x10]
    /// 76891E  E8 65 FD FF FF     call 0x768688      ; GetDropPosition
    /// 7686B5  8B 45 10           mov eax,[ebp+0x10] ; 环数
    /// 7686BA  0F 8E A6 00 00 00  jle 0x768766       ; &lt;=0 直接放弃找空地
    /// </code>
    /// <c>sub_768688</c> 里 <c>[ebp-0x18]</c> 从 1 递增，共走 <c>[ebp+0x10]</c> 轮，
    /// 每轮扫 <c>[-r,+r]×[-r,+r]</c>，所以这个数就是「向外扩几圈找空地」。
    ///
    /// 全镜像 <c>sub_7688A0</c> 的 E8 调用点共 15 个，半径全表（本轮逐点反汇编取证）：
    /// <code>
    /// 调用点      所属函数                     半径     路径
    /// 0064E79D    sub_64E6F4                  [ebp-8]  脚本按名掉物（唯一由调用方传参）
    /// 00682CE3    sub_682CA4                  2        单品定点掉落
    /// 006E632D    sub_6E626C                  2        按名从背包剔除并落地
    /// 0071F7DB    sub_71F740                  3        怪物死亡「天赐」支
    /// 0071FC0D    sub_71FA20 段1              5        MonItemsTree 专属链
    /// 0071FC84    sub_71FA20 段1              5        同上（另一臂）
    /// 0071FDDA    sub_71FA20 段2              3        怪物自有掉落表
    /// 0071FE51    sub_71FA20 段2              3        同上（另一臂）
    /// 0071FF48    sub_71FA20 段3              3        sub_752CAC 表（C# 无对应）
    /// 0072021D    sub_72016C                  4        掉落控制四相（NativeDropControlRuntime）
    /// 0073CE13    sub_73CC98                  1        玩家手动丢物 CM_DROPITEM
    /// 0073FEFE    sub_73FC70                  2        玩家死亡：装备掉落
    /// 00740236    sub_740078                  2        玩家死亡：背包散落
    /// 00740482    sub_740300                  2        玩家死亡：TSpecialDropItem worker
    /// 00748DE2    sub_748D48                  2        玩家死亡：按图配额 worker
    /// </code>
    /// 金币走的是另一个子程序 <c>sub_768AAC</c>，半径同样是立即数：
    /// <c>0x768ADC 6A 03 push 3</c> → <c>0x768AF4 call 0x768688</c>。
    ///
    /// 14/15 个调用点是立即数，唯一的变量半径来自脚本 API <c>sub_64E6F4</c> 的
    /// <c>ecx</c> 形参（<c>0x64E796 8B 4D F8 mov ecx,[ebp-8]</c>）。**没有任何一条腿
    /// 读配置**：<c>DropItemRage</c> / <c>nDropItemRage</c> / <c>DropItemRange</c>
    /// 三个名字在全镜像 ASCII（大小写不敏感）、UTF-16LE、GBK 三路皆 0 命中，
    /// 故 C# 里的 <c>_MIN(g_Config.nDropItemRage, 7)</c> 无原生依据。
    /// </summary>
    public partial class TBaseObject
    {
        /// <summary>
        /// 战神 <c>sub_71FA20</c> 段2（怪物自有掉落表）的散落半径。表里每中一件就地落地，
        /// 两条臂各自把半径压进 ecx：
        /// <code>
        /// 71FDC2  6A 01 / 6A 00 / 50 / 68 00 01 72 00   ; 绕过否决 / "(爆天赐)"
        /// 71FDCF  B9 03 00 00 00        mov ecx,3
        /// 71FDDA  E8 C1 8A 04 00        call sub_7688A0
        /// 71FE46  B9 03 00 00 00        mov ecx,3       ; "怪物死亡:" 臂
        /// 71FE51  E8 4A 8A 04 00        call sub_7688A0
        /// </code>
        /// C# 把段2 的物件先攒进 <c>m_ItemList</c>（<c>UserEngine.MonGetRandomItems</c>）
        /// 再由 <c>ScatterBagItems</c> 统一落地，所以半径要在那个调用点上给死。
        /// </summary>
        private const int NativeMonsterOwnTableScatterRange = 3;

        /// <summary>
        /// 玩家死亡散落的半径。玩家死亡走的是与怪物完全不同的一条原生腿 ——
        /// <c>TPlayer.Die sub_6C07A0 @0x6C07D8</c> 调策略梯 <c>sub_741368</c>，
        /// 梯子按六个地图旗标字节四选一，四个 worker 的半径**一律是 2**：
        /// <code>
        /// 741447  E8 B4 EE FF FF   call 0x740300   ; TSpecialDropItem  -> 740479 mov ecx,2
        /// 741457  E8 EC 78 00 00   call 0x748D48   ; 按图配额          -> 748DD9 mov ecx,2
        /// 741461  E8 0A E8 FF FF   call 0x73FC70   ; 装备掉落          -> 73FEF5 mov ecx,2
        /// 741469  E8 0A EC FF FF   call 0x740078   ; 背包散落          -> 74022D mov ecx,2
        /// </code>
        /// 其中背包散落 <c>sub_740078</c> 就是 <c>TPlayObject.ScatterBagItems</c> 的本体，
        /// 那个 override 自带 <c>const int DropWide = 2</c>，已经对上。
        ///
        /// 本常量供**基类** 1-arg 虚入口用。该入口在当前树上其实到不了：唯一调用点是
        /// <c>TBaseObject.Base.cs</c> 死亡分支里 <c>m_btRaceServer == RC_PLAYOBJECT</c>
        /// 那一支的 <c>ScatterBagItems(null)</c>，而 <c>RC_PLAYOBJECT</c> 全树只有
        /// <c>TPlayObject</c> 构造函数会赋（<c>TPlayObject.Base.cs</c>、
        /// <c>UsrEngn.cs</c> 两处赋值的对象都是 <c>TPlayObject</c>），于是虚派发必然
        /// 落到 <c>TPlayObject</c> 的 override 上。基类值取 2 而不是原先的
        /// <c>_MIN(g_Config.nDropItemRage, 7)</c>：既去掉了无原生依据的旋钮，
        /// 又让这条目前不可达的路一旦被将来的子类走到，落到的也是它唯一可能代表的
        /// 那条原生腿（玩家死亡散落）的值。
        /// </summary>
        private const int NativePlayerDeathScatterRange = 2;
    }
}
