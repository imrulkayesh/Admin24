using AlphaCare.Interface;
//using AlphaCare.Interface.DashboardInterface;
using AlphaCare.Models;
using AlphaCare.Models.Dashboard_Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

        public HomeController(
            ILogger<HomeController> logger,
            ICommonMethod SessionHelper,
            //IUserDashboardRepository UserDashboard,
            IModuleRepository moduleRepository)
        {
            _logger = logger;
            _SessionHelper = SessionHelper;
            //_UserDashboard = UserDashboard;
            _moduleRepository = moduleRepository;
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

        // =========================
        // MODULE DASHBOARD   /Home/Module/{id}
        // Module tile click -> this page. The left sidebar shows this module's menu
        // (_SideBar reads the session values saved below).
        // =========================
        [HttpGet]
        public IActionResult Module(int id)
        {
            string staffId = HttpContext.Session.GetString("STAFFID") ?? "";

            // user may open only his own modules
            var module = _moduleRepository.GetModules(staffId).FirstOrDefault(m => m.ID == id);
            if (module == null)
                return RedirectToAction("Denied", "Accounts");

            var tree = _moduleRepository.BuildMenuTree(_moduleRepository.GetModuleTasks(staffId, id));

            // same as old SessionName.ModuleId / TaskList - sidebar uses these on every page
            HttpContext.Session.SetInt32("ModuleId", module.ID);
            HttpContext.Session.SetString("ModuleName", module.MODULENAME ?? "");
            HttpContext.Session.SetString("ModuleIcon", module.ICON ?? "fa-folder");
            HttpContext.Session.SetString("ModuleMenu", JsonSerializer.Serialize(tree));

            var vm = new ModuleDashboardViewModel
            {
                Module = module,
                Menu = tree
            };
            return View(vm);   // Views/Home/Module.cshtml
        }
    }
}
