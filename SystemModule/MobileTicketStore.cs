using System;
using System.Collections.Concurrent;

namespace SystemModule
{
    /// <summary>
    /// 两段式登录的 ticket 存储:HTTP 账号服验密码成功后签发 ticket→账号映射,
    /// TCP 网关在 CM_LOGIN_AUTH 阶段用 ticket 反查账号。
    ///
    /// 进程内共享；分进程部署时由网关注入原生 account.ticket 解析器。
    /// </summary>
    public static class MobileTicketStore
    {
        private sealed class Entry
        {
            public string Account;
            public long ExpireTick; // Environment.TickCount64 毫秒
        }

        private static readonly ConcurrentDictionary<string, Entry> _map = new ConcurrentDictionary<string, Entry>();

        /// <summary>
        /// 跨进程 ticket 解析钩子。账号服与网关分进程部署时,由网关启动期注入一个
        /// 查询原生 account.ticket/account.normal 的实现:ticket→账号,无效返回 null。
        /// 未注入时仅用进程内 _map。
        /// </summary>
        public static Func<string, string> ExternalResolver;

        /// <summary>ticket 默认有效期(毫秒)。</summary>
        public static long TicketTtlMs = 5 * 60 * 1000;

        /// <summary>
        /// 网关在 ForwardLoginAuth 里组 LoginSvr 登录串时填充的密码位。
        /// LoginSvr 侧需信任网关(账号已由 HTTP 账号服验过),或将该值设为库中实际密码。
        /// ⚠ 待联调:与 LoginSvr 的账号验证策略对齐。
        /// </summary>
        public static string GatewayPass = "@mobilegw";

        public static void Issue(string ticket, string account)
        {
            if (string.IsNullOrEmpty(ticket)) return;
            _map[ticket] = new Entry { Account = account, ExpireTick = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond + TicketTtlMs };
        }

        /// <summary>反查账号;无效/过期返回 null。优先进程内,其次外部共享存储。</summary>
        public static string ResolveAccount(string ticket)
        {
            if (string.IsNullOrEmpty(ticket)) return null;
            if (_map.TryGetValue(ticket, out var e))
            {
                if (DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond <= e.ExpireTick) return e.Account;
                _map.TryRemove(ticket, out _);
            }
            var ext = ExternalResolver;
            if (ext != null)
            {
                try { return ext(ticket); } catch { }
            }
            return null;
        }

        public static void Remove(string ticket)
        {
            if (!string.IsNullOrEmpty(ticket)) _map.TryRemove(ticket, out _);
        }
    }
}
