// Repositories/DispatchDashboardProvider.cs   (replaces the template)
// Dispatch module dashboard. Reuses the lists of DispatchRepository (waiting list, approver list, gate list, desk lists),
// so the numbers are always the same as the numbers on the real Dispatch pages.
// Scope of "my dispatches" = created by me OR sent from me (same rule as GetMyWaitingList).
// Program.cs (already added):  builder.Services.AddScoped<IModuleDashboardProvider, DispatchDashboardProvider>();
using AlphaCare.Interface;
using AlphaCare.Models;
using Dapper;
using QCMS.Services;                 // DatabaseService (same using as DispatchRepository)
using System.Threading.Tasks;

namespace Admin360.Repositories
{
    public class DispatchDashboardProvider : IModuleDashboardProvider
    {
        private readonly DatabaseService _databaseService;
        private readonly IHttpContextAccessor _http;

        public DispatchDashboardProvider(DatabaseService databaseService, IHttpContextAccessor http)
        {
            _databaseService = databaseService;
            _http = http;
        }

        // ---- COUNT queries: the same rules as the lists in DispatchRepository (GetMyWaitingList, GetPendingApprovals,
        //      GetGateCheckList, GetDeskSendList, GetDeskReceiveList) but they only COUNT - no rows, no sorting, no big transfer.
        //      If a rule changes in DispatchRepository, change it here too.
        private const string Active = " EXISTS (SELECT 1 FROM DP_TRANSITS DT WHERE DT.DISPATCHID = D.ID AND DT.ISACTIVE = 1) ";
        private const string ListFrom = @"
                  FROM DP_DISPATCH D
                 INNER JOIN EM_EMPLOYEE E ON E.USERID = D.CREATEDBY
                 INNER JOIN EM_EMPLOYEE SE ON SE.USERID = D.FROMUSER
                 INNER JOIN DP_ISSUETYPE IT ON IT.ID = D.ISSUETYPEID";

        private const string SqlWaitingMine = "SELECT COUNT(*)" + ListFrom + @"
                 WHERE D.STATUSID = 1 AND D.GATEPASS >= 0 AND (D.CREATEDBY = :StaffId1 OR D.FROMUSER = :StaffId2)
                   AND D.CREATEDATE >= TRUNC(SYSDATE) - 6";

        private const string SqlForMyApproval = "SELECT COUNT(*)" + ListFrom + @"
                 WHERE NVL(D.ISOPEN, 0) = 0 AND" + Active + @"AND EXISTS (SELECT 1 FROM DP_APPROVER A
                          WHERE A.APPROVERID = :Approver
                            AND A.BUSINESSCENTERID = E.BUSINESSCENTERID
                            AND A.DEPARTMENTID = E.DEPARTMENTID
                            AND A.COMPANYID = E.COMPANYID
                            AND A.PRIORITY = D.STATUSID)
                   AND D.CREATEDATE >= TRUNC(SYSDATE) - 6";

        private const string SqlGate = @"
                SELECT COUNT(*)
                  FROM DP_DISPATCH D
                 INNER JOIN EM_EMPLOYEE SE ON SE.STAFFID = D.FROMUSER
                 INNER JOIN EM_EMPLOYEE E ON E.STAFFID = D.CREATEDBY
                  LEFT OUTER JOIN DP_ISSUETYPE IT ON IT.ID = D.ISSUETYPEID
                 WHERE" + Active + @"
                   AND D.STATUSID IN (2, 3, 6, 7) AND D.ISOPEN = 0 AND D.GATEPASS = 0
                   AND E.BUSINESSCENTERID = :CenterId
                   AND TRUNC(D.CREATEDATE) BETWEEN TRUNC(SYSDATE) - 6 AND TRUNC(SYSDATE)";

        private const string SqlDeskSend = "SELECT COUNT(*)" + ListFrom + @"
                 WHERE NVL(D.ISOPEN, 0) = 0 AND" + Active + @"AND D.STATUSID IN (2, 6) AND IT.ISPERSONAL = 0 AND D.DIRECTSEND = 0
                   AND E.BUSINESSCENTERID = :CenterId
                   AND TRUNC(D.CREATEDATE) BETWEEN TRUNC(SYSDATE) - 6 AND TRUNC(SYSDATE)";

        private const string SqlDeskReceive = "SELECT COUNT(*)" + ListFrom + @"
                 WHERE NVL(D.ISOPEN, 0) = 0 AND" + Active + @"AND D.STATUSID IN (3, 7) AND D.DIRECTSEND = 0
                   AND ( (D.TOUSER <> '0' AND EXISTS (SELECT 1 FROM EM_EMPLOYEE RE WHERE RE.USERID = D.TOUSER AND RE.BUSINESSCENTERID = :CenterId))
                      OR (D.TOUSER = '0' AND SE.BUSINESSCENTERID = :CenterId2) )
                   AND D.CREATEDATE >= TRUNC(SYSDATE) - 6";

        // ONLY the last DAYS days are read (today + 6 days before). Smaller number = faster page.
        // The SQL texts below use the same number (-6); if you change DAYS, change "- 6" in them too.
        private const int DAYS = 7;

        // page that opens ONE dispatch (put your real url here, e.g. "~/Dispatch/Details?id=").  "" = the row is not a link.
        private const string DetailsUrl = "";

        public bool CanHandle(ModuleModel module) =>
            (module.MODULENAME ?? "").IndexOf("dispatch", StringComparison.OrdinalIgnoreCase) >= 0;

        public class Row
        {
            public long ID { get; set; }
            public string? TRANSACTIONID { get; set; }
            public DateTime CREATEDATE { get; set; }
            public int STATUSID { get; set; }            // 1 waiting approval | 2..8 approved / on the way | 99+ rejected (status + 98)
            public int GATEPASS { get; set; }            // 1 = gate checked
            public string? ISSUETYPENAME { get; set; }
            public string? RECEIVERNAME { get; set; }
            public string? BUSINESSCENTER { get; set; }
        }

        private static bool Rejected(Row r) => r.STATUSID >= 99;
        private static bool Waiting(Row r) => r.STATUSID == 1;
        private static bool Approved(Row r) => r.STATUSID >= 2 && r.STATUSID < 99;
        private static string StatusName(Row r) => Rejected(r) ? "Rejected" : Waiting(r) ? "Waiting Approval" : r.STATUSID == 2 ? "Approved" : "In Process";
        private static string StatusColor(Row r) => Rejected(r) ? "#dc2626" : Waiting(r) ? "#d97706" : r.STATUSID == 2 ? "#16a34a" : "#0891b2";

        // a failed side-count must never hide the whole dashboard (it shows 0)
        private int Count(string sql, object param)
        {
            try { using var c = _databaseService.GetConnection(); return c.ExecuteScalar<int>(sql, param); }
            catch { return 0; }
        }

        public ModuleDashboardData Build(ModuleModel module, string staffId, List<SideMenuItem> menu)
        {
            var now = DateTime.Now;
            var today = now.Date;
            var from = today.AddDays(-(DAYS - 1));                 // last 7 days (today included)
            var to = today.AddDays(1);

            // user's location: from the session (no query); only if missing ask the database
            int centerId = _http.HttpContext?.Session.GetInt32("BusinessCenterID") ?? 0;

            // all queries run AT THE SAME TIME (each with its own connection) -> the page waits for the slowest one only
            const string sqlRows = @"
                    SELECT D.ID, D.TRANSACTIONID, D.CREATEDATE, NVL(D.STATUSID, 0) AS STATUSID, NVL(D.GATEPASS, 0) AS GATEPASS,
                           IT.ISSUETYPENAME, D.RECEIVERNAME, D.BUSINESSCENTER
                      FROM DP_DISPATCH D
                     INNER JOIN DP_ISSUETYPE IT ON IT.ID = D.ISSUETYPEID
                     WHERE NVL(D.ISOPEN, 0) = 0
                       AND NVL(D.GATEPASS, 0) >= 0
                       AND D.CREATEDATE >= :FromDate
                       AND D.CREATEDATE <  :ToDate
                       AND (D.CREATEDBY = :StaffId1 OR D.FROMUSER = :StaffId2)";

            var tRows = Task.Run(() =>
            {
                using var c = _databaseService.GetConnection();
                // bind variables are positional in ODP.NET: property order = order in the SQL
                return c.Query<Row>(sqlRows, new { FromDate = from, ToDate = to, StaffId1 = staffId, StaffId2 = staffId }).ToList();
            });
            var tWaiting = Task.Run(() => Count(SqlWaitingMine, new { StaffId1 = staffId, StaffId2 = staffId }));
            var tApprove = Task.Run(() => Count(SqlForMyApproval, new { Approver = staffId }));
            var tCenter = Task.Run(() =>
            {
                if (centerId > 0) return centerId;
                try
                {
                    using var c = _databaseService.GetConnection();
                    return c.ExecuteScalar<int?>("SELECT BUSINESSCENTERID FROM EM_EMPLOYEE WHERE STAFFID = :StaffId AND ROWNUM = 1", new { StaffId = staffId }) ?? 0;
                }
                catch { return 0; }
            });
            Task.WaitAll(tRows, tWaiting, tApprove, tCenter);          // tRows: a real error here is reported to the page

            centerId = tCenter.Result;
            var tGate = centerId > 0 ? Task.Run(() => Count(SqlGate, new { CenterId = centerId })) : Task.FromResult(0);
            var tSend = centerId > 0 ? Task.Run(() => Count(SqlDeskSend, new { CenterId = centerId })) : Task.FromResult(0);
            var tRecv = centerId > 0 ? Task.Run(() => Count(SqlDeskReceive, new { CenterId = centerId, CenterId2 = centerId })) : Task.FromResult(0);
            Task.WaitAll(tGate, tSend, tRecv);

            var rows = tRows.Result;
            var thisMonth = rows;                                      // rows already = last week only
            int waitingMine = tWaiting.Result, forMyApproval = tApprove.Result;
            int gate = tGate.Result, deskSend = tSend.Result, deskReceive = tRecv.Result;

            var d = new ModuleDashboardData();
            d.Stats.Add(new DashStat
            {
                Title = "My Dispatches",
                Value = thisMonth.Count.ToString(),
                Icon = "fa-solid fa-truck-fast",
                Color = "#4f46e5",
                Sub = "last 7 days · today " + rows.Count(r => r.CREATEDATE.Date == today)
            });
            d.Stats.Add(new DashStat
            {
                Title = "Waiting Approval",
                Value = waitingMine.ToString(),
                Icon = "fa-solid fa-hourglass-half",
                Color = "#d97706",
                Sub = "my dispatches"
            });
            d.Stats.Add(new DashStat
            {
                Title = "Pending For My Approval",
                Value = forMyApproval.ToString(),
                Icon = "fa-solid fa-user-check",
                Color = "#7c3aed",
                Sub = forMyApproval > 0 ? "as approver - please review" : "as approver"
            });
            d.Stats.Add(new DashStat
            {
                Title = "Approved / In Process",
                Value = thisMonth.Count(Approved).ToString(),
                Icon = "fa-solid fa-circle-check",
                Color = "#16a34a",
                Sub = "last 7 days · gate checked " + thisMonth.Count(r => Approved(r) && r.GATEPASS == 1)
            });
            d.Stats.Add(new DashStat
            {
                Title = "Rejected",
                Value = thisMonth.Count(Rejected).ToString(),
                Icon = "fa-solid fa-ban",
                Color = "#dc2626",
                Sub = "last 7 days"
            });
            d.Stats.Add(new DashStat
            {
                Title = "At My Location",
                Value = (gate + deskSend + deskReceive).ToString(),
                Icon = "fa-solid fa-warehouse",
                Color = "#0891b2",
                Sub = "gate " + gate + " · send " + deskSend + " · receive " + deskReceive
            });

            // ---------- chart 1: my dispatches per day (last 14 days) ----------
            var days = Enumerable.Range(-(DAYS - 1), DAYS).Select(i => today.AddDays(i)).ToList();
            d.Charts.Add(new DashChart
            {
                Id = "chDays",
                Title = "My dispatches: last 7 days",
                Type = "bar",
                Unit = "dispatches",
                Labels = days.Select(x => x.ToString("dd MMM")).ToList(),
                Series = new List<DashSeries> { new DashSeries { Label = "Dispatches", Data = days.Select(x => (double)rows.Count(r => r.CREATEDATE.Date == x)).ToList() } },
                Colors = days.Select(x => x == today ? "#f59e0b" : "#6366f1").ToList()
            });

            // ---------- chart 2: status last 7 days ----------
            d.Charts.Add(new DashChart
            {
                Id = "chStatus",
                Title = "Last 7 days by status",
                Type = "doughnut",
                Unit = "dispatches",
                Labels = new List<string> { "Approved / In Process", "Waiting Approval", "Rejected" },
                Series = new List<DashSeries> { new DashSeries { Label = "Dispatches", Data = new List<double> { thisMonth.Count(Approved), thisMonth.Count(Waiting), thisMonth.Count(Rejected) } } },
                Colors = new List<string> { "#16a34a", "#d97706", "#dc2626" }
            });

            // ---------- chart 3: issue type last 7 days (top 6) ----------
            var types = thisMonth.GroupBy(r => string.IsNullOrWhiteSpace(r.ISSUETYPENAME) ? "-" : r.ISSUETYPENAME!)
                                 .Select(g => new { Name = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Take(6).ToList();
            d.Charts.Add(new DashChart
            {
                Id = "chTypes",
                Title = "Issue type: last 7 days",
                Type = "hbar",
                Unit = "dispatches",
                Labels = types.Select(x => x.Name).ToList(),
                Series = new List<DashSeries> { new DashSeries { Label = "Dispatches", Data = types.Select(x => (double)x.Count).ToList() } },
                Colors = new List<string> { "#0891b2" }
            });

            // ---------- recent dispatches ----------
            d.RecentTitle = "Recent Dispatches";
            foreach (var r in rows.OrderByDescending(r => r.CREATEDATE).Take(8))
            {
                d.Recent.Add(new DashRecent
                {
                    Title = (r.TRANSACTIONID ?? "#" + r.ID) + " · " + (r.ISSUETYPENAME ?? ""),
                    Sub = r.CREATEDATE.ToString("ddd, dd MMM · hh:mm tt") + (string.IsNullOrWhiteSpace(r.RECEIVERNAME) ? "" : " · To: " + r.RECEIVERNAME),
                    Meta = (r.BUSINESSCENTER ?? "") + (r.GATEPASS == 1 ? " · Gate checked" : ""),
                    Status = StatusName(r),
                    StatusColor = StatusColor(r),
                    Icon = "fa-solid fa-truck",
                    Url = string.IsNullOrEmpty(DetailsUrl) ? null : DetailsUrl + r.ID
                });
            }

            // ---------- quick actions = the user's own Dispatch menu (real urls, only what the user may open) ----------
            d.Actions = GenericModuleDashboardProvider.BuildFromMenu(menu).Actions;
            return d;
        }
    }
}