// Models/ModuleDashboardModels.cs
// One common shape for EVERY module dashboard. The page (Views/Home/Module.cshtml) draws this shape,
// so a new module only needs a new provider class - the page never changes.
namespace AlphaCare.Models
{
    public class DashStat                       // a summary card
    {
        public string Title { get; set; } = "";
        public string Value { get; set; } = "0";
        public string? Sub { get; set; }        // small line under the number
        public string Icon { get; set; } = "fa-solid fa-chart-simple";
        public string Color { get; set; } = "#4f46e5";
        public string? Url { get; set; }        // "~/Controller/Action" (resolved by the controller)
    }

    public class DashSeries
    {
        public string Label { get; set; } = "";
        public List<double> Data { get; set; } = new();
    }

    public class DashChart
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Type { get; set; } = "bar";           // bar | hbar | line | doughnut
        public List<string> Labels { get; set; } = new();
        public List<DashSeries> Series { get; set; } = new();
        public List<string> Colors { get; set; } = new();   // one colour per label (doughnut/bar) - or one for a line
        public string? Unit { get; set; }                    // shown in the tooltip, e.g. "hrs"
    }

    public class DashRecent                     // one row of "recent activities"
    {
        public string Title { get; set; } = "";
        public string? Sub { get; set; }
        public string? Meta { get; set; }
        public string? Status { get; set; }
        public string StatusColor { get; set; } = "#64748b";
        public string Icon { get; set; } = "fa-regular fa-calendar";
        public string? Url { get; set; }
    }

    public class DashAction                     // quick action button
    {
        public string Title { get; set; } = "";
        public string Icon { get; set; } = "fa-solid fa-arrow-right";
        public string Color { get; set; } = "#4f46e5";
        public string? Url { get; set; }
    }

    public class ModuleDashboardData
    {
        public List<DashStat> Stats { get; set; } = new();
        public List<DashChart> Charts { get; set; } = new();
        public string RecentTitle { get; set; } = "Recent Activities";
        public List<DashRecent> Recent { get; set; } = new();
        public List<DashAction> Actions { get; set; } = new();
        public string? Note { get; set; }                    // shown when a module has no statistics yet
        public string UpdatedAt { get; set; } = DateTime.Now.ToString("dd MMM yyyy, hh:mm tt");
    }
}
// ModuleDashboardViewModel (existing, Models/ModuleModels.cs) does NOT change.
