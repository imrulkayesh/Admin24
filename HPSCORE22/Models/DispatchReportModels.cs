// Models/DispatchReportModels.cs
// Dispatch phase 7 - reports (port of YA Pages/DispatchReport/*, Pages/Dispatch/DispatchReport, LWReport, LWUserList,
// GatepassStatus and LocationWiseDispatchOnlineReport). One generic page: every report says which filters it uses
// and which columns it shows; the rows come straight from the query (column name -> value).
namespace AlphaCare.Models
{
    // the filter values of a report (all optional; each report uses only its own)
    public class DispatchReportFilter
    {
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public int LocationId { get; set; }
        public int DepartmentId { get; set; }
        public int IssueTypeId { get; set; }
        public int CompanyId { get; set; }
        public string? Staff { get; set; }                 // staff id (or the auto complete text "ID-...")
        public string Mode { get; set; } = "sender";       // item list: "sender" or "receiver"

        // notice board (phase 8)
        public int DispatchType { get; set; }              // DP_DISPATCHTYPE.ID (3 = LC)
        public string? Text { get; set; }                  // staff id / name / mobile / tracking no
        public string? LcNo { get; set; }
        public string? LcAmount { get; set; }
        public string? Status { get; set; }                // "" = all, "waiting", "received"

        public decimal? LcAmountValue =>
            decimal.TryParse((LcAmount ?? "").Trim(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var v)
                ? v : null;
    }

    // one column of the result table / Excel sheet
    public class DispatchReportColumn
    {
        public string Key { get; set; } = "";              // column name in the query (upper case, as Oracle returns it)
        public string Header { get; set; } = "";
        public string Kind { get; set; } = "text";         // text | date | day (date only) | num | tx (dispatch id -> tracking)
                                                           // | ge (gate entry id -> details) | notice (-> notice details) | nrecv (hand over button)

        public DispatchReportColumn() { }
        public DispatchReportColumn(string key, string header, string kind = "text") { Key = key; Header = header; Kind = kind; }
    }

    // which filters the page shows
    [Flags]
    public enum ReportFilters
    {
        None = 0,
        Dates = 1,          // from + to
        FromOnly = 2,       // from date only
        Location = 4,
        Department = 8,
        IssueType = 16,
        Company = 32,
        Staff = 64,
        Mode = 128,         // sender / receiver
        NoticeType = 256,   // dispatch type of the notice board
        Text = 512,         // free text (staff id / name / mobile / tracking no)
        Lc = 1024,          // LC no. + LC amount (only for dispatch type 3)
        NoticeStatus = 2048 // waiting / received
    }

    public class DispatchReportViewModel
    {
        public string Action { get; set; } = "";           // controller action of this report (form + export link)
        public string Title { get; set; } = "";
        public string Icon { get; set; } = "fa-table";
        public string? Note { get; set; }                  // what the report shows (the old rule in plain words)
        public ReportFilters Filters { get; set; }
        public bool AutoRun { get; set; }                  // shows the result without pressing Search (old pages that bound on load)

        public DispatchReportFilter Filter { get; set; } = new();
        public bool Searched { get; set; }
        public string? Message { get; set; }

        public List<DispatchReportColumn> Columns { get; set; } = new();
        public List<IDictionary<string, object?>> Rows { get; set; } = new();
        public int MaxRows { get; set; }
        public bool Truncated => MaxRows > 0 && Rows.Count >= MaxRows;

        // drop downs
        public List<BusinessCenterModel> Locations { get; set; } = new();
        public List<DepartmentModel> Departments { get; set; } = new();
        public List<IssueTypeSetupModel> IssueTypes { get; set; } = new();
        public List<DispatchCompanyModel> Companies { get; set; } = new();
        public List<DispatchTypeModel> DispatchTypes { get; set; } = new();
    }
}
