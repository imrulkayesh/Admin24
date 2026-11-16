// Repositories/Admin/LoginLogRepository.cs
// USER_LOGIN_LOG (24/7 user login activity tracking). Table script: App_Data/Sql/USER_LOGIN_LOG.sql.
// Same stack as every other repository: DatabaseService (Oracle) + Dapper, parameterized queries only.
// The write methods are called from the login/logout flow, so they must never throw: a missing table or a
// database hiccup is logged (ILogger) and the user still gets logged in/out.
using AlphaCare.Interface;
using AlphaCare.Models;
using Dapper;
using QCMS.Services;

namespace Admin360.Repositories
{
    public class LoginLogRepository : ILoginLogRepository
    {
        public const int ReportMaxRows = 5000;

        private readonly DatabaseService _databaseService;
        private readonly ILogger<LoginLogRepository> _logger;

        public LoginLogRepository(DatabaseService databaseService, ILogger<LoginLogRepository> logger)
        {
            _databaseService = databaseService;
            _logger = logger;
        }

        private static string? Cut(string? s, int max) =>
            string.IsNullOrWhiteSpace(s) ? null : (s.Length <= max ? s : s[..max]);

        // =====================================================================================================
        // WRITES (never throw)
        // =====================================================================================================
        public long LogSuccess(string userId, string username, string? ip, string? deviceInfo, string? userAgent, string? sessionId)
        {
            try
            {
                using var conn = _databaseService.GetConnection();
                long id = conn.ExecuteScalar<long>("SELECT SEQ_USER_LOGIN_LOG.NEXTVAL FROM DUAL");
                const string sql = @"
                    INSERT INTO USER_LOGIN_LOG
                           (ID, USERID, USERNAME, LOGIN_TIME, LAST_ACTIVITY, IP_ADDRESS, DEVICE_INFO, USER_AGENT, LOGIN_STATUS, SESSION_ID)
                    VALUES (:Id, :UserId, :Username, SYSDATE, SYSDATE, :Ip, :Device, :Ua, 'SUCCESS', :Sid)";
                conn.Execute(sql, new
                {
                    Id = id,
                    UserId = Cut(userId, 50),
                    Username = Cut(username, 100) ?? "?",
                    Ip = Cut(ip, 50),
                    Device = Cut(deviceInfo, 200),
                    Ua = Cut(userAgent, 500),
                    Sid = Cut(sessionId, 100)
                });
                return id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "USER_LOGIN_LOG: could not write the SUCCESS row for {User}", username);
                return 0;
            }
        }

        public void LogFailure(string username, string reason, string? ip, string? deviceInfo, string? userAgent)
        {
            try
            {
                using var conn = _databaseService.GetConnection();
                const string sql = @"
                    INSERT INTO USER_LOGIN_LOG
                           (ID, USERNAME, LOGIN_TIME, IP_ADDRESS, DEVICE_INFO, USER_AGENT, LOGIN_STATUS, FAILURE_REASON)
                    VALUES (SEQ_USER_LOGIN_LOG.NEXTVAL, :Username, SYSDATE, :Ip, :Device, :Ua, 'FAILED', :Reason)";
                conn.Execute(sql, new
                {
                    Username = Cut(username, 100) ?? "?",
                    Ip = Cut(ip, 50),
                    Device = Cut(deviceInfo, 200),
                    Ua = Cut(userAgent, 500),
                    Reason = Cut(reason, 200)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "USER_LOGIN_LOG: could not write the FAILED row for {User}", username);
            }
        }

        public void LogLogout(long logId, string? userId, string logoutType)
        {
            try
            {
                using var conn = _databaseService.GetConnection();
                int done = 0;
                if (logId > 0)
                {
                    const string byId = @"
                        UPDATE USER_LOGIN_LOG
                           SET LOGOUT_TIME = SYSDATE, LOGOUT_TYPE = :Kind, LAST_ACTIVITY = SYSDATE
                         WHERE ID = :Id AND LOGOUT_TIME IS NULL";
                    done = conn.Execute(byId, new { Id = logId, Kind = logoutType });
                }
                if (done == 0 && !string.IsNullOrWhiteSpace(userId))
                {
                    // the session was already gone (e.g. logout after idle) -> close the newest open row of the user
                    const string byUser = @"
                        UPDATE USER_LOGIN_LOG
                           SET LOGOUT_TIME = SYSDATE, LOGOUT_TYPE = :Kind
                         WHERE LOGOUT_TIME IS NULL AND LOGIN_STATUS = 'SUCCESS'
                           AND ID = (SELECT MAX(ID) FROM USER_LOGIN_LOG
                                      WHERE USERID = :UserId AND LOGIN_STATUS = 'SUCCESS' AND LOGOUT_TIME IS NULL)";
                    conn.Execute(byUser, new { Kind = logoutType, UserId = Cut(userId, 50) });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "USER_LOGIN_LOG: could not close row {Id} ({Kind})", logId, logoutType);
            }
        }

        public void Touch(long logId)
        {
            if (logId <= 0) return;
            try
            {
                using var conn = _databaseService.GetConnection();
                conn.Execute("UPDATE USER_LOGIN_LOG SET LAST_ACTIVITY = SYSDATE WHERE ID = :Id AND LOGOUT_TIME IS NULL",
                             new { Id = logId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "USER_LOGIN_LOG: could not touch row {Id}", logId);
            }
        }

        public long FindOpenLogId(string userId)
        {
            try
            {
                using var conn = _databaseService.GetConnection();
                const string sql = @"
                    SELECT NVL(MAX(ID), 0) FROM USER_LOGIN_LOG
                     WHERE USERID = :UserId AND LOGIN_STATUS = 'SUCCESS' AND LOGOUT_TIME IS NULL";
                return conn.ExecuteScalar<long>(sql, new { UserId = Cut(userId, 50) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "USER_LOGIN_LOG: could not look up the open row of {User}", userId);
                return 0;
            }
        }

        public int CloseStale(int idleMinutes)
        {
            try
            {
                using var conn = _databaseService.GetConnection();
                // nothing heard of the session for longer than the idle timeout -> it expired at its last activity
                const string sql = @"
                    UPDATE USER_LOGIN_LOG
                       SET LOGOUT_TIME = NVL(LAST_ACTIVITY, LOGIN_TIME), LOGOUT_TYPE = 'EXPIRED'
                     WHERE LOGIN_STATUS = 'SUCCESS' AND LOGOUT_TIME IS NULL
                       AND NVL(LAST_ACTIVITY, LOGIN_TIME) < SYSDATE - (:Idle / 1440)";
                return conn.Execute(sql, new { Idle = idleMinutes });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "USER_LOGIN_LOG: could not close the stale rows");
                return 0;
            }
        }

        // =====================================================================================================
        // ADMIN PAGES (throw -> the controller shows a friendly message, like the dispatch reports)
        // =====================================================================================================
        // same rule as DispatchRepository.HasMenuPermission + the super admin bypass of ModuleRepository (ROLEID=1)
        public bool HasMenuPermission(string staffId, string url)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT COUNT(1) FROM UC_USERROLE R
                 WHERE R.STAFFID = :StaffId
                   AND (R.ROLEID = 1 OR R.ROLEID IN (
                        SELECT TP.ROLEID
                          FROM UC_TASKPERMISSION_COPY TP
                         INNER JOIN UC_TASKMANAGER_COPY T ON T.ID = TP.TASKID
                         WHERE LOWER(LTRIM(TRIM(T.URL), '~/')) = :Url))";
            string u = (url ?? "").Trim().TrimStart('~', '/').ToLowerInvariant();
            return conn.ExecuteScalar<int>(sql, new { StaffId = staffId, Url = u }) > 0;
        }

        public LoginDashboardViewModel GetDashboard()
        {
            using var conn = _databaseService.GetConnection();
            var vm = new LoginDashboardViewModel();

            const string today = @"
                SELECT NVL(SUM(CASE WHEN LOGIN_STATUS = 'SUCCESS' THEN 1 ELSE 0 END), 0) AS LOGINS,
                       NVL(SUM(CASE WHEN LOGIN_STATUS = 'FAILED'  THEN 1 ELSE 0 END), 0) AS FAILED,
                       COUNT(DISTINCT CASE WHEN LOGIN_STATUS = 'SUCCESS' THEN UPPER(USERNAME) END) AS USERS
                  FROM USER_LOGIN_LOG
                 WHERE LOGIN_TIME >= TRUNC(SYSDATE)";
            var t = conn.QueryFirst(today);
            vm.TodayLogins = Convert.ToInt32(t.LOGINS);
            vm.TodayFailed = Convert.ToInt32(t.FAILED);
            vm.TodayUsers = Convert.ToInt32(t.USERS);

            const string active = @"
                SELECT ID, USERID, USERNAME, LOGIN_TIME, LAST_ACTIVITY, IP_ADDRESS, DEVICE_INFO
                  FROM USER_LOGIN_LOG
                 WHERE LOGIN_STATUS = 'SUCCESS' AND LOGOUT_TIME IS NULL
                 ORDER BY NVL(LAST_ACTIVITY, LOGIN_TIME) DESC";
            vm.Active = conn.Query<ActiveLoginRow>(active).ToList();
            vm.ActiveUsers = vm.Active.Select(a => (a.USERNAME ?? "").ToUpperInvariant()).Distinct().Count();

            const string trend = @"
                SELECT TRUNC(LOGIN_TIME) AS DAY,
                       SUM(CASE WHEN LOGIN_STATUS = 'SUCCESS' THEN 1 ELSE 0 END) AS SUCCESS_COUNT,
                       SUM(CASE WHEN LOGIN_STATUS = 'FAILED'  THEN 1 ELSE 0 END) AS FAILED_COUNT
                  FROM USER_LOGIN_LOG
                 WHERE LOGIN_TIME >= TRUNC(SYSDATE) - 13
                 GROUP BY TRUNC(LOGIN_TIME)
                 ORDER BY TRUNC(LOGIN_TIME)";
            vm.Trend = conn.Query<LoginTrendPoint>(trend).ToList();

            const string recent = @"
                SELECT * FROM (
                    SELECT ID, USERNAME, LOGIN_TIME, LOGOUT_TIME, LOGIN_STATUS, LOGOUT_TYPE, FAILURE_REASON, IP_ADDRESS, DEVICE_INFO
                      FROM USER_LOGIN_LOG
                     ORDER BY ID DESC
                ) WHERE ROWNUM <= 20";
            vm.Recent = conn.Query<RecentLoginRow>(recent).ToList();

            return vm;
        }

        public List<IDictionary<string, object?>> Search(LoginLogFilter f)
        {
            var p = new DynamicParameters();
            string sql = @"
                SELECT L.ID, L.USERNAME, L.USERID, L.LOGIN_TIME, L.LOGOUT_TIME,
                       CASE WHEN L.LOGIN_STATUS = 'FAILED' THEN NULL
                            ELSE ROUND((NVL(L.LOGOUT_TIME, NVL(L.LAST_ACTIVITY, L.LOGIN_TIME)) - L.LOGIN_TIME) * 1440) END AS DURATION_MIN,
                       CASE WHEN L.LOGIN_STATUS = 'FAILED' THEN 'Failed'
                            WHEN L.LOGOUT_TIME IS NULL THEN 'Active'
                            WHEN L.LOGOUT_TYPE = 'MANUAL' THEN 'Logged out'
                            ELSE 'Expired' END AS STATE,
                       L.FAILURE_REASON, L.IP_ADDRESS, L.DEVICE_INFO, L.SESSION_ID
                  FROM USER_LOGIN_LOG L
                 WHERE 1 = 1";

            if (f.From.HasValue) { sql += " AND L.LOGIN_TIME >= :FromDate"; p.Add("FromDate", f.From.Value.Date); }
            if (f.To.HasValue) { sql += " AND L.LOGIN_TIME < :ToDate + 1"; p.Add("ToDate", f.To.Value.Date); }
            if (!string.IsNullOrWhiteSpace(f.Username))
            {
                sql += " AND (UPPER(L.USERNAME) LIKE '%' || UPPER(:UserText) || '%' OR UPPER(L.USERID) = UPPER(:UserText))";
                p.Add("UserText", f.Username.Trim());
            }
            if (!string.IsNullOrWhiteSpace(f.Ip)) { sql += " AND L.IP_ADDRESS LIKE :Ip || '%'"; p.Add("Ip", f.Ip.Trim()); }
            switch (f.Status)
            {
                case "SUCCESS": sql += " AND L.LOGIN_STATUS = 'SUCCESS'"; break;
                case "FAILED": sql += " AND L.LOGIN_STATUS = 'FAILED'"; break;
                case "ACTIVE": sql += " AND L.LOGIN_STATUS = 'SUCCESS' AND L.LOGOUT_TIME IS NULL"; break;
            }
            sql += " ORDER BY L.ID DESC";

            using var conn = _databaseService.GetConnection();
            string limited = "SELECT * FROM (" + sql + ") WHERE ROWNUM <= " + ReportMaxRows;
            return conn.Query(limited, p).Select(r => (IDictionary<string, object?>)r).ToList();
        }
    }
}
