// Controllers/Admin/AdminController.cs
// Admin module (port of YA Pages/Admin). The setup screens share one generic page - see
// AdminController.Setup.cs (SetupDef) and Views/Admin/Setup.cshtml; DepartmentAssign has its own page.
// Menu permission is checked server side per URL (UC_TASKMANAGER_COPY, e.g. 'Admin/Country'),
// exactly like the Dispatch pages; a super admin (ROLEID = 1) always may.
using AlphaCare.Interface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Admin24.Controllers.Admin
{
    [Authorize]
    public partial class AdminController : Controller
    {
        private readonly IAdminSetupRepository _repo;
        private readonly ILogger<AdminController> _logger;

        public AdminController(IAdminSetupRepository repo, ILogger<AdminController> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        private string StaffId => HttpContext.Session.GetString("STAFFID") ?? "";

        private bool Allowed(string url) => !string.IsNullOrWhiteSpace(StaffId) && _repo.HasMenuPermission(StaffId, url);

        private IActionResult? PageGuard(string url)
        {
            if (string.IsNullOrWhiteSpace(StaffId)) return RedirectToAction("Login", "Accounts");
            if (!Allowed(url)) return RedirectToAction("Denied", "Accounts");
            return null;
        }

        private JsonResult Fail(string message) => Json(new { success = false, message });

        // the old file had an Index without a view; the module tiles of Home are the real entry point
        public IActionResult Index() => RedirectToAction("Index", "Home");
    }
}
