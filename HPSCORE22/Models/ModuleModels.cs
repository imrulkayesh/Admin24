// Models/ModuleModels.cs
namespace AlphaCare.Models
{
    // UC_MODULE row (old Default.aspx used: ID, MODULENAME, ICON)
    public class ModuleModel
    {
        public int ID { get; set; }
        public string MODULENAME { get; set; } = "";
        public string? ICON { get; set; }
        public string? URL { get; set; }
    }

    // UC_TASKMANAGER row
    public class TaskMenuRow
    {
        public int ID { get; set; }
        public string? TEXTBAN { get; set; }
        public string? TEXTENG { get; set; }
        public string? URL { get; set; }
        public int PARENTID { get; set; }
        public int ORDERCOLUMN { get; set; }
        public string? ICON { get; set; }
        public int MODULEID { get; set; }
    }

    // Menu tree kept in session ("ModuleMenu") and drawn in the sidebar
    public class SideMenuItem
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string? Url { get; set; }
        public string? Icon { get; set; }
        public List<SideMenuItem> Children { get; set; } = new();
    }

    // Model of the module dashboard page (Views/Home/Module.cshtml)
    public class ModuleDashboardViewModel
    {
        public ModuleModel Module { get; set; } = new();
        public List<SideMenuItem> Menu { get; set; } = new();
    }
}
