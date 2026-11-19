// Controllers/Service/ServiceController.Setup.cs
// Service setup pages (port of YA Pages/Service):
//   /Service/ServiceCenter        <- ServiceCenter.aspx        (name, location, center admin)
//   /Service/Category             <- Category.aspx             (categories of a service center)
//   /Service/ServiceTeam          <- ServiceTeam.aspx          (teams of a service center + supervisor)
//   /Service/AddTeamMember        <- AddTeamMember.aspx        (members of a team)
//   /Service/Machine              <- Machine.aspx              (outlets / machines of a location)
//   /Service/AccessoriesCategory  <- AccessoriesCategory.aspx
//   /Service/Accessories          <- Accessories.aspx          (accessories + stock of a service center)
// Add / edit only: the delete buttons were switched off in YA. Every page and every save needs the page in the user's menu.
using AlphaCare.Models;
using Microsoft.AspNetCore.Mvc;

namespace QCMS.Controllers
{
    public partial class ServiceController
    {
        // list filter: the location of the query string, else the user's location (old default), else the first one
        private int PickCenter(int centerId, List<BusinessCenterModel> centers) =>
            centerId > 0 && centers.Any(c => c.ID == centerId) ? centerId
            : centers.Any(c => c.ID == UserCenterId) ? UserCenterId
            : centers.FirstOrDefault()?.ID ?? 0;

        private static int PickOption(int id, List<ServiceOption> options) =>
            id > 0 && options.Any(o => o.ID == id) ? id : (int)(options.FirstOrDefault()?.ID ?? 0);

        private bool ServiceCenterInCenter(int serviceCenterId, int centerId) =>
            serviceCenterId > 0 && centerId > 0 && _repo.GetServiceCentersByCenter(centerId).Any(s => s.ID == serviceCenterId);

        // =====================================================================================================
        // SERVICE CENTER
        // =====================================================================================================
        [HttpGet]
        public IActionResult ServiceCenter()
        {
            if (PageGuard("Service/ServiceCenter") is { } r) return r;
            var centers = _repo.GetAllBusinessCenters();
            var vm = new ServiceSetupViewModel
            {
                CenterId = PickCenter(0, centers),
                BusinessCenters = centers,
                Centers = _repo.GetServiceCenters()                 // old GetAll(): every service center
            };
            ViewData["Title"] = "Service Center";
            return View("ServiceCenter", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveServiceCenter(int id, string? name, int centerId, string? adminStaffId)
        {
            if (!Allowed("Service/ServiceCenter")) return Fail("You are not allowed to change service centers.");
            name = (name ?? "").Trim();
            var errors = new List<string>();
            if (centerId <= 0) errors.Add("Select Business Center");
            if (name.Length == 0) errors.Add("Enter Service Center Name");
            if (name.Length > 100) errors.Add("Service Center Name is too long (maximum 100 characters).");
            string staff = FirstPart(adminStaffId);
            var admin = staff.Length > 0 ? _dispatch.GetEmployee(staff) : null;
            if (admin == null) errors.Add("Enter Admin Staff ID (employee).");
            if (errors.Count > 0) return Fail(string.Join(" ", errors));

            try
            {
                if (_repo.ServiceCenterNameExists(name, centerId, id)) return Fail("This Center name already exists. Please try another.");
                if (id > 0)
                {
                    if (_repo.UpdateServiceCenter(id, name, centerId, admin!.STAFFID, UserId) == 0) return Fail("Service center not found. Please reload the page.");
                }
                else _repo.InsertServiceCenter(name, centerId, admin!.STAFFID, UserId);
                return Json(new { success = true, message = id > 0 ? "Service center updated." : "Service center saved." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save service center failed. Id={Id}", id);
                return Fail("Save failed. Please try again.");
            }
        }

        // =====================================================================================================
        // CATEGORY
        // =====================================================================================================
        [HttpGet]
        public IActionResult Category(int centerId = 0, int serviceCenterId = 0)
        {
            if (PageGuard("Service/Category") is { } r) return r;
            var centers = _repo.GetAllBusinessCenters();
            centerId = PickCenter(centerId, centers);
            var serviceCenters = centerId > 0 ? _repo.GetServiceCentersByCenter(centerId) : new List<ServiceOption>();
            serviceCenterId = PickOption(serviceCenterId, serviceCenters);

            var vm = new ServiceSetupViewModel
            {
                CenterId = centerId,
                ServiceCenterId = serviceCenterId,
                BusinessCenters = centers,
                ServiceCenters = serviceCenters,
                Categories = serviceCenterId > 0 ? _repo.GetCategories(serviceCenterId) : new List<ServiceCategoryModel>()
            };
            ViewData["Title"] = "Service Category";
            return View("Category", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveCategory(int id, string? name, int centerId, int serviceCenterId)
        {
            if (!Allowed("Service/Category")) return Fail("You are not allowed to change service categories.");
            name = (name ?? "").Trim();
            var errors = new List<string>();
            if (centerId <= 0) errors.Add("Select Business Center");
            if (!ServiceCenterInCenter(serviceCenterId, centerId)) errors.Add("Select Service Center");
            if (name.Length == 0) errors.Add("Enter Category");
            if (name.Length > 100) errors.Add("Category Name is too long (maximum 100 characters).");
            if (errors.Count > 0) return Fail(string.Join(" ", errors));

            try
            {
                if (_repo.CategoryNameExists(name, serviceCenterId, id)) return Fail("This category name already exists. Please try another.");
                if (id > 0)
                {
                    if (_repo.UpdateCategory(id, name, serviceCenterId) == 0) return Fail("Category not found. Please reload the page.");
                }
                else _repo.InsertCategory(name, serviceCenterId);
                return Json(new { success = true, message = id > 0 ? "Category updated." : "Category saved." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save service category failed. Id={Id}", id);
                return Fail("Save failed. Please try again.");
            }
        }

        // =====================================================================================================
        // SERVICE TEAM
        // =====================================================================================================
        [HttpGet]
        public IActionResult ServiceTeam(int centerId = 0, int serviceCenterId = 0)
        {
            if (PageGuard("Service/ServiceTeam") is { } r) return r;
            var centers = _repo.GetAllBusinessCenters();
            centerId = PickCenter(centerId, centers);
            var serviceCenters = centerId > 0 ? _repo.GetServiceCentersByCenter(centerId) : new List<ServiceOption>();
            serviceCenterId = PickOption(serviceCenterId, serviceCenters);

            var vm = new ServiceSetupViewModel
            {
                CenterId = centerId,
                ServiceCenterId = serviceCenterId,
                BusinessCenters = centers,
                ServiceCenters = serviceCenters,
                TeamRows = serviceCenterId > 0 ? _repo.GetTeams(serviceCenterId) : new List<ServiceTeamModel>()
            };
            ViewData["Title"] = "Service Team";
            return View("ServiceTeam", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveServiceTeam(int id, string? name, int centerId, int serviceCenterId, string? supervisorStaffId)
        {
            if (!Allowed("Service/ServiceTeam")) return Fail("You are not allowed to change service teams.");
            name = (name ?? "").Trim();
            var errors = new List<string>();
            if (centerId <= 0) errors.Add("Select Business Center");
            if (!ServiceCenterInCenter(serviceCenterId, centerId)) errors.Add("Select Service Center");
            if (name.Length == 0) errors.Add("Enter Team Name");
            if (name.Length > 100) errors.Add("Team Name is too long (maximum 100 characters).");
            string staff = FirstPart(supervisorStaffId);
            var sup = staff.Length > 0 ? _dispatch.GetEmployee(staff) : null;
            if (sup == null) errors.Add("Enter Supervisor Staff ID (employee).");
            if (errors.Count > 0) return Fail(string.Join(" ", errors));

            try
            {
                if (_repo.TeamNameExists(name, serviceCenterId, id)) return Fail("This Team name already exists. Please try another.");
                if (id > 0)
                {
                    if (_repo.UpdateTeam(id, name, serviceCenterId, centerId, sup!.STAFFID, StaffId) == 0) return Fail("Team not found. Please reload the page.");
                }
                else _repo.InsertTeam(name, serviceCenterId, centerId, sup!.STAFFID, StaffId);
                return Json(new { success = true, message = id > 0 ? "Team updated." : "Team saved." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save service team failed. Id={Id}", id);
                return Fail("Save failed. Please try again.");
            }
        }

        // =====================================================================================================
        // ADD TEAM MEMBER
        // =====================================================================================================
        [HttpGet]
        public IActionResult AddTeamMember(int centerId = 0, int serviceCenterId = 0, int teamId = 0)
        {
            if (PageGuard("Service/AddTeamMember") is { } r) return r;
            var centers = _repo.GetAllBusinessCenters();
            centerId = PickCenter(centerId, centers);
            var serviceCenters = centerId > 0 ? _repo.GetServiceCentersByCenter(centerId) : new List<ServiceOption>();
            serviceCenterId = PickOption(serviceCenterId, serviceCenters);
            var teams = serviceCenterId > 0 ? _repo.GetTeamOptions(serviceCenterId) : new List<ServiceOption>();
            teamId = PickOption(teamId, teams);

            var vm = new ServiceSetupViewModel
            {
                CenterId = centerId,
                ServiceCenterId = serviceCenterId,
                TeamId = teamId,
                BusinessCenters = centers,
                ServiceCenters = serviceCenters,
                Teams = teams,
                Members = teamId > 0 ? _repo.GetTeamMembers(teamId) : new List<ServiceTeamMemberModel>()
            };
            ViewData["Title"] = "Team Member";
            return View("AddTeamMember", vm);
        }

        // team drop down of Add Team Member (old GetByServiceCenterIdOld)
        [HttpGet]
        public IActionResult Teams(int serviceCenterId) =>
            Json(serviceCenterId > 0 ? _repo.GetTeamOptions(serviceCenterId).Select(o => new { id = o.ID, name = o.NAME }) : Enumerable.Empty<object>());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveTeamMember(int id, int centerId, int serviceCenterId, int teamId, string? staffId)
        {
            if (!Allowed("Service/AddTeamMember")) return Fail("You are not allowed to change team members.");
            var errors = new List<string>();
            if (centerId <= 0) errors.Add("Select Business Center");
            if (!ServiceCenterInCenter(serviceCenterId, centerId)) errors.Add("Select Service Center");
            if (teamId <= 0 || !_repo.GetTeamOptions(serviceCenterId).Any(t => t.ID == teamId)) errors.Add("Select Service Team");
            string staff = FirstPart(staffId);
            var emp = staff.Length > 0 ? _dispatch.GetEmployee(staff) : null;
            if (emp == null) errors.Add("Enter Staff ID (employee).");
            if (errors.Count > 0) return Fail(string.Join(" ", errors));

            try
            {
                if (id > 0)
                {
                    if (_repo.UpdateTeamMember(id, teamId, serviceCenterId, centerId, emp!.STAFFID, UserId) == 0) return Fail("Member not found. Please reload the page.");
                }
                else _repo.InsertTeamMember(teamId, serviceCenterId, centerId, emp!.STAFFID, UserId);
                return Json(new { success = true, message = id > 0 ? "Member updated." : "Member saved." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save team member failed. Id={Id}", id);
                return Fail("Save failed. Please try again.");
            }
        }

        // =====================================================================================================
        // MACHINE
        // =====================================================================================================
        [HttpGet]
        public IActionResult Machine(int centerId = 0)
        {
            if (PageGuard("Service/Machine") is { } r) return r;
            var centers = _repo.GetAllBusinessCenters();
            centerId = PickCenter(centerId, centers);
            var vm = new ServiceSetupViewModel
            {
                CenterId = centerId,
                BusinessCenters = centers,
                Companies = _dispatch.GetCompanies(),
                Departments = _dispatch.GetDepartments(),
                DefaultCompanyId = UserCompanyId,
                Machines = centerId > 0 ? _repo.GetMachines(centerId) : new List<ServiceMachineModel>()
            };
            ViewData["Title"] = "Outlet / Machine";
            return View("Machine", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveMachine(int id, int centerId, int companyId, int departmentId, string? name, string? code)
        {
            if (!Allowed("Service/Machine")) return Fail("You are not allowed to change machines.");
            name = (name ?? "").Trim();
            code = (code ?? "").Trim();
            var errors = new List<string>();
            if (centerId <= 0) errors.Add("Select Business Center");
            if (companyId <= 0) errors.Add("Select Company");
            if (departmentId <= 0) errors.Add("Select Department");
            if (name.Length == 0) errors.Add("Enter Machine Name");
            if (name.Length > 100) errors.Add("Machine Name is too long (maximum 100 characters).");
            if (code.Length > 50) errors.Add("Machine Code is too long (maximum 50 characters).");
            if (errors.Count > 0) return Fail(string.Join(" ", errors));

            try
            {
                if (_repo.MachineNameExists(name, centerId, companyId, id)) return Fail("This Machine Name already exists. Please try another.");
                if (id > 0)
                {
                    if (_repo.UpdateMachine(id, centerId, companyId, departmentId, name, code, StaffId) == 0) return Fail("Machine not found. Please reload the page.");
                }
                else _repo.InsertMachine(centerId, companyId, departmentId, name, code, StaffId);
                return Json(new { success = true, message = id > 0 ? "Machine updated." : "Machine saved." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save machine failed. Id={Id}", id);
                return Fail("Save failed. Please try again.");
            }
        }

        // =====================================================================================================
        // ACCESSORIES CATEGORY
        // =====================================================================================================
        [HttpGet]
        public IActionResult AccessoriesCategory()
        {
            if (PageGuard("Service/AccessoriesCategory") is { } r) return r;
            ViewData["Title"] = "Accessories Category";
            return View("AccessoriesCategory", _repo.GetAccessoryCategories());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveAccessoriesCategory(int id, string? name)
        {
            if (!Allowed("Service/AccessoriesCategory")) return Fail("You are not allowed to change accessories categories.");
            name = (name ?? "").Trim();
            if (name.Length == 0) return Fail("Enter Category");
            if (name.Length > 100) return Fail("Category Name is too long (maximum 100 characters).");
            try
            {
                if (_repo.AccessoryCategoryNameExists(name, id)) return Fail("This category name already exists. Please try another.");
                if (id > 0)
                {
                    if (_repo.UpdateAccessoryCategory(id, name) == 0) return Fail("Category not found. Please reload the page.");
                }
                else _repo.InsertAccessoryCategory(name);
                return Json(new { success = true, message = id > 0 ? "Category updated." : "Category saved." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save accessories category failed. Id={Id}", id);
                return Fail("Save failed. Please try again.");
            }
        }

        // =====================================================================================================
        // ACCESSORIES
        // =====================================================================================================
        [HttpGet]
        public IActionResult Accessories(int centerId = 0, int serviceCenterId = 0)
        {
            if (PageGuard("Service/Accessories") is { } r) return r;
            var centers = _repo.GetAllBusinessCenters();
            centerId = PickCenter(centerId, centers);
            var serviceCenters = centerId > 0 ? _repo.GetServiceCentersByCenter(centerId) : new List<ServiceOption>();
            serviceCenterId = PickOption(serviceCenterId, serviceCenters);

            var vm = new ServiceSetupViewModel
            {
                CenterId = centerId,
                ServiceCenterId = serviceCenterId,
                BusinessCenters = centers,
                ServiceCenters = serviceCenters,
                AccessoryCategories = _repo.GetAccessoryCategories(),
                Accessories = serviceCenterId > 0 ? _repo.GetAccessories(serviceCenterId) : new List<ServiceAccessoryModel>()
            };
            ViewData["Title"] = "Accessories";
            return View("Accessories", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveAccessory(int id, int categoryId, string? name, string? stock, int centerId, int serviceCenterId)
        {
            if (!Allowed("Service/Accessories")) return Fail("You are not allowed to change accessories.");
            name = (name ?? "").Trim();
            var errors = new List<string>();
            if (centerId <= 0) errors.Add("Select Business Center");
            if (!ServiceCenterInCenter(serviceCenterId, centerId)) errors.Add("Select Service Center");
            if (categoryId <= 0) errors.Add("Select Category");
            if (!TryWhole(stock, 0, 999999999, out int stk)) errors.Add("Enter Stock (whole number).");
            if (name.Length == 0) errors.Add("Enter Accessories Name");
            if (name.Length > 200) errors.Add("Accessories Name is too long (maximum 200 characters).");
            if (errors.Count > 0) return Fail(string.Join(" ", errors));

            try
            {
                if (_repo.AccessoryNameExists(name, categoryId, serviceCenterId, id)) return Fail("This Accessories already exists. Please try another.");
                if (id > 0)
                {
                    if (_repo.UpdateAccessory(id, categoryId, name, stk, centerId, serviceCenterId, StaffId) == 0) return Fail("Accessory not found. Please reload the page.");
                }
                else _repo.InsertAccessory(categoryId, name, stk, centerId, serviceCenterId, StaffId);
                return Json(new { success = true, message = id > 0 ? "Accessory updated." : "Accessory saved." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save accessory failed. Id={Id}", id);
                return Fail("Save failed. Please try again.");
            }
        }
    }
}
