// Repositories/GenericModuleDashboardProvider.cs
// Used by HomeController when no module provider matches. Also reused by the other providers for "quick actions".
using AlphaCare.Interface;
using AlphaCare.Models;

namespace Admin360.Repositories
{
    public class GenericModuleDashboardProvider : IModuleDashboardProvider
    {
        public bool CanHandle(ModuleModel module) => true;

        public ModuleDashboardData Build(ModuleModel module, string staffId, List<SideMenuItem> menu)
        {
            var data = BuildFromMenu(menu);
            data.Note = "Statistics for this module are not configured yet. Your menu shortcuts are shown below.";
            return data;
        }

        /// <summary>Stat cards + quick actions made only from the user's menu (works for every module).</summary>
        public static ModuleDashboardData BuildFromMenu(List<SideMenuItem> menu)
        {
            var leaves = menu.SelectMany(g => g.Children.Count > 0 ? g.Children : new List<SideMenuItem> { g })
                             .Where(i => !string.IsNullOrWhiteSpace(i.Url) && i.Url != "#").ToList();
            var colors = new[] { "#4f46e5", "#0891b2", "#d97706", "#7c3aed", "#059669", "#db2777", "#2563eb", "#0d9488" };

            var d = new ModuleDashboardData();
            d.Stats.Add(new DashStat { Title = "Menu Groups", Value = menu.Count.ToString(), Icon = "fa-solid fa-folder-tree", Color = colors[0] });
            d.Stats.Add(new DashStat { Title = "Menu Items", Value = leaves.Count.ToString(), Icon = "fa-solid fa-list-check", Color = colors[1] });
            for (int i = 0; i < Math.Min(8, leaves.Count); i++)
                d.Actions.Add(new DashAction { Title = leaves[i].Title, Url = leaves[i].Url, Color = colors[i % colors.Length], Icon = leaves[i].Icon ?? "fa-solid fa-arrow-right" });
            return d;
        }
    }
}
