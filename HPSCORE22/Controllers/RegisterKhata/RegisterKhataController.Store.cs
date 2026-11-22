// Controllers/RegisterKhata/RegisterKhataController.Store.cs
// Register Khata warehouse pages (port of YA Pages/RegisterKhata):
//   /RegisterKhata/ItemMaster           <- ItemMaster.aspx           (items of the user's warehouse)
//   /RegisterKhata/ItemIn               <- ItemIn.aspx               (stock in: ITEMSTOCK + quantity)
//   /RegisterKhata/Issue                <- Issue.aspx                (issue to a staff: ITEMSTOCK - quantity, optional dispatch)
//   /RegisterKhata/Receive              <- Receive.aspx              (return of an issue: ITEMSTOCK + quantity, closes the issue)
//   /RegisterKhata/WarehousePermission  <- WarehousePermission.aspx  (EM_EMPLOYEE.WAREHOUSEID of a user)
// "The user's warehouse" = old GetWarehouseByBusinessCenerId: EM_EMPLOYEE.WAREHOUSEID of the logged in user when he belongs to
// the chosen location. Matched on the session STAFFID (EM_EMPLOYEE.STAFFID holds staff ids). YA compared STAFFID with
// SessionName.UserId, which found nothing for users whose login id is not their staff id.
using AlphaCare.Models;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;

namespace QCMS.Controllers
{
    public partial class RegisterKhataController
    {
        private const int IssueTypeForDispatch = 6;     // old objDispatch.Update(..., Convert.ToInt32(6), ...)

        private static bool TryQty(string? text, out decimal qty) =>
            decimal.TryParse((text ?? "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out qty) && qty > 0 && qty < 1_000_000_000m;

        private static bool TryPrice(string? text, out decimal? price)
        {
            price = null;
            string t = (text ?? "").Trim();
            if (t.Length == 0) return true;                                                         // price was optional in YA
            if (!decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var p) || p < 0) return false;
            price = p;
            return true;
        }

        private List<RkOption> MyWarehouses(int centerId) => centerId > 0 ? _repo.GetUserWarehouses(StaffId, centerId) : new List<RkOption>();

        private bool IsMyWarehouse(long warehouseId) => warehouseId > 0 && _repo.IsUserWarehouse(StaffId, warehouseId);

        private RkStorePageModel StorePage(int centerId, long warehouseId)
        {
            var centers = _repo.GetAllBusinessCenters();
            var vm = new RkStorePageModel { Locations = centers, CenterId = PickLocation(centerId, centers) };
            vm.Warehouses = MyWarehouses(vm.CenterId);
            vm.WarehouseId = warehouseId > 0 && vm.Warehouses.Any(w => w.ID == warehouseId) ? warehouseId : vm.Warehouses.FirstOrDefault()?.ID ?? 0;
            return vm;
        }

        // ---------- lookups ----------
        [HttpGet]
        public IActionResult Warehouses(int centerId) => Json(MyWarehouses(centerId).Select(o => new { id = o.ID, name = o.NAME }));

        [HttpGet]
        public IActionResult WarehouseLookups(long warehouseId)
        {
            if (warehouseId <= 0) return Json(new { departments = Array.Empty<object>(), items = Array.Empty<object>() });
            return Json(new
            {
                departments = _repo.GetDepartments(warehouseId).Select(o => new { id = o.ID, name = o.NAME }),
                items = _repo.GetItemOptions(warehouseId, true).Select(o => new { id = o.ID, name = o.NAME })
            });
        }

        // old ddlItemName_OnSelectedIndexChanged (+ Issue: LoadItemstock)
        [HttpGet]
        public IActionResult ItemInfo(long itemId, long warehouseId = 0)
        {
            var u = itemId > 0 ? _repo.GetItemUnit(itemId) : null;
            if (u == null) return Json(new { found = false });
            decimal? available = warehouseId > 0 ? _repo.GetAvailableQty(warehouseId, itemId) : null;
            return Json(new { found = true, unitId = u.UNITID, unitName = u.UNITNAME, itemCode = u.ITEMCODE, available });
        }

        // =====================================================================================================
        // ITEM MASTER
        // =====================================================================================================
        [HttpGet]
        public IActionResult ItemMaster(int centerId = 0, long warehouseId = 0)
        {
            if (PageGuard("RegisterKhata/ItemMaster") is { } g) return g;
            var vm = StorePage(centerId, warehouseId);
            vm.Units = _repo.GetUnits();
            if (vm.WarehouseId > 0) vm.ItemRows = _repo.GetItems(vm.WarehouseId);
            ViewData["Title"] = "Item Master";
            return View("ItemMaster", vm);
        }

        // old btnSave_Click / btnEdit_Click (edit changes the unit and the description only)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveItem(long id, long warehouseId, string? itemName, string? itemCode, long unitId, string? description)
        {
            if (!Allowed("RegisterKhata/ItemMaster")) return Fail("You are not allowed to change items.");
            itemName = (itemName ?? "").Trim();
            itemCode = (itemCode ?? "").Trim();
            description = (description ?? "").Trim();
            if (unitId <= 0 || !_repo.GetUnits().Any(u => u.ID == unitId)) return Fail("Select Item Unit");
            if (description.Length > 500) return Fail("Description is too long (maximum 500 characters).");

            try
            {
                if (id > 0)
                {
                    var item = _repo.GetItem(id);
                    if (item == null || !IsMyWarehouse(item.WAREHOUSEID ?? 0)) return Fail("Item not found in your warehouse. Please reload the page.");
                    _repo.UpdateItem(id, unitId, description, UserId);
                    return Json(new { success = true, message = "Item updated." });
                }

                var errors = new List<string>();
                if (!IsMyWarehouse(warehouseId)) errors.Add("Select Service Center (warehouse)");
                if (itemName.Length == 0) errors.Add("Enter Item Name");
                if (itemCode.Length == 0) errors.Add("Enter Item Code");
                if (itemName.Length > 200 || itemCode.Length > 100) errors.Add("Item name or code is too long.");
                if (errors.Count > 0) return Fail(string.Join(" ", errors));
                if (_repo.ItemCodeExists(itemCode, warehouseId)) return Fail("This Item code already exists in the warehouse. Please try another.");

                _repo.InsertItem(warehouseId, itemName, itemCode, unitId, description, StaffId);
                return Json(new { success = true, message = "Item saved." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save RK item failed. Id={Id}", id);
                return Fail("Save Fail. " + DbReason(ex));
            }
        }

        // =====================================================================================================
        // ITEM IN
        // =====================================================================================================
        [HttpGet]
        public IActionResult ItemIn(int centerId = 0, long warehouseId = 0)
        {
            if (PageGuard("RegisterKhata/ItemIn") is { } g) return g;
            var vm = StorePage(centerId, warehouseId);
            if (vm.WarehouseId > 0)
            {
                vm.Items = _repo.GetItemOptions(vm.WarehouseId, true);
                vm.ItemInRows = _repo.GetItemIns(vm.WarehouseId);
            }
            ViewData["Title"] = "Item In";
            return View("ItemIn", vm);
        }

        // old btnSave_Click (ITEMSTOCK + qty) / btnEdit_Click (quantity + price, ITEMSTOCK - old + new)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveItemIn(long id, long warehouseId, long itemId, string? quantity, string? price, string? validityDate)
        {
            if (!Allowed("RegisterKhata/ItemIn")) return Fail("You are not allowed to add stock.");
            if (!TryQty(quantity, out decimal qty)) return Fail("Enter Item Quantity (a number greater than 0).");
            if (!TryPrice(price, out decimal? pr)) return Fail("Price must be a number.");
            if (!TryDate(validityDate, out var valid)) return Fail("Validity Date is not a valid date.");

            try
            {
                if (id > 0)
                {
                    var row = _repo.GetItemIn(id);
                    if (row == null || !IsMyWarehouse(row.WAREHOUSEID ?? 0)) return Fail("Entry not found in your warehouse. Please reload the page.");
                    if (_repo.UpdateItemIn(id, qty, pr, UserId) == 0) return Fail("Entry not found. Please reload the page.");
                    return Json(new { success = true, message = "Item In updated." });
                }

                if (!IsMyWarehouse(warehouseId)) return Fail("Select Service Center (warehouse)");
                if (itemId <= 0 || !_repo.GetItemOptions(warehouseId, true).Any(i => i.ID == itemId)) return Fail("Select Item Name");
                var unit = _repo.GetItemUnit(itemId);
                if (unit?.UNITID == null) return Fail("Enter Unit Name (the item has no unit).");

                _repo.InsertItemIn(warehouseId, itemId, qty, unit.UNITID, pr, valid, StaffId);
                return Json(new { success = true, message = "Item In saved." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save RK item in failed. Id={Id}", id);
                return Fail("Save Fail. " + DbReason(ex));
            }
        }

        // =====================================================================================================
        // ISSUE
        // =====================================================================================================
        [HttpGet]
        public IActionResult Issue(int centerId = 0, long warehouseId = 0)
        {
            if (PageGuard("RegisterKhata/Issue") is { } g) return g;
            var vm = StorePage(centerId, warehouseId);
            if (vm.WarehouseId > 0)
            {
                vm.Departments = _repo.GetDepartments(vm.WarehouseId);
                vm.Items = _repo.GetItemOptions(vm.WarehouseId, true);
            }
            ViewData["Title"] = "Issue";
            return View("Issue", vm);
        }

        // old [WebMethod] staffChecker (HRIS)
        [HttpGet]
        public async Task<IActionResult> HrisStaff(string staffId)
        {
            var r = await _hris.FindAsync(FirstPart(staffId));
            return Json(new { status = r.Status, staffId = r.StaffId, name = r.Name, designation = r.Designation, contact = r.Contact, email = r.Email });
        }

        // old btnSave_OnClick (both branches: with / without "Dispatch Send")
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveIssue(RkIssueSave r)
        {
            if (!Allowed("RegisterKhata/Issue")) return Fail("You are not allowed to issue items.");
            r.StaffId = FirstPart(r.StaffId);
            r.Purpose = (r.Purpose ?? "").Trim();

            var errors = new List<string>();
            if (r.LocationId <= 0) errors.Add("Select Business Center");
            if (r.WarehouseId <= 0 || !MyWarehouses(r.LocationId).Any(w => w.ID == r.WarehouseId)) errors.Add("Select Service Center (warehouse)");
            if (r.DepartmentId <= 0 || !_repo.GetDepartments(r.WarehouseId).Any(d => d.ID == r.DepartmentId)) errors.Add("Select Department");
            if (r.StaffId.Length == 0) errors.Add("Enter Staff");
            if (r.Purpose.Length == 0) errors.Add("Enter Purpose");
            if (r.StaffId.Length > 50 || r.Purpose.Length > 500) errors.Add("Staff or purpose is too long.");
            if (!TryDate(r.ReturnDate, out var returnDate)) errors.Add("Return Date is not a valid date.");
            else if (returnDate.HasValue && returnDate.Value < DateTime.Today) errors.Add("Return Date should not be earlier than Issue Date.");
            if (errors.Count > 0) return Fail(string.Join(" ", errors));

            var lines = r.Lines ?? new List<RkIssueLine>();
            if (lines.Count == 0) return Fail("Please add item first.");
            if (lines.Select(l => l.ItemId).Distinct().Count() != lines.Count) return Fail("An item is added twice. Please remove the duplicate.");

            var items = _repo.GetItemOptions(r.WarehouseId, true).ToDictionary(i => i.ID);
            var checkedLines = new List<RkIssueLineChecked>();
            foreach (var l in lines)
            {
                if (!items.TryGetValue(l.ItemId, out var item)) return Fail("An item does not belong to this warehouse. Please reload the page.");
                if (!TryQty(l.Qty, out decimal qty)) return Fail($"Quantity of {item.NAME} must be a number greater than 0.");
                var unit = _repo.GetItemUnit(l.ItemId);
                string rem = (l.Remarks ?? "").Trim();
                if (rem.Length > 500) rem = rem[..500];
                checkedLines.Add(new RkIssueLineChecked { ItemId = l.ItemId, UnitId = unit?.UNITID, UnitName = unit?.UNITNAME, ItemName = item.NAME ?? "", Qty = qty, Remarks = rem });
            }
            r.ReturnDate = returnDate?.ToString("yyyy-MM-dd");

            // "Dispatch Send": the issued items go to the staff as a dispatch (issue type 6), sender = the logged in user
            Func<string, string?>? sendDispatch = null;
            string? dispatchTx = null;
            if (r.Dispatch)
            {
                var receiver = _dispatch.GetEmployee(r.StaffId);
                var sender = _dispatch.GetEmployee(StaffId);
                if (receiver == null) return Fail("Dispatch Send: staff " + r.StaffId + " is not in the employee list, so a dispatch cannot be addressed to him.");
                if (sender == null) return Fail("Dispatch Send: your employee record was not found.");
                if (_dispatch.GetIssueType(IssueTypeForDispatch) == null) return Fail("Dispatch Send: dispatch issue type 6 is not active.");

                sendDispatch = trNo =>
                {
                    long draftId = _dispatch.GetOrCreateDraft(StaffId);
                    _dispatch.ClearDraftItems(draftId, StaffId);
                    foreach (var line in checkedLines)
                        _dispatch.AddDraftItem(draftId, StaffId, line.ItemName, line.Qty, line.UnitName ?? "", line.Remarks, "");
                    var m = new SubmitDispatchModel
                    {
                        DispatchId = draftId,
                        IssueTypeId = IssueTypeForDispatch,
                        ReceiverStaffId = receiver.STAFFID,
                        SenderStaffId = StaffId,
                        ReceiverName = receiver.EMPLOYEENAME ?? "",
                        ReceiverMobile = receiver.OFFICEMOBILE ?? "",
                        ReceiverAddress = "",
                        ReceiverDepartment = receiver.DEPARTMENTCODE ?? "",
                        ReceiverCenter = receiver.CENTERNAME ?? "",
                        SenderDepartmentId = sender.DEPARTMENTID ?? 0,
                        SenderCenterId = sender.BUSINESSCENTERID ?? 0,
                        ReceiverDepartmentId = receiver.DEPARTMENTID ?? 0,
                        ReceiverCenterId = receiver.BUSINESSCENTERID ?? 0,
                        DirectSend = 0,
                        SenderName = sender.EMPLOYEENAME ?? "",
                        SenderMobile = sender.OFFICEMOBILE ?? ""
                    };
                    dispatchTx = _dispatch.SubmitDispatch(m, StaffId, UserId, UserCenterId);
                    return dispatchTx == null ? "The dispatch could not be created, so the issue was not saved. Please try again." : null;
                };
            }

            try
            {
                var (trNo, error) = _repo.SaveIssue(r, checkedLines, StaffId, sendDispatch);
                if (error != null) return Fail(error);
                return Json(new { success = true, trNo, message = $"Issue {trNo} saved." + (dispatchTx != null ? $" Dispatch {dispatchTx} sent." : "") });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save RK issue failed.");
                return Fail("Save Fail. " + DbReason(ex));
            }
        }

        // =====================================================================================================
        // RECEIVE
        // =====================================================================================================
        [HttpGet]
        public IActionResult Receive(int centerId = 0)
        {
            if (PageGuard("RegisterKhata/Receive") is { } g) return g;
            var vm = StorePage(centerId, 0);
            vm.WarehouseId = 0;                          // old: "Select" first, the lists follow the chosen warehouse
            ViewData["Title"] = "Receive";
            return View("Receive", vm);
        }

        // old ddlWarehouse / ddlDepartment SelectedIndexChanged: departments + open issues ("TRNO--STAFFID")
        [HttpGet]
        public IActionResult ReceiveLookups(long warehouseId, long departmentId = 0)
        {
            if (!Allowed("RegisterKhata/Receive") || warehouseId <= 0) return Json(new { departments = Array.Empty<object>(), issues = Array.Empty<object>() });
            return Json(new
            {
                departments = departmentId > 0 ? null : _repo.GetDepartments(warehouseId).Select(o => new { id = o.ID, name = o.NAME }),
                issues = _repo.GetOpenIssues(warehouseId, departmentId).Select(o => new { id = o.ID, name = o.NAME })
            });
        }

        // old ddlIssueTrNo_SelectedIndexChanged: staff, return date and the lines still to receive
        [HttpGet]
        public IActionResult IssueLines(long issueId)
        {
            if (!Allowed("RegisterKhata/Receive")) return Fail("Not allowed.");
            var h = _repo.GetIssueHeader(issueId);
            if (h == null || string.IsNullOrEmpty(h.TRNO)) return Fail("This issue is not open any more.");
            var lines = _repo.GetPendingLines(h.TRNO);
            return Json(new
            {
                success = true,
                trNo = h.TRNO, staffId = h.STAFFID, purpose = h.PURPOSE,
                issueDate = h.ISSUEDATE?.ToString("dd/MM/yyyy"), returnDate = h.RETURNDATE?.ToString("dd/MM/yyyy"),
                lines = lines.Select(l => new { id = l.ID, itemId = l.ITEMID, itemName = l.ITEMNAME, itemCode = l.ITEMCODE, unitName = l.UNITNAME,
                                                issQty = l.ISSQTY, recQty = l.RECQTY, remarks = l.REMARKS })
            });
        }

        // old btnSave_OnClick
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveReceive(long issueId, List<RkReceiveLine> lines)
        {
            if (!Allowed("RegisterKhata/Receive")) return Fail("You are not allowed to receive items.");
            if (issueId <= 0) return Fail("Select Issue TR No");
            lines ??= new List<RkReceiveLine>();
            if (lines.Count == 0) return Fail("Please add item first.");
            if (lines.Select(l => l.LineId).Distinct().Count() != lines.Count) return Fail("An item is listed twice. Please reload the issue.");
            try
            {
                var (closed, error) = _repo.SaveReceive(issueId, lines, StaffId);
                if (error != null) return Fail(error);
                return Json(new { success = true, closed, message = "Receive saved." + (closed ? " Everything of this issue is back - the issue is closed." : "") });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save RK receive failed. Issue={Issue}", issueId);
                return Fail("Save Fail. " + DbReason(ex));
            }
        }

        // =====================================================================================================
        // WAREHOUSE PERMISSION
        // =====================================================================================================
        [HttpGet]
        public IActionResult WarehousePermission()
        {
            if (PageGuard("RegisterKhata/WarehousePermission") is { } g) return g;
            ViewData["Title"] = "Warehouse Permission";
            return View("WarehousePermission", new RkPermissionPageModel { Warehouses = _repo.GetAllWarehouses() });
        }

        // old tbxUserName_OnTextChanged: the user and his current warehouse
        [HttpGet]
        public IActionResult StaffWarehouse(string staffId)
        {
            if (!Allowed("RegisterKhata/WarehousePermission")) return Fail("Not allowed.");
            string id = FirstPart(staffId);
            var e = id.Length > 0 ? _dispatch.GetEmployee(id) : null;
            if (e == null) return Fail("Employee not found.");
            return Json(new { success = true, staffId = e.STAFFID, name = e.EMPLOYEENAME, center = e.CENTERNAME, warehouseId = _repo.GetEmployeeWarehouse(e.STAFFID) });
        }

        // old btnSave_Click: EM_EMPLOYEE.WAREHOUSEID (one warehouse per user)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveWarehousePermission(string? staffId, long warehouseId)
        {
            if (!Allowed("RegisterKhata/WarehousePermission")) return Fail("You are not allowed to change warehouse permissions.");
            string id = FirstPart(staffId);
            var e = id.Length > 0 ? _dispatch.GetEmployee(id) : null;
            if (e == null) return Fail("Enter User Name (employee).");
            if (warehouseId <= 0 || !_repo.GetAllWarehouses().Any(w => w.ID == warehouseId)) return Fail("Please select at least One Warehouse !");
            try
            {
                _repo.SetEmployeeWarehouse(e.STAFFID, warehouseId);
                return Json(new { success = true, message = "Warehouse saved for " + e.EMPLOYEENAME + "." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save warehouse permission failed. Staff={Staff}", id);
                return Fail("Save failed. " + DbReason(ex));
            }
        }
    }
}
