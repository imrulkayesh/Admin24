// Controllers/Admin/AdminController.Setup.cs
// The Admin setup screens of YA (Pages/Admin/*.aspx) as one generic page, the same way the dispatch
// reports share one page (ReportDef): every screen declares its form fields, cascades, grid columns and
// duplicate rule in a SetupDef below and renders Views/Admin/Setup.cshtml.
//   /Admin/Country          <- Country.aspx            /Admin/Division   <- Division.aspx
//   /Admin/District         <- District.aspx           /Admin/Thana      <- Thana.aspx
//   /Admin/Location         <- Location.aspx           /Admin/BusinessCenter <- BusinessCenter.aspx
//   /Admin/GroupofCompany   <- GroupofCompany.aspx     /Admin/Company    <- Company.aspx
//   /Admin/Department       <- Department.aspx         /Admin/DepartmentUnit <- DepartmentUnit.aspx
//   /Admin/Designation      <- Designation.aspx        /Admin/Gender     <- Gender.aspx
//   /Admin/Religion         <- Religion.aspx           /Admin/DepartmentAssign <- DepartmentAssign.aspx (own page)
// Dropdown changes and Edit reload the page with GET parameters (like the old AutoPostBack);
// Save / Delete post JSON like the dispatch setup pages.
using AlphaCare.Models;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using C = AlphaCare.Models.DispatchReportColumn;
using F = AlphaCare.Models.SetupField;

namespace Admin24.Controllers.Admin
{
    public partial class AdminController
    {
        private sealed class SetupDef
        {
            public string Key = "";
            public string MenuUrl = "";
            public string Title = "";
            public string Icon = "fa-gear";
            public string? Note;
            public bool CanDelete = true;                 // every old page had the delete button
            public string DupField = "";                  // old CheckDuplicate: the name column
            public string? DupParentField;                //   ... checked inside this parent (null = global)
            public string DupMessage = "This name already exists. Please try another.";
            public string? FilterField;                   // the select whose value filters the grid
            public List<F> Fields = new();
            public List<C> Columns = new();
        }

        private static readonly Dictionary<string, SetupDef> Defs = new()
        {
            [nameof(Country)] = new SetupDef
            {
                Key = nameof(Country), MenuUrl = "Admin/Country", Title = "Country", Icon = "fa-earth-asia",
                DupField = "COUNTRYNAME", DupMessage = "This Country already exists. Please try another.",
                Fields = new() { new("COUNTRYNAME", "Country Name", required: true), new("COUNTRYCODE", "Country Code", maxLen: 10) },
                Columns = new() { new("COUNTRYNAME", "Country"), new("COUNTRYCODE", "Code") }
            },
            [nameof(Division)] = new SetupDef
            {
                Key = nameof(Division), MenuUrl = "Admin/Division", Title = "Division", Icon = "fa-map",
                DupField = "DIVISIONNAME", DupParentField = "COUNTRYID", DupMessage = "This Division name already exists. Please try another.",
                FilterField = "COUNTRYID",
                Fields = new() { new("COUNTRYID", "Country", "select", required: true, lookupKey: "Country"),
                                 new("DIVISIONNAME", "Division Name", required: true) },
                Columns = new() { new("DIVISIONNAME", "Division") }
            },
            [nameof(District)] = new SetupDef
            {
                Key = nameof(District), MenuUrl = "Admin/District", Title = "District", Icon = "fa-map-location",
                DupField = "DISTRICTNAME", DupParentField = "DIVISIONID", DupMessage = "This District name already exists. Please try another.",
                FilterField = "DIVISIONID",
                Fields = new() { new("COUNTRYID", "Country", "select", save: false, lookupKey: "Country"),
                                 new("DIVISIONID", "Division", "select", required: true, lookupKey: "Division", parent: "COUNTRYID"),
                                 new("DISTRICTNAME", "District Name", required: true) },
                Columns = new() { new("DISTRICTNAME", "District") }
            },
            [nameof(Thana)] = new SetupDef
            {
                Key = nameof(Thana), MenuUrl = "Admin/Thana", Title = "Thana", Icon = "fa-map-pin",
                DupField = "THANANAME", DupParentField = "DISTRICTID", DupMessage = "This Thana name already exists. Please try another.",
                FilterField = "DISTRICTID",
                Fields = new() { new("COUNTRYID", "Country", "select", save: false, lookupKey: "Country"),
                                 new("DIVISIONID", "Division", "select", save: false, lookupKey: "Division", parent: "COUNTRYID"),
                                 new("DISTRICTID", "District", "select", required: true, lookupKey: "District", parent: "DIVISIONID"),
                                 new("THANANAME", "Thana Name", required: true) },
                Columns = new() { new("THANANAME", "Thana") }
            },
            [nameof(Location)] = new SetupDef
            {
                Key = nameof(Location), MenuUrl = "Admin/Location", Title = "Location", Icon = "fa-location-dot",
                DupField = "NAME", DupParentField = "THANAID", DupMessage = "This Location already exists. Please try another.",
                FilterField = "THANAID",
                Fields = new() { new("COUNTRYID", "Country", "select", required: true, lookupKey: "Country"),
                                 new("DIVISIONID", "Division", "select", save: false, lookupKey: "Division", parent: "COUNTRYID"),
                                 new("DISTRICTID", "District", "select", save: false, lookupKey: "District", parent: "DIVISIONID"),
                                 new("THANAID", "Thana", "select", required: true, lookupKey: "Thana", parent: "DISTRICTID"),
                                 new("NAME", "Location Name", required: true),
                                 new("LOCATIONCODE", "Location Code", maxLen: 20),
                                 new("TIMEZONE", "Time Zone", "select", maxLen: 100, lookupKey: "TimeZone") },
                Columns = new() { new("NAME", "Location"), new("LOCATIONCODE", "Code"), new("TIMEZONE", "Time Zone") }
            },
            [nameof(BusinessCenter)] = new SetupDef
            {
                Key = nameof(BusinessCenter), MenuUrl = "Admin/BusinessCenter", Title = "Business Center", Icon = "fa-building",
                DupField = "CENTERNAME", DupParentField = "LOCATIONID", DupMessage = "This Business Center already exists. Please try another.",
                FilterField = "LOCATIONID",
                Fields = new() { new("COUNTRYID", "Country", "select", save: false, lookupKey: "Country"),
                                 new("LOCATIONID", "Location", "select", required: true, lookupKey: "Location", parent: "COUNTRYID"),
                                 new("CENTERNAME", "Center Name", required: true),
                                 new("CENTERCODE", "Center Code", maxLen: 20),
                                 new("ADDRESS", "Address", maxLen: 200) },
                Columns = new() { new("CENTERNAME", "Business Center"), new("CENTERCODE", "Code"), new("ADDRESS", "Address") }
            },
            [nameof(GroupofCompany)] = new SetupDef
            {
                Key = nameof(GroupofCompany), MenuUrl = "Admin/GroupofCompany", Title = "Group of Company", Icon = "fa-building-flag",
                DupField = "COMPANYTITLE", DupMessage = "This Group of Company already exists. Please try another.",
                Fields = new() { new("COMPANYTITLE", "Group of Company", maxLen: 100, required: true),
                                 new("ESTABLISHDATE", "Establish Date", "date") },
                Columns = new() { new("COMPANYTITLE", "Group of Company"), new("ESTABLISHDATE", "Establish Date", "day"), new("CREATEDBY", "Created By") }
            },
            [nameof(Company)] = new SetupDef
            {
                Key = nameof(Company), MenuUrl = "Admin/Company", Title = "Company", Icon = "fa-industry",
                DupField = "COMPANYNAME", DupParentField = "GROUPOFCOMPANYID", DupMessage = "This Company already exists. Please try another.",
                FilterField = "GROUPOFCOMPANYID",
                Fields = new() { new("GROUPOFCOMPANYID", "Group of Company", "select", required: true, lookupKey: "GroupofCompany"),
                                 new("COMPANYNAME", "Company Name", maxLen: 100, required: true),
                                 new("COMESTABLISHDATE", "Establish Date", "date") },
                Columns = new() { new("COMPANYNAME", "Company"), new("COMPANYTITLE", "Group of Company"), new("COMESTABLISHDATE", "Establish Date", "day"), new("CREATEDBY", "Created By") }
            },
            [nameof(Department)] = new SetupDef
            {
                Key = nameof(Department), MenuUrl = "Admin/Department", Title = "Department", Icon = "fa-sitemap",
                DupField = "DEPARTMENTNAME", DupMessage = "This Department already exists. Please try another.",
                Fields = new() { new("DEPARTMENTNAME", "Department Name", required: true), new("DEPARTMENTCODE", "Department Code", maxLen: 20) },
                Columns = new() { new("DEPARTMENTNAME", "Department"), new("DEPARTMENTCODE", "Code"), new("CREATEDBY", "Created By") }
            },
            [nameof(DepartmentUnit)] = new SetupDef
            {
                Key = nameof(DepartmentUnit), MenuUrl = "Admin/DepartmentUnit", Title = "Department Unit", Icon = "fa-diagram-project",
                DupField = "DEPTUNITNAME", DupParentField = "DEPARTMENTID", DupMessage = "This Department Unit already exists. Please try another.",
                FilterField = "DEPARTMENTID",
                Fields = new() { new("DEPARTMENTID", "Department", "select", required: true, lookupKey: "Department"),
                                 new("DEPTUNITNAME", "Unit Name", required: true),
                                 new("DEPTUNITCODE", "Unit Code", maxLen: 20) },
                Columns = new() { new("DEPTUNITNAME", "Unit"), new("DEPTUNITCODE", "Code"), new("DEPARTMENTNAME", "Department") }
            },
            [nameof(Designation)] = new SetupDef
            {
                Key = nameof(Designation), MenuUrl = "Admin/Designation", Title = "Designation", Icon = "fa-id-badge",
                DupField = "DESIGNATIONNAME", DupMessage = "This Designation already exists. Please try another.",
                Fields = new() { new("DESIGNATIONNAME", "Designation Name", required: true), new("PRIORITY", "Priority", "int") },
                Columns = new() { new("DESIGNATIONNAME", "Designation"), new("PRIORITY", "Priority", "num"), new("CREATEDBY", "Created By") }
            },
            [nameof(Gender)] = new SetupDef
            {
                Key = nameof(Gender), MenuUrl = "Admin/Gender", Title = "Gender", Icon = "fa-venus-mars",
                DupField = "GENDERNAME", DupMessage = "This Gender already exists. Please try another.",
                Fields = new() { new("GENDERNAME", "Gender Name", required: true) },
                Columns = new() { new("GENDERNAME", "Gender") }
            },
            [nameof(Religion)] = new SetupDef
            {
                Key = nameof(Religion), MenuUrl = "Admin/Religion", Title = "Religion", Icon = "fa-hands-praying",
                DupField = "RELIGIONNAME", DupMessage = "This Religion already exists. Please try another.",
                Fields = new() { new("RELIGIONNAME", "Religion Name", required: true) },
                Columns = new() { new("RELIGIONNAME", "Religion") }
            },
        };

        // ---- one GET action per page (the URL is what the menu permission checks) ----
        [HttpGet] public IActionResult Country() => SetupPage(nameof(Country));
        [HttpGet] public IActionResult Division() => SetupPage(nameof(Division));
        [HttpGet] public IActionResult District() => SetupPage(nameof(District));
        [HttpGet] public IActionResult Thana() => SetupPage(nameof(Thana));
        [HttpGet] public IActionResult Location() => SetupPage(nameof(Location));
        [HttpGet] public IActionResult BusinessCenter() => SetupPage(nameof(BusinessCenter));
        [HttpGet] public IActionResult GroupofCompany() => SetupPage(nameof(GroupofCompany));
        [HttpGet] public IActionResult Company() => SetupPage(nameof(Company));
        [HttpGet] public IActionResult Department() => SetupPage(nameof(Department));
        [HttpGet] public IActionResult DepartmentUnit() => SetupPage(nameof(DepartmentUnit));
        [HttpGet] public IActionResult Designation() => SetupPage(nameof(Designation));
        [HttpGet] public IActionResult Gender() => SetupPage(nameof(Gender));
        [HttpGet] public IActionResult Religion() => SetupPage(nameof(Religion));

        private IActionResult SetupPage(string key)
        {
            var def = Defs[key];
            if (PageGuard(def.MenuUrl) is { } guard) return guard;

            var vm = new AdminSetupViewModel
            {
                Key = def.Key, Title = def.Title, Icon = def.Icon, Note = def.Note,
                CanDelete = def.CanDelete, Fields = def.Fields, Columns = def.Columns
            };

            // edit mode: the record (incl. its cascade ids) fills the form; otherwise the query string drives the cascades
            long.TryParse(Request.Query["edit"], out long editId);
            IDictionary<string, object?>? record = editId > 0 ? _repo.GetRecord(key, editId) : null;
            if (record == null) editId = 0;
            vm.EditId = editId;

            foreach (var f in def.Fields)
            {
                string val = "";
                if (record != null && record.TryGetValue(f.Key, out object? rv) && rv != null)
                    val = rv is DateTime dt ? dt.ToString("yyyy-MM-dd") : Convert.ToString(rv) ?? "";
                else if (Request.Query.TryGetValue(f.Key, out var qv))
                    val = qv.ToString();
                vm.Values[f.Key] = val;
            }

            // cascades: resolve options top-down; an invalid selection falls back to the first option
            foreach (var f in def.Fields.Where(x => x.Kind == "select"))
            {
                List<SetupOption> options = f.LookupKey == "TimeZone"
                    ? TimeZoneInfo.GetSystemTimeZones().Select(z => new SetupOption { Value = z.DisplayName, Text = z.DisplayName }).ToList()
                    : _repo.Lookup(f.LookupKey!, f.Parent != null && int.TryParse(vm.Values[f.Parent], out int p) ? p : 0);
                vm.Options[f.Key] = options;
                if (options.All(o => o.Value != vm.Values[f.Key]))
                    vm.Values[f.Key] = options.FirstOrDefault()?.Value ?? "";
            }

            int filter = def.FilterField != null && int.TryParse(vm.Values[def.FilterField], out int fv) ? fv : 0;
            try { vm.Rows = _repo.GetRows(key, filter); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Admin setup grid {Key} failed.", key);
                TempData["msg"] = "The list could not be loaded. Please try again.";
            }

            ViewData["Title"] = def.Title;
            return View("Setup", vm);
        }

        // ---- save (insert / update) - one endpoint for every setup page, same JSON shape as dispatch ----
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveSetup(string key, long id)
        {
            if (!Defs.TryGetValue(key ?? "", out var def)) return Fail("Unknown page.");
            if (!Allowed(def.MenuUrl)) return Fail("You are not allowed to change this page.");

            var values = new Dictionary<string, object?>();
            var errors = new List<string>();
            foreach (var f in def.Fields)
            {
                string raw = (Request.Form[f.Key].FirstOrDefault() ?? "").Trim();
                if (f.Required && raw.Length == 0) { errors.Add($"Enter {f.Label}."); continue; }
                if (!f.Save) continue;

                switch (f.Kind)
                {
                    case "select":
                        if (f.LookupKey == "TimeZone") { if (raw.Length > 0) values[f.Key] = raw; else values[f.Key] = null; }
                        else if (int.TryParse(raw, out int sv) && sv > 0) values[f.Key] = sv;
                        else if (f.Required) errors.Add($"Select {f.Label}.");
                        else values[f.Key] = 0;
                        break;
                    case "int":
                        if (raw.Length == 0) values[f.Key] = 0;
                        else if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iv)) values[f.Key] = iv;
                        else errors.Add($"{f.Label} must be a whole number.");
                        break;
                    case "date":
                        if (raw.Length == 0) values[f.Key] = null;
                        else if (DateTime.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dv)) values[f.Key] = dv;
                        else errors.Add($"{f.Label} is not a valid date.");
                        break;
                    default:
                        if (raw.Length > f.MaxLen) errors.Add($"{f.Label} is too long (maximum {f.MaxLen} characters).");
                        else values[f.Key] = raw.Length == 0 ? null : raw;
                        break;
                }
            }
            if (errors.Count > 0) return Fail(string.Join(" ", errors));

            try
            {
                string name = Convert.ToString(values.GetValueOrDefault(def.DupField)) ?? "";
                int parent = def.DupParentField != null ? Convert.ToInt32(values.GetValueOrDefault(def.DupParentField) ?? 0) : 0;
                if (name.Length > 0 && _repo.Duplicate(key!, name, parent, id)) return Fail(def.DupMessage);

                if (id > 0)
                {
                    if (_repo.Update(key!, id, values, StaffId) == 0) return Fail("Record not found. Please reload the page.");
                    return Json(new { success = true, message = def.Title + " updated." });
                }
                _repo.Insert(key!, values, StaffId);
                return Json(new { success = true, message = def.Title + " saved." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Admin setup save {Key} failed. Id={Id}", key, id);
                return Fail("Save failed. Please try again.");
            }
        }

        // ---- delete (old Common().Delete) ----
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteSetup(string key, long id)
        {
            if (!Defs.TryGetValue(key ?? "", out var def)) return Fail("Unknown page.");
            if (!def.CanDelete) return Fail("Delete is not available on this page.");
            if (!Allowed(def.MenuUrl)) return Fail("You are not allowed to change this page.");
            try
            {
                return _repo.Delete(key!, id) > 0
                    ? Json(new { success = true, message = def.Title + " deleted." })
                    : Fail("Record not found. Please reload the page.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Admin setup delete {Key} failed. Id={Id}", key, id);
                return Fail("Delete failed. The record is probably in use.");
            }
        }

        // =====================================================================================================
        // DEPARTMENT ASSIGN (old DepartmentAssign.aspx: which departments a company runs at a business center)
        // =====================================================================================================
        [HttpGet]
        public IActionResult DepartmentAssign(int companyId = 0, int countryId = 0, int locationId = 0, int centerId = 0)
        {
            if (PageGuard("Admin/DepartmentAssign") is { } guard) return guard;

            var vm = new DepartmentAssignViewModel
            {
                Companies = _repo.Lookup("Company", 0),
                Countries = _repo.Lookup("Country", 0)
            };
            vm.CompanyId = vm.Companies.Any(o => o.Value == companyId.ToString()) ? companyId : int.TryParse(vm.Companies.FirstOrDefault()?.Value, out int c) ? c : 0;
            vm.CountryId = vm.Countries.Any(o => o.Value == countryId.ToString()) ? countryId : int.TryParse(vm.Countries.FirstOrDefault()?.Value, out int cn) ? cn : 0;
            vm.Locations = _repo.Lookup("Location", vm.CountryId);
            vm.LocationId = vm.Locations.Any(o => o.Value == locationId.ToString()) ? locationId : int.TryParse(vm.Locations.FirstOrDefault()?.Value, out int l) ? l : 0;
            vm.Centers = _repo.Lookup("Center", vm.LocationId);
            vm.CenterId = vm.Centers.Any(o => o.Value == centerId.ToString()) ? centerId : int.TryParse(vm.Centers.FirstOrDefault()?.Value, out int ct) ? ct : 0;

            vm.Loaded = vm.CompanyId > 0 && vm.CenterId > 0;
            vm.Departments = vm.Loaded
                ? _repo.GetDepartmentsWithAssignment(vm.CompanyId, vm.CenterId)
                : _repo.GetDepartmentsWithAssignment(0, 0);

            ViewData["Title"] = "Department Assign";
            return View("DepartmentAssign", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveDepartmentAssign(int companyId, int centerId, List<int>? departmentIds)
        {
            if (!Allowed("Admin/DepartmentAssign")) return Fail("You are not allowed to change department assignments.");
            if (companyId <= 0 || centerId <= 0) return Fail("Select the Company and the Business Center.");
            try
            {
                _repo.SaveDepartmentAssign(companyId, centerId, departmentIds ?? new List<int>(), StaffId);
                return Json(new { success = true, message = "Department assignment saved." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Department assign save failed. Company={CompanyId} Center={CenterId}", companyId, centerId);
                return Fail("Save failed. Please try again.");
            }
        }
    }
}
