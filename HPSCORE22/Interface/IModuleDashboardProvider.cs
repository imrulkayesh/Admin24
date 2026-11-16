// Interface/IModuleDashboardProvider.cs
using AlphaCare.Models;

namespace AlphaCare.Interface
{
    /// <summary>One class per module. HomeController picks the first provider whose CanHandle() is true.
    /// A module without a provider still gets the quick-view menu + a generic dashboard.</summary>
    public interface IModuleDashboardProvider
    {
        bool CanHandle(ModuleModel module);
        ModuleDashboardData Build(ModuleModel module, string staffId, List<SideMenuItem> menu);
    }
}
