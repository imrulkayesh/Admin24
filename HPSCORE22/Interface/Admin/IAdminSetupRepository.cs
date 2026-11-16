// Interface/Admin/IAdminSetupRepository.cs
// Data access of the Admin setup pages (port of YA Pages/Admin + App_Code/dal/Settings).
// Every table / column / query lives behind a compile-time key (whitelist in AdminSetupRepository);
// user input only ever travels as bind parameters.
using AlphaCare.Models;

namespace AlphaCare.Interface
{
    public interface IAdminSetupRepository
    {
        bool HasMenuPermission(string staffId, string url);

        List<SetupOption> Lookup(string lookupKey, int parentId);                       // dropdowns (cascades)
        List<IDictionary<string, object?>> GetRows(string key, int filterId);           // the grid of a page
        IDictionary<string, object?>? GetRecord(string key, long id);                   // fill the form on Edit
        bool Duplicate(string key, string name, int parentId, long excludeId);          // old CheckDuplicate
        long Insert(string key, Dictionary<string, object?> values, string staffId);
        int Update(string key, long id, Dictionary<string, object?> values, string staffId);
        int Delete(string key, long id);                                                // old Common().Delete

        // Department Assign (old DepartmentAssign.aspx)
        List<DepartmentAssignRow> GetDepartmentsWithAssignment(int companyId, int centerId);
        void SaveDepartmentAssign(int companyId, int centerId, List<int> departmentIds, string userId);
    }
}
