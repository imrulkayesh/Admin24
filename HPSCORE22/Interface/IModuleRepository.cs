// Interface/IModuleRepository.cs
using AlphaCare.Models;

namespace AlphaCare.Interface
{
    public interface IModuleRepository
    {
        /// <summary>Admin (ROLEID 1 in UC_USERROLE) sees all modules, others only permitted modules.</summary>
        bool IsSuperAdmin(string staffId);
        List<ModuleModel> GetModules(string staffId);
        List<TaskMenuRow> GetModuleTasks(string staffId, int moduleId);
        int? GetModuleIdByUrl(string url);

        /// <summary>Flat rows to parent/child tree (old GetParanet + GetChild)</summary>
        List<SideMenuItem> BuildMenuTree(List<TaskMenuRow> rows);
    }
}
