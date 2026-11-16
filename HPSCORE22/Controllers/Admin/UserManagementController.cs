// Controllers/Admin/UserManagementController.cs
// UserManagement module (port of YA Pages/UserManagement):
//   /UserManagement/UserSetup        <- UserSetup.aspx         (create / edit user + employee data)
//   /UserManagement/UserStatusUpdate <- UserStatusUpdate.aspx  (same grid + Activate / Deactivate)
//   /UserManagement/UserRole         <- UserRole.aspx          (one user -> many roles, checkboxes)
//   /UserManagement/RoleSetup        <- RoleSetup.aspx         (UC_ROLESETUP CRUD)
//   /UserManagement/Module           <- Module.aspx            (UC_MODULE_COPY CRUD - Admin24's own menu)
//   /UserManagement/TaskManager      <- TaskManager.aspx       (UC_TASKMANAGER_COPY CRUD)
//   /UserManagement/TaskPermission   <- TaskPermission.aspx    (role -> checked menu tree, UC_TASKPERMISSION_COPY)
//   /UserManagement/ChangePassword   <- ChangePassword.aspx    (reset the legacy UC_USERS password)
// Excluded: ChangeStatus.aspx / Default.aspx (empty pages in YA).
using AlphaCare.Interface;
using AlphaCare.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Admin24.Controllers.Admin
{
    [Authorize]
    public class UserManagementController : Controller
    {
        private readonly IUserManagementRepository _repo;
        private readonly ILogger<UserManagementController> _logger;

        public UserManagementController(IUserManagementRepository repo, ILogger<UserManagementController> logger)
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

        // first line of the database error ("ORA-00001: unique constraint ... violated") - these are
        // admin-only configuration pages, so the admin sees why a save was refused instead of a blind
        // "please try again" (one of these hidden errors is how the Task Permission save bug went unseen)
        private static string DbReason(Exception ex)
        {
            string line = (ex.GetBaseException().Message ?? "").Split('\n')[0].Trim();
            if (line.Length > 180) line = line[..180] + "...";
            return line.Length > 0 ? "(" + line + ")" : "Please try again.";
        }

        private static string FirstPart(string? text) => (text ?? "").Trim().Split('-')[0].Trim();

        // =====================================================================================================
        // USER SETUP / USER STATUS UPDATE (same page; the second one adds the Activate / Deactivate buttons)
        // =====================================================================================================
        [HttpGet]
        public IActionResult UserSetup(int centerId = 0, string? search = null) =>
            UserPage("UserManagement/UserSetup", false, centerId, search);

        [HttpGet]
        public IActionResult UserStatusUpdate(int centerId = 0, string? search = null) =>
            UserPage("UserManagement/UserStatusUpdate", true, centerId, search);

        private IActionResult UserPage(string url, bool withStatusButtons, int centerId, string? search)
        {
            if (PageGuard(url) is { } guard) return guard;

            var vm = new UserSetupViewModel
            {
                WithStatusButtons = withStatusButtons,
                Search = (search ?? "").Trim(),
                Roles = _repo.GetRolesForUserSetup(_repo.IsSuperAdmin(StaffId)),
                Centers = _repo.Lookup(withStatusButtons ? "AllCenters" : "Centers"),   // old: UserSetup filtered LOCATIONSTATUS=1
                Companies = _repo.Lookup("Companies"),
                Departments = _repo.Lookup("Departments"),
                Designations = _repo.Lookup("Designations")
            };

            // old: opens with the user's own business center selected
            if (centerId <= 0) centerId = HttpContext.Session.GetInt32("BusinessCenterID") ?? 0;
            vm.CenterId = centerId;

            try
            {
                vm.Rows = vm.Search.Length > 0 ? _repo.SearchUsers(FirstPart(vm.Search))
                        : centerId > 0 ? _repo.GetUsers(centerId)
                        : new List<UserRow>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "User list failed.");
                TempData["msg"] = "The user list could not be loaded. Please try again.";
            }

            ViewData["Title"] = withStatusButtons ? "User Status Update" : "User Setup";
            return View("UserSetup", vm);
        }

        // fills the form when the Edit button of a row is pressed (old btnEdit_Command)
        [HttpGet]
        public IActionResult GetUser(string? staffId)
        {
            if (!Allowed("UserManagement/UserSetup") && !Allowed("UserManagement/UserStatusUpdate"))
                return Json(new { found = false });
            var u = _repo.GetUserById(FirstPart(staffId));
            return u == null ? Json(new { found = false }) : Json(new { found = true, user = u });
        }

        [HttpGet]
        public IActionResult SearchStaff(string? term)
        {
            if (string.IsNullOrWhiteSpace(term) || term.Trim().Length < 2) return Json(new List<string>());
            return Json(_repo.SearchStaffIds(term.Trim()));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveUser(SaveUserRequest r)
        {
            string url = r.IsEdit ? "UserManagement/UserStatusUpdate" : "UserManagement/UserSetup";
            if (!Allowed("UserManagement/UserSetup") && !Allowed(url)) return Fail("You are not allowed to change users.");

            r.StaffId = FirstPart(r.StaffId);
            r.Name = (r.Name ?? "").Trim();
            r.Mobile = (r.Mobile ?? "").Trim();
            r.Email = (r.Email ?? "").Trim();

            var errors = new List<string>();
            if (r.StaffId.Length == 0) errors.Add("Enter the Staff ID.");
            if (r.Name.Length == 0) errors.Add("Enter the Name.");
            if (r.RoleId <= 0) errors.Add("Select the Role.");
            if (r.CenterId <= 0) errors.Add("Select the Business Center.");
            if (r.CompanyId <= 0) errors.Add("Select the Company.");
            if (r.DepartmentId <= 0) errors.Add("Select the Department.");
            if (r.DesignationId <= 0) errors.Add("Select the Designation.");
            if (errors.Count > 0) return Fail(string.Join(" ", errors));

            try
            {
                if (!r.IsEdit)
                {
                    // old: duplicate staff id / username check first
                    if (_repo.UserExists(r.StaffId)) return Fail("This username/Staff ID already exists. Please try another.");
                    _repo.CreateUser(r, StaffId);
                    return Json(new { success = true, message = "User saved." });
                }
                if (!_repo.UserExists(r.StaffId)) return Fail("User not found. Please reload the page.");
                _repo.UpdateUser(r, StaffId);
                return Json(new { success = true, message = "User updated." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save user failed. Staff={Staff} Edit={Edit}", r.StaffId, r.IsEdit);
                return Fail(r.IsEdit ? "Update failed. Please try again." : "Save failed. Please try again.");
            }
        }

        // old btnActive_Command / btnInactive_Command (UserStatusUpdate page only)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SetUserStatus(string? staffId, bool active)
        {
            if (!Allowed("UserManagement/UserStatusUpdate")) return Fail("You are not allowed to change the user status.");
            string id = FirstPart(staffId);
            if (id.Length == 0) return Fail("Staff ID is missing.");
            try
            {
                return _repo.SetUserActive(id, active) > 0
                    ? Json(new { success = true, message = active ? "User activated successfully." : "User deactivated successfully." })
                    : Fail("User not found. Please reload the page.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "User status change failed. Staff={Staff}", id);
                return Fail("The status could not be changed. Please try again.");
            }
        }

        // =====================================================================================================
        // USER ROLE (old UserRole.aspx: one user -> many roles)
        // =====================================================================================================
        [HttpGet]
        public IActionResult UserRole()
        {
            if (PageGuard("UserManagement/UserRole") is { } guard) return guard;
            ViewData["Title"] = "User Role";
            return View(new UserRoleViewModel { Roles = _repo.GetAllRoles() });
        }

        // the checkboxes of the typed user (old tbxUserName_OnTextChanged)
        [HttpGet]
        public IActionResult GetUserRoles(string? staffId)
        {
            if (!Allowed("UserManagement/UserRole")) return Json(new { found = false });
            string id = FirstPart(staffId);
            string? name = id.Length > 0 ? _repo.GetEmployeeName(id) : null;
            if (name == null) return Json(new { found = false });
            return Json(new { found = true, staffId = id, name, roleIds = _repo.GetUserRoleIds(id) });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveUserRoles(string? staffId, List<int>? roleIds)
        {
            if (!Allowed("UserManagement/UserRole")) return Fail("You are not allowed to change user roles.");
            string id = FirstPart(staffId);
            if (id.Length == 0) return Fail("Enter the Staff ID.");
            if (_repo.GetEmployeeName(id) == null) return Fail("Employee not found.");
            if (roleIds == null || roleIds.Count == 0) return Fail("Please select at least one Role!");
            try
            {
                _repo.SaveUserRoles(id, roleIds, StaffId);
                return Json(new { success = true, message = "User roles saved." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save user roles failed. Staff={Staff}", id);
                return Fail("Save failed. Please try again.");
            }
        }

        // =====================================================================================================
        // ROLE SETUP (old RoleSetup.aspx)
        // =====================================================================================================
        [HttpGet]
        public IActionResult RoleSetup()
        {
            if (PageGuard("UserManagement/RoleSetup") is { } guard) return guard;
            ViewData["Title"] = "Role Setup";
            return View(new RoleSetupViewModel { Parents = _repo.GetRoleParents(), Rows = _repo.GetRoles() });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveRole(int id, string? name, string? description, int parentId)
        {
            if (!Allowed("UserManagement/RoleSetup")) return Fail("You are not allowed to change roles.");
            name = (name ?? "").Trim();
            description = (description ?? "").Trim();
            if (name.Length == 0) return Fail("Enter the Role name.");
            if (name.Length > 50) return Fail("The Role name is too long (maximum 50 characters).");
            if (description.Length > 150) return Fail("The Description is too long (maximum 150 characters).");
            try
            {
                if (_repo.RoleNameExists(name, id)) return Fail("This Role already exists. Please try another.");
                int priority = _repo.GetRolePriority(parentId) + 1;         // old: parent's priority + 1
                if (id > 0)
                {
                    if (_repo.GetRole(id) == null) return Fail("Role not found. Please reload the page.");
                    _repo.UpdateRole(id, name, description, parentId, priority, StaffId);
                    return Json(new { success = true, message = "Role updated." });
                }
                _repo.InsertRole(name, description, parentId, priority, StaffId);
                return Json(new { success = true, message = "Role saved." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save role failed. Id={Id}", id);
                return Fail("Save failed. Please try again.");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteRole(int id)
        {
            if (!Allowed("UserManagement/RoleSetup")) return Fail("You are not allowed to change roles.");
            try
            {
                return _repo.DeleteRole(id) > 0 ? Json(new { success = true, message = "Role deleted." })
                                                : Fail("Role not found. Please reload the page.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Delete role failed. Id={Id}", id);
                return Fail("Delete failed. The role is probably in use.");
            }
        }

        // =====================================================================================================
        // MODULE (old Module.aspx -> UC_MODULE_COPY, Admin24's own module list)
        // =====================================================================================================
        [HttpGet]
        public IActionResult Module()
        {
            if (PageGuard("UserManagement/Module") is { } guard) return guard;
            ViewData["Title"] = "Module";
            return View(_repo.GetModules());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveModule(int id, string? name, string? url, string? icon, int order)
        {
            if (!Allowed("UserManagement/Module")) return Fail("You are not allowed to change modules.");
            name = (name ?? "").Trim();
            url = (url ?? "").Trim();
            icon = (icon ?? "").Trim();
            if (name.Length == 0) return Fail("Enter the Module name.");
            if (name.Length > 50) return Fail("The Module name is too long (maximum 50 characters).");
            if (url.Length > 150) return Fail("The URL is too long (maximum 150 characters).");
            if (icon.Length > 50) return Fail("The Icon is too long (maximum 50 characters).");
            try
            {
                if (_repo.ModuleNameExists(name, id)) return Fail("This Module already exists. Please try another.");
                if (id > 0)
                {
                    if (_repo.GetModule(id) == null) return Fail("Module not found. Please reload the page.");
                    _repo.UpdateModule(id, name, order, url, icon, StaffId);
                    return Json(new { success = true, message = "Module updated." });
                }
                _repo.InsertModule(name, order, url, icon, StaffId);
                return Json(new { success = true, message = "Module saved." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save module failed. Id={Id}", id);
                return Fail("Save failed. " + DbReason(ex));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteModule(int id)
        {
            if (!Allowed("UserManagement/Module")) return Fail("You are not allowed to change modules.");
            try
            {
                return _repo.DeleteModule(id) > 0 ? Json(new { success = true, message = "Module deleted." })
                                                  : Fail("Module not found. Please reload the page.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Delete module failed. Id={Id}", id);
                return Fail("Delete failed. The module is probably in use.");
            }
        }

        // =====================================================================================================
        // TASK MANAGER (old TaskManager.aspx -> UC_TASKMANAGER_COPY, Admin24's own menu entries)
        // =====================================================================================================
        [HttpGet]
        public IActionResult TaskManager(int moduleId = 0)
        {
            if (PageGuard("UserManagement/TaskManager") is { } guard) return guard;

            var vm = new TaskSetupViewModel { Modules = _repo.GetModules().Select(m => new SetupOption { Value = m.ID.ToString(), Text = m.MODULENAME ?? "" }).ToList() };
            if (vm.Modules.All(m => m.Value != moduleId.ToString()))
                moduleId = int.TryParse(vm.Modules.FirstOrDefault()?.Value, out int first) ? first : 0;
            vm.ModuleId = moduleId;
            vm.Parents = _repo.GetRootTasks(moduleId);
            vm.Rows = moduleId > 0 ? _repo.GetTasks(moduleId) : new List<TaskRow>();

            ViewData["Title"] = "Task Manager";
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveTask(int id, string? titleEng, string? titleBan, string? url, int parentId, int order, string? icon, int moduleId)
        {
            if (!Allowed("UserManagement/TaskManager")) return Fail("You are not allowed to change menu entries.");
            titleEng = (titleEng ?? "").Trim();
            titleBan = (titleBan ?? "").Trim();
            url = (url ?? "").Trim();
            icon = (icon ?? "").Trim();
            if (titleEng.Length == 0) return Fail("Enter the Title (English).");
            if (titleEng.Length > 100 || titleBan.Length > 100) return Fail("The Title is too long (maximum 100 characters).");
            if (url.Length > 150) return Fail("The URL is too long (maximum 150 characters).");
            if (icon.Length > 50) return Fail("The Icon is too long (maximum 50 characters).");
            if (moduleId <= 0) return Fail("Select the Module.");
            try
            {
                var t = new TaskRow
                {
                    ID = id, TEXTENG = titleEng, TEXTBAN = titleBan.Length == 0 ? titleEng : titleBan,
                    URL = url, PARENTID = parentId, ORDERCOLUMN = order, ICON = icon, MODULEID = moduleId
                };
                if (id > 0)
                {
                    if (_repo.GetTask(id) == null) return Fail("Menu entry not found. Please reload the page.");
                    _repo.UpdateTask(t, StaffId);
                    return Json(new { success = true, message = "Menu entry updated." });
                }
                _repo.InsertTask(t, StaffId);
                return Json(new { success = true, message = "Menu entry saved." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save task failed. Id={Id}", id);
                return Fail("Save failed. " + DbReason(ex));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteTask(int id)
        {
            if (!Allowed("UserManagement/TaskManager")) return Fail("You are not allowed to change menu entries.");
            try
            {
                return _repo.DeleteTask(id) > 0 ? Json(new { success = true, message = "Menu entry deleted." })
                                                : Fail("Menu entry not found. Please reload the page.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Delete task failed. Id={Id}", id);
                return Fail("Delete failed. The entry is probably in use.");
            }
        }

        // =====================================================================================================
        // TASK PERMISSION (old TaskPermission.aspx -> UC_TASKPERMISSION_COPY)
        // =====================================================================================================
        [HttpGet]
        public IActionResult TaskPermission(int roleId = 0)
        {
            if (PageGuard("UserManagement/TaskPermission") is { } guard) return guard;

            var vm = new TaskPermissionViewModel { Roles = _repo.GetRoleParents(), RoleId = roleId };
            vm.Tree = _repo.GetTaskTree(roleId);

            ViewData["Title"] = "Task Permission";
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveTaskPermission(int roleId, List<int>? taskIds)
        {
            if (!Allowed("UserManagement/TaskPermission")) return Fail("You are not allowed to change permissions.");
            if (roleId <= 0) return Fail("Select the Role.");
            try
            {
                // old rule: a parent with any checked child is stored too (the menu needs the parent row)
                var ids = (taskIds ?? new List<int>()).ToHashSet();
                foreach (var root in _repo.GetTaskTree(0))
                    if (root.Children.Any(c => ids.Contains(c.ID))) ids.Add(root.ID);

                _repo.SaveTaskPermissions(roleId, ids.ToList(), StaffId);
                return Json(new { success = true, message = "Permissions saved." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save task permission failed. Role={RoleId}", roleId);
                return Fail("Save failed. " + DbReason(ex));
            }
        }

        // =====================================================================================================
        // CHANGE PASSWORD (old ChangePassword.aspx - resets the legacy UC_USERS password)
        // =====================================================================================================
        [HttpGet]
        public IActionResult ChangePassword(string? userId = null)
        {
            if (string.IsNullOrWhiteSpace(StaffId)) return RedirectToAction("Login", "Accounts");
            // old page: ?UserId=... resets that user, otherwise your own account (no menu permission check in YA either)
            ViewBag.UserId = string.IsNullOrWhiteSpace(userId) ? (HttpContext.Session.GetString("USERNAME") ?? "") : userId.Trim();
            ViewData["Title"] = "Change Password";
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveChangePassword(string? userId, string? password, string? confirm)
        {
            if (string.IsNullOrWhiteSpace(StaffId)) return Fail("Your session has expired. Please log in again.");
            userId = (userId ?? "").Trim();
            // old rule: only the match of the two boxes was checked
            if (string.IsNullOrEmpty(password) || password != confirm) return Fail("Password and confirm password does not match");
            if (userId.Length == 0) return Fail("The user is missing.");
            // resetting someone ELSE's password still needs the page permission (YA relied on the menu link)
            string self = HttpContext.Session.GetString("USERNAME") ?? "";
            if (!userId.Equals(self, StringComparison.OrdinalIgnoreCase) && !Allowed("UserManagement/ChangePassword"))
                return Fail("You are not allowed to reset another user's password.");
            try
            {
                return _repo.ChangePassword(userId, password) > 0
                    ? Json(new { success = true, message = "Password changed successfully." })
                    : Fail("User not found.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Change password failed for {User}", userId);
                return Fail("The password could not be changed. Please try again.");
            }
        }
    }
}
