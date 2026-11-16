// Services/Admin/LoginActivity.cs
// Small helpers around USER_LOGIN_LOG used by AccountsController and the Program.cs middleware.
// Everything here swallows its own errors: activity tracking must never break a request.
using AlphaCare.Interface;

namespace AlphaCare.Services
{
    public static class LoginActivity
    {
        public const string LogIdKey = "LOGINLOGID";            // session: USER_LOGIN_LOG.ID of this login
        private const string TouchedKey = "LOGINLOGTOUCH";      // session: last LAST_ACTIVITY write (ticks)
        private static readonly TimeSpan TouchEvery = TimeSpan.FromMinutes(5);

        /// <summary>Client IP. Behind IIS/ARR the real client is the first address of X-Forwarded-For.</summary>
        public static string? Ip(HttpContext ctx)
        {
            string? fwd = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(fwd))
            {
                string first = fwd.Split(',')[0].Trim();
                if (first.Length > 0) return first;
            }
            return ctx.Connection.RemoteIpAddress?.ToString();
        }

        public static string? UserAgent(HttpContext ctx) => ctx.Request.Headers.UserAgent.FirstOrDefault();

        /// <summary>"Chrome / Windows" style short label out of the User-Agent (good enough for the report).</summary>
        public static string? Device(string? ua)
        {
            if (string.IsNullOrWhiteSpace(ua)) return null;

            string browser =
                ua.Contains("Edg/") || ua.Contains("Edge/") ? "Edge" :
                ua.Contains("OPR/") || ua.Contains("Opera") ? "Opera" :
                ua.Contains("SamsungBrowser") ? "Samsung Internet" :
                ua.Contains("Firefox/") ? "Firefox" :
                ua.Contains("Chrome/") ? "Chrome" :
                ua.Contains("Safari/") ? "Safari" :
                ua.Contains("MSIE") || ua.Contains("Trident/") ? "Internet Explorer" : "Other";

            string os =
                ua.Contains("Windows NT 10") ? "Windows 10/11" :
                ua.Contains("Windows") ? "Windows" :
                ua.Contains("Android") ? "Android" :
                ua.Contains("iPhone") || ua.Contains("iPad") ? "iOS" :
                ua.Contains("Mac OS X") ? "macOS" :
                ua.Contains("Linux") ? "Linux" : "Other";

            bool mobile = ua.Contains("Mobile") || ua.Contains("Android") || ua.Contains("iPhone");
            return $"{browser} / {os}{(mobile ? " (mobile)" : "")}";
        }

        /// <summary>Keeps LAST_ACTIVITY fresh while the user works - one UPDATE per 5 minutes at most.</summary>
        public static void TouchIfDue(HttpContext ctx)
        {
            try
            {
                if (ctx.User.Identity?.IsAuthenticated != true) return;
                string? idText = ctx.Session.GetString(LogIdKey);
                if (!long.TryParse(idText, out long logId) || logId <= 0) return;

                string? last = ctx.Session.GetString(TouchedKey);
                if (long.TryParse(last, out long ticks) && DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc) < TouchEvery)
                    return;

                ctx.RequestServices.GetRequiredService<ILoginLogRepository>().Touch(logId);
                ctx.Session.SetString(TouchedKey, DateTime.UtcNow.Ticks.ToString());
            }
            catch { /* tracking must never break the request */ }
        }

        /// <summary>
        /// "Keep me signed in" restored the session silently (LoginSession.TryRestore succeeded) - connect the
        /// new session to the user's open log row, or write a new SUCCESS row when the old one was already
        /// closed as EXPIRED (so the dashboard sees the user again).
        /// </summary>
        public static void AfterRestore(HttpContext ctx)
        {
            try
            {
                var repo = ctx.RequestServices.GetRequiredService<ILoginLogRepository>();
                string userId = ctx.User.FindFirst("USERID")?.Value ?? "";
                if (userId.Length == 0) return;

                long logId = repo.FindOpenLogId(userId);
                if (logId == 0)
                {
                    string? ua = UserAgent(ctx);
                    logId = repo.LogSuccess(userId, ctx.User.Identity?.Name ?? "", Ip(ctx), Device(ua), ua, ctx.Session.Id);
                }
                if (logId > 0)
                {
                    ctx.Session.SetString(LogIdKey, logId.ToString());
                    ctx.Session.SetString(TouchedKey, DateTime.UtcNow.Ticks.ToString());
                }
            }
            catch { }
        }

        /// <summary>The login cookie was valid but the session is gone and cannot be restored -> the login expired.</summary>
        public static void OnExpired(HttpContext ctx)
        {
            try
            {
                string userId = ctx.User.FindFirst("USERID")?.Value ?? "";
                if (userId.Length == 0) return;
                ctx.RequestServices.GetRequiredService<ILoginLogRepository>().LogLogout(0, userId, "EXPIRED");
            }
            catch { }
        }
    }
}
