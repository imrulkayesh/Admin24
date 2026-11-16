// Controllers/Dispatch/DispatchController.Setup.cs
// Dispatch phase 6 - setup pages (port of YA Pages/Dispatch):
//   /Dispatch/IssueType      <- IssueType.aspx       (add / edit: name, returnable, external, personal)
//   /Dispatch/Approver       <- Approver.aspx        (per location: add / edit / delete approvers and their level)
//   /Dispatch/GateMan        <- AssignGateMan.aspx   (per location: add / edit gate men and their gate number)
//   /Dispatch/Steps          <- Steps.aspx           (add / edit DP_STEPS)
//   /Dispatch/Status         <- Status.aspx          (add / edit DP_STATUS names)
// Every page and every save needs the page in the user's role menu (old dalCommon.IsPermitted).
using AlphaCare.Models;
using Microsoft.AspNetCore.Mvc;

namespace QCMS.Controllers
{
    public partial class DispatchController
    {
        private bool Allowed(string url) => !string.IsNullOrWhiteSpace(StaffId) && _repo.HasMenuPermission(StaffId, url);

        private IActionResult? PageGuard(string url)
        {
            if (string.IsNullOrWhiteSpace(StaffId)) return RedirectToAction("Login", "Accounts");
            if (!Allowed(url)) return RedirectToAction("Denied", "Accounts");
            return null;
        }

        private static bool TryWhole(string? text, int min, int max, out int value) =>
            int.TryParse((text ?? "").Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value)
            && value >= min && value <= max;

        // =====================================================================================================
        // ISSUE TYPE
        // =====================================================================================================
        [HttpGet]
        public IActionResult IssueType()
        {
            if (PageGuard("Dispatch/IssueType") is { } r) return r;
            ViewData["Title"] = "Issue Type";
            return View("IssueType", _repo.GetIssueTypesForSetup());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveIssueType(int id, string? name, bool returnable, bool external, bool personal)
        {
            if (!Allowed("Dispatch/IssueType")) return Fail("You are not allowed to change issue types.");
            name = (name ?? "").Trim();
            if (name.Length == 0) return Fail("Enter Issue Type");
            if (name.Length > 50) return Fail("Issue Type is too long (maximum 50 characters).");
            try
            {
                if (id > 0 && !_repo.IssueTypeExists(id)) return Fail("Issue type not found. Please reload the page.");
                if (_repo.IssueTypeNameExists(name, id)) return Fail("This Type name already exists. Please try another.");
                if (id > 0) _repo.UpdateIssueType(id, name, returnable, external, personal, StaffId);
                else _repo.InsertIssueType(name, returnable, external, personal, StaffId);
                return Json(new { success = true, message = id > 0 ? "Issue type updated." : "Issue type saved." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save issue type failed. Id={Id}", id);
                return Fail("Save failed. Please try again.");
            }
        }

        // =====================================================================================================
        // APPROVER
        // =====================================================================================================
        [HttpGet]
        public IActionResult Approver(int centerId = 0)
        {
            if (PageGuard("Dispatch/Approver") is { } r) return r;
            var centers = _repo.GetBusinessCenters();
            // old page: the first location of the drop down; now the user's own location when it is in the list
            if (centerId <= 0) centerId = centers.Any(c => c.ID == UserCenterId) ? UserCenterId : centers.FirstOrDefault()?.ID ?? 0;

            var vm = new ApproverSetupViewModel
            {
                CenterId = centerId,
                BusinessCenters = centers,
                Departments = _repo.GetDepartments(),
                Companies = _repo.GetCompanies(),
                Rows = centerId > 0 ? _repo.GetApprovers(centerId) : new List<ApproverSetupModel>()
            };
            ViewData["Title"] = "Dispatch Approver";
            return View("Approver", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveApprover(int id, string? staffId, int companyId, int centerId, int departmentId, string? priority, bool isFinal)
        {
            if (!Allowed("Dispatch/Approver")) return Fail("You are not allowed to change approvers.");

            var errors = new List<string>();
            string staff = FirstPart(staffId);
            var emp = staff.Length > 0 ? _repo.GetEmployee(staff) : null;
            if (emp == null) errors.Add("Select the Approver (employee).");
            if (companyId <= 0) errors.Add("Select the Company.");
            if (centerId <= 0) errors.Add("Select the Location.");
            if (departmentId <= 0) errors.Add("Select the Department.");
            if (!TryWhole(priority, 1, 99, out int level)) errors.Add("Priority must be a whole number from 1 to 99.");
            if (errors.Count > 0) return Fail(string.Join(" ", errors));

            try
            {
                if (id > 0 && !_repo.ApproverRowExists(id)) return Fail("Approver not found. Please reload the page.");
                if (_repo.ApproverDuplicate(id, emp!.STAFFID, companyId, centerId, departmentId, level))
                    return Fail("This approver is already set for this company, location, department and priority.");

                if (id > 0) _repo.UpdateApprover(id, companyId, emp.STAFFID, level, centerId, departmentId, isFinal, UserId);
                else _repo.InsertApprover(companyId, emp.STAFFID, level, centerId, departmentId, isFinal, StaffId);
                return Json(new { success = true, message = id > 0 ? "Approver updated." : "Approver saved." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save approver failed. Id={Id}", id);
                return Fail("Save failed. Please try again.");
            }
        }

        // old btnDelete_Command (the only delete button that was switched on in the old setup pages)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteApprover(int id)
        {
            if (!Allowed("Dispatch/Approver")) return Fail("You are not allowed to change approvers.");
            try
            {
                return _repo.DeleteApprover(id) > 0 ? Json(new { success = true, message = "Approver deleted." })
                                                     : Fail("Approver not found. Please reload the page.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Delete approver failed. Id={Id}", id);
                return Fail("Delete failed. Please try again.");
            }
        }

        // =====================================================================================================
        // GATE MAN
        // =====================================================================================================
        [HttpGet]
        public IActionResult GateMan(int centerId = 0)
        {
            if (PageGuard("Dispatch/GateMan") is { } r) return r;
            var centers = _repo.GetBusinessCenters();
            if (centerId <= 0) centerId = centers.Any(c => c.ID == UserCenterId) ? UserCenterId : centers.FirstOrDefault()?.ID ?? 0;

            var vm = new GateManSetupViewModel
            {
                CenterId = centerId,
                BusinessCenters = centers,
                Rows = centerId > 0 ? _repo.GetGateMen(centerId) : new List<GateManSetupModel>()
            };
            ViewData["Title"] = "Assign Gate Man";
            return View("GateMan", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveGateMan(int id, string? staffId, int centerId, string? gateNo)
        {
            if (!Allowed("Dispatch/GateMan")) return Fail("You are not allowed to change gate men.");

            var errors = new List<string>();
            string staff = FirstPart(staffId);
            var emp = staff.Length > 0 ? _repo.GetEmployee(staff) : null;
            if (emp == null) errors.Add("Select the Gate Man (employee).");
            if (centerId <= 0) errors.Add("Select the Location.");
            if (!TryWhole(gateNo, 1, 999, out int gate)) errors.Add("Gate No must be a whole number from 1 to 999.");
            if (errors.Count > 0) return Fail(string.Join(" ", errors));

            try
            {
                if (id > 0 && !_repo.ActiveGateManExists(id)) return Fail("Gate man not found. Please reload the page.");
                if (_repo.GateManActiveElsewhere(emp!.STAFFID, id)) return Fail("This employee is already assigned to a gate.");

                if (id > 0) _repo.UpdateGateMan(id, emp.STAFFID, gate, centerId, UserId);
                else _repo.InsertGateMan(emp.STAFFID, gate, centerId, UserId);
                return Json(new { success = true, message = id > 0 ? "Gate man updated." : "Gate man saved." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save gate man failed. Id={Id}", id);
                return Fail("Save failed. Please try again.");
            }
        }

        // =====================================================================================================
        // STEPS
        // =====================================================================================================
        [HttpGet]
        public IActionResult Steps()
        {
            if (PageGuard("Dispatch/Steps") is { } r) return r;
            var vm = new StepSetupViewModel { Rows = _repo.GetSteps(), IssueTypes = _repo.GetIssueTypesForSetup() };
            ViewData["Title"] = "Dispatch Steps";
            return View("Steps", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveStep(int id, string? name, string? priority, int issueTypeId, bool isFinal)
        {
            if (!Allowed("Dispatch/Steps")) return Fail("You are not allowed to change steps.");
            name = (name ?? "").Trim();
            var errors = new List<string>();
            if (name.Length == 0) errors.Add("Enter Step");
            if (name.Length > 150) errors.Add("Step is too long (maximum 150 characters).");
            if (!TryWhole(priority, 1, 999, out int level)) errors.Add("Priority must be a whole number from 1 to 999.");
            if (issueTypeId <= 0) errors.Add("Select Issue Type");
            if (errors.Count > 0) return Fail(string.Join(" ", errors));

            try
            {
                if (id > 0 && !_repo.StepExists(id)) return Fail("Step not found. Please reload the page.");
                if (!_repo.IssueTypeExists(issueTypeId)) return Fail("Issue type not found. Please reload the page.");
                if (_repo.StepNameExists(name, issueTypeId, id)) return Fail("This Step name already exists. Please try another.");

                if (id > 0) _repo.UpdateStep(id, name, level, issueTypeId, isFinal, StaffId);
                else _repo.InsertStep(name, level, issueTypeId, isFinal, StaffId);
                return Json(new { success = true, message = id > 0 ? "Step updated." : "Step saved." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save step failed. Id={Id}", id);
                return Fail("Save failed. Please try again.");
            }
        }

        // =====================================================================================================
        // STATUS
        // =====================================================================================================
        [HttpGet]
        public IActionResult Status()
        {
            if (PageGuard("Dispatch/Status") is { } r) return r;
            ViewData["Title"] = "Dispatch Status";
            return View("Status", _repo.GetStatuses());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveStatus(int id, string? name)
        {
            if (!Allowed("Dispatch/Status")) return Fail("You are not allowed to change statuses.");
            name = (name ?? "").Trim();
            if (name.Length == 0) return Fail("Enter Status");
            if (name.Length > 50) return Fail("Status is too long (maximum 50 characters).");
            try
            {
                if (id > 0 && !_repo.StatusExists(id)) return Fail("Status not found. Please reload the page.");
                if (_repo.StatusNameExists(name, id)) return Fail("This Status name already exists. Please try another.");
                if (id > 0) _repo.UpdateStatus(id, name);
                else _repo.InsertStatus(name);
                return Json(new { success = true, message = id > 0 ? "Status updated." : "Status saved." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save status failed. Id={Id}", id);
                return Fail("Save failed. Please try again.");
            }
        }
    }
}
