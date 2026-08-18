using SystemModule;

namespace GameSvr
{
    public partial class MapManager
    {
        private readonly Dictionary<string, Envirnoment> m_MapList = new Dictionary<string, Envirnoment>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 跨地图连接图(每图的 GROUP 链): 源地图名 → 该地图的所有出口 GROUP。
        /// 对应原版 uMapPath 的 MAP 节点 [+0x0C] group-list;每个 GROUP 对应"通往某一个邻居
        /// 地图"的一条边,其 [+0x08] 挂着该边的 PORTAL 链(同一对地图之间可以有多个传送点)。
        /// 数据源 = MapInfo 的 route 行(Maps.LoadMapInfo 末段 → AddMapRoute),
        /// 等价于原版 route 解析器 sub_5F48D4 → sub_5F4FF4 建 GROUP+PORTAL 记录。
        /// 仅供 autogotomap(原版 sub_6D3024 → 寻路 sub_5F4D4C)使用。
        /// </summary>
        private readonly Dictionary<string, List<NativeMapRouteGroup>> _nativeMapRouteGroups =
            new Dictionary<string, List<NativeMapRouteGroup>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 启动时预计算的 next-hop 表: 源地图名 → (目标地图名 → (下一跳地图名, 跳数))。
        /// 对应原版 MAP 节点 [+0x14] 的 next-hop 字典,由启动时的递归全对 pass
        /// sub_5F51F8 建立(sub_792838 → sub_5F4B9C → sub_5F51F8)。
        /// value 只存 (下一跳, 跳数),【不存完整路径】(原版 DICT entry [+4]/[+8])。
        /// </summary>
        private readonly Dictionary<string, Dictionary<string, NativeMapRouteHop>> _nativeMapNextHop =
            new Dictionary<string, Dictionary<string, NativeMapRouteHop>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>next-hop 表是否已建(建表在 MapInfo 全部 route 行加载完之后惰性触发一次)。</summary>
        private bool _nativeMapNextHopBuilt;

        /// <summary>
        /// 原版 PORTAL 记录(20 字节): [+0x00]/[+0x04] = 源侧 X,Y(**tie-break 代价**),
        /// [+0x08]/[+0x0C] = 目标侧落点, [+0x10] = 同 GROUP 内的下一个 portal。
        /// ⚠ 落点坐标是【退化字段】: 原版把它传给 sub_5F4CF4 当 arg1,但被
        /// 0x5F4CFB `xor eax,eax` 直接销毁 —— 选路只看源侧 X,Y。忠实照抄。
        /// </summary>
        private readonly struct NativeMapRoutePortal
        {
            public NativeMapRoutePortal(short sourceX, short sourceY, short destX, short destY)
            {
                SourceX = sourceX;
                SourceY = sourceY;
                DestX = destX;
                DestY = destY;
            }

            /// <summary>[+0x00] 源侧 X —— 参与 min(X²+Y²) 的原始坐标。</summary>
            public short SourceX { get; }
            /// <summary>[+0x04] 源侧 Y —— 参与 min(X²+Y²) 的原始坐标。</summary>
            public short SourceY { get; }
            /// <summary>[+0x08] 目标侧落点 X(退化,不参与选路)。</summary>
            public short DestX { get; }
            /// <summary>[+0x0C] 目标侧落点 Y(退化,不参与选路)。</summary>
            public short DestY { get; }
        }

        /// <summary>
        /// 原版 GROUP 记录: [+0x04] = 邻居 MAP 节点(其名字在 [+4]), [+0x08] = 该边的 portal 链头。
        /// 一个 GROUP == "本图通往某一个邻居图"的一条边。
        /// </summary>
        private sealed class NativeMapRouteGroup
        {
            public NativeMapRouteGroup(string neighbourMapName)
            {
                NeighbourMapName = neighbourMapName;
            }

            /// <summary>[+0x04] 邻居地图名(sub_5F542C 按此名字找 GROUP)。</summary>
            public string NeighbourMapName { get; }

            /// <summary>[+0x08] 该边的 portal 链(保持 route 行的原始顺序,不去重)。</summary>
            public List<NativeMapRoutePortal> Portals { get; } = new List<NativeMapRoutePortal>();
        }

        /// <summary>原版 DICT entry: [+0x04] = 下一跳地图, [+0x08] = 跳数。</summary>
        private readonly struct NativeMapRouteHop
        {
            public NativeMapRouteHop(string nextHopMapName, int hops)
            {
                NextHopMapName = nextHopMapName;
                Hops = hops;
            }

            public string NextHopMapName { get; }
            public int Hops { get; }
        }

        /// <summary>
        /// 原版 TMapPathNode(unit uMapPath,元素 0x14=20 字节):
        /// +0x00 ShortString[15] 地图名 / +0x10 Word X / +0x12 Word Y。
        /// </summary>
        internal readonly struct NativeMapPathNode
        {
            public NativeMapPathNode(string mapName, int x, int y)
            {
                MapName = mapName ?? string.Empty;
                X = (ushort)x;
                Y = (ushort)y;
            }

            public string MapName { get; }
            public ushort X { get; }
            public ushort Y { get; }
        }

        public IList<Envirnoment> Maps => m_MapList.Values.ToList();

        public IList<Envirnoment> GetMapList()
        {
            return m_MapList.Values.ToList();
        }

        public void MakeSafePkZone()
        {
            SafeEvent SafeEvent;
            TStartPoint StartPoint;
            Envirnoment Envir;
            for (var i = 0; i < M2Share.StartPointList.Count; i++)
            {
                StartPoint = M2Share.StartPointList[i];
                if (StartPoint != null && StartPoint.m_nType > 0)
                {
                    Envir = FindMap(StartPoint.m_sMapName);
                    if (Envir != null)
                    {
                        int nMinX = StartPoint.m_nCurrX - StartPoint.m_nRange;
                        int nMaxX = StartPoint.m_nCurrX + StartPoint.m_nRange;
                        int nMinY = StartPoint.m_nCurrY - StartPoint.m_nRange;
                        int nMaxY = StartPoint.m_nCurrY + StartPoint.m_nRange;
                        for (var nX = nMinX; nX <= nMaxX; nX++)
                        {
                            for (var nY = nMinY; nY <= nMaxY; nY++)
                            {
                                if (nX < nMaxX && nY == nMinY || nY < nMaxY && nX == nMinX || nX == nMaxX || nY == nMaxY)
                                {
                                    SafeEvent = new SafeEvent(Envir, nX, nY, StartPoint.m_nType);
                                    M2Share.EventManager.AddEvent(SafeEvent);
                                }
                            }
                        }
                    }
                }
            }
        }

        public IList<Envirnoment> GetMineMaps()
        {
            var list = new List<Envirnoment>();
            foreach (var item in m_MapList.Values)
            {
                if (item.Flag.boMINE)
                {
                    list.Add(item);
                }
            }
            return list;
        }

        public IList<Envirnoment> GetDoorMapList()
        {
            var list = new List<Envirnoment>();
            foreach (var item in m_MapList.Values)
            {
                if (item.m_DoorList.Count > 0)
                {
                    list.Add(item);
                }
            }
            return list;
        }

        public Envirnoment AddMapInfo(string sMapName, string sMapDesc, int nServerNumber, TMapFlag MapFlag, object QuestNPC)
        {
            var m_sMapFileName = string.Empty;
            var sTempName = sMapName;
            if (sTempName.IndexOf('|') > -1)
            {
                m_sMapFileName = HUtil32.GetValidStr3(sTempName, ref sMapName, new[] { '|' });
            }
            else
            {
                sTempName = HUtil32.ArrestStringEx(sTempName, '<', '>', ref m_sMapFileName);
                if (m_sMapFileName == "")
                {
                    m_sMapFileName = sMapName;
                }
                else
                {
                    sMapName = sTempName;
                }
            }
            var envirnoment = new Envirnoment
            {
                sMapName = sMapName,
                m_sMapFileName = m_sMapFileName,
                sMapDesc = sMapDesc,
                nServerIndex = nServerNumber,
                Flag = MapFlag,
                QuestNPC = QuestNPC
            };
            int minMap = 0;
            if (!M2Share.MiniMapList.TryGetValue(envirnoment.sMapName, out minMap))
            {
                if (!string.IsNullOrWhiteSpace(envirnoment.m_sMapFileName))
                    M2Share.MiniMapList.TryGetValue(envirnoment.m_sMapFileName, out minMap);
            }

            if (minMap > 0)
                envirnoment.nMinMap = minMap;
            var mapPath = Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sMapDir, m_sMapFileName + ".map");
            if (envirnoment.LoadMapData(mapPath))
            {
                _ = NativeDropControlLoader.TryLoadMap(M2Share.sRootPath,
                    envirnoment.sMapName, envirnoment.NativeDropControl,
                    out _);
                if (M2Share.ServerSwitches.IsBitSet(2, 0x80))
                {
                    _ = NativeMapRunPermission.TryLoad(
                        M2Share.g_Config.sEnvirDir, envirnoment, true,
                        out _);
                }
                if (!m_MapList.ContainsKey(sMapName))
                {
                    m_MapList.Add(sMapName, envirnoment);
                    return envirnoment;
                }
                else
                {
                    M2Share.ErrorMessage("地图名称重复 [" + sMapName + "]，请确认配置文件是否正确.");
                }
            }
            else
            {
                M2Share.ErrorMessage("地图文件: " + mapPath + "未找到,或者加载出错!!!");
            }
            return null;
        }

        /// <summary>
        /// 登记一条 MapInfo 连接行(TGateObj/OS_GATEOBJECT 传送门 + autogotomap route 边)。
        /// 由 Maps.LoadMapInfo 末段的连接行第二遍(扫非 '['/非 ';' 行,原版 sub_69599C
        /// @0x695C62 起)按「源图,X,Y -> 目标图,X,Y」拆分后调用。门登记 1:1 复刻原版
        /// sub_696328(@0x696328) + sub_779328(@0x779328):
        ///   1. 源图查不到(FindMap==null): 原版 sub_69599C @0x695CF7→@0x695D00 `je`
        ///      静默跳过 —— 不记日志、不建门、不建 route 边。
        ///   2. 目标图查不到: 原版 sub_696328 @0x69634B(sub_696228 按名查图)→@0x696354
        ///      `je 0x696374` 记 [Warning] 无效连接点。
        ///   3. 源格 attribute==0(可行走)校验: 原版 sub_779328 @0x77934D(GetMapCellInfo
        ///      sub_7776A8, cell 12 字节, byte[cell+0]=attribute)→@0x77935D `cmp byte[eax],0`
        ///      /@0x779360 `jne` 失败。C# 侧 MapCellinfo.Valid ⇔ Attribute==Walk(0),
        ///      与原版逐位同构。
        ///   4. 目标格 attribute==0 校验: 原版 sub_779328 @0x779382(对目标 Envir 再取格)
        ///      →@0x77938E `cmp byte[eax],0`/@0x779391 `jne` 失败。此校验的前置门
        ///      sub_78FE80(@0x77936B, `mov al,1; ret`)恒真,故目标格校验**恒执行**。
        ///   5. 两格皆 attribute==0 → new TGateObj{DEnvir,nDMapX,nDMapY}(原版
        ///      @0x7793B2 GetMem(16) kind=4 头插源格 cell+8、@0x7793D4 cell+2=1、
        ///      @0x7793DA srcEnvir[+0x18].Add) —— C# 由 AddToMap 落格(MOVE-34 读取侧
        ///      扫 objlist 找 OS_GATEOBJECT,已验)。
        ///   6. sub_779328 返回 0(格无效/attribute!=0): 原版 sub_696328 @0x696372 `jne`
        ///      落到 @0x696374 记 [Warning]。
        /// autogotomap 的 route 边登记(RegisterNativeMapRoutePortal)对应原版 sub_5F48D4
        /// (@0x695CAE,独立于门登记成败),仅在两图都已加载时建边 —— 触发条件保持不变。
        /// </summary>
        public bool AddMapRoute(string sSMapNO, int nSMapX, int nSMapY, string sDMapNO, int nDMapX, int nDMapY)
        {
            bool result = false;
            Envirnoment SEnvir = FindMap(sSMapNO);
            if (SEnvir == null)
            {
                // (1) 源图查不到 —— 原版 sub_69599C @0x695D00 静默跳过
                return false;
            }
            Envirnoment DEnvir = FindMap(sDMapNO);
            if (DEnvir == null)
            {
                // (2) 目标图查不到 —— 原版 sub_696328 @0x696354 → 记 [Warning]
                LogInvalidLinkPoint(SEnvir, nSMapX, nSMapY, sDMapNO, nDMapX, nDMapY);
                return false;
            }
            // route/autogotomap 边(原版 sub_5F48D4,独立于门登记成败;仅两图都在时可建)
            RegisterNativeMapRoutePortal(SEnvir, DEnvir, (short)nSMapX, (short)nSMapY,
                (short)nDMapX, (short)nDMapY);
            // (3)(4) 源格 + 目标格都必须 attribute==0(可行走) —— 原版 sub_779328
            //        @0x77935D(源) / @0x77938E(目标),sub_78FE80 恒真故目标格恒查。
            var srcInBounds = false;
            MapCellinfo srcCell = SEnvir.GetMapCellInfo(nSMapX, nSMapY, ref srcInBounds);
            var dstInBounds = false;
            MapCellinfo dstCell = DEnvir.GetMapCellInfo(nDMapX, nDMapY, ref dstInBounds);
            if (srcInBounds && srcCell.Attribute == CellAttribute.Walk
                && dstInBounds && dstCell.Attribute == CellAttribute.Walk)
            {
                // (5) 造门落格 —— 原版 sub_779328 @0x7793B2..@0x7793DD
                var GateObj = new TGateObj
                {
                    boFlag = false,
                    DEnvir = DEnvir,
                    nDMapX = (short)nDMapX,
                    nDMapY = (short)nDMapY
                };
                SEnvir.AddToMap(nSMapX, nSMapY, CellType.OS_GATEOBJECT, GateObj);
                result = true;
            }
            else
            {
                // (6) 格无效 / attribute!=0 —— 原版 sub_696328 @0x696374 记 [Warning]
                LogInvalidLinkPoint(SEnvir, nSMapX, nSMapY, sDMapNO, nDMapX, nDMapY);
            }
            return result;
        }

        /// <summary>
        /// 原版 sub_696328 @0x6963B4-@0x6963D2 的无效连接点告警:
        /// Format(格式串 @0x696404, GBK)="[Warning]: 无效连接点：%s %d:%d->%s %d:%d",
        /// 6 个参数依次 = 源图名、源X、源Y、目标图名、目标X、目标Y,再 MainOutMessage(cl=1)。
        /// ⚠ 源图名用【已加载 Envir 的规范名】(原版 [SEnvir+0x44]=Envirnoment.sMapName),
        /// 目标图名用【route 行里的原始 token】(原版 sub_696328 [ebp+0x10]=未经查表的 sDMapNO)。
        /// </summary>
        private static void LogInvalidLinkPoint(Envirnoment SEnvir, int nSMapX, int nSMapY,
            string sDMapNO, int nDMapX, int nDMapY)
        {
            M2Share.MainOutMessage(
                $"[Warning]: 无效连接点：{SEnvir.sMapName} {nSMapX}:{nSMapY}->{sDMapNO} {nDMapX}:{nDMapY}");
        }

        /// <summary>
        /// 把一条 MapInfo route 行登记成 GROUP+PORTAL 记录(原版 sub_5F4FF4)。
        /// 键/值统一用已解析地图的规范名 Envirnoment.sMapName(route 行里可能写地图编号/文件名,
        /// FindMap 已负责解析),与原版 map-name hash 的键一致。
        /// GROUP 按邻居地图名归并(原版 sub_5F542C 按名字找 GROUP);
        /// **同一对地图的多条 route 行各自成为一个 portal 追加到同一 GROUP 的链上 —— 不去重**,
        /// 因为 tie-break 正是在这条链上取 min(源侧X²+源侧Y²)。
        /// </summary>
        private void RegisterNativeMapRoutePortal(Envirnoment source, Envirnoment dest,
            short nSMapX, short nSMapY, short nDMapX, short nDMapY)
        {
            if (source == null || dest == null) return;
            var sourceKey = source.sMapName;
            var neighbourName = dest.sMapName;
            if (string.IsNullOrEmpty(sourceKey) || string.IsNullOrEmpty(neighbourName)) return;
            // 自环不是跨地图连接(原版 sub_5F5144 也不插自身条目)
            if (string.Equals(sourceKey, neighbourName, StringComparison.OrdinalIgnoreCase)) return;

            if (!_nativeMapRouteGroups.TryGetValue(sourceKey, out var groups))
            {
                groups = new List<NativeMapRouteGroup>();
                _nativeMapRouteGroups.Add(sourceKey, groups);
            }

            NativeMapRouteGroup group = null;
            for (var i = 0; i < groups.Count; i++)
            {
                if (string.Equals(groups[i].NeighbourMapName, neighbourName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    group = groups[i];
                    break;
                }
            }
            if (group == null)
            {
                group = new NativeMapRouteGroup(neighbourName);
                groups.Add(group);
            }
            group.Portals.Add(new NativeMapRoutePortal(nSMapX, nSMapY, nDMapX, nDMapY));

            // 边集变了 → next-hop 表失效,下次查询重建(原版是在 route 全部加载完后跑一次 pass)
            _nativeMapNextHopBuilt = false;
            _nativeMapNextHop.Clear();
        }

        /// <summary>已登记的 portal 总数(诊断/审计用)。</summary>
        internal int NativeMapRouteEdgeCount
        {
            get
            {
                var total = 0;
                foreach (var groups in _nativeMapRouteGroups.Values)
                    for (var i = 0; i < groups.Count; i++) total += groups[i].Portals.Count;
                return total;
            }
        }

        /// <summary>已登记的 GROUP(有向边)总数(诊断/审计用)。</summary>
        internal int NativeMapRouteGroupCount
        {
            get
            {
                var total = 0;
                foreach (var groups in _nativeMapRouteGroups.Values) total += groups.Count;
                return total;
            }
        }

        /// <summary>
        /// 建立全部 next-hop 表(原版启动时的递归全对 pass sub_5F51F8,
        /// 由 sub_792838 → sub_5F4B9C 驱动)。惰性触发一次,之后复用。
        /// </summary>
        internal void BuildNativeMapNextHopTables()
        {
            _nativeMapNextHop.Clear();
            // 原版对每张图跑一次 DFS pass(sub_5F4B9C 遍历 map 列表调 sub_5F51F8)
            foreach (var sourceKey in _nativeMapRouteGroups.Keys)
            {
                var closed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var inProgress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                BuildNativeMapNextHopFor(sourceKey, closed, inProgress);
            }
            _nativeMapNextHopBuilt = true;
        }

        /// <summary>
        /// 原版 sub_5F51F8(自递归 DFS)。为 mapName 建立/补全其 next-hop 字典:
        ///   先递归进邻居,把邻居字典按 hops+1 合并进来(sub_5F50E8),
        ///   再把直连邻居以 hops=1 插入(sub_5F5144)。
        /// 环保护 = per-edge in-progress 标记 + per-map closed 标记,**都在建表期**。
        /// </summary>
        private Dictionary<string, NativeMapRouteHop> BuildNativeMapNextHopFor(string mapName,
            HashSet<string> closed, HashSet<string> inProgress)
        {
            if (string.IsNullOrEmpty(mapName)) return null;
            if (_nativeMapNextHop.TryGetValue(mapName, out var existing) && closed.Contains(mapName))
                return existing;

            if (!_nativeMapNextHop.TryGetValue(mapName, out var table))
            {
                table = new Dictionary<string, NativeMapRouteHop>(StringComparer.OrdinalIgnoreCase);
                _nativeMapNextHop[mapName] = table;
            }

            if (!_nativeMapRouteGroups.TryGetValue(mapName, out var groups))
            {
                closed.Add(mapName);
                return table;
            }

            // 原版顺序: 先 DFS+MERGE(0x5F5249/0x5F5252),再插入直连邻居(0x5F527B)。
            for (var i = 0; i < groups.Count; i++)
            {
                var neighbour = groups[i].NeighbourMapName;
                if (string.Equals(neighbour, mapName, StringComparison.OrdinalIgnoreCase)) continue;
                // per-edge in-progress 守卫(原版 [group+0x11]):本 pass 内同一条边只递归一次
                var edgeKey = mapName + " " + neighbour;
                if (inProgress.Contains(edgeKey)) continue;
                inProgress.Add(edgeKey);

                var childTable = BuildNativeMapNextHopFor(neighbour, closed, inProgress);
                if (childTable != null)
                {
                    // sub_5F50E8: 邻居字典里每个目标以 hops+1 relax 进本图,next-hop = 该邻居
                    foreach (var child in childTable)
                        RelaxNativeMapNextHop(table, mapName, child.Key, neighbour, child.Value.Hops + 1);
                }
            }

            // sub_5F5144: 直连邻居 hops=1
            for (var i = 0; i < groups.Count; i++)
            {
                var neighbour = groups[i].NeighbourMapName;
                if (string.Equals(neighbour, mapName, StringComparison.OrdinalIgnoreCase)) continue;
                RelaxNativeMapNextHop(table, mapName, neighbour, neighbour, 1);
            }

            closed.Add(mapName);
            return table;
        }

        /// <summary>
        /// 原版 sub_5F5144 的 relax: 仅当【严格更小】才覆盖
        /// (`if ([v+8] > a4) { [v+8]=a4; [v+4]=nextHop; }`);从不插入自身条目。
        /// 等跳数时保留第一个通过严格 `>` 测试的赢家(即 DFS 顺序中的先到者)。
        /// </summary>
        private static void RelaxNativeMapNextHop(Dictionary<string, NativeMapRouteHop> table,
            string ownerMapName, string destMapName, string nextHopMapName, int hops)
        {
            if (string.IsNullOrEmpty(destMapName) || string.IsNullOrEmpty(nextHopMapName)) return;
            // 无自身条目(原版 sub_5F5144 开头 sub_40591C 比较 key == 本图名则跳过)
            if (string.Equals(destMapName, ownerMapName, StringComparison.OrdinalIgnoreCase)) return;

            if (table.TryGetValue(destMapName, out var current))
            {
                if (current.Hops > hops)                       // 严格更小才覆盖
                    table[destMapName] = new NativeMapRouteHop(nextHopMapName, hops);
                return;
            }
            table[destMapName] = new NativeMapRouteHop(nextHopMapName, hops);
        }

        /// <summary>
        /// 原版 sub_5F53E4: 在【当前图】的 next-hop 字典里按【目标图名】查下一跳。
        /// 查不到 / 值为空 → 返回 null(= 原版返回 0 ⇒ 不可达 / 已到目标图)。
        /// </summary>
        private string GetNativeMapNextHop(string currentMapName, string destMapName)
        {
            if (!_nativeMapNextHopBuilt) BuildNativeMapNextHopTables();
            if (string.IsNullOrEmpty(currentMapName) || string.IsNullOrEmpty(destMapName)) return null;
            if (!_nativeMapNextHop.TryGetValue(currentMapName, out var table)) return null;
            return table.TryGetValue(destMapName, out var hop) ? hop.NextHopMapName : null;
        }

        /// <summary>
        /// 原版 autogotomap 的服务端寻路 sub_5F4D4C。
        /// 架构 = 【启动预计算的最小跳数 next-hop 表】 + 【同边 portal tie-break】,
        /// 不是贪心几何朝目标。
        /// 同地图 → 单个 waypoint(目标坐标),客户端本地走格(原版顶部短路)。
        /// 跨地图 → 每跳: (1) 按目标图名查 next-hop 图(sub_5F53E4),
        ///   (2) 取通往它的那个 GROUP(sub_5F542C), (3) 只在该 GROUP 的 portal 链上取
        ///   min(源侧X² + 源侧Y²)(sub_5F4CF4), (4) 发节点 {当前所在图, 该 portal 源侧 X/Y}。
        /// next-hop 为 null(已到目标图) → 最后一个节点 = {目标图, 调用方传入的真实目标 X/Y}。
        /// 查询循环【无 visited、无跳数上限】(原版没有,靠表无环)。
        /// 首探就查不到 → 返回空数组 → 调用方发 SysMsg "目标不可到达"。
        /// </summary>
        internal IList<NativeMapPathNode> FindNativeMapPath(string sourceMapName,
            string destMapName, int nTargetX, int nTargetY)
        {
            var path = new List<NativeMapPathNode>();
            var source = FindMap(sourceMapName);
            var dest = FindMap(destMapName);
            if (source == null || dest == null) return path;   // 空数组 = 不可达

            // 同地图短路(原版 sub_40BDCC 名字比较 → 0x5F4DE1 写目标坐标):只发目标这一个 waypoint。
            if (string.Equals(source.sMapName, dest.sMapName, StringComparison.OrdinalIgnoreCase))
            {
                path.Add(new NativeMapPathNode(dest.sMapName, nTargetX, nTargetY));
                return path;
            }

            var destName = dest.sMapName;
            var currentMapName = source.sMapName;

            // 首探: 查不到 ⇒ 0 个节点(原版 0x5F4E53 test edi,edi / jz → 尾部长度 0)
            var nextHop = GetNativeMapNextHop(currentMapName, destName);
            if (nextHop == null) return path;

            // while ((cur = nextHop(cur, dest)) != nil) —— 无 visited、无上限
            while (nextHop != null)
            {
                var portal = SelectNativeMapRoutePortal(currentMapName, nextHop);
                if (portal == null)
                {
                    // GROUP/portal 链缺失(表与边不一致): 原版 sub_5F4CF4 返回 nil,
                    // 该跳无坐标可发 → 停止(退化为不可达/截断)
                    break;
                }
                // 节点 = {你当前所在的图, 选中 portal 的源侧坐标}
                path.Add(new NativeMapPathNode(currentMapName, portal.Value.SourceX,
                    portal.Value.SourceY));
                currentMapName = nextHop;
                nextHop = GetNativeMapNextHop(currentMapName, destName);
            }

            // next-hop 为 nil ⇒ 已在目标图上 ⇒ 最后一个节点用【真实目标坐标】(原版 0x5F4F18)
            path.Add(new NativeMapPathNode(currentMapName, nTargetX, nTargetY));
            return path;
        }

        /// <summary>
        /// 原版 sub_5F542C(按名字找 GROUP) + sub_5F4CF4(组内 portal tie-break)。
        /// 只在"当前图通往 nextHopMapName 的那一条边"的 portal 链上选,
        /// 代价 = min(源侧X² + 源侧Y²)。
        /// </summary>
        private NativeMapRoutePortal? SelectNativeMapRoutePortal(string currentMapName,
            string nextHopMapName)
        {
            if (!_nativeMapRouteGroups.TryGetValue(currentMapName, out var groups)) return null;
            for (var i = 0; i < groups.Count; i++)
            {
                if (!string.Equals(groups[i].NeighbourMapName, nextHopMapName,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                var portals = groups[i].Portals;
                NativeMapRoutePortal? best = null;
                // 原版 esi 初值 0xFFFFFFFF(MAXUINT 哨兵),仅当 best > cand 才更新 ⇒ 取最小
                var bestCost = long.MaxValue;
                for (var p = 0; p < portals.Count; p++)
                {
                    var cost = NativeMapRouteCost(portals[p].SourceX, portals[p].SourceY);
                    if (bestCost > cost)          // 严格更小才更新(等值保留先到者)
                    {
                        bestCost = cost;
                        best = portals[p];
                    }
                }
                return best;                      // 链为空 ⇒ null(原版返回 var_4 = nil)
            }
            return null;
        }

        /// <summary>
        /// 原版 sub_5F4CF4 的代价: **portal 源侧原始坐标的 X²+Y²**,取最小。
        /// 逐字节依据(idat_Q_marriage_ghost_autogoto_20260803.md APPENDIX run D/E):
        ///   0x5F4D0A `or esi,0FFFFFFFFh` 哨兵 → 0x5F4D20/25 imul 平方 → 0x5F4D27 add 求和
        ///   → 0x5F4D29 `cmp esi,ecx` / 0x5F4D2B `jbe` 跳过 ⇒ 仅当 best>cand 才更新 = 求最小。
        ///   X=[node+0x00]、Y=[node+0x04] 是 portal 自己的原始源侧坐标;
        ///   0x5F4CFB `xor eax,eax` 销毁了传入的落点指针 ⇒ 函数内**没有任何减法**,
        ///   既不减目标、也不减上一落点。等价于"距地图原点(0,0)最近的 portal"。
        /// 这是原版怪癖(作者本意疑为"离上一落点最近",但代码忽略了它),照抄。
        /// </summary>
        private static long NativeMapRouteCost(int nPortalSourceX, int nPortalSourceY)
        {
            long x = nPortalSourceX;
            long y = nPortalSourceY;
            return x * x + y * y;
        }

        public Envirnoment FindMap(string sMapName)
        {
            if (m_MapList.TryGetValue(sMapName, out var Map))
                return Map;
            // 按 sMapDesc (地图编号 "0") 或 m_sMapFileName 匹配
            foreach (var env in m_MapList.Values)
            {
                if (string.Equals(env.sMapDesc, sMapName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(env.m_sMapFileName, sMapName, StringComparison.OrdinalIgnoreCase))
                    return env;
            }
            return null;
        }

        internal Envirnoment FindMapByNativeName(string mapName)
        {
            return mapName != null && m_MapList.TryGetValue(mapName,
                out var environment)
                ? environment
                : null;
        }

        public Envirnoment GetMapInfo(int nServerIdx, string sMapName)
        {
            Envirnoment result = null;
            if (m_MapList.TryGetValue(sMapName, out var envirnoment))
            {
                if (envirnoment.nServerIndex == nServerIdx)
                {
                    result = envirnoment;
                }
            }
            return result;
        }

        
        
        
        
        
        public int GetMapOfServerIndex(string sMapName)
        {
            if (m_MapList.TryGetValue(sMapName, out var envirnoment))
            {
                return envirnoment.nServerIndex;
            }
            return 0;
        }

        public void LoadMapDoor()
        {
            for (var i = 0; i < Maps.Count; i++)
            {
                this.Maps[i].AddDoorToMap();
            }
        }

        public void ProcessMapDoor()
        {
        }

        public void ReSetMinMap()
        {
            
            
            
            
            
            
            
            
            
            
            
            
        }

        public void Run()
        {

        }
    }
}
