using System.IO;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// 原版 AutoGotoMap = sub_6D3024(PAS 脚本 API This_Player.AutoGotoMap(map,x,y))。
    /// 服务端做【跨地图路由】,把途经的地图/坐标数组下发给客户端,由客户端自动寻路。
    ///
    /// 路由架构(见 MapManager.FindNativeMapPath): **启动预计算的最小跳数 next-hop 表
    /// (sub_5F51F8 递归全对 pass) + 同边 portal tie-break(sub_5F4CF4 取源侧 min(X²+Y²))**,
    /// 不是贪心几何朝目标。
    ///
    /// 原版行为(Tier-1,M2Server_reunpacked_20260803.i64):
    ///   sub_6D3024 读 curMapName@[esi+0x115] / curX@[esi+0x12C] / curY@[esi+0x130];
    ///   0x6D3087 call sub_5F4D4C(寻路) → TMapPathNodeArr;
    ///   0x6D308F call sub_406A88(=Length ⇒ 节点数);
    ///   0x6D30A4 len = count*20(每节点 20 字节);
    ///   0x6D30B1 mov dx,0B22h(=2850);
    ///   0x6D30B7 call [ebx+254h](带 body 的发送器 sub_6D7BF8);
    ///   不可达 → SysMsg "目标不可到达" 经 [ebx+0xD4]。
    ///
    /// 节点布局(TMapPathNode,unit uMapPath,20 字节):
    ///   +0x00 ShortString[15] 地图名(1 长度字节 + 15 字节内容)
    ///   +0x10 Word X(小端)
    ///   +0x12 Word Y(小端)
    ///
    /// body = 节点数组【逐字节原样】(count × 20),不做 6-bit 编码 —— 原版
    /// sub_6D7BF8 只套一个固定的 0x33AABB77 跨服头(28 字节: magic + session/socket +
    /// type 0x0E + 总长 + count + msgId + 参数)后交给网关 sub_5F7554 → sub_5F6A68 原样拷贝;
    /// 客户端 wire 层的编码是 GameGate 的活。C# 侧 SendSocket(ClientPacket, byte[]) 正是这条路径:
    /// PacketHeader(RUNGATECODE + socket + GM_DATA) + ClientPacket(Recog=count, Ident=2850) + body 原样。
    /// </summary>
    public partial class TPlayObject
    {
        /// <summary>原版 0x6D30B1: mov dx,0B22h。</summary>
        private const int NativeAutoGotoMapMsgId = 2850;

        /// <summary>原版 TMapPathNode 元素大小 0x14。</summary>
        private const int NativeAutoGotoMapNodeSize = 20;

        /// <summary>原版 ShortString[15]: 1 长度字节 + 15 字节内容。</summary>
        private const int NativeAutoGotoMapNameCapacity = 15;

        /// <summary>不可达提示(原版经 vtable+0xD4 SysMsg 下发)。</summary>
        private const string NativeAutoGotoMapUnreachable = "目标不可到达";

        /// <summary>
        /// PAS: This_Player.AutoGotoMap(sMapName, nX, nY)。
        /// 按【当前所在地图】为源做跨地图贪心路由;不可达发 SysMsg "目标不可到达"。
        /// </summary>
        internal void NativeAutoGotoMap(string sTargetMapName, int nTargetX, int nTargetY)
        {
            if (m_boGhost) return;
            // 原版按源【地图】路由(curX/curY 虽被读取但未参与选路)
            var sourceMapName = m_PEnvir != null ? m_PEnvir.sMapName : m_sMapName;
            if (string.IsNullOrEmpty(sourceMapName) || string.IsNullOrEmpty(sTargetMapName))
            {
                SysMsg(NativeAutoGotoMapUnreachable, MsgColor.Red, MsgType.Hint);
                return;
            }

            var path = M2Share.MapManager.FindNativeMapPath(sourceMapName, sTargetMapName,
                nTargetX, nTargetY);
            // 原版 0x6D308F call sub_406A88(Length) → 0x6D3098 jle ⇒ 0 个节点走错误分支,
            // 发 dword_6D3110 "目标不可到达"(vtable+0xD4)。
            if (path == null || path.Count == 0)
            {
                SysMsg(NativeAutoGotoMapUnreachable, MsgColor.Red, MsgType.Hint);
                return;
            }

            SendNativeAutoGotoMapPath(path);
        }

        /// <summary>
        /// 下发消息 2850: Recog=节点数, body = count × 20 字节节点(原样,不编码)。
        /// </summary>
        private void SendNativeAutoGotoMapPath(IList<MapManager.NativeMapPathNode> path)
        {
            if (path == null || path.Count == 0) return;
            var body = BuildNativeAutoGotoMapBody(path);
            // 原版 [+0x10]=count(nParam) / [+0x14]=msgId(2850)
            var defMsg = Grobal2.MakeDefaultMsg(NativeAutoGotoMapMsgId, path.Count, 0, 0, 0);
            SendSocket(defMsg, body);
        }

        /// <summary>
        /// 逐节点写 20 字节: ShortString[15] 地图名 + Word X(@+0x10) + Word Y(@+0x12)。
        /// </summary>
        internal static byte[] BuildNativeAutoGotoMapBody(IList<MapManager.NativeMapPathNode> path)
        {
            if (path == null || path.Count == 0) return Array.Empty<byte>();
            var body = new byte[path.Count * NativeAutoGotoMapNodeSize];
            using var memoryStream = new MemoryStream(body);
            using var writer = new BinaryWriter(memoryStream);
            for (var i = 0; i < path.Count; i++)
            {
                var node = path[i];
                WriteNativeAutoGotoMapShortString(writer, node.MapName);
                writer.Write(node.X);
                writer.Write(node.Y);
            }
            return body;
        }

        /// <summary>
        /// Delphi ShortString[15]: 1 长度字节 + 固定 15 字节内容区(不足补 0)。
        /// 与 WriteClientShortString 同一形态,按 GBK 编码并按字节截断。
        /// </summary>
        private static void WriteNativeAutoGotoMapShortString(BinaryWriter writer, string value)
        {
            value ??= string.Empty;
            var bytes = HUtil32.GbkEncoding.GetBytes(value);
            while (bytes.Length > NativeAutoGotoMapNameCapacity && value.Length > 0)
            {
                value = value.Substring(0, value.Length - 1);
                bytes = HUtil32.GbkEncoding.GetBytes(value);
            }
            writer.Write((byte)bytes.Length);
            writer.Write(bytes);
            if (bytes.Length < NativeAutoGotoMapNameCapacity)
                writer.Write(new byte[NativeAutoGotoMapNameCapacity - bytes.Length]);
        }
    }
}
