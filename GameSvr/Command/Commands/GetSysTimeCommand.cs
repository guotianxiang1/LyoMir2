using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM command to show the server system time.
    /// Usage: @GetSysTime
    /// Simply displays DateTime.Now in a readable format.
    /// </summary>
    [GameCommand("GetSysTime", "显示服务器系统时间", 4)]
    public class GetSysTimeCommand : BaseCommond
    {
        [DefaultCommand]
        public void GetSysTime(TPlayObject PlayObject)
        {
            var now = System.DateTime.Now;
            PlayObject.SysMsg($"服务器系统时间: {now:yyyy-MM-dd HH:mm:ss} (星期{GetDayOfWeekCN(now.DayOfWeek)})", MsgColor.Green, MsgType.Hint);
            PlayObject.SysMsg($"Ticks: {now.Ticks}", MsgColor.Green, MsgType.Hint);
        }

        private static string GetDayOfWeekCN(System.DayOfWeek day)
        {
            return day switch
            {
                System.DayOfWeek.Monday => "一",
                System.DayOfWeek.Tuesday => "二",
                System.DayOfWeek.Wednesday => "三",
                System.DayOfWeek.Thursday => "四",
                System.DayOfWeek.Friday => "五",
                System.DayOfWeek.Saturday => "六",
                System.DayOfWeek.Sunday => "日",
                _ => "?"
            };
        }
    }
}
