using Admin360.Repositories;
using AlphaCare.Interface;
//using AlphaCare.Interface.DashboardInterface;
using AlphaCare.Models;
using AlphaCare.Models.Dashboard_Model;
using AlphaCare.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using RetailCare.Common;
using System.Text.Json;

namespace QCMS.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly ICommonMethod _SessionHelper;
        //private readonly IUserDashboardRepository _UserDashboard;
        private readonly IModuleRepository _moduleRepository;

        private readonly IMemoryCache _cache;                                       // dashboard numbers are kept for 60 seconds
        private readonly IEnumerable<IModuleDashboardProvider> _dashboardProviders;   // Meeting / Dispatch / ... (registered in Program.cs)

        public HomeController(
            ILogger<HomeController> logger,
            ICommonMethod SessionHelper,
            //IUserDashboardRepository UserDashboard,
            IModuleRepository moduleRepository,
            IMemoryCache cache,
            IEnumerable<IModuleDashboardProvider> dashboardProviders)          // <-- was missing: the field stayed null -> every dashboard call failed
        {
            _logger = logger;
            _SessionHelper = SessionHelper;
            //_UserDashboard = UserDashboard;
            _moduleRepository = moduleRepository;
            _cache = cache;
            _dashboardProviders = dashboardProviders;                          // <-- was missing
        }

        // =========================
        // HOME  (all module tiles + notice + events)
        // =========================
        [HttpGet]
        public IActionResult Index()
        {
            string staffId = HttpContext.Session.GetString("STAFFID") ?? "";

            var vm = new UserDashboardViewModel
            {
                Modules = _moduleRepository.GetModules(staffId),
                SelectedModuleId = HttpContext.Session.GetInt32("ModuleId")
            };

            return View(vm);
        }

        // ---- helpers ----
        private string StaffIdValue => HttpContext.Session.GetString("STAFFID") ?? "";

        // the user may open only his own modules (same rule as SelectModule)
        private ModuleModel? FindMyModule(int id) =>
            _moduleRepository.GetModules(StaffIdValue).FirstOrDefault(m => m.ID == id);

        // same session values as SelectModule -> the left sidebar shows this module's menu on every page
        private List<SideMenuItem> LoadModuleIntoSession(ModuleModel module)
        {
            var rows = _moduleRepository.GetModuleTasks(StaffIdValue, module.ID);
            var tree = _moduleRepository.BuildMenuTree(rows);

            HttpContext.Session.SetInt32("ModuleId", module.ID);
            HttpContext.Session.SetString("ModuleName", module.MODULENAME ?? "");
            HttpContext.Session.SetString("ModuleIcon", module.ICON ?? "fa-folder");
            HttpContext.Session.SetString("ModuleMenu", JsonSerializer.Serialize(tree));
            LoginSession.RememberModule(HttpContext, module.ID);
            return tree;
        }

        private static string NormUrl(string? u) => (u ?? "").Replace("~", "").Split('?')[0].TrimEnd('/').ToLowerInvariant();

        // ---- page: quick-view menu (rendered now) + dashboard (loaded by AJAX) ----
        [HttpGet]
        public IActionResult Module(int id)
        {
            var module = FindMyModule(id);
            if (module == null) return Forbid();

            var menu = LoadModuleIntoSession(module);
            return View("Module", new ModuleDashboardViewModel { Module = module, Menu = menu });
        }

        // ---- JSON: statistics, charts, recent list, quick actions of one module ----
        // FAST: module + menu come from the session (the Module page just saved them - no database),
        //       the finished result is cached 60 seconds per user (refresh=1 = the Refresh button, skips the cache).
        [HttpGet]
        public IActionResult ModuleDashboardData(int id, int debug = 0, int refresh = 0)
        {
            // camelCase on purpose: Program.cs sets PropertyNamingPolicy = null (PascalCase) and the page reads camelCase names
            IActionResult Out(ModuleDashboardData d) =>
                Json(d, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });

            string cacheKey = "moddash:" + id + ":" + StaffIdValue;
            if (refresh != 1 && _cache.TryGetValue(cacheKey, out ModuleDashboardData? hit) && hit != null)
                return Out(hit);

            try
            {
                ModuleModel? module;
                List<SideMenuItem>? menu = null;

                if (HttpContext.Session.GetInt32("ModuleId") == id)          // the Module action already checked that this user may open it
                {
                    module = new ModuleModel
                    {
                        ID = id,
                        MODULENAME = HttpContext.Session.GetString("ModuleName") ?? "",
                        ICON = HttpContext.Session.GetString("ModuleIcon")
                    };
                    var json = HttpContext.Session.GetString("ModuleMenu");
                    if (!string.IsNullOrEmpty(json))
                    {
                        try { menu = JsonSerializer.Deserialize<List<SideMenuItem>>(json); } catch { menu = null; }
                    }
                }
                else
                {
                    module = FindMyModule(id);
                }
                if (module == null) return Forbid();
                menu ??= _moduleRepository.BuildMenuTree(_moduleRepository.GetModuleTasks(StaffIdValue, id));

                var provider = _dashboardProviders.FirstOrDefault(p => p.CanHandle(module)) ?? new GenericModuleDashboardProvider();
                var data = provider.Build(module, StaffIdValue, menu);

                // quick actions: only pages that are in the user's menu (menu permission). If the menu uses another url style and
                // nothing matches, the actions are kept (they are only links - every page checks its own permission).
                var allowed = menu.SelectMany(g => new[] { g }.Concat(g.Children)).Select(i => NormUrl(Href(i.Url))).ToHashSet();
                var permitted = data.Actions.Where(a => allowed.Contains(NormUrl(Url.Content(a.Url ?? "#")))).ToList();
                if (permitted.Count > 0) data.Actions = permitted;

                // "~/Meeting/List" -> real url (works under an IIS virtual directory too)
                string R(string? u) => string.IsNullOrEmpty(u) ? "#" : u.StartsWith("~") ? Url.Content(u) : Href(u);
                data.Stats.ForEach(s => s.Url = R(s.Url));
                data.Recent.ForEach(r => r.Url = R(r.Url));
                data.Actions.ForEach(a => a.Url = R(a.Url));

                _cache.Set(cacheKey, data, TimeSpan.FromSeconds(60));
                return Out(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Module dashboard failed for module {ModuleId}", id);
                // super admin can add ?debug=1 to the page address to see the real error on screen
                string? detail = debug == 1 && _moduleRepository.IsSuperAdmin(StaffIdValue) ? (ex.InnerException ?? ex).Message : null;
                return StatusCode(500, new { error = "Could not load the dashboard.", detail });
            }
        }

        // same url rule as _SideMenuItems / Module.cshtml
        private string Href(string? url)
        {
            if (string.IsNullOrWhiteSpace(url) || url == "#") return "#";
            if (url.StartsWith("~")) return Url.Content(url);
            if (url.StartsWith("/")) return Url.Content("~" + url);
            return Url.Content("~/" + url);
        }




    }
}
