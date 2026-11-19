// Models/ServiceReportModels.cs
// Service reports (port of YA Reports/Service/*.aspx + new date-range reports). Same pattern as the Dispatch reports:
// one generic page, every report says which filters and columns it uses, rows come straight from the query.
// Columns reuse DispatchReportColumn (Kind: text | date | day | num | sum = number with a total row | sid = service id -> details).
namespace AlphaCare.Models
{
    public class ServiceReportFilter
    {
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public int LocationId { get; set; }            // BS_BUSINESSCENTER of the service center (machine location on Machine Breakdown)
        public int ServiceCenterId { get; set; }
        public int CategoryId { get; set; }            // SC_CATEGORY ("Service Type" on the old page)
        public string? Status { get; set; }            // "" | approval | rejected | 0..5 | instant
        public string? Instant { get; set; }           // "" | 0 | 1
        public string? ServiceId { get; set; }         // part of SERVICEID
        public string? FromStaff { get; set; }         // requester staff id (or auto complete text "ID-...")
        public string? ServicePerson { get; set; }     // service person staff id
    }

    [Flags]
    public enum ServiceReportFilters
    {
        None = 0,
        Dates = 1,
        Location = 2,
        ServiceCenter = 4,
        Category = 8,
        Status = 16,
        Instant = 32,
        ServiceId = 64,
        FromStaff = 128,
        ServicePerson = 256
    }

    public class ServiceReportViewModel
    {
        public string Action { get; set; } = "";
        public string Title { get; set; } = "";
        public string Icon { get; set; } = "fa-table";
        public string? Note { get; set; }
        public ServiceReportFilters Filters { get; set; }
        public bool AutoRun { get; set; }

        public ServiceReportFilter Filter { get; set; } = new();
        public bool Searched { get; set; }
        public string? Message { get; set; }

        public List<DispatchReportColumn> Columns { get; set; } = new();
        public List<IDictionary<string, object?>> Rows { get; set; } = new();
        public int MaxRows { get; set; }
        public bool Truncated => MaxRows > 0 && Rows.Count >= MaxRows;

        // drop downs
        public List<BusinessCenterModel> Locations { get; set; } = new();
        public List<ServiceOption> ServiceCenters { get; set; } = new();
        public List<ServiceOption> Categories { get; set; } = new();

        /// <summary>Status filter choices (value, text) - old ddlStatus + approval / rejected / instant.</summary>
        public static readonly (string Value, string Text)[] StatusChoices =
        {
            ("approval", "Waiting for Approval"), ("rejected", "Rejected"),
            ("0", "Pending (Waiting for Assign)"), ("1", "Assigned (On Process)"), ("2", "User Rating (Waiting for User Feedback)"),
            ("3", "Serviceperson Rating (Waiting for Service Person Feedback)"), ("4", "Revision Required"), ("5", "Completed"),
            ("instant", "Instant - Waiting for Approval")
        };
    }

    // one row of VW_SERVICECENTER (old Reports/Service/Default.aspx + ServiceCenter.aspx)
    public class ServiceCenterSummaryRow
    {
        public string? CENTERCODE { get; set; }
        public string? SERVICECENTERNAME { get; set; }
        public long? SERVICECOUNT { get; set; }
        public long? COMPLETED { get; set; }
        public long? PENDING { get; set; }
        public long? PROCESSING { get; set; }
        public long? FORWARDED { get; set; }
        public long? INSTANTSERVICE { get; set; }
    }

    public class ServiceSummaryViewModel
    {
        public List<ServiceCenterSummaryRow> Rows { get; set; } = new();
        public string? Message { get; set; }
    }
}
