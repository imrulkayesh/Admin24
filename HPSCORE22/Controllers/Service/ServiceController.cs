// Controllers/Service/ServiceController.cs
// Service module (port of YA Pages/Service). Request workflow:
//   /Service/NewService          <- NewService.aspx            (draft items + attachment, service center, category, machine, save)
//   /Service/Approver            <- Approver.aspx              (waiting for approval: approve / reject, rejected: approve again)
//   /Service/AssignService       <- AssignService.aspx         (un-assigned / assigned: assign + forward, instant service approval)
//   /Service/MyServiceList       <- MyServiceList.aspx         (service person: my services, revise, feedback)
//   /Service/MyServiceRequest    <- MyServiceRequest.aspx      (requester: my requests, waiting for feedback, rejected)
//   /Service/AddAccessories      <- AddAccessories.aspx        (team supervisor: requests that may need accessories)
//   /Service/InstantService      <- InstantService.aspx        (service person logs a job for an employee, assigned to himself)
//   /Service/ViewService/5       <- View.aspx + ServiceRemarks.aspx + SEComplete.aspx + Ratings.aspx + Details.aspx
//                                   + InstantServiceDetails.aspx (one detail page; the actions follow the old list rules)
// Setup pages: ServiceController.Setup.cs. Views: Views/Service/*.cshtml. Data: IServiceRepository (same SC_* tables as YA).
// Forward reuses the Dispatch module (IDispatchRepository): the user's dispatch draft is filled with the service items and sent.
using AlphaCare.Interface;
using AlphaCare.Models;
using AlphaCare.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;

namespace QCMS.Controllers
{
    [Authorize]
    public partial class ServiceController : Controller
    {
        private const int RemarksMax = 150;                  // SC_REQUESTREMARKS.REMARKS / SC_SERVICEDETAILS.REMARKS = NVARCHAR2(150)
        private const int SubjectMax = 100;                  // SC_SERVICEDETAILS.SUBJECT = NVARCHAR2(100)

        private readonly IServiceRepository _repo;
        private readonly IDispatchRepository _dispatch;
        private readonly ServiceAttachmentStore _files;
        private readonly ILogger<ServiceController> _logger;

        public ServiceController(IServiceRepository repo, IDispatchRepository dispatch, ServiceAttachmentStore files, ILogger<ServiceController> logger)
        {
            _repo = repo;
            _dispatch = dispatch;
            _files = files;
            _logger = logger;
        }

        private string StaffId => HttpContext.Session.GetString("STAFFID") ?? "";
        private string UserId => HttpContext.Session.GetString("USERID") ?? "";
        private int UserCenterId => HttpContext.Session.GetInt32("BusinessCenterID") ?? 0;
        private int UserCompanyId => HttpContext.Session.GetInt32("CompanyID") ?? 0;
        private int UserDepartmentId => HttpContext.Session.GetInt32("DepartmentID") ?? 0;
        private int RoleId => HttpContext.Session.GetInt32("RoleId") ?? 0;

        private bool Allowed(string url) => !string.IsNullOrWhiteSpace(StaffId) && _repo.HasMenuPermission(StaffId, url);

        private IActionResult? PageGuard(string url)
        {
            if (string.IsNullOrWhiteSpace(StaffId)) return RedirectToAction("Login", "Accounts");
            if (!Allowed(url)) return RedirectToAction("Denied", "Accounts");
            return null;
        }

        private bool In(ServiceList list, long id) => _repo.InList(list, id, StaffId, RoleId);

        // =====================================================================================================
        // NEW SERVICE  /Service/NewService      (old NewService.aspx)
        // =====================================================================================================
        [HttpGet]
        public IActionResult NewService()
        {
            if (PageGuard("Service/NewService") is { } r) return r;

            long draftId = _repo.GetOrCreateDraft(StaffId);
            var vm = new NewServiceViewModel
            {
                RequestId = draftId,
                CenterId = UserCenterId,
                BusinessCenters = _repo.GetAllBusinessCenters(),
                Machines = _repo.GetMachinesFor(UserCenterId, UserCompanyId),
                Items = _repo.GetItems(draftId),
                AttachmentsEnabled = _files.Enabled
            };
            ViewData["Title"] = "New Service";
            return View("NewService", vm);
        }

        // ---------- lookups (old AutoPostBack drop downs and AutoComplete web methods) ----------
        [HttpGet]
        public IActionResult ServiceCenters(int centerId) =>
            Json(centerId > 0 ? _repo.GetServiceCentersByCenter(centerId).Select(o => new { id = o.ID, name = o.NAME }) : Enumerable.Empty<object>());

        [HttpGet]
        public IActionResult Categories(int serviceCenterId) =>
            Json(serviceCenterId > 0 ? _repo.GetCategoriesByServiceCenter(serviceCenterId).Select(o => new { id = o.ID, name = o.NAME }) : Enumerable.Empty<object>());

        [HttpGet]
        public IActionResult SearchSubject(string term)
        {
            if (string.IsNullOrWhiteSpace(term) || term.Trim().Length < 2) return Json(new List<string>());
            return Json(_repo.SearchSubjects(term));
        }

        [HttpGet]
        public IActionResult SearchEmployee(string term)
        {
            if (string.IsNullOrWhiteSpace(term) || term.Trim().Length < 2) return Json(new List<string>());
            return Json(_dispatch.SearchEmployees(term));
        }

        [HttpGet]
        public IActionResult GetEmployee(string staffId)
        {
            string id = FirstPart(staffId);
            var e = id.Length > 0 ? _dispatch.GetEmployee(id) : null;
            return e == null
                ? Json(new { found = false })
                : Json(new { found = true, staffId = e.STAFFID, name = e.EMPLOYEENAME, mobile = e.OFFICEMOBILE, email = e.OFFICEMAIL,
                             centerId = e.BUSINESSCENTERID, departmentId = e.DEPARTMENTID, designation = e.DESIGNATIONNAME,
                             centerCode = e.CENTERCODE, departmentCode = e.DEPARTMENTCODE });
        }

        [HttpGet]
        public IActionResult GetEmployees(int centerId, int departmentId)
        {
            if (centerId <= 0 || departmentId <= 0) return Json(new List<object>());
            return Json(_dispatch.GetEmployeesByDepartment(centerId, departmentId)
                                 .Select(e => new { staffId = e.STAFFID, name = e.EMPLOYEENAME, mobile = e.OFFICEMOBILE }));
        }

        [HttpGet]
        public IActionResult AccessoryOptions(int categoryId) =>
            Json(categoryId > 0 ? _repo.GetAccessoriesForSupervisor(categoryId, StaffId).Select(o => new { id = o.ID, name = o.NAME }) : Enumerable.Empty<object>());

        // ---------- draft items (old btnAdd / btnEdit) ----------
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(ServiceAttachmentStore.MaxBytes + 1024 * 1024)]
        public async Task<IActionResult> AddItem(long requestId, string? subject, string? quantity, string? remarks, IFormFile? file)
        {
            subject = (subject ?? "").Trim();
            remarks = (remarks ?? "").Trim();
            if (subject.Length == 0) return Fail("Subject Is Required");
            if (subject.Length > SubjectMax) return Fail($"Subject is too long (maximum {SubjectMax} characters).");
            if (remarks.Length > RemarksMax) return Fail($"Remarks is too long (maximum {RemarksMax} characters).");
            if (!TryQuantity(quantity, out int qty)) return Fail("Quantity must be a whole number greater than 0.");
            if (!_repo.IsOwnDraft(requestId, StaffId)) return Fail("This request cannot be changed any more. Please reload the page.");

            try
            {
                // the old code kept only the quantity when the subject was already in the list -> save the file only for a new item
                string attachment = "";
                bool merges = _repo.DraftHasItem(requestId, subject);
                if (file != null && file.Length > 0 && !merges)
                {
                    var saved = await _files.SaveAsync(file, requestId);
                    if (saved.Error != null) return Fail(saved.Error);
                    attachment = saved.FileName!;
                }

                int result = _repo.AddDraftItem(requestId, StaffId, subject, qty, remarks, attachment);
                if (result == 0) return Fail("This request cannot be changed any more. Please reload the page.");
                string msg = result == 2
                    ? "The subject was already in the list - quantity added." + (file != null && file.Length > 0 ? " (The file was not attached.)" : "")
                    : "Item added.";
                return Json(new { success = true, message = msg, items = ItemRows(requestId) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Add service item failed. RequestId={RequestId}", requestId);
                return Fail("Item could not be added. Please try again.");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateItem(long id, string? quantity, string? remarks)
        {
            remarks = (remarks ?? "").Trim();
            if (!TryQuantity(quantity, out int qty)) return Fail("Quantity must be a whole number greater than 0.");
            if (remarks.Length > RemarksMax) return Fail($"Remarks is too long (maximum {RemarksMax} characters).");
            var item = _repo.GetItem(id);
            if (item == null) return Fail("Item not found.");
            try
            {
                if (_repo.UpdateDraftItem(id, StaffId, qty, remarks) == 0) return Fail("This item cannot be changed.");
                return Json(new { success = true, message = "Item updated.", items = ItemRows(item.REQUESTID) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Update service item failed. ItemId={ItemId}", id);
                return Fail("Item could not be updated. Please try again.");
            }
        }

        // ---------- SAVE (old btnSave_Click -> dalServiceRequest.Update) ----------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveRequest(long requestId, int serviceCenterId, int categoryId, int machineId)
        {
            if (!Allowed("Service/NewService")) return Fail("You are not allowed to create service requests.");
            if (serviceCenterId <= 0) return Fail("Select Service Center");
            if (categoryId <= 0) return Fail("Select Service Category");
            if (!_repo.GetCategoriesByServiceCenter(serviceCenterId).Any(c => c.ID == categoryId)) return Fail("Select Service Category");
            if (machineId < 0 || (machineId > 0 && !_repo.GetMachinesFor(UserCenterId, UserCompanyId).Any(m => m.ID == machineId)))
                return Fail("Select a valid Outlet / Machine.");

            try
            {
                string? serviceId = _repo.SubmitRequest(requestId, StaffId, serviceCenterId, categoryId, machineId, UserCenterId, UserDepartmentId);
                if (serviceId == null) return Fail("This request cannot be saved (it was already saved). Please reload the page.");
                return Json(new { success = true, serviceId, message = $"Service request {serviceId} saved and sent for approval." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save service request failed. RequestId={RequestId}", requestId);
                return Fail("Save failed. Please try again.");
            }
        }

        // attachment of an item: the owner of the draft, or somebody who may see the request
        [HttpGet]
        public IActionResult Attachment(long id)
        {
            var item = _repo.GetItem(id);
            if (item == null || string.IsNullOrWhiteSpace(item.ATTACHMENT)) return NotFound();
            if (!_repo.IsOwnDraft(item.REQUESTID, StaffId) && !_repo.CanView(item.REQUESTID, StaffId)
                && !(Allowed(ReportRequestUrl) && _repo.GetRequestDetails(item.REQUESTID) != null))     // report users: submitted requests only
                return RedirectToAction("Denied", "Accounts");

            var f = _files.Find(item.ATTACHMENT);
            if (f == null) return NotFound(_files.Enabled ? "The attached file was not found." : "Attachments are not configured on this server.");
            return PhysicalFile(f.Value.Path, f.Value.ContentType, Path.GetFileName(f.Value.Path));
        }

        // =====================================================================================================
        // APPROVER  /Service/Approver      (old Approver.aspx)
        // =====================================================================================================
        [HttpGet]
        public IActionResult Approver()
        {
            if (PageGuard("Service/Approver") is { } r) return r;
            var vm = new ServiceListPageViewModel
            {
                Title = "Service Approval",
                Icon = "fa-user-check",
                Sections =
                {
                    Section("pending", "Waiting for Approval", "fa-hourglass-half", "#f59e0b", ServiceList.ApprovalPending, "approve", "reject"),
                    Section("rejected", "Rejected Request", "fa-circle-xmark", "#dc2626", ServiceList.ApprovalRejected, "approve")
                }
            };
            ViewData["Title"] = $"({vm.Sections[0].Rows.Count}) Service Approval";
            return View("ServiceList", vm);
        }

        // old btnNewEdit / btnApproveEdit (+ ServiceRemarks "Approve")
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Approve(long id, string? remarks)
        {
            if (!TryRemarks(remarks, false, out string text, out var bad)) return bad!;
            if (!In(ServiceList.ApprovalPending, id) && !In(ServiceList.ApprovalRejected, id))
                return Fail("This request is not waiting for your approval any more.");
            return Run(id, "Request approved.", () => { _repo.ApproveRequest(id, StaffId); Remark(id, text); });
        }

        // old btnReject (list) / ServiceRemarks "Reject" before approval
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Reject(long id, string? remarks)
        {
            if (!TryRemarks(remarks, false, out string text, out var bad)) return bad!;
            if (!In(ServiceList.ApprovalPending, id)) return Fail("This request is not waiting for your approval any more.");
            return Run(id, "Request rejected.", () => { _repo.RejectRequest(id, StaffId); Remark(id, text); });
        }

        // =====================================================================================================
        // ASSIGN SERVICE  /Service/AssignService      (old AssignService.aspx)
        // =====================================================================================================
        [HttpGet]
        public IActionResult AssignService()
        {
            if (PageGuard("Service/AssignService") is { } r) return r;
            var vm = new ServiceListPageViewModel
            {
                Title = "Assign Service",
                Icon = "fa-people-arrows",
                Sections =
                {
                    Section("unassigned", "Un-Assigned Service Request", "fa-inbox", "#f59e0b", ServiceList.AdminUnassigned, "assign", "forward"),
                    Section("instant", "Instant Service Approval", "fa-bolt", "#7c3aed", ServiceList.InstantToApprove, "instant"),
                    Section("assigned", "Assigned Service Request", "fa-user-gear", "#16a34a", ServiceList.AdminAssigned, "change", "forward")
                },
                AssignServiceCenters = _repo.GetServiceCentersForAssign(StaffId, RoleId),
                BusinessCenters = _repo.GetAllBusinessCenters(),
                IssueTypes = _dispatch.GetIssueTypes()
            };
            vm.Sections[1].ShowServiceman = vm.Sections[2].ShowServiceman = true;
            ViewData["Title"] = $"({vm.Sections[0].Rows.Count}) Assign Service";
            return View("ServiceList", vm);
        }

        [HttpGet]
        public IActionResult AssignTeams(int serviceCenterId) =>
            Json(serviceCenterId > 0 ? _repo.GetTeamsForAssign(serviceCenterId, RoleId, UserId).Select(o => new { id = o.ID, name = o.NAME }) : Enumerable.Empty<object>());

        [HttpGet]
        public IActionResult AssignMembers(int serviceCenterId, int teamId) =>
            Json(serviceCenterId > 0 && teamId > 0
                ? _repo.GetMembersForAssign(serviceCenterId, teamId, RoleId).Select(o => new { id = o.STAFFID, name = o.EMPLOYEENAME + " (" + o.STAFFID + ")" })
                : Enumerable.Empty<object>());

        private bool CanManage(long id) => In(ServiceList.AdminUnassigned, id) || In(ServiceList.AdminAssigned, id);

        // old btnAssign_Click (mpAssign)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Assign(long id, int serviceCenterId, int teamId, string? memberStaffId, string? startDate, string? startHour, string? endDate, string? endHour)
        {
            if (!Allowed("Service/AssignService")) return Fail("You are not allowed to assign services.");
            var errors = new List<string>();
            if (serviceCenterId <= 0) errors.Add("Select Service Center");
            if (teamId <= 0) errors.Add("Select Team");
            if (string.IsNullOrWhiteSpace(memberStaffId)) errors.Add("Select Team Member");
            if (!TryDate(startDate, out DateTime sd)) errors.Add("Enter Start Date");
            if (!TryDate(endDate, out DateTime ed)) errors.Add("Enter End Date");
            if (!(TryHour(startHour, out string sh) & TryHour(endHour, out string eh))) errors.Add("Select the Time.");   // & = both always parsed
            if (errors.Count > 0) return Fail(string.Join(" ", errors));

            if (!CanManage(id)) return Fail("This request is not in your assign list any more.");
            if (!_repo.GetServiceCentersForAssign(StaffId, RoleId).Any(s => s.ID == serviceCenterId)) return Fail("Select Service Center");
            if (!_repo.GetTeamsForAssign(serviceCenterId, RoleId, UserId).Any(t => t.ID == teamId)) return Fail("Select Team");
            string member = memberStaffId!.Trim();
            if (!_repo.GetMembersForAssign(serviceCenterId, teamId, RoleId).Any(m => m.STAFFID == member)) return Fail("Select Team Member");

            return Run(id, "Service assigned.", () => _repo.AssignService(id, teamId, member, sd, sh, ed, eh));
        }

        // old btnForward_Click (mpForward): the service items go to the admin of another service center as a dispatch
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Forward(long id, int centerId, int serviceCenterId, int issueTypeId, bool directSend)
        {
            if (!Allowed("Service/AssignService")) return Fail("You are not allowed to forward services.");
            if (centerId <= 0) return Fail("Select Business Center");
            if (serviceCenterId <= 0 || !_repo.GetServiceCentersByCenter(centerId).Any(s => s.ID == serviceCenterId)) return Fail("Select Service Center");
            var issue = issueTypeId > 0 ? _dispatch.GetIssueType(issueTypeId) : null;
            if (issue == null) return Fail("Select Issue Type");
            if (!CanManage(id)) return Fail("This request is not in your assign list any more.");

            var admin = _repo.GetServiceCenterAdmin(serviceCenterId);
            if (admin == null) return Fail("The selected service center has no center admin (employee) - it cannot receive a dispatch.");
            var items = _repo.GetItems(id);
            if (items.Count == 0) return Fail("This request has no item to forward.");
            var sender = _dispatch.GetEmployee(StaffId);
            if (sender == null) return Fail("Your employee record was not found.");

            try
            {
                // old: CheckDispatch -> DeleteDispatchItems -> dalDispatch.Update -> InsertDispatchItems -> ForwardService
                long draftId = _dispatch.GetOrCreateDraft(StaffId);
                _dispatch.ClearDraftItems(draftId, StaffId);
                foreach (var it in items)
                {
                    string name = (it.SUBJECT ?? "").Trim();
                    if (name.Length == 0) continue;
                    _dispatch.AddDraftItem(draftId, StaffId, name, it.QUANTITY ?? 1, "", it.REMARKS ?? "", "");
                }

                var m = new SubmitDispatchModel
                {
                    DispatchId = draftId,
                    IssueTypeId = issue.ID,
                    ReceiverStaffId = admin.STAFFID,
                    SenderStaffId = StaffId,
                    ReceiverName = admin.EMPLOYEENAME ?? "",
                    ReceiverMobile = admin.OFFICEMOBILE ?? "",
                    ReceiverAddress = admin.ADDRESS ?? "",
                    ReceiverDepartment = admin.DEPARTMENTCODE ?? "",
                    ReceiverCenter = admin.CENTERCODE ?? "",
                    SenderDepartmentId = UserDepartmentId,
                    SenderCenterId = UserCenterId,
                    ReceiverDepartmentId = admin.DEPARTMENTID,
                    ReceiverCenterId = admin.BUSINESSCENTERID,
                    DirectSend = directSend ? 1 : 0,
                    SenderName = sender.EMPLOYEENAME ?? "",
                    SenderMobile = sender.OFFICEMOBILE ?? ""
                };
                string? txId = _dispatch.SubmitDispatch(m, StaffId, UserId, UserCenterId);
                if (txId == null) return Fail("The dispatch could not be created. Please check Create Dispatch and try again.");

                _repo.ForwardService(id, draftId, serviceCenterId);
                return Json(new { success = true, message = $"Service forwarded with dispatch {txId}." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Forward service failed. RequestId={RequestId}", id);
                return Fail("Forward failed. Please try again.");
            }
        }

        // old btnInstantApprove (list) / InstantServiceDetails "Approve"
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ApproveInstant(long id, string? remarks)
        {
            if (!TryRemarks(remarks, false, out string text, out var bad)) return bad!;
            if (!In(ServiceList.InstantToApprove, id)) return Fail("This instant service is not waiting for your approval any more.");
            return Run(id, "Instant service approved.", () => { _repo.ApproveInstant(id, StaffId); Remark(id, text); });
        }

        // =====================================================================================================
        // MY SERVICE LIST  /Service/MyServiceList      (old MyServiceList.aspx - service person)
        // =====================================================================================================
        [HttpGet]
        public IActionResult MyServiceList()
        {
            if (PageGuard("Service/MyServiceList") is { } r) return r;
            var vm = new ServiceListPageViewModel
            {
                Title = "My Service List",
                Icon = "fa-screwdriver-wrench",
                Sections =
                {
                    Section("tasks", "My Services", "fa-list-check", "#4f46e5", ServiceList.MyTasks, "complete"),
                    Section("revise", "Waiting for Revise", "fa-rotate", "#f59e0b", ServiceList.MyRevise, "revised"),
                    Section("feedback", "Waiting For Feedback", "fa-star-half-stroke", "#0891b2", ServiceList.MyFeedbackToGive, "rate", "feedback")
                }
            };
            ViewData["Title"] = $"({vm.Sections[0].Rows.Count}) My Service List";
            return View("ServiceList", vm);
        }

        // old btnReviseEdit ("Revised"): 4 -> 2
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult MarkRevised(long id)
        {
            if (!In(ServiceList.MyRevise, id)) return Fail("This service is not waiting for your revision any more.");
            return Run(id, "Service marked as revised.", () => _repo.UpdateStatus(id, ServiceStatus.WaitingForUserFeedback, StaffId));
        }

        // old btnFeedbackEdit ("Feedback"): 3 -> 5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult GiveFeedback(long id)
        {
            if (!In(ServiceList.MyFeedbackToGive, id)) return Fail("This service is not waiting for your feedback any more.");
            return Run(id, "Service completed.", () => _repo.UpdateStatus(id, ServiceStatus.Completed, StaffId));
        }

        // =====================================================================================================
        // MY SERVICE REQUEST  /Service/MyServiceRequest      (old MyServiceRequest.aspx - requester)
        // =====================================================================================================
        [HttpGet]
        public IActionResult MyServiceRequest()
        {
            if (PageGuard("Service/MyServiceRequest") is { } r) return r;
            var vm = new ServiceListPageViewModel
            {
                Title = "My Service Request",
                Icon = "fa-clipboard-list",
                Sections =
                {
                    Section("mine", "My Requests", "fa-clipboard-list", "#4f46e5", ServiceList.MyRequests),
                    Section("feedback", "Waiting for Feedback", "fa-star-half-stroke", "#0891b2", ServiceList.MyWaitingFeedback, "accept", "revise", "rate"),
                    Section("rejected", "Rejected", "fa-circle-xmark", "#dc2626", ServiceList.MyRejected)
                }
            };
            vm.Sections[1].ShowServiceman = true;
            ViewData["Title"] = "My Service Request";
            return View("ServiceList", vm);
        }

        // old btnReviseEdit ("Accept"): 2 -> 3
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Accept(long id)
        {
            if (!In(ServiceList.MyWaitingFeedback, id)) return Fail("This service is not waiting for your feedback any more.");
            return Run(id, "Service accepted.", () => _repo.UpdateStatus(id, ServiceStatus.WaitingForServicePersonFeedback, StaffId));
        }

        // =====================================================================================================
        // ADD ACCESSORIES  /Service/AddAccessories      (old AddAccessories.aspx - team supervisor; login only, as in YA)
        // =====================================================================================================
        [HttpGet]
        public IActionResult AddAccessories()
        {
            if (string.IsNullOrWhiteSpace(StaffId)) return RedirectToAction("Login", "Accounts");
            var vm = new ServiceListPageViewModel
            {
                Title = "Add Accessories",
                Icon = "fa-toolbox",
                Sections = { Section("services", "My Services", "fa-toolbox", "#4f46e5", ServiceList.SupervisorServices, "accessories") },
                Note = "Open a request to add the accessories it needs."
            };
            vm.Sections[0].ShowServiceman = true;
            ViewData["Title"] = "Add Accessories";
            return View("ServiceList", vm);
        }

        // =====================================================================================================
        // DETAILS  /Service/ViewService/5
        // =====================================================================================================
        [HttpGet]
        public IActionResult ViewService(long id)
        {
            if (id <= 0) return NotFound();
            // people on the request, or (read only) users of the Service Request report - its Service ID opens this page (old View.aspx)
            bool participant = _repo.CanView(id, StaffId);
            if (!participant && !Allowed(ReportRequestUrl)) return RedirectToAction("Denied", "Accounts");
            var d = _repo.GetRequestDetails(id);
            if (d == null) return NotFound();

            var vm = new ServiceDetailsViewModel
            {
                Request = d,
                Approver = d.ISAPPROVED > 0 ? _repo.GetApproverDetails(id) : null,
                Assign = _repo.GetActiveAssign(id),
                Items = _repo.GetItems(id),
                Accessories = _repo.GetRequiredAccessories(id),
                Remarks = _repo.GetRemarks(id),
                AttachmentsEnabled = _files.Enabled,
                CanRemark = participant
            };
            vm.CanApprove = In(ServiceList.ApprovalPending, id) || In(ServiceList.ApprovalRejected, id);
            vm.CanRejectApproval = In(ServiceList.ApprovalPending, id);
            vm.CanAccept = In(ServiceList.MyWaitingFeedback, id);
            vm.CanComplete = In(ServiceList.MyTasks, id);
            vm.CanRateServiceman = In(ServiceList.MyFeedbackToGive, id);
            vm.CanApproveInstant = In(ServiceList.InstantToApprove, id);
            vm.CanEditAccessories = In(ServiceList.SupervisorServices, id);
            vm.CanDone = CanDone(d, vm.CanComplete);
            if (vm.CanEditAccessories)
            {
                vm.AccessoryCategories = _repo.GetAccessoryCategories();
                vm.Currencies = _dispatch.GetCurrencies();
            }
            ViewData["Title"] = "Service " + d.SERVICEID;
            return View("ViewService", vm);
        }

        // old ServiceRemarks.aspx "Done": the service side moves the request one step (3 -> 5, 4 -> 2, otherwise + 1).
        // (YA read ViewState["Status"], which was never filled, so it always wrote STATUS = 1 - see MIGRATION_SERVICE.md)
        private bool CanDone(ServiceRequestDetailModel d, bool canComplete) =>
            d.ISAPPROVED == 1 && d.STATUS >= 1 && d.STATUS <= 4 && d.FROMSTAFFID != StaffId && !canComplete
            && (In(ServiceList.AdminAssigned, d.ID) || In(ServiceList.AdminUnassigned, d.ID)
                || In(ServiceList.MyRevise, d.ID) || In(ServiceList.MyFeedbackToGive, d.ID));

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Done(long id, string? remarks)
        {
            if (!TryRemarks(remarks, false, out string text, out var bad)) return bad!;
            var d = _repo.GetRequestDetails(id);
            if (d == null || !CanDone(d, In(ServiceList.MyTasks, id))) return Fail("This request cannot be moved on by you.");
            int next = d.STATUS == 3 ? ServiceStatus.Completed : d.STATUS == 4 ? ServiceStatus.WaitingForUserFeedback : d.STATUS + 1;
            return Run(id, "Status updated: " + ServiceStatus.Text(next) + ".", () => { _repo.UpdateStatus(id, next, StaffId); Remark(id, text); });
        }

        // old ServiceRemarks "Reject" after approval (requester, STATUS 2): back to the service person, NOOFREVISION + 1
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Revise(long id, string? remarks)
        {
            if (!TryRemarks(remarks, true, out string text, out var bad)) return bad!;
            if (!In(ServiceList.MyWaitingFeedback, id)) return Fail("This service is not waiting for your feedback any more.");
            return Run(id, "Service sent back for revision.", () => { _repo.ReviseRequest(id); Remark(id, text); });
        }

        // old SEComplete.aspx "Done": STATUS 2 + machine stop / start time
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Complete(long id, string? stopTime, string? startTime, string? remarks)
        {
            if (!TryRemarks(remarks, false, out string text, out var bad)) return bad!;
            DateTime? stop = null, start = null;
            if (!string.IsNullOrWhiteSpace(stopTime)) { if (!TryDateTime(stopTime, out var v)) return Fail("Machine Stop Time is not a valid date / time."); stop = v; }
            if (!string.IsNullOrWhiteSpace(startTime)) { if (!TryDateTime(startTime, out var v)) return Fail("Machine Start Time is not a valid date / time."); start = v; }
            if (!In(ServiceList.MyTasks, id)) return Fail("This service is not in your service list any more.");
            return Run(id, "Service done - waiting for user feedback.", () => { _repo.CompleteService(id, stop, start, StaffId); Remark(id, text); });
        }

        // old Ratings.aspx: STATUS 2 -> requester rates the service (-> 3), STATUS 3 -> service person rates the user (-> 5)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Rate(long id, int rating)
        {
            if (rating < 1 || rating > 5) return Fail("Choose 1 to 5 stars.");
            if (In(ServiceList.MyWaitingFeedback, id))
                return Run(id, "Thank you - rating saved.", () => _repo.RateByUser(id, rating, StaffId));
            if (In(ServiceList.MyFeedbackToGive, id))
                return Run(id, "Thank you - rating saved. Service completed.", () => _repo.RateByServiceman(id, rating, StaffId));
            return Fail("This service is not waiting for your rating any more.");
        }

        // old "Update Remarks"
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveRemarks(long id, string? remarks)
        {
            if (!TryRemarks(remarks, true, out string text, out var bad)) return bad!;
            if (!_repo.CanView(id, StaffId)) return Fail("You cannot add remarks to this request.");
            return Run(id, "Remarks saved.", () => _repo.AddRemark(id, StaffId, text));
        }

        // =====================================================================================================
        // REQUIRED ACCESSORIES  (old Details.aspx for the team supervisor, InstantService.aspx for the own draft)
        // =====================================================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveRequiredAccessory(long id, long requestId, int categoryId, int accessoryId, string? quantity, string? price, int currencyId, bool isNew)
        {
            bool draft = _repo.IsOwnDraft(requestId, StaffId);
            bool supervisor = !draft && In(ServiceList.SupervisorServices, requestId);
            if (!draft && !supervisor) return Fail("You cannot change the accessories of this request.");

            var errors = new List<string>();
            if (categoryId <= 0) errors.Add("Select Category");
            if (accessoryId <= 0) errors.Add("Select Accessories");
            if (!TryWhole(quantity, 1, 99999999, out int qty)) errors.Add("Enter Quantity (whole number greater than 0).");
            if (!TryWhole(price, 0, 999999999, out int pr)) errors.Add("Enter Price (whole number).");
            if (currencyId <= 0 || !_dispatch.GetCurrencies().Any(c => c.ID == currencyId)) errors.Add("Select Currency.");
            if (errors.Count > 0) return Fail(string.Join(" ", errors));
            if (!_repo.GetAccessoriesForSupervisor(categoryId, StaffId).Any(a => a.ID == accessoryId)) return Fail("Select Accessories");

            try
            {
                if (id > 0)
                {
                    // edit: only on the old Details page (supervisor)
                    var row = _repo.GetRequiredAccessory(id);
                    if (!supervisor || row == null || row.REQUESTID != requestId) return Fail("Accessory not found. Please reload the page.");
                    if (_repo.RequiredAccessoryExists(requestId, accessoryId, id)) return Fail("This Accessories already exists. Please try another.");
                    _repo.UpdateRequiredAccessory(id, requestId, accessoryId, qty, pr, currencyId, isNew ? 1 : 0);
                }
                else
                {
                    // old Details.aspx checked duplicates, old InstantService.aspx did not
                    if (supervisor && _repo.RequiredAccessoryExists(requestId, accessoryId, 0)) return Fail("This Accessories already exists. Please try another.");
                    _repo.InsertRequiredAccessory(requestId, accessoryId, qty, pr, currencyId, isNew ? 1 : 0, StaffId);
                }
                return Json(new { success = true, message = id > 0 ? "Accessory updated." : "Accessory added.", accessories = AccessoryRows(requestId) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save required accessory failed. RequestId={RequestId}", requestId);
                return Fail("Save failed. Please try again.");
            }
        }

        // old InstantService btnDeleteAccessories (the only accessory delete that was switched on)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteRequiredAccessory(long id)
        {
            var row = _repo.GetRequiredAccessory(id);
            if (row == null) return Fail("Accessory not found.");
            try
            {
                if (_repo.DeleteDraftAccessory(id, StaffId) == 0) return Fail("This accessory cannot be deleted.");
                return Json(new { success = true, message = "Accessory deleted.", accessories = AccessoryRows(row.REQUESTID) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Delete required accessory failed. Id={Id}", id);
                return Fail("Delete failed. Please try again.");
            }
        }

        // =====================================================================================================
        // INSTANT SERVICE  /Service/InstantService      (old InstantService.aspx)
        // =====================================================================================================
        [HttpGet]
        public IActionResult InstantService()
        {
            if (PageGuard("Service/InstantService") is { } r) return r;

            long draftId = _repo.GetOrCreateDraft(StaffId);          // the same open draft as New Service (old CheckRequest)
            var membership = _repo.GetMembership(StaffId);
            var vm = new InstantServiceViewModel
            {
                RequestId = draftId,
                Membership = membership,
                Categories = membership != null ? _repo.GetCategoriesByServiceCenter(membership.SERVICECENTERID) : new List<ServiceOption>(),
                Machines = _repo.GetMachinesFor(UserCenterId, UserCompanyId),
                BusinessCenters = _repo.GetAllBusinessCenters(),
                Departments = _dispatch.GetDepartments(),
                AccessoryCategories = _repo.GetAccessoryCategories(),
                Currencies = _dispatch.GetCurrencies(),
                Items = _repo.GetItems(draftId),
                Accessories = _repo.GetRequiredAccessories(draftId),
                Me = _dispatch.GetEmployee(StaffId),
                AttachmentsEnabled = _files.Enabled
            };
            ViewData["Title"] = "Instant Service";
            return View("InstantService", vm);
        }

        // old btnSave_Click: UpdateInstantService + AssignInstantService (to the user and his team)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveInstantService(long requestId, string? senderStaffId, int categoryId, int machineId,
                                                string? startDate, string? startHour, string? endDate, string? endHour)
        {
            if (!Allowed("Service/InstantService")) return Fail("You are not allowed to create instant services.");
            var membership = _repo.GetMembership(StaffId);
            if (membership == null) return Fail("You are not an active member of a service team.");

            var errors = new List<string>();
            string staff = FirstPart(senderStaffId);
            var emp = staff.Length > 0 ? _dispatch.GetEmployee(staff) : null;
            if (emp == null) errors.Add("Select the Employee.");
            if (categoryId <= 0 || !_repo.GetCategoriesByServiceCenter(membership.SERVICECENTERID).Any(c => c.ID == categoryId))
                errors.Add("Select Service Category");
            if (machineId < 0 || (machineId > 0 && !_repo.GetMachinesFor(UserCenterId, UserCompanyId).Any(m => m.ID == machineId)))
                errors.Add("Select a valid Outlet / Machine.");
            if (!TryDate(startDate, out DateTime sd)) errors.Add("Enter Start Date");
            if (!TryDate(endDate, out DateTime ed)) errors.Add("Enter End Date");
            if (!(TryHour(startHour, out string sh) & TryHour(endHour, out string eh))) errors.Add("Select the Time.");   // & = both always parsed
            if (errors.Count > 0) return Fail(string.Join(" ", errors));

            try
            {
                string? serviceId = _repo.SubmitInstant(requestId, StaffId, emp!.STAFFID, membership.SERVICECENTERID, categoryId, machineId,
                                                        UserCenterId, UserDepartmentId, membership.TEAMID, sd, sh, ed, eh);
                if (serviceId == null) return Fail("This request cannot be saved (it was already saved). Please reload the page.");
                return Json(new { success = true, serviceId, message = $"Instant service {serviceId} saved - waiting for the center admin's approval." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save instant service failed. RequestId={RequestId}", requestId);
                return Fail("Save failed. Please try again.");
            }
        }

        // =====================================================================================================
        // helpers
        // =====================================================================================================
        private JsonResult Fail(string message) => Json(new { success = false, message });

        // the auto complete text is "STAFFID-CENTER-DEPT-DESIGNATION-NAME" -> first part (old: Split('-')[0])
        private static string FirstPart(string? staffText) => (staffText ?? "").Trim().Split('-')[0].Trim();

        private ServiceListSection Section(string id, string title, string icon, string color, ServiceList list, params string[] actions) => new()
        {
            Id = id,
            Title = title,
            Icon = icon,
            Color = color,
            Rows = _repo.GetList(list, StaffId, RoleId),
            Actions = actions.ToList()
        };

        private IActionResult Run(long id, string okMessage, Action action)
        {
            try
            {
                action();
                return Json(new { success = true, message = okMessage });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Service action failed. RequestId={RequestId}", id);
                return Fail("Action failed. Please try again.");
            }
        }

        // old pages inserted a remarks row on every action, also an empty one; empty text is skipped here
        private void Remark(long id, string text)
        {
            if (text.Length > 0) _repo.AddRemark(id, StaffId, text);
        }

        private bool TryRemarks(string? remarks, bool required, out string text, out IActionResult? bad)
        {
            text = (remarks ?? "").Trim();
            bad = null;
            if (required && text.Length == 0) bad = Fail("Enter the Remarks.");
            else if (text.Length > RemarksMax) bad = Fail($"Remarks is too long (maximum {RemarksMax} characters).");
            return bad == null;
        }

        // old page: empty quantity = 1. The columns are INTEGER -> whole numbers only
        private static bool TryQuantity(string? text, out int qty)
        {
            if (string.IsNullOrWhiteSpace(text)) { qty = 1; return true; }
            return TryWhole(text, 1, 99999999, out qty);
        }

        private static bool TryWhole(string? text, int min, int max, out int value) =>
            int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;

        private static readonly string[] DateFormats = { "yyyy-MM-dd", "dd/MM/yyyy" };
        private static readonly string[] DateTimeFormats = { "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "dd/MM/yyyy hh:mm:ss tt", "dd/MM/yyyy HH:mm" };

        private static bool TryDate(string? text, out DateTime value) =>
            DateTime.TryParseExact((text ?? "").Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

        private static bool TryDateTime(string? text, out DateTime value) =>
            DateTime.TryParseExact((text ?? "").Trim(), DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

        // old ddlStartTime / ddlEndTime: values "0".."23" stored as text in STARTTIME / ENDTIME
        private static bool TryHour(string? text, out string hour)
        {
            hour = "";
            if (!TryWhole(text, 0, 23, out int h)) return false;
            hour = h.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        private object ItemRows(long requestId) =>
            _repo.GetItems(requestId).Select(i => new
            {
                id = i.ID,
                subject = i.SUBJECT,
                quantity = i.QUANTITY,
                remarks = i.REMARKS,
                attachment = string.IsNullOrWhiteSpace(i.ATTACHMENT) ? null : i.ATTACHMENT,
                attachmentUrl = string.IsNullOrWhiteSpace(i.ATTACHMENT) ? null : Url.Action(nameof(Attachment), new { id = i.ID })
            }).ToList();

        private object AccessoryRows(long requestId) =>
            _repo.GetRequiredAccessories(requestId).Select(a => new
            {
                id = a.ID,
                categoryId = a.CATEGORYID,
                accessoryId = a.ACCESSORIESID,
                name = a.ACCESSORIESNAME,
                quantity = a.QUANTITY,
                price = a.PRICE,
                currencyId = a.CURRENCYID,
                currency = a.CURRENCYCODE,
                isNew = a.ISNEW,
                newItem = a.NEWITEM
            }).ToList();
    }
}
