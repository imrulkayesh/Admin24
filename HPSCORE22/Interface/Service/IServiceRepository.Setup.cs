// Interface/Service/IServiceRepository.Setup.cs
// Service setup pages (old ServiceCenter, Category, ServiceTeam, AddTeamMember, Machine, AccessoriesCategory, Accessories).
// Delete is not offered: the delete buttons were switched off in YA.
using AlphaCare.Models;

namespace AlphaCare.Interface
{
    public partial interface IServiceRepository
    {
        // ---------- service center (SC_SERVICECENTER) ----------
        List<ServiceCenterModel> GetServiceCenters();
        ServiceCenterModel? GetServiceCenter(int id);
        bool ServiceCenterNameExists(string name, int businessCenterId, int exceptId);
        int InsertServiceCenter(string name, int businessCenterId, string adminStaffId, string userId);
        int UpdateServiceCenter(int id, string name, int businessCenterId, string adminStaffId, string userId);

        // ---------- category (SC_CATEGORY) ----------
        List<ServiceCategoryModel> GetCategories(int serviceCenterId);
        ServiceCategoryModel? GetCategory(int id);
        bool CategoryNameExists(string name, int serviceCenterId, int exceptId);
        int InsertCategory(string name, int serviceCenterId);
        int UpdateCategory(int id, string name, int serviceCenterId);

        // ---------- service team (SC_SERVICETEAM) ----------
        List<ServiceTeamModel> GetTeams(int serviceCenterId);
        ServiceTeamModel? GetTeam(int id);
        /// <summary>Old GetByServiceCenterIdOld (team drop down of Add Team Member)</summary>
        List<ServiceOption> GetTeamOptions(int serviceCenterId);
        bool TeamNameExists(string name, int serviceCenterId, int exceptId);
        int InsertTeam(string name, int serviceCenterId, int businessCenterId, string supervisorStaffId, string staffId);
        int UpdateTeam(int id, string name, int serviceCenterId, int businessCenterId, string supervisorStaffId, string staffId);

        // ---------- team member (SC_TEAMMEMBER) ----------
        /// <summary>Old GetAllTeamMemberOld(teamId)</summary>
        List<ServiceTeamMemberModel> GetTeamMembers(int teamId);
        ServiceTeamMemberModel? GetTeamMember(int id);
        int InsertTeamMember(int teamId, int serviceCenterId, int businessCenterId, string memberStaffId, string userId);
        int UpdateTeamMember(int id, int teamId, int serviceCenterId, int businessCenterId, string memberStaffId, string userId);

        // ---------- machine (SC_MACHINE) ----------
        List<ServiceMachineModel> GetMachines(int businessCenterId);
        ServiceMachineModel? GetMachine(int id);
        bool MachineNameExists(string name, int businessCenterId, int companyId, int exceptId);
        int InsertMachine(int businessCenterId, int companyId, int departmentId, string name, string code, string staffId);
        int UpdateMachine(int id, int businessCenterId, int companyId, int departmentId, string name, string code, string staffId);

        // ---------- accessories category (SC_ACCESSORIESCATEGORY) ----------
        AccessoriesCategoryModel? GetAccessoryCategory(int id);
        bool AccessoryCategoryNameExists(string name, int exceptId);
        int InsertAccessoryCategory(string name);
        int UpdateAccessoryCategory(int id, string name);

        // ---------- accessories (SC_ACCESSORIES) ----------
        List<ServiceAccessoryModel> GetAccessories(int serviceCenterId);
        ServiceAccessoryModel? GetAccessory(int id);
        bool AccessoryNameExists(string name, int categoryId, int serviceCenterId, int exceptId);
        int InsertAccessory(int categoryId, string name, int stock, int businessCenterId, int serviceCenterId, string staffId);
        int UpdateAccessory(int id, int categoryId, string name, int stock, int businessCenterId, int serviceCenterId, string staffId);
    }
}
