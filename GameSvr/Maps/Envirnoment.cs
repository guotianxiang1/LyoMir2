using SystemModule;
using SystemModule.Common;

namespace GameSvr
{
    public partial class Envirnoment
    {
        
        
        
        public int MonCount => m_nMonCount;
        
        
        
        public int HumCount => m_nHumCount;
        public int DynamicRoomPlayerCount => _dynamicRoomPlayers.Count;
        public short wWidth;
        public short wHeight;
        public string m_sMapFileName = string.Empty;
        public string sMapName = string.Empty;
        public string sMapDesc = string.Empty;
        public bool IsDynamicRoom { get; private set; }
        public string DynamicRoomName { get; private set; } = string.Empty;
        public int DynamicRoomPhysicalInstanceId { get; private set; } = -1;
        public int DynamicRoomIndex { get; private set; } = -1;
        // MOVE-17 @0x6BBFDC: cmp byte [Envir+0xB0],0 — missing/zero RUNFLAG keeps
        // over-encumbered run blocked. MapInfo path writes this from RUNFLAG token;
        // dynamic rooms inherit false until the factory copies Flag.boRUNFLAG.
        internal bool NativeCanRunWhileOverweight { get; set; } = false;
        internal NativeMapDropItemRawState NativeMapDropItems { get; } = new();
        internal NativeLimitBagItemDropState NativeLimitBagItemDrops { get; } =
            new();
        internal NativeDropControlState NativeDropControl { get; } =
            new(NativeDropControlBucketField.ItemName);
        internal int DynamicRoomState { get; set; }
        internal bool DynamicRoomBlocked { get; set; }
        private NativeDynamicRoomManager DynamicRoomManagerOwner { get; set; }
        private CellAttribute[] MapCellAttributes;
        private byte[] MapCellSkillFlags;
        private IList<CellObject>[] MapCellObjectLists;
        public int nMinMap = 0;
        public int nServerIndex = 0;
        
        
        
        public int nRequestLevel = 0;
        /// <summary>
        /// Native <c>TEnvironment+0x30</c>: minimum total active point (base + job-tier
        /// bonus) to enter when credit auth is enabled. Populated from MapActivePoint.xml
        /// by <see cref="NativeMapActivePointLoader"/> (sub_618FB8 @0x00618FB8).
        /// </summary>
        internal int NativeMapActivePointRequired { get; set; } =
            NativeMapActivePointLoader.UnconfiguredRequiredSentinel;
        public TMapFlag Flag = null;
        public byte BreakLevel => Flag?.BreakLevel ?? 0;
        public ushort CrazyBreakLevel => Flag?.CrazyBreakLevel ?? 0;
        public HashSet<int> LimitSkillIds { get; } = new();
        public bool bo2C = false;
        
        
        
        public IList<TDoorInfo> m_DoorList = null;
        public object QuestNPC = null;
        
        
        
        public IList<TMapQuestInfo> m_QuestList = null;
        public int m_dwWhisperTick = 0;
        private int m_nMonCount = 0;
        private int m_nHumCount = 0;
        private readonly HashSet<TBaseObject> _dynamicRoomPlayers = new();
        public IList<PointInfo> m_PointList;

        private bool TryGetMapCellIndex(int nX, int nY, out int index)
        {
            if (nX >= 0 && nX < wWidth && nY >= 0 && nY < wHeight)
            {
                index = nX * wHeight + nY;
                return true;
            }
            index = -1;
            return false;
        }

        private IList<CellObject> EnsureCellObjectList(int nX, int nY)
        {
            if (!TryGetMapCellIndex(nX, nY, out var index))
            {
                return null;
            }
            return MapCellObjectLists[index] ??= new List<CellObject>();
        }

        internal void ReleaseCellObjectList(int nX, int nY)
        {
            if (!TryGetMapCellIndex(nX, nY, out var index))
            {
                return;
            }
            MapCellObjectLists[index]?.Clear();
            MapCellObjectLists[index] = null;
        }

        /// <summary>
        /// Native <c>sub_77BEB4</c> @0x0077BEB4 walks the 12-byte cell array and
        /// clears only byte +0, the terrain attribute. Object chains and skill
        /// flags remain unchanged.
        /// </summary>
        internal void SetAllNativeMapCellsWalkable()
        {
            if (MapCellAttributes == null)
                return;

            Array.Fill(MapCellAttributes, CellAttribute.Walk);
        }

        public Envirnoment()
        {
            sMapName = string.Empty;
            nServerIndex = 0;
            nMinMap = 0;
            Flag = new TMapFlag();
            m_nMonCount = 0;
            m_nHumCount = 0;
            m_DoorList = new List<TDoorInfo>();
            m_QuestList = new List<TMapQuestInfo>();
            m_PointList = new List<PointInfo>();
            m_dwWhisperTick = 0;
        }

        internal void ConfigureDynamicRoom(string roomName, int physicalInstanceId,
            NativeDynamicRoomManager manager)
        {
            IsDynamicRoom = true;
            DynamicRoomName = roomName;
            DynamicRoomPhysicalInstanceId = physicalInstanceId;
            DynamicRoomIndex = -1;
            DynamicRoomState = 0;
            DynamicRoomBlocked = false;
            DynamicRoomManagerOwner = manager;
        }

        internal void SetDynamicRoomLeaseIndex(int leaseIndex)
        {
            DynamicRoomIndex = leaseIndex;
        }

        internal void ConfigureDormantDynamicRoom(string roomName)
        {
            IsDynamicRoom = true;
            DynamicRoomName = roomName;
            DynamicRoomPhysicalInstanceId = -1;
            DynamicRoomIndex = -1;
            DynamicRoomState = 0;
            DynamicRoomBlocked = false;
            DynamicRoomManagerOwner = null;
        }

        public bool AllowMagics(string magicName)
        {
            return true;
        }

        
        
        
        
        public bool AllowMagics(short magicId, int type)
        {
            return true;
        }

        public bool IsSkillAllowedAt(int nX, int nY, int skillKey)
        {
            if (GetMapCellSkillFlag(nX, nY) != 0)
                return false;
            return !LimitSkillIds.Contains(skillKey);
        }

        public byte GetMapCellSkillFlag(int nX, int nY)
        {
            return TryGetMapCellIndex(nX, nY, out var index) &&
                   MapCellSkillFlags != null
                ? MapCellSkillFlags[index]
                : (byte)0;
        }

        public void SetMapCellSkillFlag(int left, int right, int top, int bottom,
            byte value)
        {
            if (MapCellSkillFlags == null || wWidth <= 0 || wHeight <= 0)
                return;

            if (left < 0)
                left = 0;
            if (right >= wWidth)
                right = wWidth - 1;
            if (top < 0)
                top = 0;
            if (bottom > wHeight)
                bottom = wHeight - 1;
            if (right < left || bottom < top)
                return;

            for (var x = left; x <= right; x++)
            {
                for (var y = top; y <= bottom; y++)
                {
                    // The native setter accepts y == height and aliases the next
                    // column. Preserve that in-array effect without writing past
                    // managed storage at the final column.
                    var index = x * wHeight + y;
                    if ((uint)index < (uint)MapCellSkillFlags.Length)
                        MapCellSkillFlags[index] = value;
                }
            }
        }

        
        
        
        
        public object AddToMap(int nX, int nY, CellType btType, object pRemoveObject)
        {
            return AddToMap(nX, nY, btType, pRemoveObject, 0);
        }

        public object AddToMap(int nX, int nY, CellType btType,
            object pRemoveObject, int nAliveSeconds)
        {
            object result = null;
            MapCellinfo MapCellInfo;
            CellObject OSObject;
            MapItem MapItem;
            int nGoldCount;
            const string sExceptionMsg = "[Exception] TEnvirnoment::AddToMap";
            try
            {
                if (pRemoveObject == null)
                {
                    return null;
                }

                var bo1E = false;
                var mapCell = false;
                var dwAddTime = GetNativeAddToMapStamp(nAliveSeconds);
                MapCellInfo = GetMapCellInfo(nX, nY, ref mapCell);
                // sub_7776EC scans and refreshes the native chain before its
                // cell-attribute gate. A duplicate refresh returns nil.
                if (mapCell && ScanNativeAddToMapChain(
                        MapCellInfo, pRemoveObject, dwAddTime))
                {
                    return null;
                }
                // 0x777967..0x77797F: a non-walkable cell rejects every
                // object except TFireworksEvent (Delphi `is`, descendants
                // included). The dedicated managed type preserves that class
                // gate without granting all map events the exception.
                if (mapCell && (MapCellInfo.Valid ||
                                pRemoveObject is FireworksEvent))
                {
                    if (MapCellInfo.ObjList == null)
                    {
                        MapCellInfo.ObjList = EnsureCellObjectList(nX, nY);
                    }
                    else
                    {
                        if (btType == CellType.OS_ITEMOBJECT)
                        {
                            if (((MapItem)pRemoveObject).Name == Grobal2.sSTRING_GOLDNAME)
                            {
                                for (var i = 0; i < MapCellInfo.Count; i++)
                                {
                                    OSObject = MapCellInfo.ObjList[i];
                                    if (OSObject.CellType == CellType.OS_ITEMOBJECT)
                                    {
                                        MapItem = (MapItem)MapCellInfo.ObjList[i].CellObj;
                                        if (MapItem.Name == Grobal2.sSTRING_GOLDNAME)
                                        {
                                            nGoldCount = MapItem.Count + ((MapItem)pRemoveObject).Count;
                                            if (nGoldCount <= 2000)
                                            {
                                                MapItem.Count = nGoldCount;
                                                MapItem.Looks = M2Share.GetGoldShape(nGoldCount);
                                                MapItem.AniCount = 0;
                                                MapItem.Reserved = 0;
                                                OSObject.dwAddTime = HUtil32.GetTickCount();
                                                result = MapItem;
                                                bo1E = true;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    if (!bo1E)
                    {
                        OSObject = new CellObject
                        {
                            CellType = btType,
                            CellObj = pRemoveObject,
                            dwAddTime = dwAddTime
                        };
                        // 0x777A9C..0x777AAE splices the node at cell.head.
                        MapCellInfo.ObjList.Insert(0, OSObject);
                        result = pRemoveObject;
                        if (btType == CellType.OS_MOVINGOBJECT)
                        {
                            var movingObject = (TBaseObject)pRemoveObject;
                            AddDynamicRoomPlayer(movingObject);
                            if (!movingObject.m_boAddToMaped)
                            {
                                movingObject.m_boDelFormMaped = false;
                                movingObject.m_boAddToMaped = true;
                                AddObject(movingObject);
                            }
                            // 0x777AC6 `FF 53 04 call [map_vmt+0x04]` -> TDynEnvir.AddObject
                            // 0x5FD534，其尾部 0x5FD56A 派发 @OnEnter。人数 0x5FD559
                            // `inc [map+0xD8]` 在派发之前，所以这里必须排在两个计数器之后。
                            NativeDynEnvirAddObjectTrigger(movingObject);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage(sExceptionMsg + " " + ex.Message);
            }
            return result;
        }

        private static int GetNativeAddToMapStamp(int nAliveSeconds)
        {
            if (nAliveSeconds <= 0)
            {
                return HUtil32.GetTickCount();
            }

            var stamp = unchecked(HUtil32.GetTickCount()
                                  + nAliveSeconds * 1000 - 600000);
            return stamp > 0 ? stamp : 0;
        }

        private static bool ScanNativeAddToMapChain(
            MapCellinfo mapCellInfo, object addedObject, int addTime)
        {
            var objects = mapCellInfo.ObjList;
            if (objects == null)
            {
                return false;
            }

            var index = 0;
            while (index < objects.Count)
            {
                var cellObject = objects[index];
                if (cellObject.CellType == CellType.OS_MOVINGOBJECT
                    && cellObject.CellObj != null
                    && TBaseObject.IsNativeStaleCellActor(
                        cellObject.CellObj))
                {
                    // 0x7777F7..0x777811 unlinks a stale actor and keeps the
                    // previous chain cursor in place.
                    objects.RemoveAt(index);
                    continue;
                }

                if (ReferenceEquals(cellObject.CellObj, addedObject))
                {
                    cellObject.dwAddTime = addTime;
                    return true;
                }

                index++;
            }

            return false;
        }

        public void AddDoorToMap()
        {
            TDoorInfo Door;
            for (var i = 0; i < m_DoorList.Count; i++)
            {
                Door = m_DoorList[i];
                AddToMap(Door.nX, Door.nY, CellType.OS_DOOR, Door);
            }
        }

        public bool GetMapCellInfo(int nX, int nY, ref MapCellinfo MapCellInfo)
        {
            if (TryGetMapCellIndex(nX, nY, out var index))
            {
                MapCellInfo = new MapCellinfo
                {
                    Attribute = MapCellAttributes[index],
                    SkillFlag = MapCellSkillFlags[index],
                    ObjList = MapCellObjectLists[index]
                };
                return true;
            }
            return false;
        }

        /// <summary>
        /// Native <c>sub_7792EC(eax=Envir, edx=X, ecx=Y, [esp+4]=boRelease)</c>,
        /// the cell-attribute half of the stall lock. It resolves the cell through
        /// sub_7776A8 and then writes the attribute byte directly:
        /// <c>0x779310 74 08 je 0x77931A</c> — a NON-zero flag stores 0
        /// (<c>0x779315 C6 00 00</c>, walkable) and a zero flag stores 2
        /// (<c>0x77931D C6 00 02</c>, LowWall). TStallEvent's constructor calls it
        /// with 0 to claim the cell (@0x719A67 `6A 00`) and its Run/Close call it
        /// with 1 to give the cell back (@0x719AE7 / @0x7199DE `6A 01`).
        /// Returns false when the coordinates are off-map, matching the
        /// <c>test al,al / je</c> at 0x77930A.
        /// </summary>
        internal bool SetNativeStallCellAttribute(int nX, int nY, bool boRelease)
        {
            if (!TryGetMapCellIndex(nX, nY, out var index))
            {
                return false;
            }
            // 摆摊穿人 @0x77931D：claim 时仍 call sub_7792EC，但写 0(Walk) 而非 2(LowWall)。
            MapCellAttributes[index] = boRelease || Plugins.YanshenPangu1Patches.StallCellsAllowPassThrough()
                ? CellAttribute.Walk
                : CellAttribute.LowWall;
            return true;
        }

        public MapCellinfo GetMapCellInfo(int nX, int nY, ref bool success)
        {
            if (TryGetMapCellIndex(nX, nY, out var index))
            {
                success = true;
                return new MapCellinfo
                {
                    Attribute = MapCellAttributes[index],
                    SkillFlag = MapCellSkillFlags[index],
                    ObjList = MapCellObjectLists[index]
                };
            }
            success = false;
            return MapCellinfo.HighWall;
        }

        public int MoveToMovingObject(int nCX, int nCY, TBaseObject Cert, int nX, int nY, bool boFlag)
        {
            return MoveToMovingObjectCore(nCX, nCY, Cert, nX, nY, boFlag, false);
        }

        public int MoveToMovingObjectForRun(int nCX, int nCY, TBaseObject Cert, int nX, int nY, bool boFlag)
        {
            return MoveToMovingObjectCore(nCX, nCY, Cert, nX, nY, boFlag, true);
        }

        /// <summary>
        /// Native <c>sub_779CD8</c>: move the first source node whose object is
        /// <paramref name="cert"/> directly to the target cell head. Unlike
        /// <see cref="MoveToMovingObject"/>, this primitive deliberately has no
        /// bounds, terrain, occupancy, LinkPoint, or stale-object gate.
        /// </summary>
        internal bool NativeRelocateMovingObjectNodeExact(int sourceX,
            int sourceY, TBaseObject cert, int targetX, int targetY)
        {
            // 0x779CF1..0x779D22 uses movzx WORD coordinates and indexes the
            // column-major cell array directly. Invalid coordinates are native's
            // fault domain, so do not turn them into a normal false result here.
            int sourceIndex = unchecked((ushort)sourceX * wHeight +
                (ushort)sourceY);
            int targetIndex = unchecked((ushort)targetX * wHeight +
                (ushort)targetY);
            var sourceObjects = MapCellObjectLists[sourceIndex];
            if (sourceObjects == null)
            {
                return false;
            }

            for (var index = 0; index < sourceObjects.Count; index++)
            {
                var node = sourceObjects[index];
                if (!ReferenceEquals(node.CellObj, cert))
                {
                    continue;
                }

                sourceObjects.RemoveAt(index);
                if (sourceObjects.Count == 0)
                {
                    MapCellObjectLists[sourceIndex] = null;
                }

                var targetObjects = MapCellObjectLists[targetIndex] ??=
                    new List<CellObject>();
                targetObjects.Insert(0, node);
                return true;
            }
            return false;
        }

        private int MoveToMovingObjectCore(int nCX, int nCY, TBaseObject Cert, int nX, int nY, bool boFlag, bool useRunRules)
        {
            MapCellinfo MapCellInfo;
            bool mapCell = false;
            TBaseObject BaseObject;
            CellObject OSObject;
            bool bo1A;
            const string sExceptionMsg = "[Exception] TEnvirnoment::MoveToMovingObject";
            var result = 0;
            try
            {
                // 原版 sub_7797CC 第 2 步（0x779825 / 0x779835）：目标坐标必须落在
                // [0,Width) x [0,Height)，否则整个调用直接 FALSE —— 关键是这道闸在第 10 步
                // (0x779A5B) 的"从旧格摘链"之前，所以越界的尝试不会动到对象的位置。
                // C# 这边摘链在前、校验在后：越界目标会先把对象从旧格删掉，再因为
                // GetMapCellInfo 取不到目标格而永不落格 —— 对象被摘成孤儿。
                // 怪物 mover 故意允许尝试 x==Width（MOVE-42），靠的就是这道闸兜住，
                // 所以放宽怪物边界之前必须先补上它。
                if (!TryGetMapCellIndex(nX, nY, out _))
                {
                    return -1;
                }
                bo1A = true;
                MapCellInfo = GetMapCellInfo(nX, nY, ref mapCell);
                if (!boFlag && mapCell)
                {
                    if (MapCellInfo.Valid)
                    {
                        if (MapCellInfo.ObjList != null)
                        {
                            if (useRunRules)
                            {
                                if (HasRunBlockingObject(MapCellInfo, Cert))
                                {
                                    bo1A = false;
                                }
                            }
                            else
                            {
                                var i = 0;
                                while (i < MapCellInfo.Count)
                                {
                                    if (MapCellInfo.ObjList[i].CellType == CellType.OS_MOVINGOBJECT)
                                    {
                                        // MOVE-31 复核 —— 族 A 摘链臂，原生 sub_7797CC
                                        // （自述名 TEnvironment.CreatureMoveTo）的占位扫描：
                                        //   007798A4  8B 45 EC / 80 38 01   cmp byte [node],1
                                        //   007798AA  0F 85 FC 00 00 00     jne 0x7799AC   ; 非 actor -> 下一节点
                                        //   007798B0  8B 45 EC / 8B 70 04   esi := node^.POject
                                        //   007798B6  85 F6 / 0F 84 EE 00.. POject = nil -> 只跳过，不摘链
                                        //   007798C0  E8 9F C4 FE FF        call 0x765D64  ; 有效性谓词
                                        //   007798C5  84 C0
                                        //   007798C7  0F 85 CB 00 00 00     jne 0x779998   ; 有效 -> call [POject.VMT+0]
                                        //   007798CD-007798DC  摘链：prev^.Next := next / cell^.head := next
                                        //   007798E7  B3 01                 bl := 1（抑制 prev 前进）
                                        //   ...       记 [Exception] 日志后跳 0x7799AC —— continue，不占位
                                        // 旧账本 MOVE-31 判「可观测等价、不改」，前提是三项合取在 C#
                                        // 结构上不可达；但 m_sCharName 无初值、默认 null，只是「实践上
                                        // 不可达」。故按 SPWN-56 同样处理：谓词为假就摘链，不夺走任何
                                        // 已命名 + 已入图的活体。
                                        if (TBaseObject.IsNativeStaleCellActor(MapCellInfo.ObjList[i].CellObj))
                                        {
                                            MapCellInfo.Remove(i);
                                            if (MapCellInfo.Count > 0)
                                            {
                                                continue;
                                            }
                                            ReleaseCellObjectList(nX, nY);
                                            break;
                                        }
                                        BaseObject = (TBaseObject)MapCellInfo.ObjList[i].CellObj;
                                        if (BaseObject != null)
                                        {
                                            if (BaseObject.IsNativeCellBlocking())
                                            {
                                                bo1A = false;
                                                break;
                                            }
                                        }
                                    }
                                    i++;
                                }
                            }
                        }
                    }
                    else
                    {
                        result = -1;
                        bo1A = false;
                    }
                }
                // MOVE-34 — native cell+2 gate. MoveToMovingObject sub_7797CC
                // @0x7799D5 `cmp byte [cell+2],0` and @0x7799DE
                // `cmp byte [Cert+0x178],0`: the move is rejected
                // (`mov byte [ebp-0xA],0` @0x7799E7 -> FALSE @0x7799EF) only when
                // BOTH the target cell carries a LinkPoint (cell+2 != 0) AND the
                // mover is a creature (Cert+0x178 != 0, written 0x32 by the
                // TCreature ctor @0x764E5F and 0 by the TPlayer ctor @0x6AD76F,
                // so it blocks every creature and never a player). The gate sits
                // AFTER the boFlag short-circuit @0x779874 (`jne 0x7799C6`), so it
                // fires even when boFlag(ignore-occupancy) is set. cell+2 is
                // written 1:1 with the LinkPoint node insertion in the loader
                // sub_779328 (@0x7793D4 `mov byte [cell+2],1`, right after the
                // kind=4/OS_GATEOBJECT node is head-inserted into cell[8]), so a
                // scan of the cell's object list for OS_GATEOBJECT reproduces the
                // byte read exactly — the same idiom this file already uses for the
                // other two cell+2 readers (GetItemEx drop/placement avoidance
                // @0x778DEF and the Walk player gate-step @0x778F93). Walk-only:
                // a full-image scan finds cell+2 read solely in the walk mover;
                // the run path (useRunRules) has no cell+2 read and only ever runs
                // players (CommitRunMove) anyway.
                if (bo1A && !useRunRules
                    && NativeLinkPointCellBlocksMover(MapCellInfo, Cert))
                {
                    bo1A = false;
                }
                if (bo1A)
                {
                    if (GetMapCellInfo(nX, nY, ref MapCellInfo) && MapCellInfo.Attribute != 0)
                    {
                        result = -1;
                    }
                    else
                    {
                        // MOVE-35(a) — 原版 sub_7797CC 在摘链之前还要校验**旧**坐标，
                        // C# 只校验了新坐标（:353）：
                        //   007799F5  0F B7 45 FA     movzx eax,word [ebp-6]  ; 旧 X
                        //   007799FC  3B 42 3C        cmp   eax,[edx+0x3C]    ; Width
                        //   007799FF  0F 8D A8 00..   jge   0x779AAD          ; -> FALSE
                        //   00779A05  0F B7 55 F8     movzx edx,word [ebp-8]  ; 旧 Y
                        //   00779A0C  3B 51 40        cmp   edx,[ecx+0x40]    ; Height
                        //   00779A0F  0F 8D 98 00..   jge   0x779AAD          ; -> FALSE
                        // movzx 让旧坐标恒为非负，所以只有上界；TryGetMapCellIndex 的
                        // 下界对原版能产出的取值域是恒真的。怪物 mover 放宽到 x==Width
                        // （MOVE-42）之后，旧坐标同样可能落在界外。
                        if (!TryGetMapCellIndex(nCX, nCY, out _))
                        {
                            return 0;
                        }
                        CellObject movedCellObject = null;
                        if (GetMapCellInfo(nCX, nCY, ref MapCellInfo) && MapCellInfo.ObjList != null)
                        {
                            var i = 0;
                            while (i < MapCellInfo.Count)
                            {
                                OSObject = MapCellInfo.ObjList[i];
                                // 0x779A5B..0x779A64 compares only node.Object
                                // with Cert. It does not inspect the node tag.
                                if (ReferenceEquals(OSObject.CellObj, Cert))
                                {
                                    movedCellObject = OSObject;
                                    MapCellInfo.Remove(i);
                                    if (MapCellInfo.Count == 0)
                                    {
                                        ReleaseCellObjectList(nCX, nCY);
                                    }
                                    break;
                                }
                                i++;
                            }
                        }
                        // MOVE-35(b) — 原版只有找到自己并摘链之后才会置 TRUE：
                        //   00779A4C  83 7D EC 00  cmp dword [ebp-0x14],0 ; 旧格表头
                        //   00779A50  74 5B        je  0x779AAD           ; 空表 -> FALSE
                        //   00779A5B  8B 45 EC ..  mov eax,[node+4]
                        //   00779A61  3B 45 0C     cmp eax,[ebp+0xC]      ; 是不是自己
                        //   00779A64  75 35        jne 0x779A9B           ; 不是 -> 下一个
                        //   00779A95  C6 45 F7 01  mov byte [ebp-9],1     ; 唯一置 TRUE 处
                        //   00779AAD  33 C0        xor eax,eax            ; 遍历完 -> FALSE
                        // C# 无论找没找到都 Add 并返回 1，于是"从一个自己并不在的格子
                        // 搬走"会在目标格凭空多出一份登记 —— 旧格的幽灵占位就是这么来的。
                        if (movedCellObject == null)
                        {
                            return 0;
                        }
                        if (GetMapCellInfo(nX, nY, ref MapCellInfo))
                        {
                            if (MapCellInfo.ObjList == null)
                            {
                                MapCellInfo.ObjList = EnsureCellObjectList(nX, nY);
                            }
                            // MOVE-35(c) — dest cell is a native singly-linked list
                            // whose head lives at destCell[8]. After unlinking, the
                            // mover splices the same node onto the front:
                            //   00779A80  8B 45 E0        mov eax,[ebp-0x20] ; dest cell
                            //   00779A83  8B 40 08        mov eax,[eax+8]    ; old head
                            //   00779A89  89 42 0C        mov [edx+0xC],eax  ; node.next = old head
                            //   00779A92  89 50 08        mov [eax+8],edx    ; destCell[8] = node
                            // Reusing the same CellObject also preserves the native
                            // tag, object payload and dwAddTime; the mover writes none
                            // of those fields. List index 0 represents the native head.
                            MapCellInfo.ObjList.Insert(0, movedCellObject);
                            result = 1;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                M2Share.ErrorMessage(sExceptionMsg);
                M2Share.ErrorMessage(e.Message);
            }
            return result;
        }

        /// <summary>
        /// MOVE-34 — the C# reader for the native cell+2 (LinkPoint) marker used by
        /// the walk mover. Native sub_7797CC @0x7799D5/@0x7799DE blocks a mover when
        /// the target cell is a LinkPoint (cell+2 != 0) AND the mover is a creature
        /// (Cert+0x178 != 0). Because the loader sub_779328 sets cell+2 = 1 in the
        /// same breath as it head-inserts the kind=4 (OS_GATEOBJECT) node into the
        /// cell (@0x7793D4), "cell+2 != 0" is equivalent to "the cell list contains
        /// an OS_GATEOBJECT" — the exact idiom GetItemEx/Walk already use for the
        /// two other cell+2 readers. The creature test maps Cert+0x178==0 (player)
        /// to <c>m_btRaceServer == RC_PLAYOBJECT</c>, matching the existing gate-step
        /// mapping in TBaseObject.Walk / ProcessNativeMoveActionWithoutBroadcast.
        /// </summary>
        private static bool NativeLinkPointCellBlocksMover(MapCellinfo mapCellInfo, TBaseObject mover)
        {
            if (mover == null || mover.m_btRaceServer == Grobal2.RC_PLAYOBJECT)
            {
                return false;
            }
            var objList = mapCellInfo.ObjList;
            if (objList == null)
            {
                return false;
            }
            for (var i = 0; i < objList.Count; i++)
            {
                if (objList[i].CellType == CellType.OS_GATEOBJECT)
                {
                    return true;
                }
            }
            return false;
        }

        public bool CanWalk(int nX, int nY, bool boFlag)
        {
            CellObject OSObject;
            TBaseObject BaseObject;
            var result = false;
            var mapCell = false;
            MapCellinfo MapCellInfo = GetMapCellInfo(nX, nY, ref mapCell);
            if (mapCell && MapCellInfo.Valid)
            {
                if (boFlag)
                {
                    return true;
                }
                result = true;
                if (!boFlag && MapCellInfo.ObjList != null)
                {
                    var i = 0;
                    while (i < MapCellInfo.Count)
                    {
                        OSObject = MapCellInfo.ObjList[i];
                        if (OSObject.CellType == CellType.OS_MOVINGOBJECT)
                        {
                            // 战神 sub_777EF8（自述名 TEnvironment.CanWalk，见 0x778030 失败臂拼的
                            // "[Exception]: TEnvironment.CanWalk Pt.POject.CName = 空 Pt = "）：
                            //   00778014  8B 45 E8 / 80 38 01     cmp byte [node],1      ; CellType
                            //   0077801A  0F 85 D1 00 00 00       jne 0x7780F1           ; 非 actor -> 下一节点
                            //   00778020  8B 45 E8 / 8B 70 04     esi := node^.POject
                            //   00778026  85 F6 / 0F 84 C3 00..   POject = nil -> 只跳过，不摘链
                            //   0077802E  8B C6
                            //   00778030  E8 2F DD FE FF          call 0x765D64          ; 有效性谓词
                            //   00778035  84 C0
                            //   00778037  0F 85 A1 00 00 00       jne 0x7780DE           ; 有效 -> 占位判定
                            //   0077803D-0077804C  摘链：prev^.Next := next / cell^.head := next
                            //   00778057  B3 01                   bl := 1（抑制 prev 前进）
                            //   ...       记 [Exception] 日志后跳尾部 —— 是 continue，不是 break
                            if (TBaseObject.IsNativeStaleCellActor(OSObject.CellObj))
                            {
                                MapCellInfo.Remove(i);
                                if (MapCellInfo.Count > 0)
                                {
                                    continue;
                                }
                                ReleaseCellObjectList(nX, nY);
                                break;
                            }
                            BaseObject = (TBaseObject)OSObject.CellObj;
                            if (BaseObject != null)
                            {
                                if (BaseObject.IsNativeCellBlocking())
                                {
                                    result = false;
                                    break;
                                }
                            }
                        }
                        i++;
                    }
                }
            }
            return result;
        }

        public bool CanWalk(int nX, int nY)
        {
            return CanWalk(nX, nY, false);
        }

        
        
        
        
        
        
        
        
        public bool CanWalkOfItem(int nX, int nY, bool boFlag, bool boItem)
        {
            var mapCell = false;
            CellObject OSObject;
            TBaseObject BaseObject;
            var result = true;
            MapCellinfo MapCellInfo = GetMapCellInfo(nX, nY, ref mapCell);
            if (mapCell && MapCellInfo.Valid)
            {
                if (MapCellInfo.ObjList != null)
                {
                    for (var i = 0; i < MapCellInfo.Count; i++)
                    {
                        OSObject = MapCellInfo.ObjList[i];
                        if (!boFlag && OSObject.CellType == CellType.OS_MOVINGOBJECT)
                        {
                            BaseObject = (TBaseObject)OSObject.CellObj;
                            if (BaseObject != null)
                            {
                                if (BaseObject.IsNativeCellBlocking())
                                {
                                    result = false;
                                    break;
                                }
                            }
                        }
                        if (!boItem && OSObject.CellType == CellType.OS_ITEMOBJECT)
                        {
                            result = false;
                            break;
                        }
                    }
                }
            }
            return result;
        }

        private bool IsRunBlockingObject(TBaseObject baseObject)
        {
            if (baseObject == null || !baseObject.IsNativeCellBlocking())
            {
                return false;
            }

            var castle = M2Share.CastleManager.InCastleWarArea(baseObject);
            if (M2Share.g_Config.boWarDisHumRun && castle != null && castle.m_boUnderWar)
            {
                return true;
            }

            if (baseObject.m_btRaceServer == Grobal2.RC_PLAYOBJECT)
            {
                return !M2Share.g_Config.boRunHuman && !Flag.boRUNHUMAN;
            }

            if (baseObject.m_btRaceServer == Grobal2.RC_NPC)
            {
                return !M2Share.g_Config.boRunNpc;
            }

            if (baseObject.m_btRaceServer == Grobal2.RC_GUARD || baseObject.m_btRaceServer == Grobal2.RC_ARCHERGUARD)
            {
                return !M2Share.g_Config.boRunGuard;
            }

            return !M2Share.g_Config.boRunMon && !Flag.boRUNMON;
        }

        private bool HasRunBlockingObject(MapCellinfo mapCellInfo, TBaseObject movingObject)
        {
            if (mapCellInfo.ObjList == null)
            {
                return false;
            }

            for (var i = 0; i < mapCellInfo.Count; i++)
            {
                var cellObject = mapCellInfo.ObjList[i];
                if (cellObject.CellType != CellType.OS_MOVINGOBJECT)
                {
                    continue;
                }

                var baseObject = cellObject.CellObj as TBaseObject;
                if (baseObject != movingObject && IsRunBlockingObject(baseObject))
                {
                    return true;
                }
            }

            return false;
        }

        public bool CanWalkEx(int nX, int nY, bool boFlag)
        {
            var result = false;
            var mapCell = false;
            MapCellinfo MapCellInfo = GetMapCellInfo(nX, nY, ref mapCell);
            if (mapCell && MapCellInfo.Valid)
            {
                result = true;
                if (!boFlag && HasRunBlockingObject(MapCellInfo, null))
                {
                    result = false;
                }
            }
            return result;
        }

        public int DeleteFromMap(int nX, int nY, CellType cellType, object pRemoveObject)
        {
            return DeleteFromMap(nX, nY, cellType, pRemoveObject, true);
        }

        internal int RemoveMovingObjectEverywhereExact(TBaseObject actor,
            bool notifyDynamicRoomLifecycle = true)
        {
            if (actor == null || MapCellObjectLists == null)
                return 0;

            var removed = 0;
            for (var cellIndex = 0; cellIndex < MapCellObjectLists.Length;
                 cellIndex++)
            {
                var objects = MapCellObjectLists[cellIndex];
                if (objects == null) continue;

                for (var objectIndex = objects.Count - 1; objectIndex >= 0;
                     objectIndex--)
                {
                    var cellObject = objects[objectIndex];
                    if (cellObject?.CellType != CellType.OS_MOVINGOBJECT
                        || !ReferenceEquals(cellObject.CellObj, actor))
                        continue;

                    objects.RemoveAt(objectIndex);
                    removed++;
                }

                if (objects.Count == 0)
                    MapCellObjectLists[cellIndex] = null;
            }

            if (removed > 0)
                RemoveMovingObjectRegistration(actor,
                    notifyDynamicRoomLifecycle);
            return removed;
        }

        internal bool ContainsMovingObjectEverywhereExact(TBaseObject actor)
        {
            if (actor == null || MapCellObjectLists == null)
                return false;

            for (var cellIndex = 0; cellIndex < MapCellObjectLists.Length;
                 cellIndex++)
            {
                var objects = MapCellObjectLists[cellIndex];
                if (objects == null) continue;

                for (var objectIndex = objects.Count - 1; objectIndex >= 0;
                     objectIndex--)
                {
                    var cellObject = objects[objectIndex];
                    if (cellObject?.CellType == CellType.OS_MOVINGOBJECT
                        && ReferenceEquals(cellObject.CellObj, actor))
                        return true;
                }
            }
            return false;
        }

        internal TBaseObject[] SnapshotMovingObjectsExact()
        {
            if (MapCellObjectLists == null)
                return Array.Empty<TBaseObject>();

            var actors = new HashSet<TBaseObject>(
                ReferenceEqualityComparer.Instance);
            for (var cellIndex = 0; cellIndex < MapCellObjectLists.Length;
                 cellIndex++)
            {
                var objects = MapCellObjectLists[cellIndex];
                if (objects == null) continue;
                for (var objectIndex = 0; objectIndex < objects.Count;
                     objectIndex++)
                {
                    var cellObject = objects[objectIndex];
                    if (cellObject?.CellType == CellType.OS_MOVINGOBJECT
                        && cellObject.CellObj is TBaseObject actor)
                        actors.Add(actor);
                }
            }
            return actors.ToArray();
        }

        internal int RemoveItemObjectsEverywhere()
        {
            if (MapCellObjectLists == null) return 0;

            var removed = 0;
            for (var cellIndex = 0; cellIndex < MapCellObjectLists.Length;
                 cellIndex++)
            {
                var objects = MapCellObjectLists[cellIndex];
                if (objects == null) continue;
                for (var objectIndex = objects.Count - 1; objectIndex >= 0;
                     objectIndex--)
                {
                    if (objects[objectIndex]?.CellType !=
                        CellType.OS_ITEMOBJECT)
                        continue;
                    objects.RemoveAt(objectIndex);
                    removed++;
                }
                if (objects.Count == 0)
                    MapCellObjectLists[cellIndex] = null;
            }
            return removed;
        }

        internal bool ContainsItemObjects()
        {
            if (MapCellObjectLists == null) return false;
            for (var cellIndex = 0; cellIndex < MapCellObjectLists.Length;
                 cellIndex++)
            {
                var objects = MapCellObjectLists[cellIndex];
                if (objects == null) continue;
                for (var objectIndex = objects.Count - 1; objectIndex >= 0;
                     objectIndex--)
                {
                    if (objects[objectIndex]?.CellType ==
                        CellType.OS_ITEMOBJECT)
                        return true;
                }
            }
            return false;
        }

        internal bool ContainsEventAtExact(int nX, int nY,
            Event expectedEvent)
        {
            if (expectedEvent == null || MapCellObjectLists == null
                || nX < 0 || nY < 0 || nX >= wWidth || nY >= wHeight)
                return false;

            var objects = MapCellObjectLists[nX * wHeight + nY];
            if (objects == null) return false;

            for (var i = objects.Count - 1; i >= 0; i--)
            {
                var cellObject = objects[i];
                if (cellObject?.CellType == CellType.OS_EVENTOBJECT
                    && ReferenceEquals(cellObject.CellObj, expectedEvent))
                    return true;
            }
            return false;
        }

        internal bool ContainsEventEverywhereExact(Event expectedEvent)
        {
            if (expectedEvent == null || MapCellObjectLists == null)
                return false;

            for (var cellIndex = 0; cellIndex < MapCellObjectLists.Length;
                 cellIndex++)
            {
                var objects = MapCellObjectLists[cellIndex];
                if (objects == null) continue;

                for (var objectIndex = objects.Count - 1; objectIndex >= 0;
                     objectIndex--)
                {
                    var cellObject = objects[objectIndex];
                    if (cellObject?.CellType == CellType.OS_EVENTOBJECT
                        && ReferenceEquals(cellObject.CellObj, expectedEvent))
                        return true;
                }
            }
            return false;
        }

        internal int RemoveEventEverywhereExact(Event expectedEvent)
        {
            if (expectedEvent == null || MapCellObjectLists == null)
                return 0;

            var removed = 0;
            for (var cellIndex = 0; cellIndex < MapCellObjectLists.Length;
                 cellIndex++)
            {
                var objects = MapCellObjectLists[cellIndex];
                if (objects == null) continue;
                var removedFromCell = false;

                for (var objectIndex = objects.Count - 1; objectIndex >= 0;
                     objectIndex--)
                {
                    var cellObject = objects[objectIndex];
                    if (cellObject?.CellType != CellType.OS_EVENTOBJECT
                        || !ReferenceEquals(cellObject.CellObj, expectedEvent))
                        continue;

                    objects.RemoveAt(objectIndex);
                    removed++;
                    removedFromCell = true;
                }

                if (removedFromCell && objects.Count == 0)
                    MapCellObjectLists[cellIndex] = null;
            }
            return removed;
        }

        internal int DeleteFromMap(int nX, int nY, CellType cellType,
            object pRemoveObject, bool notifyDynamicRoomLifecycle)
        {
            return DeleteFromMap(nX, nY, cellType, pRemoveObject,
                notifyDynamicRoomLifecycle, suppressMapDropConsumer: false);
        }

        internal int DeleteFromMap(int nX, int nY, CellType cellType,
            object pRemoveObject, bool notifyDynamicRoomLifecycle,
            bool suppressMapDropConsumer)
        {
            CellObject OSObject;
            int n18;
            const string sExceptionMsg1 = "[Exception] TEnvirnoment::DeleteFromMap -> Except 1 ** %d";
            const string sExceptionMsg2 = "[Exception] TEnvirnoment::DeleteFromMap -> Except 2 ** %d";
            var result = -1;
            var mapCell = false;
            try
            {
                MapCellinfo MapCellInfo = GetMapCellInfo(nX, nY, ref mapCell);
                if (mapCell)
                {
                    if (mapCell)
                    {
                        try
                        {
                            if (MapCellInfo.ObjList != null)
                            {
                                n18 = 0;
                                while (true)
                                {
                                    if (MapCellInfo.Count <= n18)
                                    {
                                        break;
                                    }
                                    OSObject = MapCellInfo.ObjList[n18];
                                    if (OSObject != null)
                                    {
                                        if (OSObject.CellType == cellType && OSObject.CellObj == pRemoveObject)
                                        {
                                            MapCellInfo.Remove(n18);
                                            OSObject = null;
                                            result = 1;
                                            
                                            if (cellType == CellType.OS_MOVINGOBJECT)
                                            {
                                                var movingObject = (TBaseObject)pRemoveObject;
                                                RemoveMovingObjectRegistration(movingObject,
                                                    notifyDynamicRoomLifecycle);
                                                if (!suppressMapDropConsumer &&
                                                    movingObject is TPlayObject player)
                                                {
                                                    player.ReleaseNativeMapDropItems(this,
                                                        removeTracker: true);
                                                }
                                            }
                                            if (MapCellInfo.Count > 0)
                                            {
                                                continue;
                                            }
                                            ReleaseCellObjectList(nX, nY);
                                            break;
                                        }
                                    }
                                    else
                                    {
                                        MapCellInfo.Remove(n18);
                                        if (MapCellInfo.Count > 0)
                                        {
                                            continue;
                                        }
                                        ReleaseCellObjectList(nX, nY);
                                        break;
                                    }
                                    n18++;
                                }
                            }
                            else
                            {
                                result = -2;
                            }
                        }
                        catch (Exception ex)
                        {
                            OSObject = null;
                            M2Share.MainOutMessage(string.Format(sExceptionMsg1, cellType) + " " + ex.Message);
                        }
                    }
                    else
                    {
                        result = -3;
                    }
                }
                else
                {
                    result = 0;
                }
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage(string.Format(sExceptionMsg2, cellType) + " " + ex.Message);
            }
            return result;
        }

        public MapItem GetItem(int nX, int nY)
        {
            CellObject OSObject;
            TBaseObject BaseObject;
            MapItem result = null;
            bo2C = false;
            var mapCell = false;
            MapCellinfo MapCellInfo = GetMapCellInfo(nX, nY, ref mapCell);
            if (mapCell && MapCellInfo.Valid)
            {
                bo2C = true;
                if (MapCellInfo.ObjList != null)
                {
                    for (var i = 0; i < MapCellInfo.Count; i++)
                    {
                        OSObject = MapCellInfo.ObjList[i];
                        if (OSObject.CellType == CellType.OS_ITEMOBJECT)
                        {
                            result = (MapItem)OSObject.CellObj;
                            return result;
                        }
                        if (OSObject.CellType == CellType.OS_GATEOBJECT)
                        {
                            bo2C = false;
                        }
                        if (OSObject.CellType == CellType.OS_MOVINGOBJECT)
                        {
                            BaseObject = (TBaseObject)OSObject.CellObj;
                            if (!BaseObject.m_boDeath)
                            {
                                bo2C = false;
                            }
                        }
                    }
                }
            }
            return result;
        }

        internal MapItem GetNativePickupRangeItem(int nX, int nY,
            TPlayObject requester)
        {
            if (requester == null)
            {
                return null;
            }

            var mapCell = false;
            var mapCellInfo = GetMapCellInfo(nX, nY, ref mapCell);
            if (!mapCell || !mapCellInfo.Valid || mapCellInfo.ObjList == null)
            {
                return null;
            }

            for (var i = 0; i < mapCellInfo.Count; i++)
            {
                var cellObject = mapCellInfo.ObjList[i];
                if (cellObject.CellType != CellType.OS_ITEMOBJECT ||
                    cellObject.CellObj is not MapItem mapItem)
                {
                    continue;
                }

                if (mapItem.OfBaseObject == null ||
                    ReferenceEquals(mapItem.OfBaseObject, requester) ||
                    mapItem.OfBaseObject is TBaseObject owner &&
                    ReferenceEquals(owner.GetMaster(), requester))
                {
                    return mapItem;
                }
            }

            return null;
        }

        public MapItem GetItem(int nX, int nY, int nItemId)
        {
            var mapCell = false;
            MapCellinfo mapCellInfo = GetMapCellInfo(nX, nY, ref mapCell);
            if (!mapCell || !mapCellInfo.Valid || mapCellInfo.ObjList == null)
            {
                return null;
            }

            for (var i = 0; i < mapCellInfo.Count; i++)
            {
                CellObject cellObject = mapCellInfo.ObjList[i];
                if (cellObject.CellType == CellType.OS_ITEMOBJECT &&
                    cellObject.CellObj is MapItem mapItem &&
                    mapItem.Id == nItemId)
                {
                    return mapItem;
                }
            }

            return null;
        }

        public bool IsCheapStuff()
        {
            bool result;
            if (m_QuestList.Count > 0)
            {
                result = true;
            }
            else
            {
                result = false;
            }
            return result;
        }

        public object AddToMapItemEvent(int nX, int nY, CellType nType, object __Event)
        {
            object result = null;
            var mapCell = false;
            MapCellinfo MapCellInfo = GetMapCellInfo(nX, nY, ref mapCell);
            if (mapCell && MapCellInfo.Valid)
            {
                if (MapCellInfo.ObjList == null)
                {
                    MapCellInfo.ObjList = EnsureCellObjectList(nX, nY);
                }
                if (nType == CellType.OS_EVENTOBJECT)
                {
                    var OSObject = new CellObject();
                    OSObject.CellType = nType;
                    OSObject.CellObj = __Event;
                    OSObject.dwAddTime = HUtil32.GetTickCount();
                    MapCellInfo.ObjList.Insert(0, OSObject);
                    result = OSObject;
                }
            }
            return result;
        }

        
        
        
        
        
        
        
        
        public object AddToMapMineEvent(int nX, int nY, CellType nType, StoneMineEvent stoneMineEvent)
        {
            const string sExceptionMsg = "[Exception] TEnvirnoment::AddToMapMineEvent ";
            var mapCell = false;
            try
            {
                // Native sub_777D8C gates on three things and nothing else:
                //   0x777DA2  85 F6           test esi, esi          ; event object
                //   0x777DA4  74 4A           je   0x777DF0          ;   null -> FALSE
                //   0x777DB2  E8 F1 F8 FF FF  call 0x7776A8          ; GetMapCellInfo
                //   0x777DB7  84 C0 / 74 35   test al,al / je        ;   miss -> FALSE
                //   0x777DBE  80 38 00        cmp  byte [eax], 0     ; cell attribute
                //   0x777DC1  74 2D           je   0x777DF0          ;   zero -> FALSE
                // then allocates and links the node unconditionally.
                //
                // The 3x3 walkability scan that used to sit here has no native
                // counterpart, and its failure path returned null, leaving the ore
                // unplaced. The caller treats an absent ore node as "no ore here" and
                // builds a fresh StoneMineEvent with a fresh Random(200) count on the
                // next swing, so a cell that fails the scan yields ore forever.
                if (stoneMineEvent == null) return null;
                MapCellinfo MapCellInfo = GetMapCellInfo(nX, nY, ref mapCell);
                if (mapCell && MapCellInfo.Attribute != 0)
                {
                    if (MapCellInfo.ObjList == null)
                    {
                        MapCellInfo.ObjList = EnsureCellObjectList(nX, nY);
                    }
                    var OSObject = new CellObject
                    {
                        CellType = nType,
                        CellObj = stoneMineEvent,
                        dwAddTime = HUtil32.GetTickCount()
                    };
                    MapCellInfo.ObjList.Insert(0, OSObject);
                    return stoneMineEvent;
                }
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage(sExceptionMsg + " " + ex.Message);
            }
            return null;
        }

        
        
        
        
        
        
        public void VerifyMapTime(int nX, int nY, object BaseObject)
        {
            CellObject OSObject;
            bool boVerify;
            var mapCell = false;
            const string sExceptionMsg = "[Exception] TEnvirnoment::VerifyMapTime";
            try
            {
                boVerify = false;
                MapCellinfo MapCellInfo = GetMapCellInfo(nX, nY, ref mapCell);
                if (mapCell && MapCellInfo.ObjList != null)
                {
                    for (var i = 0; i < MapCellInfo.Count; i++)
                    {
                        OSObject = MapCellInfo.ObjList[i];
                        if (OSObject.CellType == CellType.OS_MOVINGOBJECT && OSObject.CellObj == BaseObject)
                        {
                            OSObject.dwAddTime = HUtil32.GetTickCount();
                            boVerify = true;
                            break;
                        }
                    }
                }
                if (!boVerify)
                {
                    AddToMap(nX, nY, CellType.OS_MOVINGOBJECT, BaseObject);
                }
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage(sExceptionMsg + " " + ex.Message);
            }
        }

        public bool LoadMapData(string sMapFile)
        {
            var result = false;
            int n24;
            byte[] buffer;
            int Point;
            TDoorInfo Door;
            var muiSize = 12;//固定大小
            try
            {
                if (File.Exists(sMapFile))
                {
                    using var fileStream = new FileStream(sMapFile, FileMode.Open, FileAccess.Read);
                    using var binReader = new BinaryReader(fileStream);

                    var bytData = new byte[52];
                    binReader.Read(bytData, 0, bytData.Length);
                    wWidth = BitConverter.ToInt16(bytData, 0);
                    wHeight = BitConverter.ToInt16(bytData, 2);
                    if (wWidth <= 0 || wHeight <= 0 || wWidth > 0x400 || wHeight > 0x400)
                    {
                        // sub_777088 @0x77712A: width/height > 0x400 → format error
                        var mapVersion = bytData.Length > 4 ? bytData[4] : (byte)0;
                        M2Share.MainOutMessage("[ERROR] Not support map format. Version" + mapVersion + " FileName:" + sMapFile);
                        return false;
                    }

                    Initialize(wWidth, wHeight);

                    var nMapSize = wWidth * muiSize * wHeight;
                    buffer = new byte[nMapSize];
                    binReader.Read(buffer, 0, nMapSize);
                    var buffIndex = 0;

                    for (var nW = 0; nW < wWidth; nW++)
                    {
                        n24 = nW * wHeight;
                        for (var nH = 0; nH < wHeight; nH++)
                        {
                            
                            if ((buffer[buffIndex + 1] & 0x80) != 0)
                            {
                                MapCellAttributes[n24 + nH] = CellAttribute.HighWall;
                            }
                            
                            if ((buffer[buffIndex + 5] & 0x80) != 0)
                            {
                                MapCellAttributes[n24 + nH] = CellAttribute.LowWall;
                            }
                            
                            if ((buffer[buffIndex + 6] & 0x80) != 0)
                            {
                                Point = buffer[buffIndex + 6] & 0x7F;
                                if (Point > 0)
                                {
                                    Door = new TDoorInfo
                                    {
                                        nX = nW,
                                        nY = nH,
                                        n08 = Point,
                                        Status = null
                                    };
                                    for (var i = 0; i < m_DoorList.Count; i++)
                                    {
                                        if (Math.Abs(m_DoorList[i].nX - Door.nX) <= 10)
                                        {
                                            if (Math.Abs(m_DoorList[i].nY - Door.nY) <= 10)
                                            {
                                                if (m_DoorList[i].n08 == Point)
                                                {
                                                    Door.Status = m_DoorList[i].Status;
                                                    Door.Status.nRefCount++;
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                    if (Door.Status == null)
                                    {
                                        Door.Status = new TDoorStatus
                                        {
                                            boOpened = false,
                                            bo01 = false,
                                            n04 = 0,
                                            dwOpenTick = 0,
                                            nRefCount = 1
                                        };
                                    }
                                    m_DoorList.Add(Door);
                                }
                            }
                            buffIndex += muiSize;
                        }
                    }
                    binReader.Close();
                    binReader.Dispose();
                    fileStream.Close();
                    fileStream.Dispose();
                    result = true;
                }

                var pointFileName = Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sEnvirDir, "Point", $"{sMapFile}.txt");
                if (File.Exists(pointFileName))
                {
                    var loadList = new StringList();
                    loadList.LoadFromFile(pointFileName);
                    string sX = string.Empty;
                    string sY = string.Empty;
                    var pointLinesRead = 0;
                    for (int i = 0; i < loadList.Count; i++)
                    {
                        var line = loadList[i];
                        if (string.IsNullOrEmpty(line) || line.StartsWith(";"))
                        {
                            continue;
                        }
                        pointLinesRead++;
                        line = HUtil32.GetValidStr3(line, ref sX, new[] { ",", "\t" });
                        line = HUtil32.GetValidStr3(line, ref sY, new[] { ",", "\t" });
                        var nX = (short)HUtil32.Str_ToInt(sX, -1);
                        var nY = (short)HUtil32.Str_ToInt(sY, -1);
                        if (nX >= 0 && nY >= 0 && nX < wWidth && nY < wHeight)
                        {
                            m_PointList.Add(new PointInfo(nX, nY));
                        }
                    }
                    if (pointLinesRead > 0 && m_PointList.Count == 0)
                    {
                        // sub_777088 point loader @0x77754E MainOutMessage("[Warning]: 地图 "+name+" 的随机点")
                        var mapLabel = string.IsNullOrEmpty(sMapName) ? sMapFile : sMapName;
                        M2Share.MainOutMessage("[Warning]: 地图 " + mapLabel + " 的随机点");
                    }
                }
            }
            catch (Exception)
            {
                M2Share.MainOutMessage("[Exception] TEnvirnoment.LoadMapData");
            }
            return result;
        }

        private void Initialize(short nWidth, short nHeight)
        {
            if (nWidth > 1 && nHeight > 1)
            {
                if (MapCellObjectLists != null)
                {
                    for (var i = 0; i < MapCellObjectLists.Length; i++)
                    {
                        if (MapCellObjectLists[i] != null)
                        {
                            MapCellObjectLists[i].Clear();
                            MapCellObjectLists[i] = null;
                        }
                    }
                }
                wWidth = nWidth;
                wHeight = nHeight;
                MapCellAttributes = new CellAttribute[nWidth * nHeight];
                MapCellSkillFlags = new byte[nWidth * nHeight];
                MapCellObjectLists = new IList<CellObject>[nWidth * nHeight];
            }
        }

        public bool CreateQuest(int nFlag, int nValue, string sMonName, string sItem, string sQuest, bool boGrouped)
        {
            TMapQuestInfo MapQuest;
            Merchant MapMerchant;
            var result = false;
            if (nFlag < 0)
            {
                return result;
            }
            MapQuest = new TMapQuestInfo
            {
                nFlag = nFlag
            };
            if (nValue > 1)
            {
                nValue = 1;
            }
            MapQuest.nValue = nValue;
            if (sMonName == "*")
            {
                sMonName = "";
            }
            MapQuest.sMonName = sMonName;
            if (sItem == "*")
            {
                sItem = "";
            }
            MapQuest.sItemName = sItem;
            if (sQuest == "*")
            {
                sQuest = "";
            }
            var scriptPath = "MapQuest_def";
            MapQuest.boGrouped = boGrouped;
            MapMerchant = new Merchant
            {
                m_sMapName = "0",
                m_nCurrX = 0,
                m_nCurrY = 0,
                m_sCharName = sQuest,
                m_nFlag = 0,
                m_wAppr = 0,
                m_sFilePath = scriptPath,
                m_boIsHide = true,
                m_boIsQuest = false
            };
            M2Share.UserEngine.TryAddQuestNpcExact(MapMerchant);
            MapQuest.NPC = MapMerchant;
            m_QuestList.Add(MapQuest);
            result = true;
            return result;
        }

        public int GetXYObjCount(int nX, int nY)
        {
            var result = 0;
            CellObject OSObject;
            TBaseObject BaseObject;
            var mapCell = false;
            MapCellinfo MapCellInfo = GetMapCellInfo(nX, nY, ref mapCell);
            if (mapCell && MapCellInfo.ObjList != null)
            {
                for (var i = 0; i < MapCellInfo.Count; i++)
                {
                    OSObject = MapCellInfo.ObjList[i];
                    if (OSObject.CellType == CellType.OS_MOVINGOBJECT)
                    {
                        BaseObject = (TBaseObject)OSObject.CellObj;
                        if (BaseObject != null)
                        {
                            if (BaseObject.IsNativeCellBlocking())
                            {
                                result++;
                            }
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// sub_778858, named by its own exception text at 0x778A7C:
        /// "[Exception]: TEnvironment.GetMovObjCount". Distinct from
        /// <see cref="GetXYObjCount"/>: it walks the raw cell chain, drops
        /// entries whose actor fails the liveness probe sub_765D64 (logging
        /// each one), and counts the survivors that pass
        ///   0x7789AA  80 7E 73 00  !m_boGhost
        ///   0x7789B0  80 BE E6 02 00 00 00 / 74   bo2B9 must be set
        ///   0x7789BB  sub_772DA8 / 75              !m_boDeath
        ///   0x7789C4  80 BE E3 02 00 00 00 / 75    !m_boFixedHideMode
        ///   0x7789CD  80 BE E0 02 00 00 00 / 75    !m_boAdminMode
        /// It deliberately does NOT carry the ObMode / state-0x3C exclusion
        /// that IsNativeCellBlocking applies, which is why GetXYObjCount is
        /// not reused here.
        ///
        /// <c>+0x2E0</c> is <c>m_boAdminMode</c>, not the per-class marker an
        /// earlier pass took it for. It has a SECOND writer: 0x62430B
        /// `80 B0 E0 02 00 00 01 xor byte [eax+0x2E0],1`, whose two reply
        /// strings at 0x62BB54 and 0x62BB68 read "GM 模式：开" and
        /// "GM 模式：关", so the byte is the GM-mode toggle and 0x60905D is
        /// just a class that starts in it (it raises +0x2E1 on the line
        /// before). The reject at 0x7674BA in sub_767498 lines up with the
        /// pair C# already has at the end of IsAttackTarget,
        /// `m_boAdminMode || m_boStoneMode` (+0x2E5), and it is distinct from
        /// m_boObMode, which is +0x2E2 (sub_772EB8 @0x772EBE).
        /// </summary>
        public int GetNativeMovObjCount(int nX, int nY)
        {
            var result = 0;
            var mapCell = false;
            MapCellinfo MapCellInfo = GetMapCellInfo(nX, nY, ref mapCell);
            if (mapCell && MapCellInfo.ObjList != null)
            {
                var i = 0;
                while (i < MapCellInfo.Count)
                {
                    CellObject OSObject = MapCellInfo.ObjList[i];
                    if (OSObject.CellType != CellType.OS_MOVINGOBJECT)
                    {
                        i++;
                        continue;
                    }
                    // 族 A 摘链臂。sub_778858 与 CanWalk / DoPlayerSearchViewRange 逐字节同形：
                    //   007788E0  8B 45 EC / 80 38 01     cmp byte [node],1
                    //   007788E3  0F 85 F0 00 00 00       jne 0x7789D9      ; 非 actor -> 下一节点
                    //   007788E9  8B 45 EC / 8B 70 04     esi := node^.POject
                    //   007788EF  85 F6 / 0F 84 E2 00..   POject = nil -> 只跳过，不摘链
                    //   007788F9  E8 66 D4 FE FF          call 0x765D64     ; 有效性谓词
                    //   007788FE  84 C0
                    //   00778900  0F 85 A0 00 00 00       jne 0x7789A6      ; 有效 -> 计数条件
                    //   00778906-00778915  摘链：prev^.Next := next / cell^.head := next
                    //   00778920  B3 01                   bl := 1（抑制 prev 前进）
                    //   ...       记 [Exception] 日志后跳尾部 —— continue，不计数
                    if (TBaseObject.IsNativeStaleCellActor(OSObject.CellObj))
                    {
                        MapCellInfo.Remove(i);
                        if (MapCellInfo.Count > 0)
                        {
                            continue;
                        }
                        ReleaseCellObjectList(nX, nY);
                        break;
                    }
                    if (!(OSObject.CellObj is TBaseObject BaseObject))
                    {
                        i++;
                        continue;
                    }
                    if (!BaseObject.m_boGhost && BaseObject.bo2B9 &&
                        !BaseObject.m_boDeath &&
                        !BaseObject.m_boFixedHideMode &&
                        !BaseObject.m_boAdminMode)
                    {
                        result++;
                    }
                    i++;
                }
            }
            return result;
        }

        /// <summary>
        /// Native <c>sub_77BD34</c>: returns true only when <paramref name="actor"/>
        /// is the oldest eligible player in the cell's head-linked chain.
        /// </summary>
        internal bool NativeIsOldestEligiblePlayerInCell(
            TBaseObject actor, int nX, int nY)
        {
            return NativeIsOldestEligiblePlayerInCellCore(
                actor, nX, nY, null);
        }

        internal bool NativeIsOldestEligiblePlayerInCellAtTick(
            TBaseObject actor, int nX, int nY, int currentTick)
        {
            return NativeIsOldestEligiblePlayerInCellCore(
                actor, nX, nY, currentTick);
        }

        private bool NativeIsOldestEligiblePlayerInCellCore(
            TBaseObject actor, int nX, int nY, int? fixedTick)
        {
            if (actor == null)
            {
                return false;
            }

            // 0x77BD52..0x77BD68: a dead subject remains eligible through
            // exactly 10000 ms; the `ja` rejection is unsigned.
            if (actor.m_boDeath && unchecked((uint)(
                    (fixedTick ?? HUtil32.GetTickCount()) -
                    actor.m_dwDeathTick)) > 10000u)
            {
                return false;
            }

            // 0x77BD6E..0x77BD96 deliberately excludes the last column and
            // row, unlike the general GetMapCellInfo bounds.
            if (nX < 0 || nY < 0 || nX >= wWidth - 1 ||
                nY >= wHeight - 1 ||
                !TryGetMapCellIndex(nX, nY, out var cellIndex))
            {
                return false;
            }

            var objects = MapCellObjectLists?[cellIndex];
            if (objects == null)
            {
                return false;
            }

            var foundActor = false;
            for (var i = 0; i < objects.Count; i++)
            {
                var cellObject = objects[i];
                // 0x77BDBC compares the payload before testing the node tag.
                if (ReferenceEquals(cellObject?.CellObj, actor))
                {
                    foundActor = true;
                    continue;
                }

                if (!foundActor ||
                    cellObject?.CellType != CellType.OS_MOVINGOBJECT ||
                    cellObject.CellObj is not TBaseObject candidate ||
                    candidate.m_btRaceServer != Grobal2.RC_PLAYOBJECT)
                {
                    continue;
                }

                if (!candidate.m_boDeath)
                {
                    return false;
                }

                // 0x77BDEC..0x77BDFC uses `jae`: an older dead candidate is
                // ignored starting at exactly 10000 ms.
                if (unchecked((uint)(
                        (fixedTick ?? HUtil32.GetTickCount()) -
                        candidate.m_dwDeathTick)) < 10000u)
                {
                    return false;
                }
            }

            return foundActor;
        }

        public bool GetNextPosition(short sx, short sy, int ndir, int nFlag, ref short snx, ref short sny)
        {
            bool result;
            snx = sx;
            sny = sy;
            switch (ndir)
            {
                case Grobal2.DR_UP:
                    if (sny > nFlag - 1)
                    {
                        sny -= (short)nFlag;
                    }
                    break;
                case Grobal2.DR_DOWN:
                    if (sny < wHeight - nFlag)
                    {
                        sny += (short)nFlag;
                    }
                    break;
                case Grobal2.DR_LEFT:
                    if (snx > nFlag - 1)
                    {
                        snx -= (short)nFlag;
                    }
                    break;
                case Grobal2.DR_RIGHT:
                    if (snx < wWidth - nFlag)
                    {
                        snx += (short)nFlag;
                    }
                    break;
                case Grobal2.DR_UPLEFT:
                    if (snx > nFlag - 1 && sny > nFlag - 1)
                    {
                        snx -= (short)nFlag;
                        sny -= (short)nFlag;
                    }
                    break;
                case Grobal2.DR_UPRIGHT:
                    if (snx > nFlag - 1 && sny < wHeight - nFlag)
                    {
                        snx += (short)nFlag;
                        sny -= (short)nFlag;
                    }
                    break;
                case Grobal2.DR_DOWNLEFT:
                    if (snx < wWidth - nFlag && sny > nFlag - 1)
                    {
                        snx -= (short)nFlag;
                        sny += (short)nFlag;
                    }
                    break;
                case Grobal2.DR_DOWNRIGHT:
                    if (snx < wWidth - nFlag && sny < wHeight - nFlag)
                    {
                        snx += (short)nFlag;
                        sny += (short)nFlag;
                    }
                    break;
            }
            if (snx == sx && sny == sy)
            {
                result = false;
            }
            else
            {
                result = true;
            }
            return result;
        }

        public bool CanSafeWalk(int nX, int nY)
        {
            var result = true;
            CellObject OSObject;
            var mapCell = false;
            MapCellinfo MapCellInfo = GetMapCellInfo(nX, nY, ref mapCell);
            if (mapCell && MapCellInfo.ObjList != null)
            {
                for (var i = MapCellInfo.Count - 1; i >= 0; i--)
                {
                    OSObject = MapCellInfo.ObjList[i];
                    if (OSObject.CellType == CellType.OS_EVENTOBJECT)
                    {
                        if (((Event)OSObject.CellObj).m_nDamage > 0)
                        {
                            result = false;
                        }
                    }
                }
            }
            return result;
        }

        public bool ArroundDoorOpened(int nX, int nY)
        {
            var result = true;
            TDoorInfo Door;
            for (var i = 0; i < m_DoorList.Count; i++)
            {
                Door = m_DoorList[i];
                if (Math.Abs(Door.nX - nX) <= 1 && Math.Abs(Door.nY - nY) <= 1)
                {
                    if (!Door.Status.boOpened)
                    {
                        result = false;
                        break;
                    }
                }
            }
            return result;
        }

        public object GetMovingObject(short nX, short nY, bool boFlag)
        {
            object result = null;
            CellObject OSObject;
            TBaseObject BaseObject;
            var mapCell = false;
            MapCellinfo MapCellInfo = GetMapCellInfo(nX, nY, ref mapCell);
            if (mapCell && MapCellInfo.ObjList != null)
            {
                for (var i = 0; i < MapCellInfo.Count; i++)
                {
                    OSObject = MapCellInfo.ObjList[i];
                    if (OSObject.CellType == CellType.OS_MOVINGOBJECT)
                    {
                        BaseObject = (TBaseObject)OSObject.CellObj;
                        if (BaseObject != null && !BaseObject.m_boGhost && BaseObject.bo2B9 && (!boFlag || !BaseObject.m_boDeath))
                        {
                            result = BaseObject;
                            break;
                        }
                    }
                }
            }
            return result;
        }

        public object GetQuestNPC(TBaseObject BaseObject, string sCharName, string sItem, bool boFlag)
        {
            object result = null;
            TMapQuestInfo MapQuestFlag;
            int nFlagValue;
            bool bo1D;
            for (var i = 0; i < m_QuestList.Count; i++)
            {
                MapQuestFlag = m_QuestList[i];
                nFlagValue = BaseObject.GetQuestFalgStatus(MapQuestFlag.nFlag);
                if (nFlagValue == MapQuestFlag.nValue)
                {
                    if (boFlag == MapQuestFlag.boGrouped || !boFlag)
                    {
                        bo1D = false;
                        if (!string.IsNullOrEmpty(MapQuestFlag.sMonName) && !string.IsNullOrEmpty(MapQuestFlag.sItemName))
                        {
                            if (MapQuestFlag.sMonName == sCharName && MapQuestFlag.sItemName == sItem)
                            {
                                bo1D = true;
                            }
                        }
                        if (!string.IsNullOrEmpty(MapQuestFlag.sMonName) && string.IsNullOrEmpty(MapQuestFlag.sItemName))
                        {
                            if (MapQuestFlag.sMonName == sCharName && string.IsNullOrEmpty(sItem))
                            {
                                bo1D = true;
                            }
                        }
                        if (string.IsNullOrEmpty(MapQuestFlag.sMonName) && !string.IsNullOrEmpty(MapQuestFlag.sItemName))
                        {
                            if (MapQuestFlag.sItemName == sItem)
                            {
                                bo1D = true;
                            }
                        }
                        if (bo1D)
                        {
                            result = MapQuestFlag.NPC;
                            break;
                        }
                    }
                }
            }
            return result;
        }

        public object GetItemEx(int nX, int nY, ref int nCount)
        {
            object result = null;
            CellObject OSObject;
            TBaseObject BaseObject;
            nCount = 0;
            bo2C = false;
            var mapCell = false;
            MapCellinfo MapCellInfo = GetMapCellInfo(nX, nY, ref mapCell);
            if (mapCell && MapCellInfo.Valid)
            {
                bo2C = true;
                if (MapCellInfo.ObjList != null)
                {
                    for (var i = 0; i < MapCellInfo.Count; i++)
                    {
                        OSObject = MapCellInfo.ObjList[i];
                        if (OSObject.CellType == CellType.OS_ITEMOBJECT)
                        {
                            result = OSObject.CellObj;
                            nCount++;
                        }
                        if (OSObject.CellType == CellType.OS_GATEOBJECT)
                        {
                            bo2C = false;
                        }
                        if (OSObject.CellType == CellType.OS_MOVINGOBJECT)
                        {
                            BaseObject = (TBaseObject)OSObject.CellObj;
                            if (!BaseObject.m_boDeath)
                            {
                                bo2C = false;
                            }
                        }
                    }
                }
            }
            return result;
        }

        public TDoorInfo GetDoor(int nX, int nY)
        {
            TDoorInfo Door;
            TDoorInfo result = null;
            for (var i = 0; i < m_DoorList.Count; i++)
            {
                Door = m_DoorList[i];
                if (Door.nX == nX && Door.nY == nY)
                {
                    result = Door;
                    return result;
                }
            }
            return result;
        }

        public bool IsValidObject(int nX, int nY, int nRage, object BaseObject)
        {
            MapCellinfo MapCellInfo;
            CellObject OSObject;
            var result = false;
            for (var nXX = nX - nRage; nXX <= nX + nRage; nXX++)
            {
                for (var nYY = nY - nRage; nYY <= nY + nRage; nYY++)
                {
                    var mapCell = false;
                    MapCellInfo = GetMapCellInfo(nX, nY, ref mapCell);
                    if (mapCell && MapCellInfo.ObjList != null)
                    {
                        for (var i = 0; i < MapCellInfo.Count; i++)
                        {
                            OSObject = MapCellInfo.ObjList[i];
                            if (OSObject.CellObj == BaseObject)
                            {
                                result = true;
                                return result;
                            }
                        }
                    }
                }
            }
            return result;
        }

        public int GetRangeBaseObject(int nX, int nY, int nRage, bool boFlag, IList<TBaseObject> BaseObjectList)
        {
            for (var nXX = nX - nRage; nXX <= nX + nRage; nXX++)
            {
                for (var nYY = nY - nRage; nYY <= nY + nRage; nYY++)
                {
                    GetBaseObjects(nXX, nYY, boFlag, BaseObjectList);
                }
            }
            return BaseObjectList.Count;
        }

        public bool GetMapBaseObjects(short nX, short nY, int nRage, IList<TBaseObject> BaseObjectList, CellType btType = CellType.OS_MOVINGOBJECT)
        {
            BaseObjectList.Clear();
            var nStartX = Math.Max(0, nX - nRage);
            var nEndX = Math.Min(wWidth - 1, nX + nRage);
            var nStartY = Math.Max(0, nY - nRage);
            var nEndY = Math.Min(wHeight - 1, nY + nRage);
            for (var x = nStartX; x <= nEndX; x++)
            {
                for (var y = nStartY; y <= nEndY; y++)
                {
                    var mapCell = false;
                    MapCellinfo MapCellInfo = GetMapCellInfo(x, y, ref mapCell);
                    if (mapCell && MapCellInfo.ObjList != null)
                    {
                        for (var j = 0; j < MapCellInfo.Count; j++)
                        {
                            CellObject OSObject = MapCellInfo.ObjList[j];
                            if (OSObject != null && OSObject.CellType == btType && OSObject.CellObj is TBaseObject)
                            {
                                TBaseObject BaseObject = (TBaseObject)OSObject.CellObj;
                                BaseObjectList.Add(BaseObject);
                            }
                        }
                    }
                }
            }
            return BaseObjectList.Count > 0;
        }

        
        
        
        
        
        
        
        
        public int GetBaseObjects(int nX, int nY, bool boFlag, IList<TBaseObject> BaseObjectList)
        {
            int result;
            CellObject OSObject;
            TBaseObject BaseObject;
            var mapCell = false;
            MapCellinfo MapCellInfo = GetMapCellInfo(nX, nY, ref mapCell);
            if (mapCell && MapCellInfo.ObjList != null)
            {
                for (var i = 0; i < MapCellInfo.Count; i++)
                {
                    OSObject = MapCellInfo.ObjList[i];
                    if (OSObject.CellType == CellType.OS_MOVINGOBJECT)
                    {
                        BaseObject = (TBaseObject)OSObject.CellObj;
                        if (BaseObject != null)
                        {
                            if (!BaseObject.m_boGhost && BaseObject.bo2B9)
                            {
                                if (!boFlag || !BaseObject.m_boDeath)
                                {
                                    BaseObjectList.Add(BaseObject);
                                }
                            }
                        }
                    }
                }
            }
            result = BaseObjectList.Count;
            return result;
        }

        public object GetEvent(int nX, int nY)
        {
            CellObject OSObject;
            object result = null;
            bo2C = false;
            var mapCell = false;
            MapCellinfo MapCellInfo = GetMapCellInfo(nX, nY, ref mapCell);
            if (mapCell && MapCellInfo.ObjList != null)
            {
                for (var i = 0; i < MapCellInfo.Count; i++)
                {
                    OSObject = MapCellInfo.ObjList[i];
                    if (OSObject.CellType == CellType.OS_EVENTOBJECT)
                    {
                        result = OSObject.CellObj;
                    }
                }
            }
            return result;
        }

        public Event GetEvent(int nX, int nY, int eventType)
        {
            var mapCell = false;
            MapCellinfo mapCellInfo = GetMapCellInfo(nX, nY, ref mapCell);
            if (!mapCell || mapCellInfo.ObjList == null)
            {
                return null;
            }

            for (var i = 0; i < mapCellInfo.Count; i++)
            {
                CellObject cellObject = mapCellInfo.ObjList[i];
                if (cellObject.CellType == CellType.OS_EVENTOBJECT &&
                    cellObject.CellObj is Event mapEvent &&
                    mapEvent.m_nEventType == eventType)
                {
                    return mapEvent;
                }
            }
            return null;
        }

        public void SetMapXYFlag(int nX, int nY, bool boFlag)
        {
            var mapcell = false;
            MapCellinfo MapCellInfo = GetMapCellInfo(nX, nY, ref mapcell);
            if (mapcell)
            {
                var index = nX * wHeight + nY;
                MapCellAttributes[index] = boFlag ? CellAttribute.Walk : CellAttribute.LowWall;
            }
        }

        public bool CanFly(int nsX, int nsY, int ndX, int ndY)
        {
            int n18;
            int n1C;
            var result = true;
            var r28 = (ndX - nsX) / 1.0e1;
            var r30 = (ndY - nsY) / 1.0e1;
            var n14 = 0;
            while (true)
            {
                n18 = HUtil32.Round(nsX + r28);
                n1C = HUtil32.Round(nsY + r30);
                if (!CanWalk(n18, n1C, true))
                {
                    result = false;
                    break;
                }
                n14++;
                if (n14 >= 10)
                {
                    break;
                }
            }
            return result;
        }

        public bool GetXYHuman(int nMapX, int nMapY)
        {
            var mapCell = false;
            CellObject OSObject;
            TBaseObject BaseObject;
            var result = false;
            MapCellinfo MapCellInfo = GetMapCellInfo(nMapX, nMapY, ref mapCell);
            if (mapCell && MapCellInfo.ObjList != null)
            {
                for (var i = 0; i < MapCellInfo.Count; i++)
                {
                    OSObject = MapCellInfo.ObjList[i];
                    if (OSObject.CellType == CellType.OS_MOVINGOBJECT)
                    {
                        BaseObject = (TBaseObject)OSObject.CellObj;
                        if (BaseObject.m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                        {
                            result = true;
                            break;
                        }
                    }
                }
            }
            return result;
        }

        public bool IsValidCell(int nX, int nY)
        {
            var mapCell = false;
            MapCellinfo MapCellInfo = GetMapCellInfo(nX, nY, ref mapCell);
            if (mapCell && MapCellInfo.Attribute == CellAttribute.LowWall)
            {
                return false;
            }
            return true;
        }

        public string GetEnvirInfo()
        {
            string sMsg;
            sMsg = "Map:%s(%s) DAY:%s DARK:%s SAFE:%s FIGHT:%s FIGHT3:%s QUIZ:%s NORECONNECT:%s(%s) MUSIC:%s(%d) EXPRATE:%s(%f) PKWINLEVEL:%s(%d) PKLOSTLEVEL:%s(%d) PKWINEXP:%s(%d) PKLOSTEXP:%s(%d) DECHP:%s(%d/%d) INCHP:%s(%d/%d)";
            sMsg = sMsg + " DECGAMEGOLD:%s(%d/%d) INCGAMEGOLD:%s(%d/%d) INCGAMEPOINT:%s(%d/%d) RUNHUMAN:%s RUNMON:%s NEEDHOLE:%s NORECALL:%s NOGUILDRECALL:%s NODEARRECALL:%s NOMASTERRECALL:%s NODRUG:%s MINE:%s NODROPITEM:%s";
            sMsg = sMsg + " NOTHROWITEM:%s NOPOSITIONMOVE:%s NOHORSE:%s NOHUMNOMON:%s NOCHAT:%s ";
            var result = string.Format(sMsg, sMapName, sMapDesc, HUtil32.BoolToStr(Flag.boDayLight), HUtil32.BoolToStr(Flag.boDarkness), HUtil32.BoolToStr(Flag.boSAFE), HUtil32.BoolToStr(Flag.boFightZone),
                HUtil32.BoolToStr(Flag.boFight3Zone), HUtil32.BoolToStr(Flag.boQUIZ), HUtil32.BoolToStr(Flag.boNORECONNECT), Flag.sNoReConnectMap, HUtil32.BoolToStr(Flag.boMUSIC), Flag.nMUSICID, HUtil32.BoolToStr(Flag.boEXPRATE),
                Flag.nEXPRATE / 100, HUtil32.BoolToStr(Flag.boPKWINLEVEL), Flag.nPKWINLEVEL, HUtil32.BoolToStr(Flag.boPKLOSTLEVEL), Flag.nPKLOSTLEVEL, HUtil32.BoolToStr(Flag.boPKWINEXP), Flag.nPKWINEXP, HUtil32.BoolToStr(Flag.boPKLOSTEXP),
                Flag.nPKLOSTEXP, HUtil32.BoolToStr(Flag.boDECHP), Flag.nDECHPTIME, Flag.nDECHPPOINT, HUtil32.BoolToStr(Flag.boINCHP), Flag.nINCHPTIME, Flag.nINCHPPOINT, HUtil32.BoolToStr(Flag.boDECGAMEGOLD), Flag.nDECGAMEGOLDTIME,
                Flag.nDECGAMEGOLD, HUtil32.BoolToStr(Flag.boINCGAMEGOLD), Flag.nINCGAMEGOLDTIME, Flag.nINCGAMEGOLD, HUtil32.BoolToStr(Flag.boINCGAMEPOINT), Flag.nINCGAMEPOINTTIME, Flag.nINCGAMEPOINT, HUtil32.BoolToStr(Flag.boRUNHUMAN),
                HUtil32.BoolToStr(Flag.boRUNMON), HUtil32.BoolToStr(Flag.boNEEDHOLE), HUtil32.BoolToStr(Flag.boNORECALL), HUtil32.BoolToStr(Flag.boNOGUILDRECALL), HUtil32.BoolToStr(Flag.boNODEARRECALL), HUtil32.BoolToStr(Flag.boNOMASTERRECALL),
                HUtil32.BoolToStr(Flag.boNODRUG), HUtil32.BoolToStr(Flag.boMINE), HUtil32.BoolToStr(Flag.boNODROPITEM), HUtil32.BoolToStr(Flag.boNOTHROWITEM), HUtil32.BoolToStr(Flag.boNOPOSITIONMOVE),
                HUtil32.BoolToStr(Flag.boNOHORSE), HUtil32.BoolToStr(Flag.boNOHUMNOMON), HUtil32.BoolToStr(Flag.boNOCHAT));
            return result;
        }

        public void AddObject(object BaseObject)
        {
            var actor = (TBaseObject)BaseObject;
            var btRaceServer = actor.m_btRaceServer;
            if (actor.CountsAsPlayerPresence)
            {
                m_nHumCount++;
            }
            if (btRaceServer >= Grobal2.RC_ANIMAL)
            {
                m_nMonCount++;
            }
        }

        public void DelObjectCount(object BaseObject)
        {
            var actor = (TBaseObject)BaseObject;
            var btRaceServer = actor.m_btRaceServer;
            if (actor.CountsAsPlayerPresence)
            {
                m_nHumCount--;
            }
            if (btRaceServer >= Grobal2.RC_ANIMAL)
            {
                m_nMonCount--;
            }
        }

        internal void NotifyDynamicRoomPlayerRemoved()
        {
            DynamicRoomManagerOwner?.NotifyPlayerRemoved(this);
        }

        private void AddDynamicRoomPlayer(TBaseObject actor)
        {
            if (IsDynamicRoom && actor.CountsAsPlayerPresence)
                _dynamicRoomPlayers.Add(actor);
        }

        internal bool RemoveMovingObjectRegistration(TBaseObject actor,
            bool notifyDynamicRoomLifecycle)
        {
            if (actor == null)
                return false;

            var removedCount = false;
            if (!actor.m_boDelFormMaped)
            {
                actor.m_boDelFormMaped = true;
                actor.m_boAddToMaped = false;
                DelObjectCount(actor);
                removedCount = true;
            }

            var removedDynamicRoomPlayer =
                RemoveDynamicRoomPlayer(actor, notifyDynamicRoomLifecycle);
            if (removedDynamicRoomPlayer)
            {
                // 0x779546 `FF 11 call [map_vmt+0x00]` -> TDynEnvir.DeleteObject 0x5FD574,
                // 其 0x5FD592 `dec [map+0xD8]` 在 0x5FD5A3 派发 @OnLeave 之前。只在真的摘掉
                // 了一份登记时派发：原生的 node 在 DeleteFromMap 里已经从格子摘链，
                // DeleteObject 每次实际摘除只被调一次。
                NativeDynEnvirDeleteObjectTrigger(actor);
            }

            return removedDynamicRoomPlayer || removedCount;
        }

        private bool RemoveDynamicRoomPlayer(TBaseObject actor,
            bool notifyDynamicRoomLifecycle)
        {
            if (!IsDynamicRoom || !actor.CountsAsPlayerPresence)
                return false;

            var removed = _dynamicRoomPlayers.Remove(actor);
            if (removed && notifyDynamicRoomLifecycle)
                NotifyDynamicRoomPlayerRemoved();
            return removed;
        }
    }
}
