// Controllers/Dispatch/DispatchController.cs
// Dispatch module (port of YA Pages/Dispatch). Phase 1 = create + approval, phase 2 = print label + gate check, phase 3 = desk + transport, phase 4 = receive + return, phase 5 = tracking:
//   /Dispatch/CreateDispatch   <- Dispatch.aspx + DispatchGlobal.aspx       (draft items, issue type, sender, receiver, save)
//   /Dispatch/Approval         <- Approval.aspx + ApprovalGlobal.aspx + ApprovalUpdate.aspx (single + bulk approve / reject)
//   /Dispatch/ViewDispatch/5   <- View.aspx + ApproverView.aspx + ApproverViewGlobal.aspx (details; approve / reject when allowed)
//   /Dispatch/PrintLabel       <- PrintLebel.aspx + PrintLebelPOS.aspx      (A4 label with CODE-128 / POS label with QR, with or without items)
//   /Dispatch/GateCheck        <- GateCheck.aspx + GateCheckGlobal.aspx     (gate list, scan D... / R..., gate pass, gate reject)
//   /Dispatch/DispatchDesk     <- DispatchDesk.aspx (+ DispatchView.aspx / Details.aspx from the desk) (send with transport, receive, scan)
//   /Dispatch/Transport        <- Transport.aspx                            (transport list: add / edit)
//   /Dispatch/Receive          <- Receive.aspx (+ ReceiverView.aspx / ReturnView.aspx) (my receive list, return / direct return)
//   /Dispatch/Tracking?tx=DG...<- Tracking.aspx                             (steps of a dispatch + my dispatches on the way)
// Phase 6 setup pages: DispatchController.Setup.cs, phase 7 reports: DispatchController.Reports.cs,
// phase 8 visitor gate entry + notice board: DispatchController.GateEntry.cs.
// Views: Views/Dispatch/*.cshtml. Data: IDispatchRepository (same DP_* tables as YA).
using AlphaCare.Interface;
using AlphaCare.Models;
using AlphaCare.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace QCMS.Controllers
{
    [Authorize]
    public partial class DispatchController : Controller
    {
        private const int RemarksMax = 100;                  // DP_TRANSITS.REMARKS = NVARCHAR2(100)

        private readonly IDispatchRepository _repo;
        private readonly DispatchAttachmentStore _files;
        private readonly IEmailService _email;
        private readonly IConfiguration _config;
        private readonly ILogger<DispatchController> _logger;

        public DispatchController(IDispatchRepository repo, DispatchAttachmentStore files, IEmailService email,
                                  IConfiguration config, ILogger<DispatchController> logger)
        {
            _repo = repo;
            _files = files;
            _email = email;
            _config = config;
            _logger = logger;
        }

        private string StaffId => HttpContext.Session.GetString("STAFFID") ?? "";
        private string UserId => HttpContext.Session.GetString("USERID") ?? "";
        private int UserCenterId => HttpContext.Session.GetInt32("BusinessCenterID") ?? 0;

        // =====================================================================================================
        // CREATE  /Dispatch/CreateDispatch      (old Dispatch.aspx / DispatchGlobal.aspx)
        // =====================================================================================================
        [HttpGet]
        public IActionResult CreateDispatch()
        {
            if (string.IsNullOrWhiteSpace(StaffId)) return RedirectToAction("Login", "Accounts");

            long draftId = _repo.GetOrCreateDraft(StaffId);
            var vm = new CreateDispatchViewModel
            {
                DispatchId = draftId,
                IssueTypes = _repo.GetIssueTypes(),
                BusinessCenters = _repo.GetBusinessCenters(),
                Departments = _repo.GetDepartments(),
                Items = _repo.GetItems(draftId),
                Waiting = _repo.GetMyWaitingList(StaffId),
                Me = _repo.GetEmployee(StaffId),
                AttachmentsEnabled = _files.Enabled
            };
            ViewData["Title"] = "Create Dispatch";
            return View("CreateDispatch", vm);
        }

        // ---------- lookups (old AutoPostBack text boxes / drop downs and the AutoComplete web methods) ----------
        [HttpGet]
        public IActionResult GetEmployee(string staffId)
        {
            string id = FirstPart(staffId);
            if (id.Length == 0) return Json(new { found = false });

            var e = _repo.GetEmployee(id);
            return e == null
                ? Json(new { found = false })
                : Json(new { found = true, staffId = e.STAFFID, name = e.EMPLOYEENAME, mobile = e.OFFICEMOBILE, centerId = e.BUSINESSCENTERID, departmentId = e.DEPARTMENTID,
                                    companyId = e.COMPANYID, email = e.OFFICEMAIL, designation = e.DESIGNATIONNAME,
                                    centerCode = e.CENTERCODE, departmentCode = e.DEPARTMENTCODE });
        }

        [HttpGet]
        public IActionResult GetEmployees(int centerId, int departmentId)
        {
            if (centerId <= 0 || departmentId <= 0) return Json(new List<object>());
            return Json(_repo.GetEmployeesByDepartment(centerId, departmentId)
                             .Select(e => new { staffId = e.STAFFID, name = e.EMPLOYEENAME, mobile = e.OFFICEMOBILE }));
        }

        [HttpGet]
        public IActionResult SearchEmployee(string term)
        {
            if (string.IsNullOrWhiteSpace(term) || term.Trim().Length < 2) return Json(new List<string>());
            return Json(_repo.SearchEmployees(term));
        }

        [HttpGet]
        public IActionResult SearchItem(string term)
        {
            if (string.IsNullOrWhiteSpace(term) || term.Trim().Length < 2) return Json(new List<string>());
            return Json(_repo.SearchItems(term));
        }

        // ---------- draft items (old btnAddItem / btnEdit / btnDelete) ----------
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(DispatchAttachmentStore.MaxBytes + 1024 * 1024)]
        public async Task<IActionResult> AddItem(long dispatchId, string? itemName, string? quantity, string? unit, string? notes, IFormFile? file)
        {
            itemName = (itemName ?? "").Trim();
            unit = (unit ?? "").Trim();
            notes = (notes ?? "").Trim();

            if (itemName.Length == 0) return Fail("Enter the Item name.");
            if (itemName.Length > 100) return Fail("Item name is too long (maximum 100 characters).");
            if (unit.Length > 50) return Fail("Unit is too long (maximum 50 characters).");
            if (notes.Length > 200) return Fail("Remarks is too long (maximum 200 characters).");
            if (!TryQuantity(quantity, out decimal qty)) return Fail("Quantity must be a number greater than 0.");

            if (!_repo.IsOwnDraft(dispatchId, StaffId)) return Fail("This dispatch cannot be changed any more. Please reload the page.");

            try
            {
                // the old page kept only the quantity when the item name was already in the list -> save the file only for a new item
                string attachment = "";
                bool merges = _repo.DraftHasItem(dispatchId, itemName);
                if (file != null && file.Length > 0 && !merges)
                {
                    var saved = await _files.SaveAsync(file, dispatchId);
                    if (saved.Error != null) return Fail(saved.Error);
                    attachment = saved.FileName!;
                }

                int result = _repo.AddDraftItem(dispatchId, StaffId, itemName, qty, unit, notes, attachment);
                if (result == 0) return Fail("This dispatch cannot be changed any more. Please reload the page.");

                string msg = result == 2
                    ? "The item was already in the list - quantity added." + (file != null && file.Length > 0 ? " (The file was not attached.)" : "")
                    : "Item added.";
                return Json(new { success = true, message = msg, items = ItemRows(dispatchId) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Add dispatch item failed. DispatchId={DispatchId}", dispatchId);
                return Fail("Item could not be added. Please try again.");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateItem(long id, string? quantity, string? unit, string? notes)
        {
            unit = (unit ?? "").Trim();
            notes = (notes ?? "").Trim();
            if (!TryQuantity(quantity, out decimal qty)) return Fail("Quantity must be a number greater than 0.");
            if (unit.Length > 50) return Fail("Unit is too long (maximum 50 characters).");
            if (notes.Length > 200) return Fail("Remarks is too long (maximum 200 characters).");

            var item = _repo.GetItem(id);
            if (item == null) return Fail("Item not found.");
            try
            {
                if (_repo.UpdateDraftItem(id, StaffId, qty, unit, notes) == 0) return Fail("This item cannot be changed.");
                return Json(new { success = true, message = "Item updated.", items = ItemRows(item.DISPATCHID) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Update dispatch item failed. ItemId={ItemId}", id);
                return Fail("Item could not be updated. Please try again.");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteItem(long id)
        {
            var item = _repo.GetItem(id);
            if (item == null) return Fail("Item not found.");
            try
            {
                if (_repo.DeleteDraftItem(id, StaffId) == 0) return Fail("This item cannot be deleted.");
                return Json(new { success = true, message = "Item deleted.", items = ItemRows(item.DISPATCHID) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Delete dispatch item failed. ItemId={ItemId}", id);
                return Fail("Item could not be deleted. Please try again.");
            }
        }

        // ---------- SAVE (old btnSave_Click) ----------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveDispatch(SaveDispatchRequest r)
        {
            var issue = r.IssueTypeId > 0 ? _repo.GetIssueType(r.IssueTypeId) : null;
            if (issue == null) return Fail("Please select issuetype");

            var errors = new List<string>();

            // ---- sender ----
            string senderStaff = FirstPart(r.SenderStaffId);
            string senderName = (r.SenderName ?? "").Trim();
            string senderMobile = (r.SenderMobile ?? "").Trim();
            var sender = senderStaff.Length > 0 ? _repo.GetEmployee(senderStaff) : null;
            if (sender == null) errors.Add("Select the Sender (employee).");
            if (!(r.SenderCenterId > 0)) errors.Add("Select the Sender Location.");
            if (!(r.SenderDepartmentId > 0)) errors.Add("Select the Sender Department.");
            if (senderName.Length == 0) errors.Add("Enter the Sender Name.");
            if (senderName.Length > 120) errors.Add("Sender Name is too long (maximum 120 characters).");
            if (senderMobile.Length > 20) errors.Add("Sender Mobile is too long (maximum 20 characters).");

            // ---- receiver: depends on the issue type (old btnSave_Click) ----
            var m = new SubmitDispatchModel
            {
                DispatchId = r.DispatchId,
                IssueTypeId = issue.ID,
                SenderStaffId = sender?.STAFFID ?? "0",
                SenderName = senderName,
                SenderMobile = senderMobile,
                SenderCenterId = r.SenderCenterId ?? 0,
                SenderDepartmentId = r.SenderDepartmentId ?? 0,
                DirectSend = r.DirectSend ? 1 : 0
            };

            if (issue.ISEXTERNAL == 1)
            {
                m.ReceiverName = (r.ExtReceiverName ?? "").Trim();
                m.ReceiverMobile = (r.ExtReceiverMobile ?? "").Trim();
                m.ReceiverAddress = (r.ExtReceiverAddress ?? "").Trim();
                m.ReceiverStaffId = "0";
                if (m.ReceiverName.Length == 0) errors.Add("Please Entry Receiver Details");
                if (m.ReceiverAddress.Length > 120) errors.Add("Receiver Address is too long (maximum 120 characters).");
            }
            else if (issue.ISPERSONAL == 1)
            {
                // personal: the sender himself, at the sender location / department
                m.ReceiverStaffId = m.SenderStaffId;
                m.ReceiverCenterId = m.SenderCenterId;
                m.ReceiverDepartmentId = m.SenderDepartmentId;
            }
            else
            {
                m.ReceiverName = (r.ReceiverName ?? "").Trim();
                m.ReceiverMobile = (r.ReceiverMobile ?? "").Trim();
                m.ReceiverCenterId = r.ReceiverCenterId ?? 0;
                m.ReceiverDepartmentId = r.ReceiverDepartmentId ?? 0;
                string recStaff = FirstPart(r.ReceiverStaffId);
                if (recStaff.Length > 0)
                {
                    var rec = _repo.GetEmployee(recStaff);
                    if (rec == null) errors.Add("The selected Receiver (employee) was not found.");
                    else m.ReceiverStaffId = rec.STAFFID;
                }
                if (m.ReceiverName.Length == 0) errors.Add("Please Entry Receiver Details");
                if (m.ReceiverCenterId <= 0) errors.Add("Select the Receiver Location.");
                if (m.ReceiverDepartmentId <= 0) errors.Add("Select the Receiver Department.");
                // old code saved the TEXT of the selected location / department as well
                m.ReceiverCenter = _repo.GetBusinessCenters().FirstOrDefault(c => c.ID == m.ReceiverCenterId)?.CENTERNAME ?? "";
                m.ReceiverDepartment = _repo.GetDepartments().FirstOrDefault(d => d.ID == m.ReceiverDepartmentId)?.DEPARTMENTNAME ?? "";
            }
            if (m.ReceiverName.Length > 120) errors.Add("Receiver Name is too long (maximum 120 characters).");
            if (m.ReceiverMobile.Length > 20) errors.Add("Receiver Mobile is too long (maximum 20 characters).");
            if (_repo.GetItems(r.DispatchId).Count == 0) errors.Add("Please Add Items");

            if (errors.Count > 0) return Json(new { success = false, message = string.Join(" ", errors), errors });

            try
            {
                string? txId = _repo.SubmitDispatch(m, StaffId, UserId, UserCenterId);
                if (txId == null) return Fail("This dispatch cannot be saved (it was already saved, or it has no item). Please reload the page.");

                bool mailed = await NotifyNewAsync(r.DispatchId);
                return Json(new
                {
                    success = true,
                    transactionId = txId,
                    message = $"Dispatch {txId} saved and sent for approval." + (mailed ? "" : " (Mail could not be sent.)")
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save dispatch failed. DispatchId={DispatchId}", r.DispatchId);
                return Fail("Save failed. Please try again.");
            }
        }

        // =====================================================================================================
        // APPROVAL  /Dispatch/Approval      (old Approval.aspx / ApprovalGlobal.aspx / ApprovalUpdate.aspx)
        // =====================================================================================================
        [HttpGet]
        public IActionResult Approval()
        {
            var vm = new DispatchApprovalViewModel
            {
                Pending = _repo.GetPendingApprovals(StaffId),
                Approved = _repo.GetApprovedByMe(StaffId),
                Rejected = _repo.GetRejectedForMe(StaffId)
            };
            ViewData["Title"] = $"({vm.Pending.Count}) Approval Pending";
            return View("Approval", vm);
        }

        // items pop-up of the approval list (old btnItems -> gvItems modal)
        [HttpGet]
        public IActionResult GetItems(long id)
        {
            if (!_repo.CanViewDispatch(id, StaffId)) return Json(new List<object>());
            return Json(ItemRows(id));
        }

        // old btnEdit (approve, remarks "OK") / btnSaveandSend on ApproverView (remarks typed)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(long id, string? remarks)
        {
            remarks = string.IsNullOrWhiteSpace(remarks) ? "OK" : remarks.Trim();
            if (remarks.Length > RemarksMax) return Fail($"Remarks is too long (maximum {RemarksMax} characters).");
            var (ok, msg) = await ApproveOneAsync(id, remarks);
            return Json(new { success = ok, message = msg });
        }

        // old btnReject (remarks "Rejected") / btnReject on ApproverView (remarks typed)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(long id, string? remarks)
        {
            remarks = string.IsNullOrWhiteSpace(remarks) ? "Rejected" : remarks.Trim();
            if (remarks.Length > RemarksMax) return Fail($"Remarks is too long (maximum {RemarksMax} characters).");
            var (ok, msg) = await RejectOneAsync(id, remarks);
            return Json(new { success = ok, message = msg });
        }

        // old ApprovalUpdate.aspx: approve / reject all ticked rows
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDecision(string? ids, int approve)
        {
            var list = (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                  .Select(s => long.TryParse(s, out var v) ? v : 0).Where(v => v > 0).Distinct().Take(200).ToList();
            if (list.Count == 0) return Fail("Tick at least one dispatch.");

            int done = 0;
            foreach (long id in list)
            {
                var (ok, _) = approve == 1 ? await ApproveOneAsync(id, "OK") : await RejectOneAsync(id, "Rejected");
                if (ok) done++;
            }
            string verb = approve == 1 ? "approved" : "rejected";
            return Json(new
            {
                success = done > 0,
                message = done == list.Count ? $"{done} dispatch {verb}."
                                             : $"{done} of {list.Count} dispatch {verb}. The others were already processed or are not waiting for you."
            });
        }

        // old btnApproveRejected (remarks "Approved")
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveRejected(long id)
        {
            try
            {
                string? step = _repo.ApproveRejected(id, StaffId, "Approved");
                if (step == null) return Fail("This dispatch is not in your rejected list any more.");
                if (step.Length > 0) await NotifyStepAsync(id, step);
                return Json(new { success = true, message = "Dispatch approved." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Approve rejected dispatch failed. DispatchId={DispatchId}", id);
                return Fail("Action failed. Please try again.");
            }
        }

        // old ApproverView btnSave: only the remarks of the current step
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveRemarks(long id, string? remarks)
        {
            remarks = (remarks ?? "").Trim();
            if (remarks.Length > RemarksMax) return Fail($"Remarks is too long (maximum {RemarksMax} characters).");
            if (!_repo.IsPendingForApprover(id, StaffId)) return Fail("This dispatch is not waiting for your approval.");
            try
            {
                _repo.UpdateTransitRemarks(id, StaffId, remarks);
                return Json(new { success = true, message = "Remarks saved." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save dispatch remarks failed. DispatchId={DispatchId}", id);
                return Fail("Remarks could not be saved. Please try again.");
            }
        }

        // old ApproverView delete item button
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteItemByApprover(long id)
        {
            try
            {
                if (_repo.DeleteItemAsApprover(id, StaffId) == 0) return Fail("This item cannot be deleted.");
                return Json(new { success = true, message = "Item deleted." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Approver delete item failed. ItemId={ItemId}", id);
                return Fail("Item could not be deleted. Please try again.");
            }
        }

        // =====================================================================================================
        // DETAILS  /Dispatch/ViewDispatch/5     (old View.aspx / ApproverView.aspx / ApproverViewGlobal.aspx)
        // =====================================================================================================
        [HttpGet]
        public IActionResult ViewDispatch(long id)
        {
            if (id <= 0) return NotFound();
            string? desk = DeskActionFor(id);                          // phase 3: opened from the dispatch desk
            if (desk == null && !_repo.CanViewDispatch(id, StaffId)) return RedirectToAction("Denied", "Accounts");
            string? mine = _repo.GetMyAction(id, StaffId);             // phase 4: my receive / return

            var d = _repo.GetDispatchDetails(id);
            if (d == null) return NotFound();

            var vm = new DispatchDetailsViewModel
            {
                Dispatch = d,
                Items = _repo.GetItems(id),
                Transits = _repo.GetTransits(id),
                Transports = _repo.GetTransports(id),
                CanApprove = _repo.IsPendingForApprover(id, StaffId),
                AttachmentsEnabled = _files.Enabled,
                DeskAction = desk,
                MyAction = mine
            };
            if (desk == "send")
            {
                vm.TransportTypes = _repo.GetTransportTypes();
                vm.Currencies = _repo.GetCurrencies();
            }
            ViewData["Title"] = "Dispatch " + d.TRANSACTIONID;
            return View("ViewDispatch", vm);
        }

        // attachment of an item: only for people who may see the dispatch (or the owner of the draft)
        [HttpGet]
        public IActionResult Attachment(long id)
        {
            var item = _repo.GetItem(id);
            if (item == null || string.IsNullOrWhiteSpace(item.ATTACHMENT)) return NotFound();

            // gate men see the items of any dispatch they check (old gate page linked the files directly)
            if (!_repo.IsOwnDraft(item.DISPATCHID, StaffId) && !_repo.CanViewDispatch(item.DISPATCHID, StaffId) && !IsGateMan
                && DeskActionFor(item.DISPATCHID) == null)
                return RedirectToAction("Denied", "Accounts");

            var f = _files.Find(item.ATTACHMENT);
            if (f == null) return NotFound(_files.Enabled ? "The attached file was not found." : "Attachments are not configured on this server.");
            return PhysicalFile(f.Value.Path, f.Value.ContentType, Path.GetFileName(f.Value.Path));
        }

        // =====================================================================================================
        // PRINT LABEL  /Dispatch/PrintLabel   (old PrintLebel.aspx + PrintLebelPOS.aspx)
        // =====================================================================================================
        [HttpGet]
        public IActionResult PrintLabel()
        {
            if (string.IsNullOrWhiteSpace(StaffId)) return RedirectToAction("Login", "Accounts");
            ViewData["Title"] = "Print Dispatch Label";
            return View("PrintLabel", _repo.GetPrintList(StaffId));
        }

        // the label itself, own page for the print window: items = 1 -> with item list, pos = 1 -> small POS label with QR code
        [HttpGet]
        public IActionResult Label(long id, int items = 0, int pos = 0)
        {
            if (id <= 0) return NotFound();
            if (!_repo.CanPrint(id, StaffId)) return RedirectToAction("Denied", "Accounts");

            var d = _repo.GetDispatchDetails(id);
            if (d == null) return NotFound();

            string tx = d.TRANSACTIONID ?? "";
            var vm = new DispatchLabelViewModel
            {
                Dispatch = d,
                WithItems = items == 1,
                Pos = pos == 1,
                Items = items == 1 ? _repo.GetItems(id) : new List<DispatchItemModel>(),
                CodeImage = pos == 1 ? DispatchBarcode.Qr(tx) : DispatchBarcode.Code128(tx)
            };
            return View("Label", vm);
        }

        // old UpdatePrint (NOOFPRINT + 1) - called by the label page when it opens the print dialog
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult MarkPrinted(long id)
        {
            try
            {
                if (_repo.MarkPrinted(id, StaffId) == 0) return Fail("This dispatch cannot be printed by you.");
                return Json(new { success = true, message = "Print counted." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dispatch print count failed. DispatchId={DispatchId}", id);
                return Fail("Print could not be counted.");
            }
        }

        // =====================================================================================================
        // GATE CHECK  /Dispatch/GateCheck?tx=DG...   (old GateCheck.aspx + GateCheckGlobal.aspx)
        //   list of dispatches of my location waiting for the gate | scan / type an id "D..." (dispatch) or "R..." (gate entry)
        // =====================================================================================================
        private const string GateCheckUrl = "Dispatch/GateCheck";
        private bool? _isGateMan;
        // old dalCommon.IsPermitted: the page must be in the user's menu (UC_TASKPERMISSION_COPY)
        private bool IsGateMan => _isGateMan ??= !string.IsNullOrWhiteSpace(StaffId) && _repo.HasMenuPermission(StaffId, GateCheckUrl);

        [HttpGet]
        public IActionResult GateCheck(string? tx)
        {
            if (string.IsNullOrWhiteSpace(StaffId)) return RedirectToAction("Login", "Accounts");
            if (!IsGateMan) return RedirectToAction("Denied", "Accounts");

            string q = (tx ?? "").Trim().ToUpperInvariant();
            var vm = new GateCheckViewModel { GateNo = _repo.GetGateNo(StaffId), Search = q };

            if (q.Length > 50)
                vm.Message = "This id is too long.";
            else if (q.StartsWith("D"))
            {
                long? id = _repo.FindDispatchForGate(q);
                if (id.HasValue && _repo.GetDispatchDetails(id.Value) is { } d)
                {
                    vm.Found = new DispatchDetailsViewModel
                    {
                        Dispatch = d,
                        Items = _repo.GetItems(id.Value),
                        Transits = _repo.GetTransits(id.Value),
                        Transports = _repo.GetTransports(id.Value),
                        AttachmentsEnabled = _files.Enabled
                    };
                }
                else vm.Message = "Not Permitted: " + q + " is not a submitted dispatch.";
            }
            else if (q.StartsWith("R"))
            {
                vm.Entry = _repo.FindGateEntry(q);
                if (vm.Entry != null) vm.EntryItems = _repo.GetGateEntryItems(vm.Entry.ID);
                else vm.Message = "Not Permitted: " + q + " is not an open gate entry.";
            }
            else if (q.Length > 0)
                vm.Message = "Enter a dispatch id (D...) or a gate entry id (R...).";

            if (vm.Found == null && vm.Entry == null) vm.Waiting = _repo.GetGateCheckList(UserCenterId);

            ViewData["Title"] = "Gate Check";
            return View("GateCheck", vm);
        }

        // old lnbPass / btnGatePass: let the dispatch (D...) or the gate entry (R...) pass the gate
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult GatePass(string? tx)
        {
            if (!IsGateMan) return Fail("You are not allowed to use the gate check.");
            int gateNo = _repo.GetGateNo(StaffId);
            if (gateNo <= 0) return Fail("No gate number is assigned to you.");

            string q = (tx ?? "").Trim().ToUpperInvariant();
            if (q.Length == 0 || q.Length > 50) return Fail("Enter a dispatch id or a gate entry id.");
            try
            {
                bool ok;
                if (q.StartsWith("D"))
                {
                    long? id = _repo.FindDispatchForGate(q);
                    ok = id.HasValue && _repo.GatePassDispatch(id.Value, StaffId, gateNo);
                }
                else if (q.StartsWith("R"))
                    ok = _repo.GatePassEntry(q, StaffId, gateNo);
                else
                    return Fail("Enter a dispatch id (D...) or a gate entry id (R...).");

                return ok ? Json(new { success = true, message = $"{q} passed Gate# {gateNo}." })
                          : Fail($"{q} was already gate passed or is not allowed.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gate pass failed. Id={Tx}", q);
                return Fail("Gate pass failed. Please try again.");
            }
        }

        // old btnRejectGate: reject a dispatch of the gate list
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult GateReject(long id)
        {
            if (!IsGateMan) return Fail("You are not allowed to use the gate check.");
            try
            {
                return _repo.GateRejectDispatch(id, StaffId, UserCenterId)
                    ? Json(new { success = true, message = "Dispatch rejected at the gate." })
                    : Fail("This dispatch is not in your gate list any more.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gate reject failed. DispatchId={DispatchId}", id);
                return Fail("Gate reject failed. Please try again.");
            }
        }

        // =====================================================================================================
        // DISPATCH DESK  /Dispatch/DispatchDesk   (old DispatchDesk.aspx)
        //   Waiting to Send (view -> transport + Send) | Waiting to Receive (Receive / view) | scan box (old InsertReceive)
        //   The details page (/Dispatch/ViewDispatch/5) shows the desk part when the dispatch is in one of the desk lists.
        // =====================================================================================================
        private const string DeskUrl = "Dispatch/DispatchDesk";
        private const string TransportUrl = "Dispatch/Transport";
        private bool? _isDeskMan, _isTransportAdmin;
        // old dalCommon.IsPermitted: the page must be in the user's menu
        private bool IsDeskMan => _isDeskMan ??= !string.IsNullOrWhiteSpace(StaffId) && _repo.HasMenuPermission(StaffId, DeskUrl);
        private bool IsTransportAdmin => _isTransportAdmin ??= !string.IsNullOrWhiteSpace(StaffId) && _repo.HasMenuPermission(StaffId, TransportUrl);

        // "send" / "receive" when the user is a desk man of the location that holds this dispatch now, otherwise null
        private string? DeskActionFor(long dispatchId) =>
            UserCenterId > 0 && IsDeskMan ? _repo.GetDeskAction(dispatchId, UserCenterId) : null;

        [HttpGet]
        public IActionResult DispatchDesk()
        {
            if (string.IsNullOrWhiteSpace(StaffId)) return RedirectToAction("Login", "Accounts");
            if (!IsDeskMan) return RedirectToAction("Denied", "Accounts");

            var vm = new DispatchDeskViewModel
            {
                ToSend = _repo.GetDeskSendList(UserCenterId),
                ToReceive = _repo.GetDeskReceiveList(UserCenterId)
            };
            ViewData["Title"] = "Dispatch Desk";
            return View("DispatchDesk", vm);
        }

        // old btnSaveandSend on DispatchView.aspx (remarks typed, default "OK" as the old list button)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeskSend(long id, string? remarks) => DeskForward(id, "send", remarks, "OK", "Dispatch sent.");

        // old btnReceiveEdit (remarks "Received") / btnSaveandSend "Receive" on Details.aspx
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeskReceive(long id, string? remarks) => DeskForward(id, "receive", remarks, "Received", "Dispatch received.");

        private IActionResult DeskForward(long id, string action, string? remarks, string defaultRemarks, string done)
        {
            if (!IsDeskMan || UserCenterId <= 0) return Fail("You are not allowed to use the dispatch desk.");
            remarks = string.IsNullOrWhiteSpace(remarks) ? defaultRemarks : remarks.Trim();
            if (remarks.Length > RemarksMax) return Fail($"Remarks is too long (maximum {RemarksMax} characters).");
            try
            {
                string? step = _repo.DeskForward(id, StaffId, UserCenterId, action, remarks);
                return step == null ? Fail("This dispatch is not waiting at your desk any more.")
                                    : Json(new { success = true, message = done + (step.Length > 0 ? " Next step: " + step : "") });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dispatch desk {Action} failed. DispatchId={DispatchId}", action, id);
                return Fail("Action failed. Please try again.");
            }
        }

        // old btnSave on DispatchView.aspx / Details.aspx: only the remarks of the current step
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeskRemarks(long id, string? remarks)
        {
            remarks = (remarks ?? "").Trim();
            if (remarks.Length > RemarksMax) return Fail($"Remarks is too long (maximum {RemarksMax} characters).");
            if (DeskActionFor(id) == null) return Fail("This dispatch is not waiting at your desk.");
            try
            {
                _repo.UpdateTransitRemarks(id, StaffId, remarks);
                return Json(new { success = true, message = "Remarks saved." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Desk remarks failed. DispatchId={DispatchId}", id);
                return Fail("Remarks could not be saved. Please try again.");
            }
        }

        // old btnSearch (scan / type a dispatch id): only a "<my location>-Dispatch / Received" line on the dispatch
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeskScan(string? tx)
        {
            if (!IsDeskMan) return Fail("You are not allowed to use the dispatch desk.");
            string q = (tx ?? "").Trim().ToUpperInvariant();
            if (q.Length == 0) return Fail("Enter Transaction ID");
            if (q.Length > 50) return Fail("This id is too long.");
            try
            {
                return _repo.DeskScanReceive(q, StaffId).HasValue
                    ? Json(new { success = true, message = q + ": Item Received" })
                    : Fail(q + ": Item Not Found");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Desk scan failed. Id={Tx}", q);
                return Fail("Scan failed. Please try again.");
            }
        }

        // old btnTransportSave on DispatchView.aspx
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddTransport(long id, int transportId, int currencyId, string? cost, string? driver, string? deliveryMan, string? mobile)
        {
            if (!IsDeskMan || UserCenterId <= 0) return Fail("You are not allowed to use the dispatch desk.");
            driver = (driver ?? "").Trim();
            deliveryMan = (deliveryMan ?? "").Trim();
            mobile = (mobile ?? "").Trim();

            var errors = new List<string>();
            if (transportId <= 0) errors.Add("Select the Transport.");
            if (currencyId <= 0) errors.Add("Select the Currency.");
            long costValue = 0;                                       // old page: empty cost = 0; DP_TRANSPORTDETAILS.COST is INTEGER
            if (!string.IsNullOrWhiteSpace(cost) &&
                !(long.TryParse(cost.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out costValue)
                  && costValue >= 0 && costValue < 1000000000))
                errors.Add("Cost must be a whole number (0 or more).");
            if (driver.Length > 120) errors.Add("Driver is too long (maximum 120 characters).");
            if (deliveryMan.Length > 120) errors.Add("Delivery Man is too long (maximum 120 characters).");
            if (mobile.Length > 50) errors.Add("Mobile is too long (maximum 50 characters).");
            if (errors.Count > 0) return Fail(string.Join(" ", errors));

            try
            {
                if (!_repo.AddTransportDetail(id, UserCenterId, transportId, currencyId, costValue, driver, deliveryMan, mobile))
                    return Fail("Transport could not be added (the dispatch is not waiting to be sent from your desk, or the transport / currency was not found).");
                return Json(new { success = true, message = "Transport saved." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Add transport failed. DispatchId={DispatchId}", id);
                return Fail("Transport could not be saved. Please try again.");
            }
        }

        // =====================================================================================================
        // TRANSPORT  /Dispatch/Transport   (old Transport.aspx: list + add / edit, no delete)
        // =====================================================================================================
        [HttpGet]
        public IActionResult Transport()
        {
            if (string.IsNullOrWhiteSpace(StaffId)) return RedirectToAction("Login", "Accounts");
            if (!IsTransportAdmin) return RedirectToAction("Denied", "Accounts");
            ViewData["Title"] = "Transport";
            return View("Transport", _repo.GetTransportTypes());
        }

        // id = 0 -> add (old btnSave), id > 0 -> edit (old btnEdit)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveTransport(int id, string? name, bool isExternal)
        {
            if (!IsTransportAdmin) return Fail("You are not allowed to change transports.");
            name = (name ?? "").Trim();
            if (name.Length == 0) return Fail("Enter Transport");
            if (name.Length > 100) return Fail("Transport is too long (maximum 100 characters).");
            try
            {
                if (id > 0 && _repo.GetTransportType(id) == null) return Fail("Transport not found. Please reload the page.");
                if (_repo.TransportNameExists(name, id)) return Fail("This Transport name already exists. Please try another.");

                if (id > 0) _repo.UpdateTransport(id, name, isExternal);
                else _repo.InsertTransport(name, isExternal);
                return Json(new { success = true, message = id > 0 ? "Transport updated." : "Transport saved." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save transport failed. Id={Id}", id);
                return Fail("Save failed. Please try again.");
            }
        }

        // =====================================================================================================
        // RECEIVE + RETURN  /Dispatch/Receive   (old Receive.aspx, ReceiverView.aspx, ReturnView.aspx)
        //   Waiting to Receive : I am the receiver -> Receive (old UpdateTransit, remarks "Received")
        //   Waiting to Return  : I am the sender of a returnable item -> Return (old NormalReturn) / Direct Return (old DirectReturn)
        //   The details page shows the same buttons with a remarks box when the dispatch is in one of the two lists.
        // =====================================================================================================
        [HttpGet]
        public IActionResult Receive()
        {
            if (string.IsNullOrWhiteSpace(StaffId)) return RedirectToAction("Login", "Accounts");
            var vm = new DispatchReceiveViewModel
            {
                ToReceive = _repo.GetMyReceiveList(StaffId),
                ToReturn = _repo.GetMyReturnList(StaffId)
            };
            ViewData["Title"] = $"({vm.ToReceive.Count}) Receive Dispatch";
            return View("Receive", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult MyReceive(long id, string? remarks)
        {
            remarks = string.IsNullOrWhiteSpace(remarks) ? "Received" : remarks.Trim();
            if (remarks.Length > RemarksMax) return Fail($"Remarks is too long (maximum {RemarksMax} characters).");
            try
            {
                string? step = _repo.MyReceive(id, StaffId, remarks);
                return step == null ? Fail("This dispatch is not waiting for you to receive any more.")
                                    : Json(new { success = true, message = "Dispatch received." + (step.Length > 0 ? " Next step: " + step : "") });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Receive dispatch failed. DispatchId={DispatchId}", id);
                return Fail("Action failed. Please try again.");
            }
        }

        // direct = 0 -> old NormalReturn (back through the dispatch desks), direct = 1 -> old DirectReturn
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ReturnDispatch(long id, int direct, string? remarks)
        {
            remarks = string.IsNullOrWhiteSpace(remarks) ? "Returned" : remarks.Trim();
            if (remarks.Length > RemarksMax) return Fail($"Remarks is too long (maximum {RemarksMax} characters).");
            try
            {
                string? step = _repo.ReturnDispatch(id, StaffId, remarks, direct == 1);
                return step == null ? Fail("This dispatch is not waiting for your return any more.")
                                    : Json(new { success = true, message = (direct == 1 ? "Dispatch direct returned." : "Dispatch returned.") + (step.Length > 0 ? " Next step: " + step : "") });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Return dispatch failed. DispatchId={DispatchId} Direct={Direct}", id, direct);
                return Fail("Action failed. Please try again.");
            }
        }

        // old btnSave on ReceiverView.aspx / ReturnView.aspx: only the remarks of the current step
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult MyRemarks(long id, string? remarks)
        {
            remarks = (remarks ?? "").Trim();
            if (remarks.Length > RemarksMax) return Fail($"Remarks is too long (maximum {RemarksMax} characters).");
            if (_repo.GetMyAction(id, StaffId) == null) return Fail("This dispatch is not waiting for you.");
            try
            {
                _repo.UpdateTransitRemarks(id, StaffId, remarks);
                return Json(new { success = true, message = "Remarks saved." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save my remarks failed. DispatchId={DispatchId}", id);
                return Fail("Remarks could not be saved. Please try again.");
            }
        }

        // =====================================================================================================
        // TRACKING  /Dispatch/Tracking?tx=DG...   (old Tracking.aspx)
        //   my dispatches on the way (sender or receiver) + the steps of one dispatch.
        //   Own dispatches can always be tracked; any other id needs the menu "Dispatch/Tracking" (old IsPermitted of the page),
        //   or the desk / gate role (they handle other people's dispatches).
        // =====================================================================================================
        private const string TrackingUrl = "Dispatch/Tracking";

        [HttpGet]
        public IActionResult Tracking(string? tx)
        {
            if (string.IsNullOrWhiteSpace(StaffId)) return RedirectToAction("Login", "Accounts");

            string q = (tx ?? "").Trim().ToUpperInvariant();
            var vm = new TrackingViewModel
            {
                // old pre-filled text "DG" + office + yy + mm + "000" (office padded to 3 digits, as the transaction id)
                Search = q.Length > 0 ? q : "DG" + UserCenterId.ToString("000") + DateTime.Now.ToString("yy") + DateTime.Now.ToString("MM") + "000",
                Mine = _repo.GetTrackingList(StaffId)
            };

            if (q.Length > 50)
                vm.Message = "This id is too long.";
            else if (q.Length > 0)
            {
                var found = _repo.FindForTracking(q);
                bool allowed = found != null &&
                               (_repo.CanViewDispatch(found.ID, StaffId) || _repo.HasMenuPermission(StaffId, TrackingUrl) || IsDeskMan || IsGateMan);
                if (allowed)
                {
                    vm.Found = found;
                    vm.Steps = _repo.GetTransits(found!.ID);
                }
                else vm.Message = q + ": dispatch not found.";            // same text when not allowed: does not tell that the id exists
            }

            ViewData["Title"] = "Dispatch Tracking";
            return View("Tracking", vm);
        }

        // =====================================================================================================
        // helpers
        // =====================================================================================================
        private JsonResult Fail(string message) => Json(new { success = false, message });

        // the auto complete text is "STAFFID-CENTER-DEPT-DESIGNATION-NAME" -> first part (old: Split('-')[0])
        private static string FirstPart(string? staffText) => (staffText ?? "").Trim().Split('-')[0].Trim();

        private static bool TryQuantity(string? text, out decimal qty)
        {
            // old page: empty quantity = 1
            if (string.IsNullOrWhiteSpace(text)) { qty = 1; return true; }
            return decimal.TryParse(text.Trim(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out qty)
                   && qty > 0 && qty < 100000000;
        }

        private object ItemRows(long dispatchId) =>
            _repo.GetItems(dispatchId).Select(i => new
            {
                id = i.ID,
                name = i.ITEMNAME,
                quantity = i.QUANTITY,
                unit = i.UNIT,
                notes = i.NOTES,
                attachment = string.IsNullOrWhiteSpace(i.ATTACHMENT) ? null : i.ATTACHMENT,
                attachmentUrl = string.IsNullOrWhiteSpace(i.ATTACHMENT) ? null : Url.Action(nameof(Attachment), new { id = i.ID })
            }).ToList();

        private async Task<(bool, string)> ApproveOneAsync(long id, string remarks)
        {
            try
            {
                string? step = _repo.Approve(id, StaffId, remarks);
                if (step == null) return (false, "This dispatch is not waiting for your approval any more.");

                // old order: DispatchStatus -> SendUpdateMail, then SendApprovedMail
                if (step.Length > 0) await NotifyStepAsync(id, step);
                await NotifyDecisionAsync(id, approved: true);
                return (true, "Dispatch approved.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Approve dispatch failed. DispatchId={DispatchId}", id);
                return (false, "Action failed. Please try again.");
            }
        }

        private async Task<(bool, string)> RejectOneAsync(long id, string remarks)
        {
            try
            {
                if (!_repo.Reject(id, StaffId, remarks)) return (false, "This dispatch is not waiting for your approval any more.");
                await NotifyDecisionAsync(id, approved: false);
                return (true, "Dispatch rejected.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reject dispatch failed. DispatchId={DispatchId}", id);
                return (false, "Action failed. Please try again.");
            }
        }

        // ---------- mails: a mail problem never breaks the action (the old code swallowed mail errors too) ----------
        private async Task<bool> NotifyNewAsync(long dispatchId)
        {
            try
            {
                var info = _repo.GetMailInfo(dispatchId);
                if (info == null) return false;
                bool ok = true;
                var forApprover = DispatchMailBuilder.NewForApprover(info, AbsoluteUrl(nameof(Approval)));
                foreach (var to in _repo.GetApproverMails(info.CREATEDBY ?? ""))
                    ok &= await SendAsync(to, forApprover);
                ok &= await SendAsync(info.OFFICEMAIL, DispatchMailBuilder.NewForSender(info, AbsoluteUrl(nameof(ViewDispatch), dispatchId)));
                return ok;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dispatch mail (new) failed. DispatchId={DispatchId}", dispatchId);
                return false;
            }
        }

        private async Task NotifyStepAsync(long dispatchId, string step)
        {
            try
            {
                var info = _repo.GetMailInfo(dispatchId);
                if (info == null) return;
                string link = AbsoluteUrl(nameof(ViewDispatch), dispatchId);
                if (!string.IsNullOrWhiteSpace(info.TOMAIL))
                    await SendAsync(info.TOMAIL, DispatchMailBuilder.StepForReceiver(info, step, link));
                await SendAsync(info.OFFICEMAIL, DispatchMailBuilder.StepForSender(info, step, link));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dispatch mail (step) failed. DispatchId={DispatchId}", dispatchId);
            }
        }

        private async Task NotifyDecisionAsync(long dispatchId, bool approved)
        {
            try
            {
                var info = _repo.GetMailInfo(dispatchId);
                if (info == null) return;
                string link = AbsoluteUrl(nameof(ViewDispatch), dispatchId);
                var forApprover = DispatchMailBuilder.DecisionForApprover(info, approved, StaffId, link);
                foreach (var to in _repo.GetApproverMails(info.CREATEDBY ?? ""))
                    await SendAsync(to, forApprover);
                await SendAsync(info.OFFICEMAIL, DispatchMailBuilder.DecisionForSender(info, approved, StaffId, link));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dispatch mail (decision) failed. DispatchId={DispatchId}", dispatchId);
            }
        }

        private async Task<bool> SendAsync(string? to, (string Subject, string Body) mail)
        {
            if (string.IsNullOrWhiteSpace(to)) return false;
            return await _email.SendAsync(to.Trim(), mail.Subject, mail.Body);
        }

        // same rule as MeetingController: App:BaseUrl from appsettings.json (carries the full app root,
        // e.g. https://pmc.prangroup.com/admin24), otherwise the current request. Url.Action already
        // prepends Request.PathBase, so that prefix is cut before the configured base is put in front.
        private string AbsoluteUrl(string action, long? id = null)
        {
            string path = (id.HasValue ? Url.Action(action, "Dispatch", new { id = id.Value }) : Url.Action(action, "Dispatch")) ?? "/";
            string pathBase = Request.PathBase.Value ?? "";
            if (pathBase.Length > 0 && path.StartsWith(pathBase, StringComparison.OrdinalIgnoreCase))
                path = path.Substring(pathBase.Length);
            if (!path.StartsWith('/')) path = "/" + path;
            string baseUrl = (_config["App:BaseUrl"] ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl)) baseUrl = $"{Request.Scheme}://{Request.Host}{pathBase}";
            return baseUrl + path;
        }
    }
}
