// Interface/Admin/IUserManagementRepository.cs
// UserManagement module (port of YA Pages/UserManagement + App_Code/dal/UserControl).
// Users / roles are the shared tables (UC_USERS, UC_USERROLE, UC_ROLESETUP, EM_EMPLOYEE, DP_APPROVER);
// the menu screens target Admin24's own UC_MODULE_COPY / UC_TASKMANAGER_COPY / UC_TASKPERMISSION_COPY.
using AlphaCare.Models;

namespace AlphaCare.Interface
{
    public interface IUserManagementRepository
    {
        bool HasMenuPermission(string staffId, string url);
        bool IsSuperAdmin(string staffId);

        // ---- lookups ----
        List<SetupOption> GetRolesForUserSetup(bool isSuperAdmin);      // old dalUser.GetRolesForUserSetup
        List<SetupOption> GetAllRoles();                                // every UC_ROLESETUP row
        List<SetupOption> Lookup(string key);                           // Centers / Companies / Departments / Designations
        List<string> SearchStaffIds(string term);                       // old SearchStaffId web method ("ID-NAME", max 8)

        // ---- User Setup / User Status Update ----
        List<UserRow> GetUsers(int centerId);
        List<UserRow> SearchUsers(string staffId);                      // old GetUsersSearch (exact USERID)
        UserEditModel? GetUserById(string staffId);
        bool UserExists(string staffId);
        void CreateUser(SaveUserRequest r, string byStaffId);           // UC_USERS + EM_EMPLOYEE + UC_USERROLE (+ DP_APPROVER for role 12/22)
        void UpdateUser(SaveUserRequest r, string byStaffId);
        int SetUserActive(string staffId, bool active);                 // old UserActive / UserInactive
        int ChangePassword(string username, string newPassword);        // old ChangePassword (SHA1, by USERNAME)

        // ---- User Role (multi role) ----
        List<int> GetUserRoleIds(string staffId);
        void SaveUserRoles(string staffId, List<int> roleIds, string byStaffId);   // old: delete all + insert checked
        string? GetEmployeeName(string staffId);

        // ---- Role Setup (UC_ROLESETUP) ----
        List<RoleRow> GetRoles();                                       // old GetAllRole (ID > 4)
        List<SetupOption> GetRoleParents();                             // priority dropdown (all roles)
        RoleRow? GetRole(int id);
        bool RoleNameExists(string name, int excludeId);
        int GetRolePriority(int roleId);
        void InsertRole(string name, string? description, int parentId, int priority, string byStaffId);
        void UpdateRole(int id, string name, string? description, int parentId, int priority, string byStaffId);
        int DeleteRole(int id);

        // ---- Module (UC_MODULE_COPY) ----
        List<ModuleSetupRow> GetModules();
        ModuleSetupRow? GetModule(int id);
        bool ModuleNameExists(string name, int excludeId);
        void InsertModule(string name, int order, string? url, string? icon, string byStaffId);
        void UpdateModule(int id, string name, int order, string? url, string? icon, string byStaffId);
        int DeleteModule(int id);

        // ---- Task Manager (UC_TASKMANAGER_COPY) ----
        List<TaskRow> GetTasks(int moduleId);
        TaskRow? GetTask(int id);
        List<SetupOption> GetRootTasks(int moduleId);
        void InsertTask(TaskRow t, string byStaffId);
        void UpdateTask(TaskRow t, string byStaffId);
        int DeleteTask(int id);

        // ---- Task Permission (UC_TASKPERMISSION_COPY) ----
        List<TaskTreeNode> GetTaskTree(int roleId);                     // all root tasks + children, Checked = permitted
        void SaveTaskPermissions(int roleId, List<int> taskIds, string byStaffId);
    }
}
