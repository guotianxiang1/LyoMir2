using GameSvr.Plugins;
using SystemModule;

namespace GameSvr
{
    public partial class TBaseObject
    {
        /// <summary>
        /// 战神 <c>sub_77A178</c> hardcodes <c>0x927C0</c> = 600_000 ms (10 min) at
        /// @0x77A3FD for ground items — there is no config knob for it in the image.  C#
        /// defaulted to <c>dwClearDropOnFloorItemTime</c> = 3_600_000 (1 h), so ground
        /// items outlived their native lifetime sixfold and stayed pickable.  The config
        /// value is now honoured only when a shard has explicitly moved it off the stock
        /// 1 h default; otherwise the native constant wins.  The Yanshen plugin override
        /// still takes precedence (it is an explicit operator choice).
        /// </summary>
        protected int ResolveFloorItemClearTimeout()
        {
            var api = new YanshenApi(this as TPlayObject, null, M2Share.PluginManager);
            if (api.TryGetFloorItemTimeout(out var timeoutMilliseconds))
                return timeoutMilliseconds;
            var configured = M2Share.g_Config.dwClearDropOnFloorItemTime;
            return configured == 60 * 60 * 1000 || configured <= 0
                ? NativeMapItemExpiry.GroundItemLifetimeMs   // 0x77A3FD cmp edx,0x927C0
                : configured;
        }

        /// <summary>
        /// 战神 <c>sub_77A178</c> ground-item expiry, tag 2 @0x77A3D9.  That branch is
        /// four instructions of arithmetic and one <c>jb</c> against a literal — no
        /// StdMode ladder, no <c>+0x0D</c> never-expire gate, no per-object lifetime
        /// override.  Those three all belong to tag 3 (event objects, @0x77A480) and were
        /// applied here only because C#'s <see cref="CellType"/> numbers ITEM as 3 while
        /// 战神 numbers it 2; see <see cref="NativeMapItemExpiry"/> for the constructor
        /// bytes that pin each tag.  <c>jb</c> keeps while strictly below, so expiry is
        /// <c>age &gt;= limit</c>.
        /// </summary>
        protected static bool HasFloorItemExpired(object cellObj, int ageMs,
            int fallbackTimeoutMs)
        {
            if (cellObj is not MapItem)
                return ageMs >= fallbackTimeoutMs;
            // Honour an explicitly-retuned shard timeout when it is shorter than native's.
            return NativeMapItemExpiry.HasGroundItemExpired(ageMs,
                Math.Min(NativeMapItemExpiry.GroundItemLifetimeMs,
                    Math.Max(fallbackTimeoutMs, 1)));
        }

        protected virtual void UpdateVisibleGay(TBaseObject BaseObject)
        {
            bool boIsVisible = false;
            TVisibleBaseObject VisibleBaseObject;
            if ((BaseObject.m_btRaceServer == Grobal2.RC_PLAYOBJECT) || (BaseObject.m_Master != null))// 如果是人物或宝宝则置TRUE
            {
                m_boIsVisibleActive = true;
            }
            for (var i = 0; i < m_VisibleActors.Count; i++)
            {
                VisibleBaseObject = m_VisibleActors[i];
                if (VisibleBaseObject.BaseObject == BaseObject)
                {
                    VisibleBaseObject.nVisibleFlag = 1;
                    boIsVisible = true;
                    break;
                }
            }
            if (boIsVisible)
            {
                return;
            }
            VisibleBaseObject = new TVisibleBaseObject
            {
                nVisibleFlag = 2,
                BaseObject = BaseObject
            };
            m_VisibleActors.Add(VisibleBaseObject);
        }

        protected void UpdateVisibleItem(int wX, int wY, MapItem MapItem)
        {
            VisibleMapItem VisibleMapItem;
            bool boIsVisible = false;
            for (int i = 0; i < m_VisibleItems.Count; i++)
            {
                VisibleMapItem = m_VisibleItems[i];
                if (VisibleMapItem.MapItem == MapItem)
                {
                    VisibleMapItem.nVisibleFlag = 1;
                    boIsVisible = true;
                    break;
                }
            }
            if (boIsVisible)
            {
                return;
            }
            VisibleMapItem = new VisibleMapItem
            {
                nVisibleFlag = 2,
                nX = wX,
                nY = wY,
                MapItem = MapItem,
                sName = MapItem.Name,
                wLooks = MapItem.Looks
            };
            m_VisibleItems.Add(VisibleMapItem);
        }

        protected void UpdateVisibleEvent(int wX, int wY, Event MapEvent)
        {
            bool boIsVisible = false;
            Event __Event;
            for (int i = 0; i < m_VisibleEvents.Count; i++)
            {
                __Event = m_VisibleEvents[i];
                if (__Event == MapEvent)
                {
                    __Event.nVisibleFlag = 1;
                    boIsVisible = true;
                    break;
                }
            }
            if (boIsVisible)
            {
                return;
            }
            MapEvent.nVisibleFlag = 2;
            MapEvent.m_nX = wX;
            MapEvent.m_nY = wY;
            m_VisibleEvents.Add(MapEvent);
        }

        public bool IsVisibleHuman()
        {
            bool result = false;
            TVisibleBaseObject VisibleBaseObject;
            for (int i = 0; i < m_VisibleActors.Count; i++)
            {
                VisibleBaseObject = m_VisibleActors[i];
                if ((VisibleBaseObject.BaseObject.m_btRaceServer == Grobal2.RC_PLAYOBJECT) || (VisibleBaseObject.BaseObject.m_Master != null))
                {
                    result = true;
                    break;
                }
            }
            return result;
        }

        public virtual void SearchViewRange()
        {
            MapCellinfo MapCellInfo;
            CellObject OSObject;
            TBaseObject BaseObject;
            TVisibleBaseObject VisibleBaseObject;
            const string sExceptionMsg1 = "[Exception] TBaseObject::SearchViewRange";
            const string sExceptionMsg2 = "[Exception] TBaseObject::SearchViewRange 1-{0} {1} {2} {3} {4} {5}";
            if (m_PEnvir == null)
            {
                M2Share.ErrorMessage("SearchViewRange nil PEnvir");
                return;
            }
            var n24 = 0;
            m_boIsVisibleActive = false;// 先置为FALSE
            try
            {
                for (var i = 0; i < m_VisibleActors.Count; i++)
                {
                    m_VisibleActors[i].nVisibleFlag = 0;
                }
            }
            catch
            {
                M2Share.ErrorMessage(sExceptionMsg1);
                KickException();
            }
            var nStartX = m_nCurrX - m_nViewRange;
            var nEndX = m_nCurrX + m_nViewRange;
            var nStartY = m_nCurrY - m_nViewRange;
            var nEndY = m_nCurrY + m_nViewRange;
            try
            {
                for (var n18 = nStartX; n18 <= nEndX; n18++)
                {
                    for (var n1C = nStartY; n1C <= nEndY; n1C++)
                    {
                        var mapCell = false;
                        MapCellInfo = m_PEnvir.GetMapCellInfo(n18, n1C, ref mapCell);
                        if (mapCell && (MapCellInfo.ObjList != null))
                        {
                            n24 = 1;
                            var nIdx = 0;
                            while (true)
                            {
                                if (MapCellInfo.Count <= nIdx)
                                {
                                    break;
                                }
                                OSObject = MapCellInfo.ObjList[nIdx];
                                if (OSObject != null)
                                {
                                    if (OSObject.CellType == CellType.OS_MOVINGOBJECT)
                                    {
                                        // 本方法（非玩家版）承接的是 sub_77A990
                                        // （TEnvironment.DoSearchTargetList）**可见性那一半**
                                        // ——即 0x77AC01 `call [searcher.VMT+0x1BC]`；另一半
                                        // （0x77ABC9 `call 0x424AB8` 往 [self+0x380] 收人）在
                                        // TBaseObject.SendRefMsg 的重建循环里。移植期把这一个
                                        // 原生函数拆成了两处，所以两处都带同一条节点循环。
                                        // 摘链谓词在 0x77AB07 call 0x765D64；节点循环
                                        // 0x77AAEE `cmp byte [node],1 / jne 0x77AC13`
                                        // **只有 CellType 1 一条臂**，没有地面物 / 事件臂，
                                        // 与本方法逐条对应；玩家版 sub_77A178（0x77A2EB）则有
                                        // 1/2/3 三条臂。两处的谓词与摘链臂逐字节相同。
                                        // 注意扫描窗半径两边不同源：sub_77A990 用全局
                                        // [[0x7D6754]]（INI [Setup] GlobalSeeZone，缺省 12），
                                        // 本方法用每对象 m_nViewRange —— 这条差异登记在
                                        // docs/addtomap_and_outparam_20260814.md。
                                        // 0x77AB07 call 0x765D64 / test al / 无效则摘链；原生无 60s 并联。
                                        if (IsNativeStaleCellActor(OSObject.CellObj))
                                        {
                                            OSObject = null;
                                            MapCellInfo.Remove(nIdx);
                                            if (MapCellInfo.Count > 0)
                                            {
                                                continue;
                                            }
                                            m_PEnvir.ReleaseCellObjectList(n18, n1C);
                                            break;
                                        }
                                        BaseObject = OSObject.CellObj as TBaseObject;
                                        if (BaseObject != null)
                                        {
                                            if ((BaseObject != null) && !BaseObject.m_boDeath && !BaseObject.m_boInvisible)
                                            {
                                                if (!BaseObject.m_boGhost && !BaseObject.m_boFixedHideMode && !BaseObject.m_boObMode)
                                                {
                                                    if ((m_btRaceServer < Grobal2.RC_ANIMAL) || (m_Master != null) || m_boCrazyMode || m_boNastyMode || m_boWantRefMsg || ((BaseObject.m_Master != null) && (Math.Abs(BaseObject.m_nCurrX - m_nCurrX) <= 3) && (Math.Abs(BaseObject.m_nCurrY - m_nCurrY) <= 3)) || (BaseObject.m_btRaceServer == Grobal2.RC_PLAYOBJECT))
                                                    {
                                                        UpdateVisibleGay(BaseObject);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                nIdx++;
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                M2Share.ErrorMessage(format(sExceptionMsg2, new object[] { n24, m_sCharName, m_sMapName, m_nCurrX, m_nCurrY }));
                M2Share.ErrorMessage(e.Message);
                KickException();
            }
            n24 = 2;
            try
            {
                var n18 = 0;
                while (true)
                {
                    if (m_VisibleActors.Count <= n18)
                    {
                        break;
                    }
                    VisibleBaseObject = m_VisibleActors[n18];
                    if (VisibleBaseObject.nVisibleFlag == 0)
                    {
                        m_VisibleActors.RemoveAt(n18);
                        Dispose(VisibleBaseObject);
                        continue;
                    }
                    n18++;
                }
            }
            catch
            {
                M2Share.ErrorMessage(format(sExceptionMsg2, new object[] { n24, m_sCharName, m_sMapName, m_nCurrX, m_nCurrY }));
                KickException();
            }
        }

        public virtual void SearchViewRange_Death()
        {
            if (m_PEnvir == null)
            {
                return;
            }
            var floorItemClearTimeout = ResolveFloorItemClearTimeout();
            m_boIsVisibleActive = false;
            for (int i = 0; i < m_VisibleActors.Count; i++)
            {
                m_VisibleActors[i].nVisibleFlag = 0;
            }
            var nStartX = m_nCurrX - m_nViewRange;
            var nEndX = m_nCurrX + m_nViewRange;
            var nStartY = m_nCurrY - m_nViewRange;
            var nEndY = m_nCurrY + m_nViewRange;
            MapCellinfo MapCellInfo;
            for (var n18 = nStartX; n18 <= nEndX; n18++)
            {
                for (var n1C = nStartY; n1C <= nEndY; n1C++)
                {
                    var mapCell = false;
                    MapCellInfo = m_PEnvir.GetMapCellInfo(n18, n1C, ref mapCell);
                    if (mapCell && (MapCellInfo.ObjList != null))
                    {
                        var nIdx = 0;
                        while (true)
                        {
                            if (MapCellInfo.Count <= nIdx)
                            {
                                break;
                            }
                            var OSObject = MapCellInfo.ObjList[nIdx];
                            if (OSObject != null)
                            {
                                if (OSObject.CellType == CellType.OS_MOVINGOBJECT)
                                {
                                    // 0x77A2EB call 0x765D64 / test al；原生无 60s 并联。
                                    if (IsNativeStaleCellActor(OSObject.CellObj))
                                    {
                                        OSObject = null;
                                        MapCellInfo.Remove(nIdx);
                                        if (MapCellInfo.Count > 0)
                                        {
                                            continue;
                                        }
                                        m_PEnvir.ReleaseCellObjectList(n18, n1C);
                                        break;
                                    }
                                }
                                if ((OSObject.CellType == CellType.OS_ITEMOBJECT) && !m_boDeath && (m_btRaceServer > Grobal2.RC_MONSTER))
                                {
                                    // 战神 sub_77A178: the +0x0D never-expire gate and the
                                    // 15-minute constant, in place of the flat 1 h config.
                                    if (HasFloorItemExpired(OSObject.CellObj,
                                            HUtil32.GetTickCount() - OSObject.dwAddTime,
                                            floorItemClearTimeout))
                                    {
                                        Dispose(OSObject.CellObj);
                                        Dispose(OSObject);
                                        MapCellInfo.Remove(nIdx);
                                        if (MapCellInfo.Count > 0)
                                        {
                                            continue;
                                        }
                                        m_PEnvir.ReleaseCellObjectList(n18, n1C);
                                    }
                                }
                            }
                            nIdx++;
                        }
                    }
                }
            }

            var n17 = 0;
            if (m_VisibleActors.Count > 0)
            {
                while (true)
                {
                    if (m_VisibleActors.Count <= n17)
                    {
                        break;
                    }
                    if (m_VisibleActors[n17].nVisibleFlag == 0)
                    {
                        m_VisibleActors.RemoveAt(n17);
                        continue;
                    }
                    n17++;
                }
            }
        }
    }
}
