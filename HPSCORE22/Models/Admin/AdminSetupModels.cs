// Models/Admin/AdminSetupModels.cs
// Admin module (port of YA Pages/Admin): the simple setup screens all share one generic page
// (Views/Admin/Setup.cshtml) driven by a SetupDef in AdminController.Setup.cs - same idea as the
// generic dispatch report page (ReportDef). Special page: DepartmentAssign (checkbox matrix).
namespace AlphaCare.Models
{
    // one input of the setup form. Key = the column name in the table (upper case).
    public class SetupField
    {
        public string Key = "";                     // column name (e.g. COUNTRYNAME) or cascade-only key
        public string Label = "";
        public string Kind = "text";                // text | date | int | select
        public int MaxLen = 50;                     // text fields
        public bool Required;
        public bool Save = true;                    // false = cascade-only dropdown (not a column of the table)
        public string? LookupKey;                   // select: which lookup fills it (AdminSetupRepository.Lookup)
        public string? Parent;                      // select: Key of the field this one cascades from

        public SetupField(string key, string label, string kind = "text", int maxLen = 50, bool required = false,
                          bool save = true, string? lookupKey = null, string? parent = null)
        { Key = key; Label = label; Kind = kind; MaxLen = maxLen; Required = required; Save = save; LookupKey = lookupKey; Parent = parent; }
    }

    public class SetupOption
    {
        public string Value { get; set; } = "";
        public string Text { get; set; } = "";
    }

    public class AdminSetupViewModel
    {
        public string Key = "";                     // action name (Country, Division, ...)
        public string Title = "";
        public string Icon = "fa-gear";
        public string? Note;
        public bool CanDelete;
        public List<SetupField> Fields = new();
        public Dictionary<string, List<SetupOption>> Options = new();      // select field key -> options
        public Dictionary<string, string> Values = new();                  // field key -> current value (edit / filter)
        public long EditId;                                                // > 0 = the form edits this row
        public List<DispatchReportColumn> Columns = new();                 // grid columns (reuses the report column type)
        public List<IDictionary<string, object?>> Rows = new();
    }

    // ---- Department Assign (old DepartmentAssign.aspx: company + location -> which departments apply) ----
    public class DepartmentAssignViewModel
    {
        public int CompanyId { get; set; }
        public int CountryId { get; set; }
        public int LocationId { get; set; }
        public int CenterId { get; set; }
        public List<SetupOption> Companies { get; set; } = new();
        public List<SetupOption> Countries { get; set; } = new();
        public List<SetupOption> Locations { get; set; } = new();
        public List<SetupOption> Centers { get; set; } = new();
        public List<DepartmentAssignRow> Departments { get; set; } = new();
        public bool Loaded { get; set; }                                   // true after Search (checkboxes reflect the DB)
    }

    public class DepartmentAssignRow
    {
        public int ID { get; set; }
        public string? DEPARTMENTNAME { get; set; }
        public string? DEPARTMENTCODE { get; set; }
        public bool Assigned { get; set; }
    }
}
