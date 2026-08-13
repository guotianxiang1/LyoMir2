namespace GameSvr
{
    // ✅ 战神字节证据 (Tier-1)：race 70 = TFriendAnimal，VMT 0x665988，size 0x4D8，
    //    parent = TAnimal(0x71D51C)（= C# AnimalObject，size 与父类相同，不加任何字段）。
    //    工厂 sub_679F8C 索引表[70-0xB=0x3B]=0x08=8；jt[8]=0x67A45B；case body 全文：
    //      67A45B  B2 01              mov  dl,1
    //      67A45D  A1 3C 59 66 00     mov  eax,[0x66593C]   ; classref -> TFriendAnimal
    //      67A462  E8 C5 2C FF FF     call 0x66D12C         ; TFriendAnimal.Create
    //      67A467  89 45 F8           mov  [ebp-8],eax
    //      67A46A  E9 D0 08 00 00     jmp  0x67AD3F         ; case 内无额外字段写、无 RNG
    //    原先 race 70 落工厂 default(0x67AE5E) → 返回 nil，这一族怪物根本不出现。
    //
    // 构造器 sub_66D12C 全文（父 Create 之后【只有一条】字段写）：
    //   66D145  E8 DE 06 0B 00     call 0x71D828        ; TAnimal.Create
    //   66D14A  C6 46 58 01        mov  byte [esi+0x58],1
    //   66D14E..66D168             epilogue / ret
    //
    // 唯一 VMT 覆写：槽 +0x0F0 = sub_66D16C = `B0 01 / C3`（mov al,1; ret，恒返回 True）。
    //   父 TAnimal 该槽是 sub_772DA4 = `33 C0 / C3`（恒 False）。
    //
    // ⛔ fail-closed —— 两件事都拿不到 C# 侧的落点，故一律不实现，只留证据：
    //  (1) [+0x58] 在 C# 里【没有对应字段】。全镜像写入点仅 5 个（`C6 46 58 01`）：
    //      0x63D8D2(TMerchant.Create)、0x66B2BD(TFireDragon)、0x66D14A(本类)、
    //      0x684A4D(TCastleDoor)、0x684ED5(TWallStructure)；唯一读取点 0x76B735
    //      `80 7E 58 00 cmp byte [esi+0x58],0` + `0F 85 C6 01 00 00 jne 0x76B905`，
    //      在 TCreature 虚槽 [vmt+0x10] 的实现 sub_76B6F0 内。C# 既无该字段也无 [vmt+0x10]
    //      的移植，补一个没人读的 bool 只会是死代码。
    //  (2) 虚槽 +0x0F0 在 C# 里【没有对应虚方法】。它的消费点在 IsAttackTarget
    //      （= 虚槽 +0x20，TCreature 实现 sub_7671F0，身份见
    //       TBaseObject.NativeProperTargetGate.cs:12）的「目标无主人」分支：
    //        7673D1  8B C6 / 8B 10 / FF 92 F0 00 00 00   call [target.vmt+0xF0]
    //        7673EE  84 C0 / 75 04                       test al,al / jne
    //        7673F2  33 DB                               xor ebx,ebx
    //      C# 的 TBaseObject.IsAttackTarget 对应分支（TBaseObject.cs:5358-5372）写的是
    //      `if (BaseObject.m_Master != null) result = true;` 一条，没有这个虚钩子，
    //      也没有与之配套的 [+0x3A8] 归属比较。把 +0x0F0 硬塞成某个既有 C# 布尔属性
    //      属于臆造，故 reject。
    //
    // 结论：本类在 C# 侧行为 = 纯 AnimalObject。保留具名类型是为了 (a) race 70 不再落 nil，
    // (b) 上面两条缺口有确定的挂载点，将来补 [vmt+0x10] / [vmt+0xF0] 时不用重新取证。
    public class FriendAnimal : AnimalObject
    {
        public FriendAnimal() : base()
        {
        }
    }
}
