using System;
using System.Buffers.Binary;

namespace DBSvr.Core
{
    /// <summary>
    /// 角色改名协议（opcode 0xFB0 = 4016），复刻原版校验层 <c>fn_5CD2EC</c>
    /// （VA 0x5CD2EC..0x5CD543）。
    ///
    /// 证据底本 DBServer_repaired_20260803.exe
    /// （sha256 70234272f417a07ab61ffafe1ebb255d31422a5ee25840481a5a10d6c6028666），
    /// ImageBase 0x400000。⚠️ 此处曾写“CODE 未 VMP 虚拟化”，那是错的：
    /// .vmp0/.vmp1 两段存在，CODE 有 688 处转移进去（E8 568 + E9 120）。
    /// 准确说法：**大部分游戏逻辑函数未虚拟化**，
    /// 以下每个常量均直读反汇编。
    ///
    /// 派发路径（内层是**两级表**，不是 base+idx*4）：
    ///   idx = word[msg+4] - 0xFAC = 0x04
    ///      -> grp = byte[0x5CE345+idx] = 5
    ///      -> dst = dword[0x5CE363+grp*4] = 0x5CE404
    ///      -> 0x5CE41E call fn_5CD2EC
    /// </summary>
    public static class NativeRenameCharProtocol
    {
        /// <summary>请求 opcode。0x5CE307 内层派发的 idx 基准是 0xFAC。</summary>
        public const ushort RequestCommand = 0xFB0;

        /// <summary>
        /// 回包 opcode 与请求相同：0x5CD4A5 <c>mov word ptr [ebp-0x2c], 0xfb0</c>。
        /// 错误码放在 <c>[ebp-0x30]</c>（回包记录首个 dword），
        /// 经 0x5CD4BF <c>call dword [ebx+0x60]</c> 虚调用发出。
        /// </summary>
        public const ushort ResponseCommand = 0xFB0;

        // ---- 错误码（[ebp-0x10]，即 fn_5CD2EC 的 err 局部）----

        /// <summary>初值，不应出现在回包里（0x5CD33F <c>xor eax,eax</c>）。</summary>
        public const int ResultInitial = 0;

        /// <summary>成功。<c>fn_5A8DDC</c> 返回 1（0x5CD3B4 存入 err）。</summary>
        public const int ResultSuccess = 1;

        /// <summary>
        /// 名字非法：长度越界（0x5CD36F）或字符白名单拒绝（0x5CD366）。
        /// 两条路径写的是同一个 -1。
        /// </summary>
        public const int ResultInvalidName = -1;

        /// <summary>
        /// 重名：0x5CD386 <c>call 0x5C22C8</c> 返回 true ⇒ 0x5CD38F 写 -2。
        /// 该检查读 <c>[[0x5D9B04]]</c> 的在线/占用名字索引。
        /// </summary>
        public const int ResultDuplicateName = -2;

        // ---- 77BBAA33 内部转发（改名成功后广播）----

        /// <summary>
        /// 0x5CD3F2 <c>mov dword ptr [ebp-0x84], 0x33aabb77</c>。
        /// 小端字节序即 <c>77 BB AA 33</c>，与已建模的内部转发协议魔数一致。
        /// </summary>
        public const uint ForwardMagic = 0x33AABB77;

        /// <summary>0x5CD3FC <c>mov word ptr [ebp-0x80], 1</c>。</summary>
        public const ushort ForwardVersion = 1;

        /// <summary>0x5CD402 <c>mov dword ptr [ebp-0x7c], 0x48</c>（头长）。</summary>
        public const int ForwardHeaderSize = 0x48;

        /// <summary>0x5CD409 <c>mov word ptr [ebp-0x78], 0x57</c>（转发子命令 87）。</summary>
        public const ushort ForwardCommand = 0x57;

        /// <summary>0x5CD47E <c>push 0x54</c> —— 转发记录总长。</summary>
        public const int ForwardRecordSize = 0x54;

        /// <summary>
        /// 转发体三个 ShortString 的槽位与容量，取自 0x5CD42B..0x5CD479 的
        /// <c>call 0x4035D8</c>（cl = 声明容量）：
        ///   [ebp-0x68] cl=0x14 (20) &lt;- Self+0x24  账号
        ///   [ebp-0x53] cl=0x0F (15) &lt;- Self+0x48  旧名
        ///   [ebp-0x43] cl=0x0F (15) &lt;- 新名
        /// 相对 [ebp-0x84] 的偏移分别是 0x1C / 0x31 / 0x41。
        /// </summary>
        public const int ForwardAccountOffset = 0x1C;

        /// <inheritdoc cref="ForwardAccountOffset"/>
        public const int ForwardAccountCapacity = 20;

        /// <inheritdoc cref="ForwardAccountOffset"/>
        public const int ForwardOldNameOffset = 0x31;

        /// <inheritdoc cref="ForwardAccountOffset"/>
        public const int ForwardOldNameCapacity = 15;

        /// <inheritdoc cref="ForwardAccountOffset"/>
        public const int ForwardNewNameOffset = 0x41;

        /// <inheritdoc cref="ForwardAccountOffset"/>
        public const int ForwardNewNameCapacity = 15;

        /// <summary>
        /// 校验新名字，返回原版的错误码。
        ///
        /// 守卫顺序照抄 <c>fn_5CD2EC</c>，**顺序有意义**：
        ///   1. 0x5CD335 新名为空 ⇒ 直接返回（原版**不回包**，见 IsEmptyName）
        ///   2. 0x5CD34F..0x5CD358 长度门 [4,14]  ⇒ -1
        ///   3. 0x5CD35D 字符白名单 fn_5CCDE4     ⇒ -1
        ///   4. 0x5CD376 已有错误则**跳过** DB 调用（cmp err,0 / jne 0x5CD3B7）
        ///   5. 0x5CD386 重名检查                 ⇒ -2
        /// </summary>
        /// <param name="gbkNewName">GBK 原始字节，长度即 Delphi 字符串长度。</param>
        /// <param name="isDuplicate">
        /// 重名判定结果（原版 0x5C22C8）。只在前面守卫都通过时才应被求值 ——
        /// 原版在 0x5CD37A 处 <c>jne</c> 跳过了整个 DB 调用。
        /// </param>
        public static int Validate(byte[] gbkNewName, Func<bool> isDuplicate)
        {
            // 0x5CD34F..0x5CD358：长度门。注意这是无符号语义，Len<4 也落 -1。
            if (gbkNewName == null
                || !NativeCharacterNameValidator.IsLengthAllowed(gbkNewName.Length))
                return ResultInvalidName;

            // 0x5CD35D call 0x5CCDE4 / 0x5CD362 test al,al / 0x5CD364 jne
            if (!NativeCharacterNameValidator.IsNameAllowed(gbkNewName))
                return ResultInvalidName;

            // 0x5CD376 cmp [ebp-0x10],0 / 0x5CD37A jne 0x5CD3B7
            // ⇒ 前面出错就不查重名。到这里 err 必为 0，故继续。
            if (isDuplicate != null && isDuplicate())
                return ResultDuplicateName;

            // 交由调用方执行 fn_5A8DDC 对应的主档 + 级联。
            return ResultInitial;
        }

        /// <summary>
        /// 0x5CD335 <c>cmp dword ptr [ebp-8],0</c> / <c>je 0x5CD53B</c>：
        /// 新名字为空指针/空串时**直接退出且不回包**（返回值 bool 保持 0x5CD2FF
        /// 设的 false）。这与"名字非法回 -1"是**两种不同行为**，不可合并。
        /// </summary>
        public static bool IsEmptyName(byte[] gbkNewName)
            => gbkNewName == null || gbkNewName.Length == 0;

        /// <summary>
        /// 组装 0x54 字节的 77BBAA33 转发记录（0x5CD3F2..0x5CD47E）。
        /// 布局：magic(4) / version(2) / headerSize(4) / command(2) / …
        /// 三个 ShortString 按上面的槽位与容量写入。
        /// </summary>
        public static byte[] BuildForwardRecord(string account, string oldName,
            string newName)
        {
            var buffer = new byte[ForwardRecordSize];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, 4), ForwardMagic);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4, 2), ForwardVersion);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(8, 4), ForwardHeaderSize);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(12, 2), ForwardCommand);
            WriteShortString(buffer, ForwardAccountOffset, ForwardAccountCapacity, account);
            WriteShortString(buffer, ForwardOldNameOffset, ForwardOldNameCapacity, oldName);
            WriteShortString(buffer, ForwardNewNameOffset, ForwardNewNameCapacity, newName);
            return buffer;
        }

        /// <summary>
        /// Delphi ShortString 赋值（0x4035D8，cl = 声明容量）：首字节是长度，
        /// 超长按容量截断。⚠️ 原版**不清零尾部**，但这里缓冲区本就是全 0，行为一致。
        /// </summary>
        private static void WriteShortString(byte[] buffer, int offset, int capacity,
            string value)
        {
            var bytes = LegacyGbkText.Encode(value ?? string.Empty);
            var length = Math.Min(bytes.Length, capacity);
            buffer[offset] = (byte)length;
            Array.Copy(bytes, 0, buffer, offset + 1, length);
        }
    }
}
