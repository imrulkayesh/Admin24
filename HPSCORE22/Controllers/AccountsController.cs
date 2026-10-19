//using Admin360.Interface;
using AlphaCare.Interface;
using AlphaCare.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QCMS.Repositories;
using System.Data;
using System.Security.Claims;
using System.Text.Json;

namespace AlphaCare.Controllers
{
    public class AccountsController : Controller
    {
        private readonly UserRepository _userRepository;
        private readonly ApiService _apiService;
        private readonly IModuleRepository _moduleRepository;
        private readonly ILogger<AccountsController> _logger;

        public AccountsController(UserRepository userRepository, ApiService apiService,
            IModuleRepository moduleRepository, ILogger<AccountsController> logger)
        {
            _userRepository = userRepository;
            _apiService = apiService;
            _moduleRepository = moduleRepository;
            _logger = logger;
        }

        // =========================
        // LOGIN VIEW
        // =========================
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", "Home");

            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        // =========================
        // LOGIN POST  (port of old Login.aspx.cs -> LoginButton_Click)
        // =========================
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string Username, string Password, bool Remember = false, string? returnUrl = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrEmpty(Password))
                    return Json(new { success = false, message = "Username and Password are required" });

                Username = Username.Trim();

                DataTable dt = _userRepository.GetLoginInfoByUserID(Username);

                var hris = await _apiService.HrisLoginAsync(Username, Password);

                if (!hris.Success || dt.Rows.Count == 0)
                    return Json(new { success = false, message = "Invalid Username or Password." });

                DataTable udt = _userRepository.GetEmployeeInfo(Username);
                if (udt.Rows.Count == 0)
                    return Json(new { success = false, message = "Employee information not found. Please contact Admin." });

                HttpContext.Session.Clear();

                string userId = dt.Rows[0]["ID"].ToString() ?? "";
                string userName = dt.Rows[0]["USERNAME"].ToString() ?? "";
                string fullName = udt.Rows[0]["EMPLOYEENAME"].ToString() ?? "";
                string staffId = udt.Rows[0]["STAFFID"].ToString() ?? "";
                int roleId = Convert.ToInt32(dt.Rows[0]["ROLEID"]);

                HttpContext.Session.SetString("USERID", userId);
                HttpContext.Session.SetString("USERNAME", userName);
                HttpContext.Session.SetString("UserFullName", fullName);
                HttpContext.Session.SetString("STAFFID", staffId);
                HttpContext.Session.SetInt32("RoleId", roleId);
                HttpContext.Session.SetInt32("UserTypeID", roleId);          // kept for code that still reads UserTypeID
                HttpContext.Session.SetInt32("BusinessCenterID", Convert.ToInt32(udt.Rows[0]["BUSINESSCENTERID"]));
                HttpContext.Session.SetInt32("CompanyID", Convert.ToInt32(udt.Rows[0]["COMPANYID"]));
                HttpContext.Session.SetInt32("DepartmentID", Convert.ToInt32(udt.Rows[0]["DEPARTMENTID"]));
                HttpContext.Session.SetInt32("DesignationID", Convert.ToInt32(udt.Rows[0]["DESIGNATIONID"]));

                // ---- login cookie ----
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, userName),
                    new Claim(ClaimTypes.NameIdentifier, userId),
                    new Claim("USERID", userId),
                    new Claim("STAFFID", staffId),
                    new Claim("ROLEID", roleId.ToString())
                };
                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(identity),
                    new AuthenticationProperties
                    {
                        IsPersistent = Remember,
                        ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(Remember ? 480 : 30)
                    });

                // ---- old: returnUrl handling (opens the module the returnUrl belongs to) ----
                string redirect = Url.Action("Index", "Home")!;     // old default: ~/Pages/Admin/Default.aspx (modules page)

                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    // old: SELECT MODULEID FROM UC_TASKMANAGER WHERE URL='...'
                    string urlForLookup = returnUrl.Replace("%7e", "").Replace("%2f", "/").Replace("~", "");
                    int? moduleId = _moduleRepository.GetModuleIdByUrl(urlForLookup);

                    if (moduleId.HasValue)
                        SetModuleSession(staffId, moduleId.Value);

                    redirect = returnUrl;
                }

                _logger.LogInformation("User {User} logged in", userId);
                return Json(new { success = true, redirect });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login error");
                return Json(new { success = false, message = "Login failed. Please try again or contact Admin." });
            }
        }

        // saves module + its menu in session (same as Home/SelectModule)
        private void SetModuleSession(string staffId, int moduleId)
        {
            var module = _moduleRepository.GetModules(staffId).FirstOrDefault(m => m.ID == moduleId);
            if (module == null) return;

            var tree = _moduleRepository.BuildMenuTree(_moduleRepository.GetModuleTasks(staffId, moduleId));

            HttpContext.Session.SetInt32("ModuleId", module.ID);
            HttpContext.Session.SetString("ModuleName", module.MODULENAME ?? "");
            HttpContext.Session.SetString("ModuleIcon", module.ICON ?? "fa-folder");
            HttpContext.Session.SetString("ModuleMenu", JsonSerializer.Serialize(tree));
        }

        // =========================
        // LOGOUT
        // =========================
        [HttpGet]
        public async Task<IActionResult> Logout()
        {
            HttpContext.Session.Clear();
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Denied() => View();
    }
}
