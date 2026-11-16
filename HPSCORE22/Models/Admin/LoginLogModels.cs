// Models/Admin/LoginLogModels.cs
// 24/7 user login activity tracking. Table: USER_LOGIN_LOG (script: App_Data/Sql/USER_LOGIN_LOG.sql).
// The report page reuses the dispatch report building blocks (DispatchReportColumn + dynamic rows + CSV export).
namespace AlphaCare.Models
{
    // filter of the login report (all optional)
    public class LoginLogFilter
    {
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public string? Username { get; set; }          // contains, case-insensitive
        public string? Ip { get; set; }                // starts with
        public string Status { get; set; } = "";       // "" = all | SUCCESS | FAILED | ACTIVE (logged in now)
    }

    // one point of the 14-day dashboard chart
    public class LoginTrendPoint
    {
        public DateTime DAY { get; set; }
        public int SUCCESS_COUNT { get; set; }
        public int FAILED_COUNT { get; set; }
    }

    // one open session on the dashboard ("who is online")
    public class ActiveLoginRow
    {
        public long ID { get; set; }
        public string? USERID { get; set; }
        public string USERNAME { get; set; } = "";
        public DateTime LOGIN_TIME { get; set; }
        public DateTime? LAST_ACTIVITY { get; set; }
        public string? IP_ADDRESS { get; set; }
        public string? DEVICE_INFO { get; set; }
    }

    // one row of the "last activity" table on the dashboard
    public class RecentLoginRow
    {
        public long ID { get; set; }
        public string USERNAME { get; set; } = "";
        public DateTime LOGIN_TIME { get; set; }
        public DateTime? LOGOUT_TIME { get; set; }
        public string LOGIN_STATUS { get; set; } = "";
        public string? LOGOUT_TYPE { get; set; }
        public string? FAILURE_REASON { get; set; }
        public string? IP_ADDRESS { get; set; }
        public string? DEVICE_INFO { get; set; }
    }

    public class LoginDashboardViewModel
    {
        public int TodayLogins { get; set; }           // successful logins today
        public int TodayFailed { get; set; }           // failed attempts today
        public int TodayUsers { get; set; }            // distinct users who logged in today
        public int ActiveUsers { get; set; }           // distinct users logged in right now

        public List<LoginTrendPoint> Trend { get; set; } = new();
        public List<ActiveLoginRow> Active { get; set; } = new();
        public List<RecentLoginRow> Recent { get; set; } = new();
    }

    public class LoginReportViewModel
    {
        public LoginLogFilter Filter { get; set; } = new();
        public bool Searched { get; set; }
        public string? Message { get; set; }
        public List<DispatchReportColumn> Columns { get; set; } = new();
        public List<IDictionary<string, object?>> Rows { get; set; } = new();
        public int MaxRows { get; set; }
        public bool Truncated => MaxRows > 0 && Rows.Count >= MaxRows;
    }
}
