// Interface/Admin/ILoginLogRepository.cs
// USER_LOGIN_LOG data access (24/7 user login activity tracking).
// The Log* / Touch / Close* methods NEVER throw - they swallow + log errors, so a missing table or a
// database hiccup can never break the login/logout flow itself. The read methods (dashboard / report) throw
// and the controller shows a friendly message, like the dispatch reports.
using AlphaCare.Models;

namespace AlphaCare.Interface
{
    public interface ILoginLogRepository
    {
        // ---- writes from the authentication flow (never throw) ----
        long LogSuccess(string userId, string username, string? ip, string? deviceInfo, string? userAgent, string? sessionId);
        void LogFailure(string username, string reason, string? ip, string? deviceInfo, string? userAgent);
        void LogLogout(long logId, string? userId, string logoutType);          // logId 0 -> latest open row of userId
        void Touch(long logId);                                                 // LAST_ACTIVITY = now (open rows only)
        long FindOpenLogId(string userId);                                      // latest open row of the user, 0 = none
        int CloseStale(int idleMinutes);                                        // open rows idle too long -> EXPIRED

        // ---- admin pages ----
        bool HasMenuPermission(string staffId, string url);                     // same rule as the dispatch pages + super admin
        LoginDashboardViewModel GetDashboard();
        List<IDictionary<string, object?>> Search(LoginLogFilter f);
    }
}
