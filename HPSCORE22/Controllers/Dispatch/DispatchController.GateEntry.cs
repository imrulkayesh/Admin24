// Controllers/Dispatch/DispatchController.GateEntry.cs
// Dispatch phase 8 - visitor gate entry and the notice board (port of YA Pages/Dispatch):
//   /Dispatch/GateEntry              <- GateEntry.aspx                     (host + visitor + items, Save -> "R..." gate entry)
//   /Dispatch/GateEntryDetails/5     <- GateEntryView.aspx + GateEntryDetails.aspx (details; host can return a sample)
//   /Dispatch/GateEntryLabel/5       <- GatentryPrint.aspx                 (printable gate pass with CODE-128 barcode)
//   /Dispatch/MyVisitors             <- MyVisitors.aspx                    (my returnable samples: view / print / return)
//   /Dispatch/NoticeBoard            <- DNoticeBoard.aspx                  (incoming letter / parcel: from, to, items)
//   /Dispatch/NoticeView/5           <- NView.aspx                         (details of a notice)
//   /Dispatch/NoticeReceive          <- RNItems.aspx                       (search + hand over to the receiver)
// The two gate entry reports and the notice board report are in DispatchController.Reports.cs.
// NOT ported: DNoticeBoardForward.aspx - it is not a notice page; it writes query string text into the SMS table RFL.T_DSMS
// without any login and deletes rows there.
using AlphaCare.Models;
using AlphaCare.Services;
using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;

namespace QCMS.Controllers
{
    public partial class DispatchController
    {
        private const string GateEntryUrl = "Dispatch/GateEntry";
        private const string NoticeBoardUrl = "Dispatch/NoticeBoard";
        private const string NoticeReceiveUrl = "Dispatch/NoticeReceive";

        // who may open a saved gate entry: the gate user who made it, the host, gate entry / gate check users, report users
        private bool CanSeeGateEntry(long entryId) =>
            _repo.IsGateEntryPerson(entryId, StaffId) || Allowed(GateEntryUrl) || IsGateMan
            || Allowed("Dispatch/ReportGateEntry") || Allowed("Dispatch/ReportGateEntryReturned");

        private static string Clean(string? s) => (s ?? "").Trim();

        // =====================================================================================================
        // VISITOR GATE ENTRY  /Dispatch/GateEntry   (old GateEntry.aspx)
        // =====================================================================================================
        [HttpGet]
        public IActionResult GateEntry()
        {
            if (PageGuard(GateEntryUrl) is { } r) return r;

            long draftId = _repo.GetOrCreateGateEntryDraft(StaffId);
            var vm = new GateEntryCreateViewModel
            {
                EntryId = draftId,
                BusinessCenters = _repo.GetBusinessCenters(),
                Departments = _repo.GetDepartments(),
                Items = _repo.GetGateEntryItems(draftId),
                UserCenterId = UserCenterId,
                AttachmentsEnabled = _files.GateEntryEnabled
            };
            ViewData["Title"] = "Visitor Gate Entry";
            return View("GateEntry", vm);
        }

        [HttpGet]
        public IActionResult SearchGateItem(string term)
        {
            if (string.IsNullOrWhiteSpace(term) || term.Trim().Length < 2) return Json(new List<string>());
            return Json(_repo.SearchGateEntryItems(term));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(DispatchAttachmentStore.MaxBytes + 1024 * 1024)]
        public async Task<IActionResult> AddGateEntryItem(long entryId, string? itemName, string? quantity, string? unit, string? remarks,
                                                          bool sample, bool returnable, IFormFile? file)
        {
            if (!Allowed(GateEntryUrl)) return Fail("You are not allowed to make gate entries.");
            itemName = Clean(itemName); unit = Clean(unit); remarks = Clean(remarks);

            if (itemName.Length == 0) return Fail("Enter the Item name.");
            if (itemName.Length > 100) return Fail("Item name is too long (maximum 100 characters).");
            if (unit.Length > 50) return Fail("Unit is too long (maximum 50 characters).");
            if (remarks.Length > 150) return Fail("Remarks is too long (maximum 150 characters).");
            if (!TryQuantity(quantity, out decimal qty)) return Fail("Quantity must be a number greater than 0.");
            if (!_repo.IsOwnGateEntryDraft(entryId, StaffId)) return Fail("This gate entry cannot be changed any more. Please reload the page.");

            try
            {
                string attachment = "";
                if (file != null && file.Length > 0)
                {
                    var saved = await _files.SaveGateEntryAsync(file, entryId);
                    if (saved.Error != null) return Fail(saved.Error);
                    attachment = saved.FileName!;
                }
                if (_repo.AddGateEntryItem(entryId, StaffId, itemName, qty, unit, remarks, sample, returnable, attachment) == 0)
                    return Fail("This gate entry cannot be changed any more. Please reload the page.");
                return Json(new { success = true, message = "Item added.", items = GateItemRows(entryId) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Add gate entry item failed. EntryId={EntryId}", entryId);
                return Fail("Item could not be added. Please try again.");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteGateEntryItem(long id)
        {
            long? entryId = _repo.GetGateEntryIdOfItem(id);
            if (entryId == null) return Fail("Item not found.");
            try
            {
                if (_repo.DeleteGateEntryItem(id, StaffId) == 0) return Fail("This item cannot be deleted.");
                return Json(new { success = true, message = "Item deleted.", items = GateItemRows(entryId.Value) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Delete gate entry item failed. ItemId={ItemId}", id);
                return Fail("Item could not be deleted. Please try again.");
            }
        }

        // old btnSave_Click -> dalGateEntry.Update, then GatentryPrint.aspx
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveGateEntry(SaveGateEntryRequest r)
        {
            if (!Allowed(GateEntryUrl)) return Fail("You are not allowed to make gate entries.");

            string hostId = FirstPart(r.HostStaffId);
            var host = hostId.Length > 0 ? _repo.GetEmployee(hostId) : null;
            if (host == null) return Fail("Select the employee the visitor comes to (To User).");
            if (r.HostCenterId <= 0) return Fail("Select Business Center");                       // old validator text
            if (r.HostDepartmentId <= 0) return Fail("Select the Department.");

            r.HostStaffId = host.STAFFID;
            r.VisitorStaffId = Clean(r.VisitorStaffId).Equals("N/A", StringComparison.OrdinalIgnoreCase) ? "" : FirstPart(r.VisitorStaffId);
            r.VisitorName = Clean(r.VisitorName); r.VisitorAddress = Clean(r.VisitorAddress);
            r.VisitorMobile = Clean(r.VisitorMobile); r.CardNo = Clean(r.CardNo);

            if (r.VisitorName.Length == 0) return Fail("Enter the visitor's Name.");
            if (r.VisitorName.Length > 120) return Fail("Visitor name is too long (maximum 120 characters).");
            if (r.VisitorAddress.Length > 120) return Fail("Visitor address is too long (maximum 120 characters).");
            if (r.VisitorMobile.Length > 20) return Fail("Visitor mobile is too long (maximum 20 characters).");
            if (r.CardNo.Length > 50) return Fail("Card# is too long (maximum 50 characters).");
            if (r.VisitorStaffId.Length > 50) r.VisitorStaffId = "";

            if (!_repo.IsOwnGateEntryDraft(r.EntryId, StaffId)) return Fail("This gate entry was already saved. Please reload the page.");
            try
            {
                string? tx = _repo.SubmitGateEntry(r, StaffId, UserId, UserCenterId);
                if (tx == null) return Fail("This gate entry was already saved. Please reload the page.");
                return Json(new { success = true, message = $"Gate entry {tx} saved.", url = Url.Action(nameof(GateEntryDetails), new { id = r.EntryId, saved = 1 }) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save gate entry failed. EntryId={EntryId}", r.EntryId);
                return Fail("Save failed. Please try again.");
            }
        }

        // old GateEntryView.aspx / GateEntryDetails.aspx
        [HttpGet]
        public IActionResult GateEntryDetails(long id, int saved = 0)
        {
            if (string.IsNullOrWhiteSpace(StaffId)) return RedirectToAction("Login", "Accounts");
            var e = _repo.GetGateEntry(id);
            if (e == null) return NotFound();
            if (!CanSeeGateEntry(id)) return RedirectToAction("Denied", "Accounts");

            var items = _repo.GetGateEntryItems(id);
            var vm = new GateEntryDetailsViewModel
            {
                Entry = e,
                Items = items,
                AttachmentsEnabled = _files.GateEntryEnabled,
                CanReturnSample = e.STAFFID == StaffId && e.ISRETURNED != 1 && items.Any(i => i.ISPERSONAL == 1 && i.ISRETURNABLE == 1)
            };
            ViewData["Saved"] = saved == 1;
            ViewData["Title"] = "Gate Entry " + e.GATEENTRYID;
            return View("GateEntryDetails", vm);
        }

        // old GatentryPrint.aspx (own page without layout, opens the print dialog)
        [HttpGet]
        public IActionResult GateEntryLabel(long id)
        {
            if (string.IsNullOrWhiteSpace(StaffId)) return RedirectToAction("Login", "Accounts");
            var e = _repo.GetGateEntry(id);
            if (e == null) return NotFound();
            if (!CanSeeGateEntry(id)) return RedirectToAction("Denied", "Accounts");

            return View("GateEntryLabel", new GateEntryLabelViewModel
            {
                Entry = e,
                Items = _repo.GetGateEntryItems(id),
                CodeImage = DispatchBarcode.Code128(e.GATEENTRYID ?? "")
            });
        }

        [HttpGet]
        public IActionResult GateEntryAttachment(long id)
        {
            var item = _repo.GetGateEntryItem(id);
            long? entryId = _repo.GetGateEntryIdOfItem(id);
            if (item == null || entryId == null || string.IsNullOrWhiteSpace(item.ATTACHMENTS)) return NotFound();
            if (!_repo.IsOwnGateEntryDraft(entryId.Value, StaffId) && !CanSeeGateEntry(entryId.Value))
                return RedirectToAction("Denied", "Accounts");

            var f = _files.FindGateEntry(item.ATTACHMENTS);
            if (f == null) return NotFound(_files.GateEntryEnabled ? "The attached file was not found." : "Gate entry attachments are not configured on this server.");
            return PhysicalFile(f.Value.Path, f.Value.ContentType, Path.GetFileName(f.Value.Path));
        }

        // old MyVisitors.aspx: only the logged in host's own entries, so no menu entry is needed
        [HttpGet]
        public IActionResult MyVisitors()
        {
            if (string.IsNullOrWhiteSpace(StaffId)) return RedirectToAction("Login", "Accounts");
            ViewData["Title"] = "My Visitors - Returnable Samples";
            return View("MyVisitors", _repo.GetMyReturnableSamples(StaffId));
        }

        // old btnReturn_Command -> SampleReturn
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ReturnSample(long id)
        {
            try
            {
                return _repo.ReturnSample(id, StaffId)
                    ? Json(new { success = true, message = "Sample Returned" })
                    : Fail("This sample is not waiting for your return any more.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sample return failed. EntryId={EntryId}", id);
                return Fail("Action failed. Please try again.");
            }
        }

        private object GateItemRows(long entryId) =>
            _repo.GetGateEntryItems(entryId).Select(i => new
            {
                id = i.ID,
                name = i.ITEMNAME,
                quantity = i.QUANTITY,
                unit = i.UNIT,
                remarks = i.REMARKS,
                sample = i.ISPERSONAL == 1,
                returnable = i.ISRETURNABLE == 1,
                attachment = string.IsNullOrWhiteSpace(i.ATTACHMENTS) ? null : i.ATTACHMENTS,
                attachmentUrl = string.IsNullOrWhiteSpace(i.ATTACHMENTS) ? null : Url.Action(nameof(GateEntryAttachment), new { id = i.ID })
            }).ToList();

        // =====================================================================================================
        // NOTICE BOARD  /Dispatch/NoticeBoard   (old DNoticeBoard.aspx)
        // =====================================================================================================
        [HttpGet]
        public IActionResult NoticeBoard()
        {
            if (PageGuard(NoticeBoardUrl) is { } r) return r;

            long draftId = _repo.GetOrCreateNoticeDraft(StaffId);
            var vm = new NoticeCreateViewModel
            {
                NoticeId = draftId,
                Types = _repo.GetDispatchTypes(),
                Items = _repo.GetNoticeItems(draftId),
                AttachmentsEnabled = _files.Enabled
            };
            ViewData["Title"] = "Notice Board";
            return View("NoticeBoard", vm);
        }

        [HttpGet]
        public IActionResult SearchNoticeItem(string term)
        {
            if (string.IsNullOrWhiteSpace(term) || term.Trim().Length < 2) return Json(new List<string>());
            return Json(_repo.SearchNoticeItems(term));
        }

        // old tbxIntStaffID_TextChanged / tbxReceiverStaffID_OnTextChanged
        [HttpGet]
        public IActionResult NoticeEmployee(string staffId)
        {
            string id = FirstPart(staffId);
            var e = id.Length == 0 ? null : _repo.GetNoticeEmployee(id);
            return e == null
                ? Json(new { found = false })
                : Json(new { found = true, staffId = e.STAFFID, name = e.EMPLOYEENAME, mobile = e.OFFICEMOBILE, email = e.OFFICEMAIL,
                             location = e.CENTERNAME, company = e.COMPANYNAME, department = e.DEPARTMENTNAME });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(DispatchAttachmentStore.MaxBytes + 1024 * 1024)]
        public async Task<IActionResult> AddNoticeItem(long noticeId, string? itemName, string? quantity, string? unit, string? notes, IFormFile? file)
        {
            if (!Allowed(NoticeBoardUrl)) return Fail("You are not allowed to use the notice board.");
            itemName = Clean(itemName); unit = Clean(unit); notes = Clean(notes);

            if (itemName.Length == 0) return Fail("Enter the Item name.");
            if (itemName.Length > 100) return Fail("Item name is too long (maximum 100 characters).");
            if (unit.Length > 50) return Fail("Unit is too long (maximum 50 characters).");
            if (notes.Length > 200) return Fail("Remarks is too long (maximum 200 characters).");
            if (!TryQuantity(quantity, out decimal qty)) return Fail("Quantity must be a number greater than 0.");
            if (!_repo.IsOwnNoticeDraft(noticeId, StaffId)) return Fail("This notice cannot be changed any more. Please reload the page.");

            try
            {
                string attachment = "";
                bool merges = _repo.NoticeDraftHasItem(noticeId, itemName);
                if (file != null && file.Length > 0 && !merges)
                {
                    var saved = await _files.SaveAsync(file, noticeId);
                    if (saved.Error != null) return Fail(saved.Error);
                    attachment = saved.FileName!;
                }
                int result = _repo.AddNoticeItem(noticeId, StaffId, itemName, qty, unit, notes, attachment);
                if (result == 0) return Fail("This notice cannot be changed any more. Please reload the page.");
                string msg = result == 2
                    ? "The item was already in the list - quantity added." + (file != null && file.Length > 0 ? " (The file was not attached.)" : "")
                    : "Item added.";
                return Json(new { success = true, message = msg, items = NoticeItemRows(noticeId) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Add notice item failed. NoticeId={NoticeId}", noticeId);
                return Fail("Item could not be added. Please try again.");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateNoticeItem(long id, string? quantity, string? unit, string? notes)
        {
            unit = Clean(unit); notes = Clean(notes);
            if (!TryQuantity(quantity, out decimal qty)) return Fail("Quantity must be a number greater than 0.");
            if (unit.Length > 50) return Fail("Unit is too long (maximum 50 characters).");
            if (notes.Length > 200) return Fail("Remarks is too long (maximum 200 characters).");

            var item = _repo.GetNoticeItem(id);
            if (item == null) return Fail("Item not found.");
            try
            {
                if (_repo.UpdateNoticeItem(id, StaffId, qty, unit, notes) == 0) return Fail("This item cannot be changed.");
                return Json(new { success = true, message = "Item updated.", items = NoticeItemRows(item.DISPATCHID) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Update notice item failed. ItemId={ItemId}", id);
                return Fail("Item could not be updated. Please try again.");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteNoticeItem(long id)
        {
            var item = _repo.GetNoticeItem(id);
            if (item == null) return Fail("Item not found.");
            try
            {
                if (_repo.DeleteNoticeItem(id, StaffId) == 0) return Fail("This item cannot be deleted.");
                return Json(new { success = true, message = "Item deleted.", items = NoticeItemRows(item.DISPATCHID) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Delete notice item failed. ItemId={ItemId}", id);
                return Fail("Item could not be deleted. Please try again.");
            }
        }

        // old btnSave_Click -> dalNoticeBorad.Update (needs at least one item, as the old page)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveNotice(SaveNoticeRequest r)
        {
            if (!Allowed(NoticeBoardUrl)) return Fail("You are not allowed to use the notice board.");
            if (r.DispatchType <= 0 || !_repo.GetDispatchTypes().Any(t => t.ID == r.DispatchType)) return Fail("Select the Dispatch Type.");

            decimal lc = 0;                                                   // old: "0" when the type is not LC
            if (r.DispatchType == 3)
            {
                if (!decimal.TryParse(Clean(r.LcAmount), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out lc)
                    || lc < 0 || lc >= 100000000m)
                    return Fail("Enter the LC Amount (USD) as a number.");
                lc = Math.Round(lc, 2);
            }

            r.DocFrom = Clean(r.DocFrom); r.TrackingNo = Clean(r.TrackingNo); r.FromMobile = Clean(r.FromMobile);
            r.FromEmail = Clean(r.FromEmail); r.FromAddress = Clean(r.FromAddress); r.FromRemarks = Clean(r.FromRemarks);
            r.ToStaffId = FirstPart(r.ToStaffId); r.ToName = Clean(r.ToName); r.ToLocation = Clean(r.ToLocation);
            r.ToCompany = Clean(r.ToCompany); r.ToDepartment = Clean(r.ToDepartment); r.ToLevel = Clean(r.ToLevel);
            r.ToMobile = Clean(r.ToMobile); r.ToEmail = Clean(r.ToEmail); r.ToAddress = Clean(r.ToAddress); r.ToRemarks = Clean(r.ToRemarks);

            if (r.DocFrom.Length == 0) return Fail("Enter From");                                        // old validator texts
            if (!r.RecDate.HasValue) return Fail("Enter the Receive Date.");
            string? tooLong = TooLong(
                ("From", r.DocFrom, 100), ("Tracking No", r.TrackingNo, 50), ("Sender Mobile", r.FromMobile, 15), ("Sender Email", r.FromEmail, 50),
                ("Sender Address", r.FromAddress, 200), ("Sender Remarks", r.FromRemarks, 200), ("To Staff ID", r.ToStaffId, 50),
                ("Receiver Name", r.ToName, 50), ("Receiver Location", r.ToLocation, 50), ("Receiver Company", r.ToCompany, 50),
                ("Receiver Department", r.ToDepartment, 50), ("Level", r.ToLevel, 50), ("Receiver Mobile", r.ToMobile, 50),
                ("Receiver Email", r.ToEmail, 50), ("Receiver Address", r.ToAddress, 200), ("Remarks", r.ToRemarks, 150));
            if (tooLong != null) return Fail(tooLong);

            if (r.TrackingNo.Length > 0 && _repo.TrackingNoUsed(r.TrackingNo, r.NoticeId)) return Fail($"Tracking No {r.TrackingNo} is already used.");
            if (!_repo.IsOwnNoticeDraft(r.NoticeId, StaffId)) return Fail("This notice was already saved. Please reload the page.");
            if (_repo.GetNoticeItems(r.NoticeId).Count == 0) return Fail("Please Add Items");

            try
            {
                if (!_repo.SubmitNotice(r, lc, StaffId, UserId)) return Fail("This notice was already saved. Please reload the page.");
                return Json(new { success = true, message = "Saved successfully.", url = Url.Action(nameof(NoticeView), new { id = r.NoticeId }) });
            }
            catch (OracleException ex) when (ex.Number == 1)                  // TRACKINGNO_UK: saved by someone else at the same moment
            {
                return Fail($"Tracking No {r.TrackingNo} is already used.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save notice failed. NoticeId={NoticeId}", r.NoticeId);
                return Fail("Save failed. Please try again.");
            }
        }

        private static string? TooLong(params (string Name, string Value, int Max)[] fields)
        {
            foreach (var f in fields)
                if (f.Value.Length > f.Max) return $"{f.Name} is too long (maximum {f.Max} characters).";
            return null;
        }

        // old NView.aspx
        [HttpGet]
        public IActionResult NoticeView(long id)
        {
            if (string.IsNullOrWhiteSpace(StaffId)) return RedirectToAction("Login", "Accounts");
            var n = _repo.GetNotice(id);
            if (n == null) return NotFound();
            if (!CanSeeNotice(n)) return RedirectToAction("Denied", "Accounts");

            ViewData["Title"] = "Notice " + (string.IsNullOrWhiteSpace(n.TRACKINGNO) ? "#" + n.ID : n.TRACKINGNO);
            return View("NoticeView", new NoticeDetailsViewModel { Notice = n, Items = _repo.GetNoticeItems(id), AttachmentsEnabled = _files.Enabled });
        }

        private bool CanSeeNotice(NoticeDetailModel n) =>
            n.CREATEDBY == StaffId || n.TSTAFFID == StaffId || n.RECSTAFFID == StaffId
            || Allowed(NoticeBoardUrl) || Allowed(NoticeReceiveUrl) || Allowed("Dispatch/ReportNoticeBoard");

        [HttpGet]
        public IActionResult NoticeAttachment(long id)
        {
            var item = _repo.GetNoticeItem(id);
            if (item == null || string.IsNullOrWhiteSpace(item.ATTACHMENT)) return NotFound();
            if (!_repo.IsOwnNoticeDraft(item.DISPATCHID, StaffId))
            {
                var n = _repo.GetNotice(item.DISPATCHID);
                if (n == null || !CanSeeNotice(n)) return RedirectToAction("Denied", "Accounts");
            }
            var f = _files.Find(item.ATTACHMENT);
            if (f == null) return NotFound(_files.Enabled ? "The attached file was not found." : "Attachments are not configured on this server.");
            return PhysicalFile(f.Value.Path, f.Value.ContentType, Path.GetFileName(f.Value.Path));
        }

        // old RNItems btnAssign_Click -> UpdateReceiver (the list itself is the report page NoticeReceive)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ReceiveNotice(NoticeReceiveRequest r)
        {
            if (!Allowed(NoticeReceiveUrl)) return Fail("You are not allowed to hand over notice board items.");
            r.StaffId = FirstPart(r.StaffId); r.Name = Clean(r.Name); r.Company = Clean(r.Company); r.Department = Clean(r.Department);
            r.Mobile = Clean(r.Mobile); r.Email = Clean(r.Email); r.Remarks = Clean(r.Remarks);

            if (r.Name.Length == 0) return Fail("Enter the Receiver Name.");
            string? tooLong = TooLong(("Receiver Staff ID", r.StaffId, 50), ("Receiver Name", r.Name, 50), ("Receiver Company", r.Company, 100),
                                      ("Receiver Department", r.Department, 50), ("Receiver Mobile", r.Mobile, 50), ("Receiver Email", r.Email, 50),
                                      ("Receiver Remarks", r.Remarks, 200));
            if (tooLong != null) return Fail(tooLong);
            try
            {
                return _repo.ReceiveNotice(r, UserId)
                    ? Json(new { success = true, message = "Saved successfully." })
                    : Fail("This Item's already received !");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Notice receive failed. NoticeId={NoticeId}", r.Id);
                return Fail("Save failed. Please try again.");
            }
        }

        private object NoticeItemRows(long noticeId) =>
            _repo.GetNoticeItems(noticeId).Select(i => new
            {
                id = i.ID,
                name = i.ITEMNAME,
                quantity = i.QUANTITY,
                unit = i.UNIT,
                notes = i.NOTES,
                attachment = string.IsNullOrWhiteSpace(i.ATTACHMENT) ? null : i.ATTACHMENT,
                attachmentUrl = string.IsNullOrWhiteSpace(i.ATTACHMENT) ? null : Url.Action(nameof(NoticeAttachment), new { id = i.ID })
            }).ToList();
    }
}
