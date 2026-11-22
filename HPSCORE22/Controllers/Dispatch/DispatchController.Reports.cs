// Controllers/Dispatch/DispatchController.Reports.cs
// Dispatch phase 7 - reports. One generic page (Views/Dispatch/DispatchReport.cshtml), filters in the URL (GET),
// "Excel" downloads the same rows as CSV (?export=1). Each report needs its page in the user's role menu,
// except "My Gate Pass Status" (only the user's own dispatches).
//   /Dispatch/Report                  <- Pages/Dispatch/DispatchReport.aspx          (dispatch list + current step)
//   /Dispatch/ReportItems             <- DispatchReport/DispatchDetails.aspx + LocationWiseDispatchReport.aspx (item lines)
//   /Dispatch/ReportItemsByStaff      <- DispatchReport/ItemListBySender.aspx + ItemListByReceiver.aspx
//   /Dispatch/ReportLocationSummary   <- LocationWiseDispatchOnlineReport.aspx        (items per location, opens with yesterday)
//   /Dispatch/ReportGatePassPending   <- DispatchReport/GatepassStatusReport.aspx     (approved > 7 days, not gate passed)
//   /Dispatch/MyGatePassStatus        <- Pages/Dispatch/GatepassStatus.aspx           (my approved dispatches not gate passed)
//   /Dispatch/ReportApprovers         <- Pages/Dispatch/LWReport.aspx                 (approvers by location / company / department)
//   /Dispatch/ReportUsers             <- Pages/Dispatch/LWUserList.aspx               (active users and roles)
//   /Dispatch/ReportGateEntry         <- DispatchReport/GateEntryReport.aspx          (visitor gate entries)          phase 8
//   /Dispatch/ReportGateEntryReturned <- DispatchReport/GateEntryReturnItem.aspx      (returned samples, per item)    phase 8
//   /Dispatch/ReportNoticeBoard       <- Pages/Dispatch/DNoticeBoardView.aspx         (notice board search)           phase 8
//   /Dispatch/NoticeReceive           <- Pages/Dispatch/RNItems.aspx                  (same search + hand over)       phase 8
using AlphaCare.Models;
using AlphaCare.Services;
using Microsoft.AspNetCore.Mvc;
using C = AlphaCare.Models.DispatchReportColumn;

namespace QCMS.Controllers
{
    public partial class DispatchController
    {
        private sealed class ReportDef
        {
            public string Action = "";
            public string? MenuUrl;                                   // null = own data only, no menu needed
            public string Title = "";
            public string Icon = "fa-table";
            public string? Note;
            public ReportFilters Filters;
            public bool AutoRun;
            public bool Wide = true;                                  // scroll box in both directions (default for every report)
            public List<C> Columns = new();
            public Action<DispatchReportFilter>? Defaults;            // first opening of the page
            public Func<DispatchReportFilter, string?>? Validate;     // error text, or null
            public Func<DispatchReportFilter, List<IDictionary<string, object?>>> Query = _ => new();
        }

        private static DateTime Today => DateTime.Today;
        private static void ThisMonth(DispatchReportFilter f) { f.From = new DateTime(Today.Year, Today.Month, 1); f.To = Today; }

        private static readonly List<C> ItemColumns = new()
        {
            new("TRANSACTIONID", "Dispatch ID", "tx"), new("CREATEDATE", "Date", "date"), new("SENDERRLOC", "Sender Location"),
            new("DEPARTMENTNAME", "Sender Department"), new("FROMUSER", "Sender ID"), new("SENDNAME", "Sender"), new("SENDMOBILE", "Sender Mobile"),
            new("ISSUETYPENAME", "Issue Type"), new("RECEIVERLOC", "Receiver Location"), new("TOUSER", "Receiver ID"),
            new("RECEIVERNAME", "Receiver"), new("RECEIVERMOBILE", "Receiver Mobile"),
            new("ITEMNAME", "Item"), new("QUANTITY", "Quantity", "num"), new("UNIT", "Unit"), new("NOTES", "Notes")
        };

        private ReportDef ReportOf(string action) => action switch
        {
            nameof(Report) => new ReportDef
            {
                Action = action, MenuUrl = "Dispatch/Report", Title = "Dispatch Report", Icon = "fa-file-lines",
                Note = "One row per dispatch with the step it is at now.",
                Filters = ReportFilters.Dates | ReportFilters.Location | ReportFilters.Department | ReportFilters.IssueType,
                Defaults = ThisMonth,
                Columns = new()
                {
                    new("TRANSACTIONID", "Dispatch ID", "tx"), new("CREATEDATE", "Date", "date"), new("ISSUETYPENAME", "Issue Type"),
                    new("SENDER", "Sender"), new("SENDMOBILE", "Sender Mobile"), new("RECEIVER", "Receiver"),
                    new("RECEIVERMOBILE", "Receiver Mobile"), new("STEP", "Current Step"), new("CREATEDBYNAME", "Created By")
                },
                Query = f => _repo.ReportDispatchList(f)
            },
            nameof(ReportItems) => new ReportDef
            {
                Action = action, MenuUrl = "Dispatch/ReportItems", Title = "Dispatch Details (items)", Icon = "fa-list-ul",
                Note = "One row per item, sorted by sender location.",
                Filters = ReportFilters.Dates | ReportFilters.Location | ReportFilters.Department | ReportFilters.IssueType,
                Defaults = ThisMonth, Columns = ItemColumns,
                Query = f => _repo.ReportDispatchItems(f)
            },
            nameof(ReportItemsByStaff) => new ReportDef
            {
                Action = action, MenuUrl = "Dispatch/ReportItemsByStaff", Title = "Item List by Sender / Receiver", Icon = "fa-user-tag",
                Note = "Items sent by (or sent to) one employee.",
                Filters = ReportFilters.Staff | ReportFilters.Mode | ReportFilters.Dates,
                Defaults = ThisMonth,
                Validate = f => FirstPart(f.Staff).Length == 0 ? "Enter the Staff ID." : !f.From.HasValue ? "Enter the From date." : null,
                Columns = new()
                {
                    new("TRANSACTIONID", "Dispatch ID", "tx"), new("CREATEDATE", "Date", "date"), new("FROMUSER", "Sender ID"),
                    new("TOUSER", "Receiver ID"), new("CENTERNAME", "Sender Location"), new("DEPARTMENTNAME", "Sender Department"),
                    new("ITEMNAME", "Item"), new("QUANTITY", "Quantity", "num"), new("UNIT", "Unit")
                },
                Query = f => _repo.ReportItemsByStaff(f, FirstPart(f.Staff))
            },
            nameof(ReportLocationSummary) => new ReportDef
            {
                Action = action, MenuUrl = "Dispatch/ReportLocationSummary", Title = "Location Wise Dispatch", Icon = "fa-map-location-dot",
                Note = "Items per sender location. Opens with yesterday, as the old online report.",
                Filters = ReportFilters.Dates | ReportFilters.Location, AutoRun = true,
                Defaults = f => { f.From = Today.AddDays(-1); f.To = Today.AddDays(-1); },
                Columns = new()
                {
                    new("SENDERRLOC", "Sender Location"), new("DEPARTMENTNAME", "Department"), new("CREATEDATE", "Date", "date"),
                    new("TRANSACTIONID", "Dispatch ID", "tx"), new("FROMUSER", "Sender ID"), new("SENDNAME", "Sender"),
                    new("RECEIVERNAME", "Receiver"), new("RECEIVERLOC", "Receiver Location"), new("ISSUETYPENAME", "Issue Type"),
                    new("ITEMNAME", "Item"), new("QUANTITY", "Quantity", "num"), new("UNIT", "Unit")
                },
                Query = f => _repo.ReportLocationSummary(f)
            },
            nameof(ReportGatePassPending) => new ReportDef
            {
                Action = action, MenuUrl = "Dispatch/ReportGatePassPending", Title = "Gate Pass Pending", Icon = "fa-door-closed",
                Note = "Approved more than 7 days ago and still not gate passed (active locations that are not a depot / sales office). One row per item. Created By ID shows one staff member's pending gate passes.",
                Filters = ReportFilters.FromOnly | ReportFilters.Location | ReportFilters.CreatedBy,
                Columns = new()
                {
                    new("TRANSACTIONID", "Dispatch ID", "tx"), new("CREATEDATE", "Date", "date"), new("ISSUETYPENAME", "Issue Type"),
                    new("SENDERLOCATION", "Sender Location"), new("SENDERDEPARTMENT", "Sender Department"), new("SENDERUSER", "Sender ID"),
                    new("SENDERNAME", "Sender"), new("RECEIVERLOCATION", "Receiver Location"), new("RECEIVERDEPARTMENT", "Receiver Department"),
                    new("RECEIVERUSER", "Receiver ID"), new("RECEIVERNAME", "Receiver"), new("CREATEUSER", "Created By ID"),
                    new("CREATORNAME", "Created By"), new("ITEMNAME", "Item"), new("QUANTITY", "Quantity", "num"), new("UNIT", "Unit"), new("NOTES", "Notes")
                },
                Query = f => _repo.ReportGatePassPending(f, FirstPart(f.Staff))
            },
            nameof(MyGatePassStatus) => new ReportDef
            {
                Action = action, MenuUrl = null, Title = "My Gate Pass Status", Icon = "fa-door-open",
                Note = "Your approved dispatches (created from 05 Oct 2026) that have not passed the gate yet.",
                Filters = ReportFilters.None, AutoRun = true,
                Columns = new()
                {
                    new("TRANSACTIONID", "Dispatch ID", "tx"), new("CREATEDATE", "Date", "date"), new("ISSUETYPENAME", "Issue Type"),
                    new("SENDER_LOCATION", "Sender Location"), new("SENDER_USER", "Sender ID"), new("SENDER_NAME", "Sender"),
                    new("RECEIVER_LOCATION", "Receiver Location"), new("RECEIVER_USER", "Receiver ID"), new("RECEIVER_NAME", "Receiver"),
                    new("CREATOR_NAME", "Created By")
                },
                Query = _ => _repo.ReportMyGatePassStatus(StaffId)
            },
            nameof(ReportApprovers) => new ReportDef
            {
                Action = action, MenuUrl = "Dispatch/ReportApprovers", Title = "Approver List", Icon = "fa-user-shield",
                Note = "Dispatch approvers with an active user account.",
                Filters = ReportFilters.Location | ReportFilters.Company | ReportFilters.Department,
                Columns = new()
                {
                    new("CENTERNAME", "Location"), new("COMPANYNAME", "Company"), new("DEPARTMENTCODE", "Department"),
                    new("PRIORITY", "Priority", "num"), new("ISFINALAPPROVER", "Final", "num"), new("STAFFID", "Staff ID"),
                    new("EMPLOYEENAME", "Name"), new("DESIGNATIONNAME", "Designation"), new("OFFICEMOBILE", "Mobile"), new("OFFICEMAIL", "Email")
                },
                Query = f => _repo.ReportApprovers(f)
            },
            nameof(ReportUsers) => new ReportDef
            {
                Action = action, MenuUrl = "Dispatch/ReportUsers", Title = "User List", Icon = "fa-users",
                Note = "Active users with their role.",
                Filters = ReportFilters.Location | ReportFilters.Company | ReportFilters.Department,
                Columns = new()
                {
                    new("STAFFID", "Staff ID"), new("EMPLOYEENAME", "Name"), new("DESIGNATIONNAME", "Designation"),
                    new("CENTERNAME", "Location"), new("COMPANYNAME", "Company"), new("DEPARTMENTCODE", "Department"),
                    new("ROLENAME", "Role"), new("OFFICEMOBILE", "Mobile"), new("OFFICEMAIL", "Email")
                },
                Query = f => _repo.ReportUsers(f)
            },
            nameof(ReportGateEntry) => new ReportDef
            {
                Action = action, MenuUrl = "Dispatch/ReportGateEntry", Title = "Gate Entry Report", Icon = "fa-id-card",
                Note = "Visitor gate entries by entry date. Host = the employee the visitor came to, Entry By = the gate user who made the entry.",
                Filters = ReportFilters.Dates | ReportFilters.Location | ReportFilters.Department,
                Defaults = ThisMonth,
                Columns = new()
                {
                    new("GATEENTRYID", "Gate Entry ID", "ge"), new("INDATETIME", "In Time", "date"), new("OUTDATETIME", "Out Time", "date"),
                    new("CENTERNAME", "Location"), new("DEPARTMENTNAME", "Department"), new("STAFFID", "Host ID"), new("EMPLOYEENAME", "Host"),
                    new("OFFICEMOBILE", "Host Mobile"), new("VISTORNAME", "Visitor"), new("VISTORMOBILE", "Visitor Mobile"),
                    new("VISITORADDRESS", "Visitor Address"), new("VISTORCARDNO", "Card#"), new("GATESTATUS", "Gate"), new("CREATEDBYNAME", "Entry By")
                },
                Query = f => _repo.ReportGateEntry(f)
            },
            nameof(ReportGateEntryReturned) => new ReportDef
            {
                Action = action, MenuUrl = "Dispatch/ReportGateEntryReturned", Title = "Gate Entry Returned Items", Icon = "fa-box-open",
                Note = "Gate entries whose returnable sample was returned. One row per item.",
                Filters = ReportFilters.Dates | ReportFilters.Location | ReportFilters.Department,
                Defaults = ThisMonth,
                Columns = new()
                {
                    new("GATEENTRYID", "Gate Entry ID", "ge"), new("INDATETIME", "In Time", "date"), new("CENTERNAME", "Location"),
                    new("DEPARTMENTNAME", "Department"), new("EMPLOYEENAME", "Host"), new("OFFICEMOBILE", "Host Mobile"),
                    new("VISTORNAME", "Visitor"), new("VISTORMOBILE", "Visitor Mobile"), new("ITEMNAME", "Item"),
                    new("QUANTITY", "Quantity", "num"), new("UNIT", "Unit"), new("SAMPLE", "Sample"), new("RETURNABLE", "Returnable")
                },
                Query = f => _repo.ReportGateEntryReturned(f)
            },
            nameof(ReportNoticeBoard) => new ReportDef
            {
                Action = action, MenuUrl = "Dispatch/ReportNoticeBoard", Title = "Notice Board Report", Icon = "fa-clipboard-list",
                Note = "Letters / parcels entered on the notice board, by entry date. LC No. and LC Amount are used for the LC type only.",
                Filters = ReportFilters.Text | ReportFilters.NoticeType | ReportFilters.Lc | ReportFilters.NoticeStatus | ReportFilters.Dates,
                Defaults = ThisMonth,
                Columns = NoticeColumns(withAction: false),
                Query = f => _repo.ReportNoticeBoard(f)
            },
            nameof(NoticeReceive) => new ReportDef
            {
                Action = action, MenuUrl = "Dispatch/NoticeReceive", Title = "Notice Board - Hand Over", Icon = "fa-hand-holding",
                Note = "Press the hand over button when the addressee (or someone for them) collects the item.",
                Filters = ReportFilters.Text | ReportFilters.NoticeType | ReportFilters.Lc | ReportFilters.NoticeStatus | ReportFilters.Dates,
                AutoRun = true,
                Defaults = f => { ThisMonth(f); f.Status = "waiting"; },
                Columns = NoticeColumns(withAction: true),
                Query = f => _repo.ReportNoticeBoard(f)
            },
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };

        private static List<C> NoticeColumns(bool withAction)
        {
            var list = new List<C>();
            if (withAction) list.Add(new("NOTICESTATUS", "Action", "nrecv"));
            list.AddRange(new C[]
            {
                new("TRACKINGNO", "Tracking No", "notice"), new("CREATEDATE", "Create Date", "date"), new("DTYPENAME", "Type"),
                new("LCAMOUNT", "LC Amount", "num"), new("DOCFROM", "From"), new("FROMMOBILE", "From Mobile"), new("RECDATE", "Receive Date", "day"),
                new("TSTAFFID", "To Staff ID"), new("TNAME", "To Name"), new("TMOBILE", "To Mobile"), new("TDEPARTMENT", "To Department"),
                new("NOTICESTATUS", "Status"), new("RECSTAFFID", "Receiver Staff ID"), new("RECEIVERNAME", "Receiver Name"),
                new("RECEIVERCOMPANY", "Receiver Company"), new("RECMOBILE", "Receiver Mobile"), new("RECEIVEDDATE", "Received Date", "date")
            });
            return list;
        }

        [HttpGet] public IActionResult Report(DispatchReportFilter f, int run = 0, int export = 0) => RunDispatchReport(nameof(Report), f, run, export);
        [HttpGet] public IActionResult ReportItems(DispatchReportFilter f, int run = 0, int export = 0) => RunDispatchReport(nameof(ReportItems), f, run, export);
        [HttpGet] public IActionResult ReportItemsByStaff(DispatchReportFilter f, int run = 0, int export = 0) => RunDispatchReport(nameof(ReportItemsByStaff), f, run, export);
        [HttpGet] public IActionResult ReportLocationSummary(DispatchReportFilter f, int run = 0, int export = 0) => RunDispatchReport(nameof(ReportLocationSummary), f, run, export);
        [HttpGet] public IActionResult ReportGatePassPending(DispatchReportFilter f, int run = 0, int export = 0) => RunDispatchReport(nameof(ReportGatePassPending), f, run, export);
        [HttpGet] public IActionResult MyGatePassStatus(int export = 0) => RunDispatchReport(nameof(MyGatePassStatus), new DispatchReportFilter(), 1, export);
        [HttpGet] public IActionResult ReportApprovers(DispatchReportFilter f, int run = 0, int export = 0) => RunDispatchReport(nameof(ReportApprovers), f, run, export);
        [HttpGet] public IActionResult ReportUsers(DispatchReportFilter f, int run = 0, int export = 0) => RunDispatchReport(nameof(ReportUsers), f, run, export);
        [HttpGet] public IActionResult ReportGateEntry(DispatchReportFilter f, int run = 0, int export = 0) => RunDispatchReport(nameof(ReportGateEntry), f, run, export);
        [HttpGet] public IActionResult ReportGateEntryReturned(DispatchReportFilter f, int run = 0, int export = 0) => RunDispatchReport(nameof(ReportGateEntryReturned), f, run, export);
        [HttpGet] public IActionResult ReportNoticeBoard(DispatchReportFilter f, int run = 0, int export = 0) => RunDispatchReport(nameof(ReportNoticeBoard), f, run, export);
        [HttpGet] public IActionResult NoticeReceive(DispatchReportFilter f, int run = 0, int export = 0) => RunDispatchReport(nameof(NoticeReceive), f, run, export);

        private IActionResult RunDispatchReport(string action, DispatchReportFilter f, int run, int export)
        {
            if (string.IsNullOrWhiteSpace(StaffId)) return RedirectToAction("Login", "Accounts");
            var def = ReportOf(action);
            if (def.MenuUrl != null && !Allowed(def.MenuUrl)) return RedirectToAction("Denied", "Accounts");

            f ??= new DispatchReportFilter();
            f.Mode = f.Mode == "receiver" ? "receiver" : "sender";
            f.Staff = (f.Staff ?? "").Trim();
            if (f.Staff.Length > 200) f.Staff = f.Staff[..200];
            f.Text = (f.Text ?? "").Trim();
            if (f.Text.Length > 100) f.Text = f.Text[..100];
            f.LcNo = (f.LcNo ?? "").Trim();
            if (f.LcNo.Length > 50) f.LcNo = f.LcNo[..50];
            f.LcAmount = (f.LcAmount ?? "").Trim();
            if (f.LcAmount.Length > 20) f.LcAmount = "";
            f.Status = f.Status is "waiting" or "received" ? f.Status : "";
            bool searching = run == 1 || export == 1 || def.AutoRun;
            if (run != 1 && export != 1) def.Defaults?.Invoke(f);        // first opening: default dates
            if (f.From.HasValue && f.To.HasValue && f.To < f.From) (f.From, f.To) = (f.To, f.From);

            var vm = new DispatchReportViewModel
            {
                Action = def.Action, Title = def.Title, Icon = def.Icon, Note = def.Note, Filters = def.Filters, AutoRun = def.AutoRun,
                Wide = def.Wide, Filter = f, Columns = def.Columns, MaxRows = Admin360.Repositories.DispatchRepository.ReportMaxRows
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
                        _logger.LogError(ex, "Dispatch report {Report} failed.", action);
                        vm.Message = "The report could not be loaded. Please try again.";
                    }
                }

                if (export == 1 && vm.Searched)
                {
                    byte[] file = DispatchReportCsv.Build(def.Title, def.Columns.Where(c => c.Kind != "nrecv").ToList(), vm.Rows);
                    return File(file, "text/csv", $"{def.Action}_{DateTime.Now:yyyyMMddHHmm}.csv");
                }
            }

            if (def.Filters.HasFlag(ReportFilters.Location)) vm.Locations = _repo.GetBusinessCenters();
            if (def.Filters.HasFlag(ReportFilters.Department)) vm.Departments = _repo.GetDepartments();
            if (def.Filters.HasFlag(ReportFilters.IssueType)) vm.IssueTypes = _repo.GetIssueTypesForSetup();
            if (def.Filters.HasFlag(ReportFilters.Company)) vm.Companies = _repo.GetCompanies();
            if (def.Filters.HasFlag(ReportFilters.NoticeType)) vm.DispatchTypes = _repo.GetDispatchTypes();

            ViewData["Title"] = def.Title;
            return View("DispatchReport", vm);
        }
    }
}
