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
        private readonly ILoginLogRepository _loginLog;        // USER_LOGIN_LOG (24/7 login activity tracking)
        private readonly ILogger<AccountsController> _logger;

        // "Keep me signed in" ticked -> the login cookie lives this many days. Not ticked -> 30 minutes (sliding).
        private const int RememberDays = 7;

        public AccountsController(UserRepository userRepository, ApiService apiService,
            IModuleRepository moduleRepository, ILoginLogRepository loginLog, ILogger<AccountsController> logger)
        {
            _userRepository = userRepository;
            _apiService = apiService;
            _moduleRepository = moduleRepository;
            _loginLog = loginLog;
            _logger = logger;
        }

        // Pending Dispatch Gate Check records of the staff (D.CREATEDBY = the HRIS user id typed on the login page = staff id).
        // A failure here must not hide the inactive message, so it is logged and an empty list is returned.
        private List<object> PendingGateChecks(string staffId)
        {
            try
            {
                return _userRepository.GetPendingGateCheckDispatches(staffId)
                    .Where(p => !string.IsNullOrWhiteSpace(p.TRANSACTIONID))
                    .Select(p => (object)new
                    {
                        transactionId = p.TRANSACTIONID,
                        date = p.CREATEDATE?.ToString("dd/MM/yyyy"),
                        issueType = p.ISSUETYPENAME,
                        receiver = p.RECEIVER_NAME,
                        receiverLocation = p.RECEIVER_LOCATION
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pending dispatch gate check lookup failed for {StaffId}", staffId);
                return new List<object>();
            }
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
            // USER_LOGIN_LOG: who tried, from where, with what (the password itself is never logged)
            string? ip = LoginActivity.Ip(HttpContext);
            string? ua = LoginActivity.UserAgent(HttpContext);
            string? device = LoginActivity.Device(ua);

            try
            {
                if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrEmpty(Password))
                    return Json(new { success = false, message = "Username and Password are required" });

                Username = Username.Trim();

                DataTable dt = _userRepository.GetLoginInfoByUserID(Username);

                var hris = await _apiService.HrisLoginAsync(Username, Password);

                if (!hris.Success || dt.Rows.Count == 0)
                {
                    // old Login.aspx.cs: on a failed login check UC_USERS for ISACTIVE=0 + INACTIVETYPE='Dispatch'
                    // (account switched off by the Dispatch module) and show the specific message instead
                    if (_userRepository.IsInactiveForDispatchGate(Username))
                    {
                        _loginLog.LogFailure(Username, "Inactive - Dispatch Gate check pending", ip, device, ua);
                        return Json(new
                        {
                            success = false,
                            message = "Your User ID is inactive because the Dispatch Gate check is pending.",
                            pendingDispatches = PendingGateChecks(Username)      // the dispatches still waiting for the gate check
                        });
                    }

                    _loginLog.LogFailure(Username, "Invalid username or password", ip, device, ua);
                    return Json(new { success = false, message = "Invalid Username or Password." });
                }

                DataTable udt = _userRepository.GetEmployeeInfo(Username);
                if (udt.Rows.Count == 0)
                {
                    _loginLog.LogFailure(Username, "Employee information not found", ip, device, ua);
                    return Json(new { success = false, message = "Employee information not found. Please contact Admin." });
                }

                HttpContext.Session.Clear();

                string userId = dt.Rows[0]["ID"].ToString() ?? "";
                string userName = dt.Rows[0]["USERNAME"].ToString() ?? "";
                string fullName = udt.Rows[0]["EMPLOYEENAME"].ToString() ?? "";
                string staffId = udt.Rows[0]["STAFFID"].ToString() ?? "";
                int roleId = Convert.ToInt32(dt.Rows[0]["ROLEID"]);

                LoginSession.Fill(HttpContext.Session, dt, udt);      // all session values (same code is used to restore them later)

                // USER_LOGIN_LOG: open the row of this login; the id stays in the session so logout/expiry can close it
                long loginLogId = _loginLog.LogSuccess(userId, userName, ip, device, ua, HttpContext.Session.Id);
                if (loginLogId > 0)
                    HttpContext.Session.SetString(LoginActivity.LogIdKey, loginLogId.ToString());

                // ---- login cookie ----
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, userName),
                    new Claim(ClaimTypes.NameIdentifier, userId),
                    new Claim("USERID", userId),
                    new Claim("STAFFID", staffId),
                    new Claim("ROLEID", roleId.ToString()),
                    new Claim(LoginSession.RememberClaim, Remember ? "1" : "0")     // lets Program.cs restore the session later
                };
                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(identity),
                    new AuthenticationProperties
                    {
                        IsPersistent = Remember,
                        ExpiresUtc = Remember ? DateTimeOffset.UtcNow.AddDays(RememberDays) : DateTimeOffset.UtcNow.AddMinutes(30)
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
            LoginSession.RememberModule(HttpContext, module.ID);
        }

        // =========================
        // LOGOUT
        // =========================
        [HttpGet]
        public async Task<IActionResult> Logout()
        {
            // USER_LOGIN_LOG: close the row of this login (falls back to the user's newest open row
            // when the session - and with it the stored id - is already gone)
            long.TryParse(HttpContext.Session.GetString(LoginActivity.LogIdKey), out long loginLogId);
            _loginLog.LogLogout(loginLogId, User.FindFirst("USERID")?.Value, "MANUAL");

            HttpContext.Session.Clear();
            Response.Cookies.Delete(LoginSession.LastModuleCookie);
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Denied() => View();
    }
}
