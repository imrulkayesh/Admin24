// Models/Admin/UserManagementModels.cs
// UserManagement module (port of YA Pages/UserManagement). Users/roles live in the shared tables
// (UC_USERS, UC_USERROLE, UC_ROLESETUP, EM_EMPLOYEE); the menu screens (Module / Task Manager /
// Task Permission) manage Admin24's OWN menu tables (UC_MODULE_COPY / UC_TASKMANAGER_COPY /
// UC_TASKPERMISSION_COPY) so the legacy YA menus stay untouched.
namespace AlphaCare.Models
{
    // ---- User Setup / User Status Update ----
    public class UserSetupViewModel
    {
        public int CenterId { get; set; }                        // grid filter (old ddlBusinessCenter)
        public bool WithStatusButtons { get; set; }              // true = UserStatusUpdate page (adds Active/Inactive)
        public string? Search { get; set; }                      // staff id search (old tbxSearch)
        public List<SetupOption> Roles { get; set; } = new();
        public List<SetupOption> Centers { get; set; } = new();
        public List<SetupOption> Companies { get; set; } = new();
        public List<SetupOption> Departments { get; set; } = new();
        public List<SetupOption> Designations { get; set; } = new();
        public List<UserRow> Rows { get; set; } = new();
    }

    public class UserRow
    {
        public string? ID { get; set; }                          // UC_USERS.ID (= staff id)
        public string? USERNAME { get; set; }
        public string? EMPLOYEENAME { get; set; }
        public string? OFFICEMOBILE { get; set; }
        public string? CENTERCODE { get; set; }
        public string? DEPARTMENTCODE { get; set; }
        public string? DESIGNATIONNAME { get; set; }
        public string? ROLENAME { get; set; }
        public int ISACTIVE { get; set; }
    }

    public class UserEditModel                                   // GetUser (fills the form on Edit)
    {
        public string? STAFFID { get; set; }
        public string? USERNAME { get; set; }
        public string? EMPLOYEENAME { get; set; }
        public string? OFFICEMOBILE { get; set; }
        public string? OFFICEMAIL { get; set; }
        public int ROLEID { get; set; }
        public int COMPANYID { get; set; }
        public int BUSINESSCENTERID { get; set; }
        public int DEPTID { get; set; }
        public int DESIGNATIONID { get; set; }
        public int ISACTIVE { get; set; }
    }

    public class SaveUserRequest
    {
        public string? StaffId { get; set; }
        public string? Name { get; set; }
        public string? Mobile { get; set; }
        public string? Email { get; set; }
        public string? Password { get; set; }                    // optional on edit (YA always overwrote it)
        public int RoleId { get; set; }
        public int CenterId { get; set; }
        public int CompanyId { get; set; }
        public int DepartmentId { get; set; }
        public int DesignationId { get; set; }
        public bool IsEdit { get; set; }
    }

    // ---- User Role (multi role checkboxes) ----
    public class UserRoleViewModel
    {
        public List<SetupOption> Roles { get; set; } = new();    // all UC_ROLESETUP rows
    }

    // ---- Role Setup ----
    public class RoleSetupViewModel
    {
        public List<SetupOption> Parents { get; set; } = new();  // priority / parent dropdown (all roles)
        public List<RoleRow> Rows { get; set; } = new();
    }

    public class RoleRow
    {
        public int ID { get; set; }
        public string? ROLENAME { get; set; }
        public string? DESCRIPTION { get; set; }
        public int PARRENTID { get; set; }
        public string? CREATEDBY { get; set; }
        public DateTime? CREATEDATE { get; set; }
    }

    // ---- Module (UC_MODULE_COPY) ----
    public class ModuleSetupRow
    {
        public int ID { get; set; }
        public string? MODULENAME { get; set; }
        public string? URL { get; set; }
        public string? ICON { get; set; }
        public int MODULEORDER { get; set; }
        public string? CREATEDBY { get; set; }
        public DateTime? CREATEDATE { get; set; }
    }

    // ---- Task Manager (UC_TASKMANAGER_COPY) ----
    public class TaskSetupViewModel
    {
        public int ModuleId { get; set; }
        public List<SetupOption> Modules { get; set; } = new();
        public List<SetupOption> Parents { get; set; } = new();  // root tasks of the module
        public List<TaskRow> Rows { get; set; } = new();
    }

    public class TaskRow
    {
        public int ID { get; set; }
        public string? TEXTENG { get; set; }
        public string? TEXTBAN { get; set; }
        public string? URL { get; set; }
        public int PARENTID { get; set; }
        public int ORDERCOLUMN { get; set; }
        public string? ICON { get; set; }
        public int MODULEID { get; set; }
        public string? CREATEDBY { get; set; }
        public DateTime? CREATEDATE { get; set; }
    }

    // ---- Task Permission (role -> checked menu tree) ----
    public class TaskPermissionViewModel
    {
        public int RoleId { get; set; }
        public List<SetupOption> Roles { get; set; } = new();
        public List<TaskTreeNode> Tree { get; set; } = new();
    }

    public class TaskTreeNode
    {
        public int ID { get; set; }
        public string Text { get; set; } = "";
        public string? Module { get; set; }                      // module name on root nodes
        public bool Checked { get; set; }
        public List<TaskTreeNode> Children { get; set; } = new();
    }
}
