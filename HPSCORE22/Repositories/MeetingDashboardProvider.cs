// Repositories/MeetingDashboardProvider.cs
// Meeting module dashboard. Scope = "my meetings" (same rule as MeetingList: participant OR chairperson OR creator),
// plus the number of meetings waiting for ME as a room admin.
// Program.cs:  builder.Services.AddScoped<IModuleDashboardProvider, MeetingDashboardProvider>();
using AlphaCare.Interface;
using AlphaCare.Models;
using Admin360.Services;          // DatabaseService (change namespace if yours differs)
using Dapper;
using System.Threading.Tasks;
using QCMS.Services;

namespace Admin360.Repositories
{
    public class MeetingDashboardProvider : IModuleDashboardProvider
    {
        private readonly DatabaseService _databaseService;
        public MeetingDashboardProvider(DatabaseService databaseService) { _databaseService = databaseService; }

        // module is recognised by its name in UC_MODULE (change here if your module is named differently)
        public bool CanHandle(ModuleModel module) =>
            (module.MODULENAME ?? "").IndexOf("meeting", StringComparison.OrdinalIgnoreCase) >= 0;

        public class Row
        {
            public int ID { get; set; }
            public string? MEETINGTITLE { get; set; }
            public DateTime STARTTIME { get; set; }
            public DateTime ENDTIME { get; set; }
            public int ISAPPROVED { get; set; }          // 0 waiting, 1 approved, 2 rejected, 3 cancelled
            public string? ROOMTITLE { get; set; }
            public string? CENTERCODE { get; set; }
        }

        private static string StatusName(int s) => s switch { 1 => "Approved", 2 => "Rejected", 3 => "Cancelled", _ => "Waiting" };
        private static string StatusColor(int s) => s switch { 1 => "#16a34a", 2 => "#dc2626", 3 => "#64748b", _ => "#d97706" };

        public ModuleDashboardData Build(ModuleModel module, string staffId, List<SideMenuItem> menu)
        {
            var now = DateTime.Now;
            var today = now.Date;
            var monthStart = new DateTime(today.Year, today.Month, 1);
            var from = monthStart.AddMonths(-1);              // covers "last 7 days" even at the start of a month
            var to = monthStart.AddMonths(2);

            // both queries run AT THE SAME TIME (own connection each)
            // bind variables are positional in ODP.NET: the order of the properties = the order in the SQL
            const string sql = @"
                    SELECT M.ID, M.MEETINGTITLE, M.STARTTIME, M.ENDTIME, NVL(M.ISAPPROVED, 0) AS ISAPPROVED,
                           MR.ROOMTITLE, B.CENTERCODE
                      FROM MT_MEETING M
                     INNER JOIN MT_MEETINGROOM MR ON MR.ID = M.MROOMID
                     INNER JOIN BS_BUSINESSCENTER B ON B.ID = MR.BUSINESSCENTERID
                     WHERE NVL(M.STATUS, 0) = 0
                       AND M.STARTTIME >= :FromDate
                       AND M.STARTTIME <  :ToDate
                       AND ( EXISTS (SELECT 1 FROM MT_PERTICIPANT MP
                                      WHERE MP.MEETINGID = M.ID AND MP.STAFFID = :StaffId1)
                             OR M.CHAIRPERSONSTAFFID = :StaffId2
                             OR M.CREATEDBY = :StaffId3 )";
            var tRows = Task.Run(() =>
            {
                using var c = _databaseService.GetConnection();
                return c.Query<Row>(sql, new { FromDate = from, ToDate = to, StaffId1 = staffId, StaffId2 = staffId, StaffId3 = staffId }).ToList();
            });
            var tAdmin = Task.Run(() =>
            {
                try
                {
                    using var c = _databaseService.GetConnection();
                    return c.ExecuteScalar<int>(@"
                        SELECT COUNT(*)
                          FROM MT_MEETING M
                         INNER JOIN MT_MEETINGROOM MR ON MR.ID = M.MROOMID
                         WHERE MR.ADMINSTAFFID = :StaffId
                           AND NVL(M.ISAPPROVED, 0) = 0
                           AND NVL(M.STATUS, 0) = 0
                           AND M.ENDTIME >= SYSDATE", new { StaffId = staffId });
                }
                catch { return 0; }                          // a failed side-query must not hide the whole dashboard
            });
            Task.WaitAll(tRows, tAdmin);
            var rows = tRows.Result;
            int adminWaiting = tAdmin.Result;

            bool Live(Row r) => r.ISAPPROVED == 0 || r.ISAPPROVED == 1;     // waiting or approved
            var thisMonth = rows.Where(r => r.STARTTIME >= monthStart && r.STARTTIME < monthStart.AddMonths(1)).ToList();

            // ---------- stat cards ----------
            var todayRows = rows.Where(r => Live(r) && r.STARTTIME.Date == today).ToList();
            int running = todayRows.Count(r => r.ISAPPROVED == 1 && r.STARTTIME <= now && r.ENDTIME > now);
            var next = todayRows.Where(r => r.STARTTIME > now).OrderBy(r => r.STARTTIME).FirstOrDefault();
            var upcoming = rows.Where(r => Live(r) && r.STARTTIME > now && r.STARTTIME < today.AddDays(8)).ToList();
            int waitingMine = rows.Count(r => r.ISAPPROVED == 0 && r.ENDTIME >= now);
            var approvedMonth = thisMonth.Where(r => r.ISAPPROVED == 1).ToList();
            double approvedHours = approvedMonth.Sum(r => (r.ENDTIME - r.STARTTIME).TotalHours);
            int rejCan = thisMonth.Count(r => r.ISAPPROVED == 2 || r.ISAPPROVED == 3);

            var d = new ModuleDashboardData();
            d.Stats.Add(new DashStat
            {
                Title = "Today's Meetings",
                Value = todayRows.Count.ToString(),
                Icon = "fa-solid fa-calendar-day",
                Color = "#4f46e5",
                Sub = running > 0 ? running + " running now" : next != null ? "Next at " + next.STARTTIME.ToString("hh:mm tt") : "Nothing more today",
                Url = "~/Meeting/MeetingCalendar"
            });
            d.Stats.Add(new DashStat
            {
                Title = "Upcoming (7 days)",
                Value = upcoming.Count.ToString(),
                Icon = "fa-solid fa-forward",
                Color = "#0891b2",
                Sub = upcoming.Count(r => r.ISAPPROVED == 1) + " approved · " + upcoming.Count(r => r.ISAPPROVED == 0) + " waiting",
                Url = "~/Meeting/MeetingSchedule"
            });
            d.Stats.Add(new DashStat
            {
                Title = "Waiting Approval",
                Value = waitingMine.ToString(),
                Icon = "fa-solid fa-hourglass-half",
                Color = "#d97706",
                Sub = "my meetings",
                Url = "~/Meeting/MeetingList"
            });
            d.Stats.Add(new DashStat
            {
                Title = "Approved This Month",
                Value = approvedMonth.Count.ToString(),
                Icon = "fa-solid fa-circle-check",
                Color = "#16a34a",
                Sub = Math.Round(approvedHours, 1) + " hours in total",
                Url = "~/Meeting/MeetingSchedule"
            });
            d.Stats.Add(new DashStat
            {
                Title = "Rejected / Cancelled",
                Value = rejCan.ToString(),
                Icon = "fa-solid fa-ban",
                Color = "#dc2626",
                Sub = "this month",
                Url = "~/Meeting/MeetingList"
            });
            d.Stats.Add(new DashStat
            {
                Title = "Waiting For My Approval",
                Value = adminWaiting.ToString(),
                Icon = "fa-solid fa-user-check",
                Color = "#7c3aed",
                Sub = adminWaiting > 0 ? "as room admin - please review" : "as room admin",
                Url = "~/Meeting/RoomAdmin"
            });

            // ---------- chart 1: meetings per day (6 days back .. 7 days ahead) ----------
            var days = Enumerable.Range(-6, 14).Select(i => today.AddDays(i)).ToList();
            d.Charts.Add(new DashChart
            {
                Id = "chDays",
                Title = "Meetings: last 7 & next 7 days",
                Type = "bar",
                Unit = "meetings",
                Labels = days.Select(x => x.ToString("dd MMM")).ToList(),
                Series = new List<DashSeries> { new DashSeries { Label = "Meetings", Data = days.Select(x => (double)rows.Count(r => Live(r) && r.STARTTIME.Date == x)).ToList() } },
                Colors = days.Select(x => x == today ? "#f59e0b" : "#6366f1").ToList()          // today is highlighted
            });

            // ---------- chart 2: status of this month ----------
            var st = new[] { 1, 0, 2, 3 };
            d.Charts.Add(new DashChart
            {
                Id = "chStatus",
                Title = "This month by status",
                Type = "doughnut",
                Unit = "meetings",
                Labels = st.Select(StatusName).ToList(),
                Series = new List<DashSeries> { new DashSeries { Label = "Meetings", Data = st.Select(s => (double)thisMonth.Count(r => r.ISAPPROVED == s)).ToList() } },
                Colors = st.Select(StatusColor).ToList()
            });

            // ---------- chart 3: room usage hours (approved, this month) ----------
            var rooms = approvedMonth.GroupBy(r => (r.ROOMTITLE ?? "-") + " (" + (r.CENTERCODE ?? "") + ")")
                                     .Select(g => new { Room = g.Key, Hours = g.Sum(r => (r.ENDTIME - r.STARTTIME).TotalHours) })
                                     .OrderByDescending(x => x.Hours).Take(6).ToList();
            d.Charts.Add(new DashChart
            {
                Id = "chRooms",
                Title = "Room usage this month (hours)",
                Type = "hbar",
                Unit = "hrs",
                Labels = rooms.Select(x => x.Room).ToList(),
                Series = new List<DashSeries> { new DashSeries { Label = "Hours", Data = rooms.Select(x => Math.Round(x.Hours, 1)).ToList() } },
                Colors = new List<string> { "#0891b2" }
            });

            // ---------- recent / upcoming list (the 8 meetings nearest to now) ----------
            d.RecentTitle = "Recent & Upcoming Meetings";
            foreach (var r in rows.OrderBy(r => Math.Abs((r.STARTTIME - now).TotalMinutes)).Take(8).OrderByDescending(r => r.STARTTIME))
            {
                d.Recent.Add(new DashRecent
                {
                    Title = r.MEETINGTITLE ?? "(no title)",
                    Sub = r.STARTTIME.ToString("ddd, dd MMM") + " · " + r.STARTTIME.ToString("hh:mm tt") + " - " + r.ENDTIME.ToString("hh:mm tt"),
                    Meta = (r.ROOMTITLE ?? "") + (string.IsNullOrEmpty(r.CENTERCODE) ? "" : " (" + r.CENTERCODE + ")"),
                    Status = StatusName(r.ISAPPROVED),
                    StatusColor = StatusColor(r.ISAPPROVED),
                    Url = "~/Meeting/ViewMeeting?id=" + r.ID
                });
            }

            // ---------- quick actions ----------
            d.Actions.Add(new DashAction { Title = "Create Meeting", Icon = "fa-solid fa-plus", Color = "#4f46e5", Url = "~/Meeting/CreateMeeting" });
            d.Actions.Add(new DashAction { Title = "My Meetings", Icon = "fa-solid fa-list", Color = "#0891b2", Url = "~/Meeting/MeetingList" });
            d.Actions.Add(new DashAction { Title = "Schedule", Icon = "fa-solid fa-table-list", Color = "#d97706", Url = "~/Meeting/MeetingSchedule" });
            d.Actions.Add(new DashAction { Title = "Calendar", Icon = "fa-regular fa-calendar-days", Color = "#7c3aed", Url = "~/Meeting/MeetingCalendar" });
            d.Actions.Add(new DashAction { Title = "Room Usage", Icon = "fa-solid fa-chart-column", Color = "#059669", Url = "~/Meeting/RoomUsage" });
            d.Actions.Add(new DashAction { Title = "Room Admin", Icon = "fa-solid fa-user-check", Color = "#db2777", Url = "~/Meeting/RoomAdmin" });
            return d;
        }
    }
}
