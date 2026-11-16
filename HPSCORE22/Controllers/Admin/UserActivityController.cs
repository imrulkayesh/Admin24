// Controllers/Admin/UserActivityController.cs
// 24/7 user login activity tracking - admin pages over USER_LOGIN_LOG (script: App_Data/Sql/USER_LOGIN_LOG.sql):
//   /UserActivity/Dashboard  - totals of today, active users right now, 14-day trend, last activity
//   /UserActivity/Report     - searchable history (date range, username, IP, status) + Excel (CSV) + PDF export
// Both pages are permission-checked server side like the Dispatch pages (UC_TASKMANAGER_COPY URL
// 'UserActivity/Dashboard' / 'UserActivity/Report'); a super admin (ROLEID=1) always may.
using AlphaCare.Interface;
using AlphaCare.Models;
using AlphaCare.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rotativa.AspNetCore;
using Rotativa.AspNetCore.Options;
using C = AlphaCare.Models.DispatchReportColumn;

namespace QCMS.Controllers
{
    [Authorize]
    public class UserActivityController : Controller
    {
        // sessions are idle-limited to 30 min (Program.cs) - a row quiet for longer than this has expired
        private const int StaleAfterMinutes = 35;

        private readonly ILoginLogRepository _repo;
        private readonly ILogger<UserActivityController> _logger;

        public UserActivityController(ILoginLogRepository repo, ILogger<UserActivityController> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        private string StaffId => HttpContext.Session.GetString("STAFFID") ?? "";

        private IActionResult? PageGuard(string url)
        {
            if (string.IsNullOrWhiteSpace(StaffId)) return RedirectToAction("Login", "Accounts");
            if (!_repo.HasMenuPermission(StaffId, url)) return RedirectToAction("Denied", "Accounts");
            return null;
        }

        // the columns of the report grid, the CSV and the PDF (keys = column names of LoginLogRepository.Search)
        private static readonly List<C> ReportColumns = new()
        {
            new("USERNAME", "Username"), new("USERID", "User ID"),
            new("LOGIN_TIME", "Login Time", "date"), new("LOGOUT_TIME", "Logout Time", "date"),
            new("DURATION_MIN", "Minutes", "num"), new("STATE", "Status"), new("FAILURE_REASON", "Failure Reason"),
            new("IP_ADDRESS", "IP Address"), new("DEVICE_INFO", "Device / Browser"), new("SESSION_ID", "Session ID")
        };

        // =====================================================================================================
        // DASHBOARD
        // =====================================================================================================
        [HttpGet]
        public IActionResult Dashboard()
        {
            if (PageGuard("UserActivity/Dashboard") is { } guard) return guard;

            var vm = new LoginDashboardViewModel();
            try
            {
                _repo.CloseStale(StaleAfterMinutes);    // sessions nothing was heard of -> EXPIRED (browser closed etc.)
                vm = _repo.GetDashboard();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login dashboard failed (USER_LOGIN_LOG missing / not reachable?)");
                ViewBag.Message = "The dashboard could not be loaded. Has the USER_LOGIN_LOG table been created (App_Data/Sql/USER_LOGIN_LOG.sql)?";
            }
            ViewData["Title"] = "User Login Dashboard";
            return View(vm);
        }

        // =====================================================================================================
        // REPORT (grid + Excel CSV + PDF)
        // =====================================================================================================
        [HttpGet]
        public IActionResult Report(LoginLogFilter f, int run = 0, int export = 0, int pdf = 0)
        {
            if (PageGuard("UserActivity/Report") is { } guard) return guard;

            f ??= new LoginLogFilter();
            f.Username = (f.Username ?? "").Trim();
            if (f.Username.Length > 100) f.Username = f.Username[..100];
            f.Ip = (f.Ip ?? "").Trim();
            if (f.Ip.Length > 50) f.Ip = f.Ip[..50];
            f.Status = f.Status is "SUCCESS" or "FAILED" or "ACTIVE" ? f.Status : "";

            bool searching = run == 1 || export == 1 || pdf == 1;
            if (!searching) { f.From = DateTime.Today.AddDays(-6); f.To = DateTime.Today; }   // first opening: last 7 days
            if (f.From.HasValue && f.To.HasValue && f.To < f.From) (f.From, f.To) = (f.To, f.From);

            var vm = new LoginReportViewModel
            {
                Filter = f,
                Columns = ReportColumns,
                MaxRows = Admin360.Repositories.LoginLogRepository.ReportMaxRows
            };

            if (searching)
            {
                try
                {
                    vm.Rows = _repo.Search(f);
                    vm.Searched = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Login report failed (USER_LOGIN_LOG missing / not reachable?)");
                    vm.Message = "The report could not be loaded. Has the USER_LOGIN_LOG table been created (App_Data/Sql/USER_LOGIN_LOG.sql)?";
                }

                if (export == 1 && vm.Searched)      // Excel (same CSV builder as the dispatch reports)
                {
                    byte[] file = DispatchReportCsv.Build("User Login Report", vm.Columns, vm.Rows);
                    return File(file, "text/csv", $"UserLoginReport_{DateTime.Now:yyyyMMddHHmm}.csv");
                }

                if (pdf == 1 && vm.Searched)         // PDF (Rotativa/wkhtmltopdf, configured in Program.cs)
                {
                    return new ViewAsPdf("ReportPdf", vm)
                    {
                        FileName = $"UserLoginReport_{DateTime.Now:yyyyMMddHHmm}.pdf",
                        PageSize = Size.A4,
                        PageOrientation = Orientation.Landscape,
                        PageMargins = new Margins(8, 8, 8, 8),
                        CustomSwitches = "--footer-right \"Page [page] of [topage]\" --footer-font-size 7"
                    };
                }
            }

            ViewData["Title"] = "User Login Report";
            return View(vm);
        }
    }
}
