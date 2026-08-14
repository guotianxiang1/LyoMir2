using SystemModule;

namespace GameSvr.Features.Siege
{
    /// <summary>
    /// 沙巴克城门状态管理 - MVI实现
    /// Sabak Gate Status Management - Model-View-Intent Implementation
    ///
    /// 基于战神引擎逆向工程：
    /// - TDoorStatus: 城门状态结构（引用计数、开关状态、时间戳）
    /// - TDoorInfo: 城门信息（位置、状态引用）
    /// - CastleDoor: 城门实体对象
    /// - Envirnoment.cs:1240-1247: 城门状态初始化逻辑
    ///
    /// MVI架构：
    /// - Model: DoorStatusSnapshot（不可变状态快照）
    /// - View: 状态查询接口（GetDoorStatus, QueryDoorState等）
    /// - Intent: 状态变更操作（OpenDoor, CloseDoor, UpdateDoorTick等）
    /// </summary>
    public static class SabakGateStatus
    {
        #region Constants

        // 城门状态常量
        private const int DefaultDoorOpenTick = 5000;     // 默认开门持续时间（毫秒）
        private const int MaxDoorCount = 32;              // 最大城门数量

        // 状态消息
        private const string DoorOpenedMsg = "城门已打开";
        private const string DoorClosedMsg = "城门已关闭";
        private const string DoorStatusUpdatedMsg = "城门状态已更新";
        private const string InvalidDoorMsg = "无效的城门";

        #endregion

        #region Model (不可变状态快照)

        /// <summary>
        /// 城门状态快照（不可变）
        /// 对应原版 TDoorStatus 结构
        /// </summary>
        public sealed class DoorStatusSnapshot
        {
            /// <summary>
            /// 城门X坐标
            /// </summary>
            public int DoorX { get; }

            /// <summary>
            /// 城门Y坐标
            /// </summary>
            public int DoorY { get; }

            /// <summary>
            /// 是否已打开
            /// 对应 TDoorStatus.boOpened
            /// </summary>
            public bool IsOpened { get; }

            /// <summary>
            /// 状态标志
            /// 对应 TDoorStatus.bo01
            /// </summary>
            public bool StatusFlag { get; }

            /// <summary>
            /// 状态值
            /// 对应 TDoorStatus.n04
            /// </summary>
            public int StatusValue { get; }

            /// <summary>
            /// 开门时间戳
            /// 对应 TDoorStatus.dwOpenTick
            /// </summary>
            public int OpenTick { get; }

            /// <summary>
            /// 引用计数
            /// 对应 TDoorStatus.nRefCount
            /// </summary>
            public int RefCount { get; }

            /// <summary>
            /// 是否有效（状态对象存在）
            /// </summary>
            public bool IsValid { get; }

            public DoorStatusSnapshot(
                int doorX,
                int doorY,
                bool isOpened,
                bool statusFlag,
                int statusValue,
                int openTick,
                int refCount,
                bool isValid)
            {
                DoorX = doorX;
                DoorY = doorY;
                IsOpened = isOpened;
                StatusFlag = statusFlag;
                StatusValue = statusValue;
                OpenTick = openTick;
                RefCount = refCount;
                IsValid = isValid;
            }

            /// <summary>
            /// 创建无效状态快照
            /// </summary>
            public static DoorStatusSnapshot CreateInvalid(int doorX = 0, int doorY = 0)
            {
                return new DoorStatusSnapshot(doorX, doorY, false, false, 0, 0, 0, false);
            }

            /// <summary>
            /// 从 TDoorInfo 创建快照
            /// </summary>
            public static DoorStatusSnapshot FromDoorInfo(TDoorInfo doorInfo)
            {
                if (doorInfo == null)
                    return CreateInvalid();

                var status = doorInfo.Status;
                if (status == null)
                    return CreateInvalid(doorInfo.nX, doorInfo.nY);

                return new DoorStatusSnapshot(
                    doorInfo.nX,
                    doorInfo.nY,
                    status.boOpened,
                    status.bo01,
                    status.n04,
                    status.dwOpenTick,
                    status.nRefCount,
                    true);
            }
        }

        #endregion

        #region View (状态查询接口)

        /// <summary>
        /// 获取指定地图的城门状态
        /// </summary>
        /// <param name="envir">地图环境</param>
        /// <param name="doorX">城门X坐标</param>
        /// <param name="doorY">城门Y坐标</param>
        /// <returns>城门状态快照，未找到返回无效快照</returns>
        public static DoorStatusSnapshot GetDoorStatus(Envirnoment envir, int doorX, int doorY)
        {
            if (envir?.m_DoorList == null)
                return DoorStatusSnapshot.CreateInvalid(doorX, doorY);

            foreach (var door in envir.m_DoorList)
            {
                if (door != null && door.nX == doorX && door.nY == doorY)
                {
                    return DoorStatusSnapshot.FromDoorInfo(door);
                }
            }

            return DoorStatusSnapshot.CreateInvalid(doorX, doorY);
        }

        /// <summary>
        /// 获取指定索引的城门状态
        /// </summary>
        /// <param name="envir">地图环境</param>
        /// <param name="doorIndex">城门索引</param>
        /// <returns>城门状态快照，索引无效返回无效快照</returns>
        public static DoorStatusSnapshot GetDoorStatusByIndex(Envirnoment envir, int doorIndex)
        {
            if (envir?.m_DoorList == null)
                return DoorStatusSnapshot.CreateInvalid();

            if (doorIndex < 0 || doorIndex >= envir.m_DoorList.Count)
                return DoorStatusSnapshot.CreateInvalid();

            var doorInfo = envir.m_DoorList[doorIndex];
            return DoorStatusSnapshot.FromDoorInfo(doorInfo);
        }

        /// <summary>
        /// 获取地图所有城门状态
        /// </summary>
        /// <param name="envir">地图环境</param>
        /// <returns>所有城门状态快照数组</returns>
        public static DoorStatusSnapshot[] GetAllDoorStatuses(Envirnoment envir)
        {
            if (envir?.m_DoorList == null || envir.m_DoorList.Count == 0)
                return System.Array.Empty<DoorStatusSnapshot>();

            var snapshots = new DoorStatusSnapshot[envir.m_DoorList.Count];
            for (var i = 0; i < envir.m_DoorList.Count; i++)
            {
                snapshots[i] = DoorStatusSnapshot.FromDoorInfo(envir.m_DoorList[i]);
            }

            return snapshots;
        }

        /// <summary>
        /// 检查城门是否打开
        /// </summary>
        public static bool IsDoorOpen(Envirnoment envir, int doorX, int doorY)
        {
            var status = GetDoorStatus(envir, doorX, doorY);
            return status.IsValid && status.IsOpened;
        }

        /// <summary>
        /// 统计打开的城门数量
        /// </summary>
        public static int CountOpenDoors(Envirnoment envir)
        {
            if (envir?.m_DoorList == null)
                return 0;

            var count = 0;
            foreach (var door in envir.m_DoorList)
            {
                if (door?.Status != null && door.Status.boOpened)
                    count++;
            }

            return count;
        }

        /// <summary>
        /// 统计关闭的城门数量
        /// </summary>
        public static int CountClosedDoors(Envirnoment envir)
        {
            if (envir?.m_DoorList == null)
                return 0;

            var count = 0;
            foreach (var door in envir.m_DoorList)
            {
                if (door?.Status != null && !door.Status.boOpened)
                    count++;
            }

            return count;
        }

        #endregion

        #region Intent (状态变更操作)

        /// <summary>
        /// 打开城门
        /// </summary>
        /// <param name="envir">地图环境</param>
        /// <param name="doorX">城门X坐标</param>
        /// <param name="doorY">城门Y坐标</param>
        /// <returns>操作结果</returns>
        public static DoorOperationResult OpenDoor(Envirnoment envir, int doorX, int doorY)
        {
            var doorInfo = FindDoorInfo(envir, doorX, doorY);
            if (doorInfo == null)
                return DoorOperationResult.DoorNotFound;

            if (doorInfo.Status == null)
            {
                // 基于 Envirnoment.cs:1240-1247 的初始化逻辑
                doorInfo.Status = new TDoorStatus
                {
                    boOpened = false,
                    bo01 = false,
                    n04 = 0,
                    dwOpenTick = 0,
                    nRefCount = 1
                };
            }

            if (doorInfo.Status.boOpened)
                return DoorOperationResult.AlreadyOpen;

            // 执行开门操作
            doorInfo.Status.boOpened = true;
            doorInfo.Status.dwOpenTick = HUtil32.GetTickCount();

            LogDoorOperation(doorX, doorY, DoorOpenedMsg);
            return DoorOperationResult.Success;
        }

        /// <summary>
        /// 关闭城门
        /// </summary>
        /// <param name="envir">地图环境</param>
        /// <param name="doorX">城门X坐标</param>
        /// <param name="doorY">城门Y坐标</param>
        /// <returns>操作结果</returns>
        public static DoorOperationResult CloseDoor(Envirnoment envir, int doorX, int doorY)
        {
            var doorInfo = FindDoorInfo(envir, doorX, doorY);
            if (doorInfo == null)
                return DoorOperationResult.DoorNotFound;

            if (doorInfo.Status == null)
                return DoorOperationResult.StatusNotInitialized;

            if (!doorInfo.Status.boOpened)
                return DoorOperationResult.AlreadyClosed;

            // 执行关门操作
            doorInfo.Status.boOpened = false;
            doorInfo.Status.dwOpenTick = 0;

            LogDoorOperation(doorX, doorY, DoorClosedMsg);
            return DoorOperationResult.Success;
        }

        /// <summary>
        /// 切换城门状态（打开↔关闭）
        /// </summary>
        public static DoorOperationResult ToggleDoor(Envirnoment envir, int doorX, int doorY)
        {
            var doorInfo = FindDoorInfo(envir, doorX, doorY);
            if (doorInfo == null)
                return DoorOperationResult.DoorNotFound;

            if (doorInfo.Status == null)
            {
                doorInfo.Status = new TDoorStatus
                {
                    boOpened = false,
                    bo01 = false,
                    n04 = 0,
                    dwOpenTick = 0,
                    nRefCount = 1
                };
            }

            if (doorInfo.Status.boOpened)
                return CloseDoor(envir, doorX, doorY);
            else
                return OpenDoor(envir, doorX, doorY);
        }

        /// <summary>
        /// 更新城门开启时间戳
        /// </summary>
        public static DoorOperationResult UpdateOpenTick(Envirnoment envir, int doorX, int doorY, int newTick)
        {
            var doorInfo = FindDoorInfo(envir, doorX, doorY);
            if (doorInfo == null)
                return DoorOperationResult.DoorNotFound;

            if (doorInfo.Status == null)
                return DoorOperationResult.StatusNotInitialized;

            doorInfo.Status.dwOpenTick = newTick;
            return DoorOperationResult.Success;
        }

        /// <summary>
        /// 重置城门状态
        /// </summary>
        public static DoorOperationResult ResetDoorStatus(Envirnoment envir, int doorX, int doorY)
        {
            var doorInfo = FindDoorInfo(envir, doorX, doorY);
            if (doorInfo == null)
                return DoorOperationResult.DoorNotFound;

            // 重新初始化状态
            doorInfo.Status = new TDoorStatus
            {
                boOpened = false,
                bo01 = false,
                n04 = 0,
                dwOpenTick = 0,
                nRefCount = 1
            };

            return DoorOperationResult.Success;
        }

        /// <summary>
        /// 批量打开所有城门
        /// </summary>
        public static int OpenAllDoors(Envirnoment envir)
        {
            if (envir?.m_DoorList == null)
                return 0;

            var openedCount = 0;
            foreach (var door in envir.m_DoorList)
            {
                if (door != null)
                {
                    var result = OpenDoor(envir, door.nX, door.nY);
                    if (result == DoorOperationResult.Success)
                        openedCount++;
                }
            }

            return openedCount;
        }

        /// <summary>
        /// 批量关闭所有城门
        /// </summary>
        public static int CloseAllDoors(Envirnoment envir)
        {
            if (envir?.m_DoorList == null)
                return 0;

            var closedCount = 0;
            foreach (var door in envir.m_DoorList)
            {
                if (door != null)
                {
                    var result = CloseDoor(envir, door.nX, door.nY);
                    if (result == DoorOperationResult.Success)
                        closedCount++;
                }
            }

            return closedCount;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// 查找城门信息
        /// </summary>
        private static TDoorInfo FindDoorInfo(Envirnoment envir, int doorX, int doorY)
        {
            if (envir?.m_DoorList == null)
                return null;

            foreach (var door in envir.m_DoorList)
            {
                if (door != null && door.nX == doorX && door.nY == doorY)
                    return door;
            }

            return null;
        }

        /// <summary>
        /// 验证城门索引
        /// </summary>
        private static bool ValidateDoorIndex(Envirnoment envir, int doorIndex)
        {
            if (envir?.m_DoorList == null)
                return false;

            return doorIndex >= 0 && doorIndex < envir.m_DoorList.Count;
        }

        /// <summary>
        /// 记录城门操作日志
        /// </summary>
        private static void LogDoorOperation(int doorX, int doorY, string message)
        {
            M2Share.MainOutMessage($"[SabakGate]({doorX},{doorY}) {message}");
        }

        #endregion
    }

    #region Result Types

    /// <summary>
    /// 城门操作结果枚举
    /// </summary>
    public enum DoorOperationResult
    {
        /// <summary>操作成功</summary>
        Success,

        /// <summary>城门未找到</summary>
        DoorNotFound,

        /// <summary>状态未初始化</summary>
        StatusNotInitialized,

        /// <summary>城门已经打开</summary>
        AlreadyOpen,

        /// <summary>城门已经关闭</summary>
        AlreadyClosed,

        /// <summary>无效的城门索引</summary>
        InvalidIndex,

        /// <summary>环境未初始化</summary>
        EnvironmentNotInitialized
    }

    #endregion
}
