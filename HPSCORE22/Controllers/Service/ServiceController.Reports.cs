// Controllers/Service/ServiceController.Reports.cs
// Service reports - same pattern as DispatchController.Reports.cs: one generic page (Views/Service/ServiceReport.cshtml),
// filters in the URL (GET), "Excel" downloads the same rows as CSV (?export=1, DispatchReportCsv). Each report needs its page
// in the user's role menu (YA's report pages only checked the login - see MIGRATION_SERVICE_REPORTS.md).
//   /Service/ReportSummary            <- Reports/Service/Default.aspx         (VW_SERVICECENTER, one chart per service center)
//   /Service/ReportServiceCenter      <- Reports/Service/ServiceCenter.aspx   (VW_SERVICECENTER as a table + totals)
//   /Service/ReportServiceRequest     <- Reports/Service/ServiceRequest.aspx  (request list; the old search form now works)
//   /Service/ReportCenterStatus       new: status breakdown per service center for a date range
//   /Service/ReportServicePerson      new: service person performance (assigned, completed, rating, days)
//   /Service/ReportAccessories        new: required accessories (spare parts) per request
//   /Service/ReportMachineBreakdown   new: machine stop / start time recorded on "Complete Service"
using AlphaCare.Models;
using AlphaCare.Services;
using Microsoft.AspNetCore.Mvc;
using C = AlphaCare.Models.DispatchReportColumn;
using RF = AlphaCare.Models.ServiceReportFilters;

namespace QCMS.Controllers
{
    public partial class ServiceController
    {
        private const string ReportRequestUrl = "Service/ReportServiceRequest";

        private sealed class ServiceReportDef
        {
            public string Action = "";
            public string MenuUrl = "";
            public string Title = "";
            public string Icon = "fa-table";
            public string? Note;
            public RF Filters;
            public bool AutoRun;
            public List<C> Columns = new();
            public Action<ServiceReportFilter>? Defaults;            // first opening of the page
            public Func<ServiceReportFilter, string?>? Validate;     // error text, or null
            public Func<ServiceReportFilter, List<IDictionary<string, object?>>> Query = _ => new();
        }

        private static void ThisMonth(ServiceReportFilter f) { var t = DateTime.Today; f.From = new DateTime(t.Year, t.Month, 1); f.To = t; }

        private static string? NeedFrom(ServiceReportFilter f) => !f.From.HasValue ? "Enter the From date." : null;

        private ServiceReportDef ServiceReportOf(string action) => action switch
        {
            nameof(ReportServiceCenter) => new ServiceReportDef
            {
                Action = action, MenuUrl = "Service/ReportServiceCenter", Title = "Service Center Report", Icon = "fa-building",
                Note = "Request counts per service center, exactly as YA's report (database view VW_SERVICECENTER).",
                Filters = RF.None, AutoRun = true,
                Columns = new()
                {
                    new("SERVICECENTER", "Service Center"), new("EMPLOYEENAME", "Admin"), new("OFFICEMOBILE", "Mobile"), new("OFFICEMAIL", "Email"),
                    new("SERVICECOUNT", "Request", "sum"), new("COMPLETED", "Completed", "sum"), new("FORWARDED", "Forwarded", "sum"),
                    new("PROCESSING", "Processing", "sum"), new("PENDING", "Pending", "sum"), new("INSTANTSERVICE", "Instant", "sum")
                },
                Query = _ => _repo.ReportServiceCenter()
            },
            nameof(ReportServiceRequest) => new ServiceReportDef
            {
                Action = action, MenuUrl = ReportRequestUrl, Title = "Service Request Report", Icon = "fa-file-lines",
                Note = "One row per service request with its current service person. Service Center = the center it was sent to or forwarded to. Status 0..5 = approved requests in that step.",
                Filters = RF.ServiceId | RF.ServicePerson | RF.Dates | RF.Location | RF.ServiceCenter | RF.Category | RF.Instant | RF.FromStaff | RF.Status,
                AutoRun = true, Defaults = ThisMonth,
                Columns = new()
                {
                    new("CREATEDATE", "Date", "date"), new("SERVICEID", "Service ID", "sid"), new("CATEGORYNAME", "Type"),
                    new("FROMSTAFFID", "Staff ID"), new("EMPLOYEENAME", "Request From"), new("OFFICEMOBILE", "Mobile"),
                    new("REQLOCATION", "Location"), new("DEPARTMENTCODE", "Department"), new("SERVICECENTER", "Service Center"),
                    new("MACHINE_OUTLET", "Machine/Outlet"), new("TEAMNAME", "Team"), new("SERVICEMAN", "Service Person"),
                    new("SERVICEMOBILE", "Service Mobile"), new("INSTANT", "Instant"), new("STATUSTEXT", "Status"),
                    new("NOOFREVISION", "Revisions", "num"), new("RELEASEDATE", "Released", "date"),
                    new("RATINGFROMUSER", "Service Rating", "num"), new("RATINGFROMSERVICEMAN", "User Rating", "num")
                },
                Query = f => _repo.ReportServiceRequests(f, FirstPart(f.FromStaff), FirstPart(f.ServicePerson))
            },
            nameof(ReportCenterStatus) => new ServiceReportDef
            {
                Action = action, MenuUrl = "Service/ReportCenterStatus", Title = "Service Center Status", Icon = "fa-chart-column",
                Note = "Requests created in the period, counted by their status today. Forwarded = sent on as a dispatch. Completed % = completed / approved. Avg Days = request date to release date of completed requests.",
                Filters = RF.Dates | RF.Location | RF.ServiceCenter | RF.Instant, AutoRun = true, Defaults = ThisMonth, Validate = NeedFrom,
                Columns = new()
                {
                    new("SERVICECENTER", "Service Center"), new("ADMINNAME", "Admin"), new("TOTAL", "Total", "sum"),
                    new("WAITINGAPPROVAL", "Waiting Approval", "sum"), new("REJECTED", "Rejected", "sum"), new("WAITINGASSIGN", "Waiting Assign", "sum"),
                    new("ONPROCESS", "On Process", "sum"), new("USERFEEDBACK", "User Feedback", "sum"), new("SPFEEDBACK", "Service Person Feedback", "sum"),
                    new("REVISE", "Revise", "sum"), new("COMPLETED", "Completed", "sum"), new("FORWARDED", "Forwarded", "sum"),
                    new("INSTANT", "Instant", "sum"), new("COMPLETEDPCT", "Completed %", "num"), new("AVGDAYS", "Avg Days", "num")
                },
                Query = f => _repo.ReportCenterStatus(f)
            },
            nameof(ReportServicePerson) => new ServiceReportDef
            {
                Action = action, MenuUrl = "Service/ReportServicePerson", Title = "Service Person Performance", Icon = "fa-user-gear",
                Note = "Approved requests created in the period, by the person they are assigned to now. Avg Rating = the requesters' star rating (1-5). Avg Days = assign date to release date of completed requests.",
                Filters = RF.Dates | RF.Location | RF.ServiceCenter | RF.ServicePerson | RF.Instant, AutoRun = true, Defaults = ThisMonth, Validate = NeedFrom,
                Columns = new()
                {
                    new("STAFFID", "Staff ID"), new("EMPLOYEENAME", "Service Person"), new("TEAMNAME", "Team"), new("SERVICECENTERNAME", "Service Center"),
                    new("ASSIGNED", "Assigned", "sum"), new("ONPROCESS", "On Process", "sum"), new("REVISE", "Revise", "sum"),
                    new("WAITINGFEEDBACK", "Waiting Feedback", "sum"), new("COMPLETED", "Completed", "sum"), new("REVISIONS", "Revisions", "sum"),
                    new("RATED", "Rated", "sum"), new("AVGRATING", "Avg Rating", "num"), new("AVGDAYS", "Avg Days", "num")
                },
                Query = f => _repo.ReportServicePerson(f, FirstPart(f.ServicePerson))
            },
            nameof(ReportAccessories) => new ServiceReportDef
            {
                Action = action, MenuUrl = "Service/ReportAccessories", Title = "Accessories Usage", Icon = "fa-toolbox",
                Note = "Required accessories added to requests created in the period. Amount = Quantity x Price in the row's currency.",
                Filters = RF.Dates | RF.Location | RF.ServiceCenter | RF.Status, AutoRun = true, Defaults = ThisMonth, Validate = NeedFrom,
                Columns = new()
                {
                    new("CREATEDATE", "Date", "date"), new("SERVICEID", "Service ID", "sid"), new("SERVICECENTER", "Service Center"),
                    new("REQUESTBY", "Request From"), new("ACCESSORIESCATEGORY", "Category"), new("ACCESSORIESNAME", "Accessories"),
                    new("QUANTITY", "Quantity", "sum"), new("PRICE", "Price", "num"), new("AMOUNT", "Amount", "num"), new("CURRENCYCODE", "Currency"),
                    new("NEWITEM", "Is New"), new("ADDEDBY", "Added By"), new("STATUSTEXT", "Status")
                },
                Query = f => _repo.ReportAccessories(f)
            },
            nameof(ReportMachineBreakdown) => new ServiceReportDef
            {
                Action = action, MenuUrl = "Service/ReportMachineBreakdown", Title = "Machine Breakdown", Icon = "fa-gears",
                Note = "Requests for an outlet / machine created in the period. Stop / Start = the times the service person entered on Complete Service; Down Hours = Start - Stop. Location = where the machine is.",
                Filters = RF.Dates | RF.Location | RF.ServiceCenter | RF.Status, AutoRun = true, Defaults = ThisMonth, Validate = NeedFrom,
                Columns = new()
                {
                    new("CREATEDATE", "Date", "date"), new("SERVICEID", "Service ID", "sid"), new("MACHINELOCATION", "Location"),
                    new("MACHINECODE", "Machine Code"), new("MACHINENAME", "Machine"), new("CATEGORYNAME", "Type"),
                    new("SERVICECENTER", "Service Center"), new("SERVICEMAN", "Service Person"), new("BTIMEFROM", "Stop Time", "date"),
                    new("BTIMETO", "Start Time", "date"), new("DOWNHOURS", "Down Hours", "sum"), new("STATUSTEXT", "Status")
                },
                Query = f => _repo.ReportMachineBreakdown(f)
            },
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };

        [HttpGet] public IActionResult ReportServiceCenter(int export = 0) => RunServiceReport(nameof(ReportServiceCenter), new ServiceReportFilter(), 1, export);
        [HttpGet] public IActionResult ReportServiceRequest(ServiceReportFilter f, int run = 0, int export = 0) => RunServiceReport(nameof(ReportServiceRequest), f, run, export);
        [HttpGet] public IActionResult ReportCenterStatus(ServiceReportFilter f, int run = 0, int export = 0) => RunServiceReport(nameof(ReportCenterStatus), f, run, export);
        [HttpGet] public IActionResult ReportServicePerson(ServiceReportFilter f, int run = 0, int export = 0) => RunServiceReport(nameof(ReportServicePerson), f, run, export);
        [HttpGet] public IActionResult ReportAccessories(ServiceReportFilter f, int run = 0, int export = 0) => RunServiceReport(nameof(ReportAccessories), f, run, export);
        [HttpGet] public IActionResult ReportMachineBreakdown(ServiceReportFilter f, int run = 0, int export = 0) => RunServiceReport(nameof(ReportMachineBreakdown), f, run, export);

        private static readonly HashSet<string> StatusValues = new(ServiceReportViewModel.StatusChoices.Select(s => s.Value));

        private IActionResult RunServiceReport(string action, ServiceReportFilter f, int run, int export)
        {
            if (string.IsNullOrWhiteSpace(StaffId)) return RedirectToAction("Login", "Accounts");
            var def = ServiceReportOf(action);
            if (!Allowed(def.MenuUrl)) return RedirectToAction("Denied", "Accounts");

            // clean the filter (everything comes from the query string)
            f ??= new ServiceReportFilter();
            f.Status = StatusValues.Contains(f.Status ?? "") ? f.Status : "";
            f.Instant = f.Instant is "0" or "1" ? f.Instant : "";
            f.ServiceId = Cut(f.ServiceId, 30);
            f.FromStaff = Cut(f.FromStaff, 200);
            f.ServicePerson = Cut(f.ServicePerson, 200);
            if (f.LocationId < 0) f.LocationId = 0;
            if (f.ServiceCenterId < 0) f.ServiceCenterId = 0;
            if (f.CategoryId < 0) f.CategoryId = 0;

            bool searching = run == 1 || export == 1 || def.AutoRun;
            if (run != 1 && export != 1) def.Defaults?.Invoke(f);          // first opening: default dates
            if (f.From.HasValue && f.To.HasValue && f.To < f.From) (f.From, f.To) = (f.To, f.From);

            var vm = new ServiceReportViewModel
            {
                Action = def.Action, Title = def.Title, Icon = def.Icon, Note = def.Note, Filters = def.Filters, AutoRun = def.AutoRun,
                Filter = f, Columns = def.Columns, MaxRows = Admin360.Repositories.ServiceRepository.ReportMaxRows
            };

            if (searching)
            {
                string? error = def.Validate?.Invoke(f);
                if (error != null) vm.Message = error;
                else
                {
                    try
                    {
                        vm.Rows = def.Query(f);
                        vm.Searched = true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Service report {Report} failed.", action);
                        vm.Message = "The report could not be loaded. Please try again.";
                    }
                }

                if (export == 1 && vm.Searched)
                {
                    byte[] file = DispatchReportCsv.Build(def.Title, def.Columns, vm.Rows);
                    return File(file, "text/csv", $"{def.Action}_{DateTime.Now:yyyyMMddHHmm}.csv");
                }
            }

            if (def.Filters.HasFlag(RF.Location)) vm.Locations = _repo.GetAllBusinessCenters();
            if (def.Filters.HasFlag(RF.ServiceCenter)) vm.ServiceCenters = _repo.GetAllServiceCenterOptions();
            if (def.Filters.HasFlag(RF.Category) && f.ServiceCenterId > 0) vm.Categories = _repo.GetCategoriesByServiceCenter(f.ServiceCenterId);

            ViewData["Title"] = def.Title;
            return View("ServiceReport", vm);
        }

        private static string Cut(string? text, int max)
        {
            string t = (text ?? "").Trim();
            return t.Length > max ? t[..max] : t;
        }

        // =====================================================================================================
        // SERVICE SUMMARY  /Service/ReportSummary      (old Reports/Service/Default.aspx: one pie chart per service center)
        // =====================================================================================================
        [HttpGet]
        public IActionResult ReportSummary()
        {
            if (PageGuard("Service/ReportSummary") is { } r) return r;
            var vm = new ServiceSummaryViewModel();
            try
            {
                vm.Rows = _repo.GetServiceCenterSummary();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Service summary failed.");
                vm.Message = "The summary could not be loaded. Please try again.";
            }
            ViewData["Title"] = "Service Summary- " + DateTime.Now.ToString("MMMM yyyy");      // old Page.Title
            return View("ServiceSummary", vm);
        }
    }
}
