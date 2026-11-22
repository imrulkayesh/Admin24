// Controllers/RegisterKhata/RegisterKhataController.cs
// Register Khata module (port of YA Pages/RegisterKhata). Registers of a location:
//   /RegisterKhata/GuestKhata      <- GuestKhata.aspx     (guest register)
//   /RegisterKhata/Documents       <- Documents.aspx      (document in / out register)
//   /RegisterKhata/VehicleInOut    <- VehicleInOut.aspx   (vehicle in / out register, last 15 days)
// Warehouse pages: RegisterKhataController.Store.cs. Reports: RegisterKhataController.Reports.cs.
// Add / edit only: the delete buttons were switched off in YA. Every page and save needs the page in the user's role menu.
using AlphaCare.Interface;
using AlphaCare.Models;
using AlphaCare.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Text.RegularExpressions;

namespace QCMS.Controllers
{
    [Authorize]
    public partial class RegisterKhataController : Controller
    {
        private readonly IRegisterKhataRepository _repo;
        private readonly IDispatchRepository _dispatch;
        private readonly HrisStaffService _hris;
        private readonly ILogger<RegisterKhataController> _logger;

        public RegisterKhataController(IRegisterKhataRepository repo, IDispatchRepository dispatch, HrisStaffService hris,
                                       ILogger<RegisterKhataController> logger)
        {
            _repo = repo;
            _dispatch = dispatch;
            _hris = hris;
            _logger = logger;
        }

        private string StaffId => HttpContext.Session.GetString("STAFFID") ?? "";
        private string UserId => HttpContext.Session.GetString("USERID") ?? "";
        private int UserCenterId => HttpContext.Session.GetInt32("BusinessCenterID") ?? 0;

        private bool Allowed(string url) => !string.IsNullOrWhiteSpace(StaffId) && _repo.HasMenuPermission(StaffId, url);

        private IActionResult? PageGuard(string url)
        {
            if (string.IsNullOrWhiteSpace(StaffId)) return RedirectToAction("Login", "Accounts");
            if (!Allowed(url)) return RedirectToAction("Denied", "Accounts");
            return null;
        }

        private JsonResult Fail(string message) => Json(new { success = false, message });

        // first line of the Oracle message (YA's warehouse pages showed "Save Fail" + ex.Message)
        private static string DbReason(Exception ex)
        {
            string m = (ex.Message ?? "").Split('\n')[0].Trim();
            return m.Length > 200 ? m[..200] : m;
        }

        // auto complete text "STAFFID-..." -> STAFFID (old Split('-')[0])
        private static string FirstPart(string? text) => (text ?? "").Trim().Split('-')[0].Trim();

        private static readonly string[] DateFormats = { "yyyy-MM-dd", "dd/MM/yyyy" };
        private static bool TryDate(string? text, out DateTime? value)
        {
            value = null;
            string t = (text ?? "").Trim();
            if (t.Length == 0) return true;                                   // optional, as in YA
            if (!DateTime.TryParseExact(t, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return false;
            value = d.Date;
            return true;
        }

        private static bool Digits(string? text) => !string.IsNullOrWhiteSpace(text) && Regex.IsMatch(text.Trim(), @"^\d+$");

        private int PickLocation(int locationId, List<BusinessCenterModel> centers) =>
            locationId > 0 && centers.Any(c => c.ID == locationId) ? locationId
            : centers.Any(c => c.ID == UserCenterId) ? UserCenterId
            : centers.FirstOrDefault()?.ID ?? 0;

        private static string D(DateTime? d) => d?.ToString("yyyy-MM-dd") ?? "";

        private static void Trim(RkRegisterSave r)
        {
            foreach (var p in typeof(RkRegisterSave).GetProperties().Where(p => p.PropertyType == typeof(string) && p.CanWrite))
                p.SetValue(r, ((string?)p.GetValue(r))?.Trim() ?? "");
        }

        private IActionResult SaveRegister(string url, RkRegisterSave r, List<string> errors, Func<DateTime?, DateTime?, int> insert,
                                           Func<DateTime?, DateTime?, int> update, string what)
        {
            if (!Allowed(url)) return Fail("You are not allowed to change this register.");
            if (r.LocationId <= 0 || !_repo.GetAllBusinessCenters().Any(c => c.ID == r.LocationId)) errors.Insert(0, "Select Location");
            if (!TryDate(r.InDate, out var inDate)) errors.Add("In Date is not a valid date.");
            if (!TryDate(r.OutDate, out var outDate)) errors.Add("Out Date is not a valid date.");
            foreach (var t in new[] { r.InTime, r.OutTime }) if ((t ?? "").Length > 50) { errors.Add("Time is too long (maximum 50 characters)."); break; }
            if ((r.Remarks ?? "").Length > 500) errors.Add("Remarks is too long (maximum 500 characters).");
            if (errors.Count > 0) return Fail(string.Join(" ", errors));

            try
            {
                if (r.Id > 0)
                {
                    if (update(inDate, outDate) == 0) return Fail(what + " not found. Please reload the page.");
                    return Json(new { success = true, message = what + " updated." });
                }
                insert(inDate, outDate);
                return Json(new { success = true, message = what + " saved." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save {What} failed. Id={Id}", what, r.Id);
                return Fail((r.Id > 0 ? "Update failed. " : "Save failed. ") + DbReason(ex));
            }
        }

        // =====================================================================================================
        // GUEST KHATA
        // =====================================================================================================
        [HttpGet]
        public IActionResult GuestKhata(int locationId = 0)
        {
            if (PageGuard("RegisterKhata/GuestKhata") is { } g) return g;
            var centers = _repo.GetAllBusinessCenters();
            var vm = new RkRegisterPageModel { Locations = centers, LocationId = PickLocation(locationId, centers), Designations = _repo.GetDesignations() };
            if (vm.LocationId > 0) vm.Guests = _repo.GetGuests(vm.LocationId);
            ViewData["Title"] = "Guest Khata";
            return View("GuestKhata", vm);
        }

        [HttpGet]
        public IActionResult GetGuest(long id)
        {
            if (!Allowed("RegisterKhata/GuestKhata")) return Fail("Not allowed.");
            var x = _repo.GetGuest(id);
            if (x == null) return Fail("Guest entry not found.");
            return Json(new { success = true, row = new { id = x.ID, locationId = x.LOCATIONID, guestName = x.GUESTNAME, designationId = x.DESIGNATIONID,
                mobile = x.MOBILE, address = x.ADDRESS, reason = x.REASON, inDate = D(x.INDATE), inTime = x.INTIME, outDate = D(x.OUTDATE),
                outTime = x.OUTTIME, remarks = x.REMARKS } });
        }

        // old btnSave_Click / btnEdit_Click (validators of GuestKhata.aspx)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveGuest(RkRegisterSave r)
        {
            Trim(r);
            var errors = new List<string>();
            if (r.GuestName!.Length == 0) errors.Add("Enter Guest Name");
            if (r.DesignationId <= 0) errors.Add("Select Designation");
            if (!Digits(r.Mobile)) errors.Add("Enter Mobile No (digits only)");
            if (r.Address!.Length == 0) errors.Add("Enter Address");
            if (r.Reason!.Length == 0) errors.Add("Enter Reason");
            if (r.InTime!.Length == 0) errors.Add("Enter In Time");
            return SaveRegister("RegisterKhata/GuestKhata", r, errors,
                (i, o) => _repo.InsertGuest(r, i, o, StaffId), (i, o) => _repo.UpdateGuest(r, i, o, StaffId), "Guest entry");
        }

        // =====================================================================================================
        // DOCUMENTS
        // =====================================================================================================
        [HttpGet]
        public IActionResult Documents(int locationId = 0)
        {
            if (PageGuard("RegisterKhata/Documents") is { } g) return g;
            var centers = _repo.GetAllBusinessCenters();
            var vm = new RkRegisterPageModel { Locations = centers, LocationId = PickLocation(locationId, centers) };
            if (vm.LocationId > 0) vm.Documents = _repo.GetDocuments(vm.LocationId);
            ViewData["Title"] = "Documents";
            return View("Documents", vm);
        }

        [HttpGet]
        public IActionResult GetDocument(long id)
        {
            if (!Allowed("RegisterKhata/Documents")) return Fail("Not allowed.");
            var x = _repo.GetDocument(id);
            if (x == null) return Fail("Document entry not found.");
            return Json(new { success = true, row = new { id = x.ID, locationId = x.LOCATIONID, documentName = x.DOCUMENTNAME, senderName = x.SENDERNAME,
                senderMobile = x.SENDERMOBILE, receiverName = x.RECEIVERNAME, receiverMobile = x.RECEIVERMOBILE, transport = x.TRANSPORT,
                inDate = D(x.INDATE), inTime = x.INTIME, outDate = D(x.OUTDATE), outTime = x.OUTTIME, remarks = x.REMARKS } });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveDocument(RkRegisterSave r)
        {
            Trim(r);
            var errors = new List<string>();
            if (r.DocumentName!.Length == 0) errors.Add("Enter Document Name");
            if (r.SenderName!.Length == 0) errors.Add("Enter Sender Name");
            if (!Digits(r.SenderMobile)) errors.Add("Enter Sender Mobile (digits only)");
            if (r.ReceiverName!.Length == 0) errors.Add("Enter Receiver Name");
            if (!Digits(r.ReceiverMobile)) errors.Add("Enter Receiver Mobile (digits only)");
            if (r.Transport!.Length == 0) errors.Add("Enter Transport");
            return SaveRegister("RegisterKhata/Documents", r, errors,
                (i, o) => _repo.InsertDocument(r, i, o, StaffId), (i, o) => _repo.UpdateDocument(r, i, o, StaffId), "Document entry");
        }

        // =====================================================================================================
        // VEHICLE IN / OUT
        // =====================================================================================================
        [HttpGet]
        public IActionResult VehicleInOut(int locationId = 0)
        {
            if (PageGuard("RegisterKhata/VehicleInOut") is { } g) return g;
            var centers = _repo.GetAllBusinessCenters();
            var vm = new RkRegisterPageModel { Locations = centers, LocationId = PickLocation(locationId, centers) };
            if (vm.LocationId > 0) vm.Vehicles = _repo.GetVehicles(vm.LocationId);
            ViewData["Title"] = "Vehicle In / Out";
            return View("VehicleInOut", vm);
        }

        [HttpGet]
        public IActionResult GetVehicle(long id)
        {
            if (!Allowed("RegisterKhata/VehicleInOut")) return Fail("Not allowed.");
            var x = _repo.GetVehicle(id);
            if (x == null) return Fail("Vehicle entry not found.");
            return Json(new { success = true, row = new { id = x.ID, locationId = x.LOCATIONID, vehicleNo = x.VEHICLENO, gateNo = x.GATENO,
                driverName = x.DRIVERNAME, driverMobile = x.DRIVERMOBILE, inDate = D(x.INDATE), inTime = x.INTIME, outDate = D(x.OUTDATE),
                outTime = x.OUTTIME, remarks = x.REMARKS } });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveVehicle(RkRegisterSave r)
        {
            Trim(r);
            var errors = new List<string>();
            if (r.VehicleNo!.Length == 0) errors.Add("Enter Vehicle No");
            if (r.GateNo!.Length == 0) errors.Add("Enter Gate No");
            if (r.DriverName!.Length == 0) errors.Add("Enter Driver Name");
            if (!Digits(r.DriverMobile)) errors.Add("Enter Driver Mobile No (digits only)");
            return SaveRegister("RegisterKhata/VehicleInOut", r, errors,
                (i, o) => _repo.InsertVehicle(r, i, o, StaffId), (i, o) => _repo.UpdateVehicle(r, i, o, StaffId), "Vehicle entry");
        }

        // employee auto complete (old SearchStaffId web method) - same search as the Dispatch module
        [HttpGet]
        public IActionResult SearchEmployee(string term)
        {
            if (string.IsNullOrWhiteSpace(term) || term.Trim().Length < 2) return Json(new List<string>());
            return Json(_dispatch.SearchEmployees(term));
        }
    }
}
