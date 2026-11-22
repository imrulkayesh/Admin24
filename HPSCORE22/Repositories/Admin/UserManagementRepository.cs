// Repositories/Admin/UserManagementRepository.cs
// UserManagement module (port of YA Pages/UserManagement + App_Code/dal/UserControl) on the Admin24
// stack: DatabaseService (Oracle) + Dapper, parameterized queries only.
//
// Deliberate differences from YA (all documented in the migration report):
//  - Module / Task Manager / Task Permission write Admin24's own menu tables (UC_*_COPY), not YA's.
//  - EM_EMPLOYEE update no longer reassigns ID (the old DAL replaced the primary key on every update).
//  - On user edit the password is only overwritten when one was typed (YA always overwrote it).
//  - On user edit the selected role is upserted into UC_USERROLE (YA rewrote every row of the user,
//    which turned a multi-role user into duplicates of one role).
using System.Security.Cryptography;
using System.Text;
using AlphaCare.Interface;
using AlphaCare.Models;
using Dapper;
using QCMS.Services;

namespace Admin360.Repositories
{
    public class UserManagementRepository : IUserManagementRepository
    {
        private readonly DatabaseService _databaseService;
        private readonly ILogger<UserManagementRepository> _logger;

        public UserManagementRepository(DatabaseService databaseService, ILogger<UserManagementRepository> logger)
        {
            _databaseService = databaseService;
            _logger = logger;
        }

        // The UC_*_COPY menu tables are Admin24's own and are not part of the YA schema dump, so the
        // audit columns (CREATEDBY / CREATEDATE / UPDATEDBY / UPDATEDATE) may or may not exist on them.
        // Statements only name a column the table really has, otherwise every save dies with ORA-00904.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, HashSet<string>> _tableCols = new();
        private HashSet<string> Cols(string table)
        {
            return _tableCols.GetOrAdd(table.ToUpperInvariant(), t =>
            {
                using var conn = _databaseService.GetConnection();
                var cols = conn.Query<string>("SELECT COLUMN_NAME FROM USER_TAB_COLUMNS WHERE TABLE_NAME = :T", new { T = t })
                               .ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (cols.Count == 0)   // table owned by another schema (synonym) -> look it up there
                    cols = conn.Query<string>("SELECT COLUMN_NAME FROM ALL_TAB_COLUMNS WHERE TABLE_NAME = :T", new { T = t })
                               .ToHashSet(StringComparer.OrdinalIgnoreCase);
                return cols;
            });
        }
        private bool Has(string table, string column) => Cols(table).Contains(column);

        // NOT NULL / DEFAULT of each column + whether an enabled INSERT trigger fills values. A table copied with
        // CREATE TABLE ... AS SELECT keeps NOT NULL but loses DEFAULT values and triggers, so an insert that leaves
        // out ID / CREATEDATE dies with ORA-01400 (the Task Permission save bug). Read once per table and cached.
        private sealed record ColumnRule(bool Nullable, bool HasDefault);
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (Dictionary<string, ColumnRule> Cols, bool InsertTrigger)> _tableRules = new();
        private (Dictionary<string, ColumnRule> Cols, bool InsertTrigger) Rules(string table)
        {
            return _tableRules.GetOrAdd(table.ToUpperInvariant(), t =>
            {
                using var conn = _databaseService.GetConnection();
                const string own = "SELECT COLUMN_NAME, NULLABLE, DEFAULT_LENGTH FROM USER_TAB_COLUMNS WHERE TABLE_NAME = :T";
                const string all = "SELECT COLUMN_NAME, NULLABLE, DEFAULT_LENGTH FROM ALL_TAB_COLUMNS WHERE TABLE_NAME = :T";
                var rows = conn.Query(own, new { T = t }).ToList();
                if (rows.Count == 0) rows = conn.Query(all, new { T = t }).ToList();
                var cols = new Dictionary<string, ColumnRule>(StringComparer.OrdinalIgnoreCase);
                foreach (var r in rows)
                {
                    var d = (IDictionary<string, object?>)r;
                    string name = Convert.ToString(d["COLUMN_NAME"]) ?? "";
                    if (name.Length == 0 || cols.ContainsKey(name)) continue;
                    cols[name] = new ColumnRule(Convert.ToString(d["NULLABLE"]) != "N",
                                                d["DEFAULT_LENGTH"] != null && Convert.ToDecimal(d["DEFAULT_LENGTH"]) > 0);
                }
                bool trigger = conn.ExecuteScalar<int>(@"SELECT COUNT(1) FROM ALL_TRIGGERS
                                                          WHERE TABLE_NAME = :T AND STATUS = 'ENABLED' AND TRIGGERING_EVENT LIKE '%INSERT%'",
                                                       new { T = t }) > 0;
                return (cols, trigger);
            });
        }

        // the column exists and nothing fills it on insert (no DEFAULT) -> the statement must give a value
        private bool NeedsValue(string table, string column) =>
            Rules(table).Cols.TryGetValue(column, out var c) && !c.HasDefault;

        // ID must be supplied by the statement: NOT NULL, no DEFAULT and no INSERT trigger that fills it
        private bool NeedsGeneratedId(string table) =>
            Rules(table).Cols.TryGetValue("ID", out var c) && !c.Nullable && !c.HasDefault && !Rules(table).InsertTrigger;

        // audit columns of an INSERT / UPDATE, only the ones the table really has (":By" = the logged in staff id)
        private string AuditInsertCols(string table) =>
            (Has(table, "CREATEDBY") ? ", CREATEDBY" : "") + (NeedsValue(table, "CREATEDATE") ? ", CREATEDATE" : "");
        private string AuditInsertVals(string table) =>
            (Has(table, "CREATEDBY") ? ", :By" : "") + (NeedsValue(table, "CREATEDATE") ? ", SYSDATE" : "");
        private string AuditUpdate(string table) =>
            (Has(table, "UPDATEDBY") ? ", UPDATEDBY = :By" : "") + (Has(table, "UPDATEDATE") ? ", UPDATEDATE = SYSDATE" : "");

        // a NOT NULL column without DEFAULT that the statement does not fill -> say which one (shown on the page)
        private void EnsureCovered(string table, IEnumerable<string> givenColumns)
        {
            var rules = Rules(table);
            if (rules.InsertTrigger) return;     // a trigger may fill it - let Oracle decide
            var given = new HashSet<string>(givenColumns.Select(c => c.Trim()), StringComparer.OrdinalIgnoreCase);
            var missing = rules.Cols.Where(c => !c.Value.Nullable && !c.Value.HasDefault && !given.Contains(c.Key)).Select(c => c.Key).ToList();
            if (missing.Count > 0)
                throw new InvalidOperationException($"{table} needs a value for {string.Join(", ", missing)} (NOT NULL without default).");
        }

        // old Encryption.EncryptionWithSh: SHA1 over ASCII bytes, lower case hex (UC_USERS.PASSWORD format)
        internal static string Sha1Hex(string text)
        {
            byte[] hash = SHA1.HashData(Encoding.ASCII.GetBytes(text ?? ""));
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        // ============================================================================================
        // PERMISSION
        // ============================================================================================
        public bool HasMenuPermission(string staffId, string url)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT COUNT(1) FROM UC_USERROLE R
                 WHERE R.STAFFID = :StaffId
                   AND (R.ROLEID = 1 OR R.ROLEID IN (
                        SELECT TP.ROLEID
                          FROM UC_TASKPERMISSION_COPY TP
                         INNER JOIN UC_TASKMANAGER_COPY T ON T.ID = TP.TASKID
                         WHERE LOWER(LTRIM(TRIM(T.URL), '~/')) = :Url))";
            string u = (url ?? "").Trim().TrimStart('~', '/').ToLowerInvariant();
            return conn.ExecuteScalar<int>(sql, new { StaffId = staffId, Url = u }) > 0;
        }

        public bool IsSuperAdmin(string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<int>("SELECT COUNT(1) FROM UC_USERROLE WHERE STAFFID = :StaffId AND ROLEID = 1",
                                           new { StaffId = staffId }) > 0;
        }

        // ============================================================================================
        // LOOKUPS
        // ============================================================================================
        public List<SetupOption> GetRolesForUserSetup(bool isSuperAdmin)
        {
            using var conn = _databaseService.GetConnection();
            string sql = isSuperAdmin
                ? "SELECT ID AS \"Value\", ROLENAME AS \"Text\" FROM UC_ROLESETUP ORDER BY ROLENAME"
                : "SELECT ID AS \"Value\", ROLENAME AS \"Text\" FROM UC_ROLESETUP WHERE ID > 2 ORDER BY ROLENAME";
            return Options(conn.Query(sql));
        }

        public List<SetupOption> GetAllRoles()
        {
            using var conn = _databaseService.GetConnection();
            return Options(conn.Query("SELECT ID AS \"Value\", ROLENAME AS \"Text\" FROM UC_ROLESETUP ORDER BY ID"));
        }

        private static readonly Dictionary<string, string> Lookups = new()
        {
            // old UserSetup.LoadDdl: active locations only; UserStatusUpdate listed all - kept the stricter one
            ["Centers"] = "SELECT ID AS \"Value\", CENTERNAME AS \"Text\" FROM BS_BUSINESSCENTER WHERE LOCATIONSTATUS = '1' ORDER BY CENTERNAME",
            ["AllCenters"] = "SELECT ID AS \"Value\", CENTERNAME AS \"Text\" FROM BS_BUSINESSCENTER ORDER BY CENTERNAME",
            ["Companies"] = "SELECT ID AS \"Value\", COMPANYNAME AS \"Text\" FROM BS_COMPANY ORDER BY COMPANYNAME",
            ["Departments"] = "SELECT ID AS \"Value\", DEPARTMENTNAME AS \"Text\" FROM BS_DEPARTMENT ORDER BY DEPARTMENTNAME",
            ["Designations"] = "SELECT ID AS \"Value\", DESIGNATIONNAME AS \"Text\" FROM BS_DESIGNATION ORDER BY DESIGNATIONNAME",
        };

        public List<SetupOption> Lookup(string key)
        {
            if (!Lookups.TryGetValue(key, out string? sql)) throw new ArgumentOutOfRangeException(nameof(key));
            using var conn = _databaseService.GetConnection();
            return Options(conn.Query(sql));
        }

        private static List<SetupOption> Options(IEnumerable<dynamic> rows) =>
            rows.Select(r => { var d = (IDictionary<string, object?>)r; return new SetupOption { Value = Convert.ToString(d["Value"]) ?? "", Text = Convert.ToString(d["Text"]) ?? "" }; }).ToList();

        // old UserRole.SearchStaffId: LIKE on the staff id, first 8, "STAFFID-NAME"
        public List<string> SearchStaffIds(string term)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT STAFFID || '-' || EMPLOYEENAME AS S FROM EM_EMPLOYEE
                 WHERE LOWER(STAFFID) LIKE '%' || LOWER(:Term) || '%' AND ROWNUM <= 8";
            return conn.Query<string>(sql, new { Term = (term ?? "").Trim() }).ToList();
        }

        // ============================================================================================
        // USER SETUP / USER STATUS UPDATE
        // ============================================================================================
        private const string UserSelect = @"
            SELECT U.ID, U.USERNAME, U.ISACTIVE, E.EMPLOYEENAME, E.OFFICEMOBILE,
                   B.CENTERCODE, D.DEPARTMENTCODE, DE.DESIGNATIONNAME, R.ROLENAME
              FROM UC_USERS U
             INNER JOIN EM_EMPLOYEE E ON E.USERID = U.ID
             INNER JOIN BS_BUSINESSCENTER B ON B.ID = E.BUSINESSCENTERID
             INNER JOIN BS_COMPANY C ON C.ID = E.COMPANYID
             INNER JOIN BS_DEPARTMENT D ON D.ID = E.DEPARTMENTID
             INNER JOIN BS_DESIGNATION DE ON DE.ID = E.DESIGNATIONID
             INNER JOIN UC_ROLESETUP R ON R.ID = U.ROLEID";

        public List<UserRow> GetUsers(int centerId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Query<UserRow>(UserSelect + " WHERE E.BUSINESSCENTERID = :CenterId ORDER BY E.EMPLOYEENAME",
                                       new { CenterId = centerId }).ToList();
        }

        public List<UserRow> SearchUsers(string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Query<UserRow>(UserSelect + " WHERE E.USERID = :StaffId", new { StaffId = (staffId ?? "").Trim() }).ToList();
        }

        public UserEditModel? GetUserById(string staffId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT E.STAFFID, U.USERNAME, E.EMPLOYEENAME, E.OFFICEMOBILE, E.OFFICEMAIL, U.ROLEID,
                       E.COMPANYID, E.BUSINESSCENTERID, E.DEPARTMENTID AS DEPTID, E.DESIGNATIONID, U.ISACTIVE
                  FROM UC_USERS U
                 INNER JOIN EM_EMPLOYEE E ON E.USERID = U.ID
                 WHERE U.ID = :Id";
            return conn.QueryFirstOrDefault<UserEditModel>(sql, new { Id = (staffId ?? "").Trim() });
        }

        public bool UserExists(string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<int>("SELECT COUNT(1) FROM UC_USERS WHERE ID = :Id", new { Id = (staffId ?? "").Trim() }) > 0;
        }

        // old btnSave_Click: UC_USERS row (password = SHA1 of the staff id), EM_EMPLOYEE update-or-insert,
        // DP_APPROVER for the dispatch approver roles (12 / 22, errors swallowed as before), UC_USERROLE row
        public void CreateUser(SaveUserRequest r, string byStaffId)
        {
            string staffId = (r.StaffId ?? "").Trim();
            const string U = "UC_USERS";
            const string E = "EM_EMPLOYEE";

            // UC_USERS: optional columns only when the table has them (an unknown column = ORA-00904, the whole save failed)
            string uCols = "ID, USERNAME, PASSWORD, ROLEID"
                         + (Has(U, "DEPARTMENTID") ? ", DEPARTMENTID" : "")
                         + (NeedsValue(U, "ISACTIVE") ? ", ISACTIVE" : "")
                         + AuditInsertCols(U);
            string uVals = ":Id, :Id, :Pwd, :RoleId"
                         + (Has(U, "DEPARTMENTID") ? ", :DeptId" : "")
                         + (NeedsValue(U, "ISACTIVE") ? ", 1" : "")
                         + AuditInsertVals(U);
            EnsureCovered(U, uCols.Split(','));

            string eCols = "ID, STAFFID, USERID, DESIGNATIONID, EMPLOYEENAME, OFFICEMOBILE, OFFICEMAIL, BUSINESSCENTERID, COMPANYID, DEPARTMENTID"
                         + AuditInsertCols(E);
            string eVals = "SQ_EM_EMPLOYEE.NEXTVAL, :Id, :Id, :DesignationId, :Name, :Mobile, :Email, :CenterId, :CompanyId, :DepartmentId"
                         + AuditInsertVals(E);

            using var conn = _databaseService.GetConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();

            conn.Execute($"INSERT INTO UC_USERS ({uCols}) VALUES ({uVals})",
                         new { Id = staffId, Pwd = Sha1Hex(staffId), r.RoleId, DeptId = r.DepartmentId, By = byStaffId }, tx);

            int updated = UpdateEmployee(conn, tx, r, staffId, byStaffId);
            if (updated == 0)
                conn.Execute($"INSERT INTO EM_EMPLOYEE ({eCols}) VALUES ({eVals})",
                             new { Id = staffId, r.DesignationId, r.Name, r.Mobile, r.Email, r.CenterId, r.CompanyId, r.DepartmentId, By = byStaffId }, tx);

            InsertApproverIfNeeded(conn, tx, r, staffId, byStaffId);
            InsertUserRole(conn, tx, staffId, r.RoleId, byStaffId);
            tx.Commit();
        }

        // old btnEdit_Click
        public void UpdateUser(SaveUserRequest r, string byStaffId)
        {
            string staffId = (r.StaffId ?? "").Trim();
            const string U = "UC_USERS";
            const string R = "UC_USERROLE";

            string setSql = "ROLEID = :RoleId"
                       + (Has(U, "DEPARTMENTID") ? ", DEPARTMENTID = :DeptId" : "")
                       + AuditUpdate(U)
                       + (string.IsNullOrEmpty(r.Password) ? "" : ", PASSWORD = :Pwd");

            using var conn = _databaseService.GetConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();

            int changed = conn.Execute($"UPDATE UC_USERS SET {setSql} WHERE ID = :Id",
                                       new { r.RoleId, DeptId = r.DepartmentId, By = byStaffId, Pwd = Sha1Hex(r.Password ?? ""), Id = staffId }, tx);
            if (changed == 0) throw new InvalidOperationException("UC_USERS has no row with ID " + staffId + ".");

            UpdateEmployee(conn, tx, r, staffId, byStaffId);
            InsertApproverIfNeeded(conn, tx, r, staffId, byStaffId);

            // keep the user's other roles; make sure the selected one exists and is active
            int have = conn.ExecuteScalar<int>("SELECT COUNT(1) FROM UC_USERROLE WHERE STAFFID = :Id AND ROLEID = :RoleId",
                                               new { Id = staffId, r.RoleId }, tx);
            if (have == 0)
                InsertUserRole(conn, tx, staffId, r.RoleId, byStaffId);
            else
            {
                string roleSet = ((Has(R, "ACTIVE") ? ", ACTIVE = 1" : "") + AuditUpdate(R)).TrimStart(',', ' ');
                if (roleSet.Length > 0)
                    conn.Execute($"UPDATE UC_USERROLE SET {roleSet} WHERE STAFFID = :Id AND ROLEID = :RoleId",
                                 new { By = byStaffId, Id = staffId, r.RoleId }, tx);
            }
            tx.Commit();
        }

        // one UC_USERROLE row (User Setup create / edit). ID only when the table needs it and nothing fills it.
        private void InsertUserRole(Oracle.ManagedDataAccess.Client.OracleConnection conn, System.Data.IDbTransaction tx,
                                    string staffId, int roleId, string byStaffId)
        {
            const string R = "UC_USERROLE";
            bool id = NeedsGeneratedId(R);
            string cols = (id ? "ID, " : "") + "STAFFID, ROLEID" + (Has(R, "ACTIVE") ? ", ACTIVE" : "") + AuditInsertCols(R);
            string vals = (id ? "(SELECT NVL(MAX(ID),0)+1 FROM UC_USERROLE), " : "") + ":Id, :RoleId" + (Has(R, "ACTIVE") ? ", 1" : "") + AuditInsertVals(R);
            EnsureCovered(R, cols.Split(','));
            conn.Execute($"INSERT INTO UC_USERROLE ({cols}) VALUES ({vals})", new { Id = staffId, RoleId = roleId, By = byStaffId }, tx);
        }

        private int UpdateEmployee(Oracle.ManagedDataAccess.Client.OracleConnection conn, System.Data.IDbTransaction tx,
                                   SaveUserRequest r, string staffId, string byStaffId)
        {
            // old dalEmployee.Update minus the ID reassignment
            return conn.Execute(@"UPDATE EM_EMPLOYEE SET DESIGNATIONID = :DesignationId, EMPLOYEENAME = :Name,
                                         OFFICEMOBILE = :Mobile, OFFICEMAIL = :Email, BUSINESSCENTERID = :CenterId,
                                         COMPANYID = :CompanyId, DEPARTMENTID = :DepartmentId" + AuditUpdate("EM_EMPLOYEE") + @"
                                   WHERE STAFFID = :Id",
                                new { r.DesignationId, r.Name, r.Mobile, r.Email, r.CenterId, r.CompanyId, r.DepartmentId, By = byStaffId, Id = staffId }, tx);
        }

        // old: roles 12 / 22 also become a dispatch approver (priority 1, final); failures were ignored
        private void InsertApproverIfNeeded(Oracle.ManagedDataAccess.Client.OracleConnection conn, System.Data.IDbTransaction tx,
                                            SaveUserRequest r, string staffId, string byStaffId)
        {
            if (r.RoleId != 12 && r.RoleId != 22) return;
            try
            {
                conn.Execute(@"INSERT INTO DP_APPROVER (ID, COMPANYID, APPROVERID, PRIORITY, BUSINESSCENTERID, DEPARTMENTID, ISFINALAPPROVER, CREATEDBY)
                               VALUES (SQ_DP_APPROVER.NEXTVAL, :CompanyId, :Id, 1, :CenterId, :DepartmentId, 1, :By)",
                             new { r.CompanyId, Id = staffId, r.CenterId, r.DepartmentId, By = byStaffId }, tx);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DP_APPROVER insert skipped for {Staff} (as in YA, the save continues)", staffId);
            }
        }

        public int SetUserActive(string staffId, bool active)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute("UPDATE UC_USERS SET ISACTIVE = :Active, UPDATEDATE = SYSDATE WHERE ID = :Id",
                                new { Active = active ? 1 : 0, Id = (staffId ?? "").Trim() });
        }

        public int ChangePassword(string username, string newPassword)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute("UPDATE UC_USERS SET PASSWORD = :Pwd WHERE USERNAME = :User",
                                new { Pwd = Sha1Hex(newPassword), User = (username ?? "").Trim() });
        }

        // ============================================================================================
        // USER ROLE (multi role checkboxes)
        // ============================================================================================
        public List<int> GetUserRoleIds(string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Query<int>("SELECT ROLEID FROM UC_USERROLE WHERE STAFFID = :Id", new { Id = (staffId ?? "").Trim() }).ToList();
        }

        public void SaveUserRoles(string staffId, List<int> roleIds, string byStaffId)
        {
            string id = (staffId ?? "").Trim();
            using var conn = _databaseService.GetConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();
            conn.Execute("DELETE FROM UC_USERROLE WHERE STAFFID = :Id", new { Id = id }, tx);
            foreach (int roleId in roleIds.Distinct())
                conn.Execute("INSERT INTO UC_USERROLE (STAFFID, ROLEID, ACTIVE, CREATEDBY) VALUES (:Id, :RoleId, 1, :By)",
                             new { Id = id, RoleId = roleId, By = byStaffId }, tx);
            tx.Commit();
        }

        public string? GetEmployeeName(string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<string?>("SELECT MAX(EMPLOYEENAME) FROM EM_EMPLOYEE WHERE STAFFID = :Id",
                                               new { Id = (staffId ?? "").Trim() });
        }

        // ============================================================================================
        // ROLE SETUP (UC_ROLESETUP - shared with YA)
        // ============================================================================================
        public List<RoleRow> GetRoles()
        {
            using var conn = _databaseService.GetConnection();
            return conn.Query<RoleRow>("SELECT ID, ROLENAME, DESCRIPTION, PARRENTID, CREATEDBY, CREATEDATE FROM UC_ROLESETUP WHERE ID > 4 ORDER BY ID").ToList();
        }

        public List<SetupOption> GetRoleParents()
        {
            using var conn = _databaseService.GetConnection();
            return Options(conn.Query("SELECT ID AS \"Value\", ROLENAME AS \"Text\" FROM UC_ROLESETUP ORDER BY ID"));
        }

        public RoleRow? GetRole(int id)
        {
            using var conn = _databaseService.GetConnection();
            return conn.QueryFirstOrDefault<RoleRow>("SELECT ID, ROLENAME, DESCRIPTION, PARRENTID, CREATEDBY, CREATEDATE FROM UC_ROLESETUP WHERE ID = :Id", new { Id = id });
        }

        public bool RoleNameExists(string name, int excludeId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<int>("SELECT COUNT(1) FROM UC_ROLESETUP WHERE UPPER(ROLENAME) = UPPER(:Name) AND ID <> :Ex",
                                           new { Name = name, Ex = excludeId }) > 0;
        }

        public int GetRolePriority(int roleId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<int>("SELECT NVL(MAX(PRIORITY), 0) FROM UC_ROLESETUP WHERE ID = :Id", new { Id = roleId });
        }

        public void InsertRole(string name, string? description, int parentId, int priority, string byStaffId)
        {
            using var conn = _databaseService.GetConnection();
            conn.Execute(@"INSERT INTO UC_ROLESETUP (ID, ROLENAME, PRIORITY, PARRENTID, DESCRIPTION, CREATEDBY)
                           VALUES (SQ_UC_ROLESETUP.NEXTVAL, :Name, :Priority, :ParentId, :Description, :By)",
                         new { Name = name, Priority = priority, ParentId = parentId, Description = description, By = byStaffId });
        }

        public void UpdateRole(int id, string name, string? description, int parentId, int priority, string byStaffId)
        {
            using var conn = _databaseService.GetConnection();
            conn.Execute(@"UPDATE UC_ROLESETUP SET ROLENAME = :Name, PRIORITY = :Priority, PARRENTID = :ParentId,
                                  DESCRIPTION = :Description, UPDATEDBY = :By, UPDATEDATE = SYSDATE WHERE ID = :Id",
                         new { Name = name, Priority = priority, ParentId = parentId, Description = description, By = byStaffId, Id = id });
        }

        public int DeleteRole(int id)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute("DELETE FROM UC_ROLESETUP WHERE ID = :Id", new { Id = id });
        }

        // ============================================================================================
        // MODULE (UC_MODULE_COPY - Admin24's own menu)
        // ============================================================================================
        // CREATEDBY / CREATEDATE only when the table has them (see Cols above)
        private string ModuleSelect() =>
            "SELECT ID, MODULENAME, URL, ICON, MODULEORDER"
            + (Has("UC_MODULE_COPY", "CREATEDBY") ? ", CREATEDBY" : ", NULL AS CREATEDBY")
            + (Has("UC_MODULE_COPY", "CREATEDATE") ? ", CREATEDATE" : ", NULL AS CREATEDATE")
            + " FROM UC_MODULE_COPY";

        public List<ModuleSetupRow> GetModules()
        {
            using var conn = _databaseService.GetConnection();
            return conn.Query<ModuleSetupRow>(ModuleSelect() + " ORDER BY MODULEORDER").ToList();
        }

        public ModuleSetupRow? GetModule(int id)
        {
            using var conn = _databaseService.GetConnection();
            return conn.QueryFirstOrDefault<ModuleSetupRow>(ModuleSelect() + " WHERE ID = :Id", new { Id = id });
        }

        public bool ModuleNameExists(string name, int excludeId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<int>("SELECT COUNT(1) FROM UC_MODULE_COPY WHERE UPPER(MODULENAME) = UPPER(:Name) AND ID <> :Ex",
                                           new { Name = name, Ex = excludeId }) > 0;
        }

        public void InsertModule(string name, int order, string? url, string? icon, string byStaffId)
        {
            using var conn = _databaseService.GetConnection();
            bool audit = Has("UC_MODULE_COPY", "CREATEDBY");
            conn.Execute(@"INSERT INTO UC_MODULE_COPY (ID, MODULENAME, MODULEORDER, URL, ICON, ISACTIVE" + (audit ? ", CREATEDBY" : "") + @")
                           VALUES ((SELECT NVL(MAX(ID),0)+1 FROM UC_MODULE_COPY), :Name, :OrderNo, :Url, :Icon, 1" + (audit ? ", :By" : "") + ")",
                         audit ? new { Name = name, OrderNo = order, Url = url, Icon = icon, By = byStaffId }
                               : (object)new { Name = name, OrderNo = order, Url = url, Icon = icon });
        }

        public void UpdateModule(int id, string name, int order, string? url, string? icon, string byStaffId)
        {
            using var conn = _databaseService.GetConnection();
            bool audit = Has("UC_MODULE_COPY", "UPDATEDBY");
            conn.Execute(@"UPDATE UC_MODULE_COPY SET MODULENAME = :Name, MODULEORDER = :OrderNo, URL = :Url, ICON = :Icon"
                         + (audit ? ", UPDATEDBY = :By, UPDATEDATE = SYSDATE" : "") + " WHERE ID = :Id",
                         audit ? new { Name = name, OrderNo = order, Url = url, Icon = icon, By = byStaffId, Id = id }
                               : (object)new { Name = name, OrderNo = order, Url = url, Icon = icon, Id = id });
        }

        public int DeleteModule(int id)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute("DELETE FROM UC_MODULE_COPY WHERE ID = :Id", new { Id = id });
        }

        // ============================================================================================
        // TASK MANAGER (UC_TASKMANAGER_COPY - Admin24's own menu)
        // ============================================================================================
        private string TaskSelect() =>
            "SELECT ID, TEXTENG, TEXTBAN, URL, PARENTID, ORDERCOLUMN, ICON, MODULEID"
            + (Has("UC_TASKMANAGER_COPY", "CREATEDBY") ? ", CREATEDBY" : ", NULL AS CREATEDBY")
            + (Has("UC_TASKMANAGER_COPY", "CREATEDATE") ? ", CREATEDATE" : ", NULL AS CREATEDATE")
            + " FROM UC_TASKMANAGER_COPY";

        public List<TaskRow> GetTasks(int moduleId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Query<TaskRow>(TaskSelect() + " WHERE MODULEID = :ModuleId ORDER BY ORDERCOLUMN",
                                       new { ModuleId = moduleId }).ToList();
        }

        public TaskRow? GetTask(int id)
        {
            using var conn = _databaseService.GetConnection();
            return conn.QueryFirstOrDefault<TaskRow>(TaskSelect() + " WHERE ID = :Id", new { Id = id });
        }

        public List<SetupOption> GetRootTasks(int moduleId)
        {
            using var conn = _databaseService.GetConnection();
            return Options(conn.Query(@"SELECT ID AS ""Value"", TEXTENG AS ""Text"" FROM UC_TASKMANAGER_COPY
                                         WHERE MODULEID = :ModuleId AND PARENTID = 0 ORDER BY ORDERCOLUMN", new { ModuleId = moduleId }));
        }

        public void InsertTask(TaskRow t, string byStaffId)
        {
            using var conn = _databaseService.GetConnection();
            bool audit = Has("UC_TASKMANAGER_COPY", "CREATEDBY");
            conn.Execute(@"INSERT INTO UC_TASKMANAGER_COPY (ID, TEXTBAN, TEXTENG, URL, PARENTID, ORDERCOLUMN, ICON, MODULEID, ISACTIVE" + (audit ? ", CREATEDBY" : "") + @")
                           VALUES ((SELECT NVL(MAX(ID),0)+1 FROM UC_TASKMANAGER_COPY), :TEXTBAN, :TEXTENG, :URL, :PARENTID, :ORDERCOLUMN, :ICON, :MODULEID, 1" + (audit ? ", :By" : "") + ")",
                         audit ? new { t.TEXTBAN, t.TEXTENG, t.URL, t.PARENTID, t.ORDERCOLUMN, t.ICON, t.MODULEID, By = byStaffId }
                               : (object)new { t.TEXTBAN, t.TEXTENG, t.URL, t.PARENTID, t.ORDERCOLUMN, t.ICON, t.MODULEID });
        }

        public void UpdateTask(TaskRow t, string byStaffId)
        {
            using var conn = _databaseService.GetConnection();
            bool audit = Has("UC_TASKMANAGER_COPY", "UPDATEDBY");
            conn.Execute(@"UPDATE UC_TASKMANAGER_COPY SET TEXTBAN = :TEXTBAN, TEXTENG = :TEXTENG, URL = :URL, PARENTID = :PARENTID,
                                  ORDERCOLUMN = :ORDERCOLUMN, ICON = :ICON, MODULEID = :MODULEID"
                         + (audit ? ", UPDATEDBY = :By, UPDATEDATE = SYSDATE" : "") + " WHERE ID = :ID",
                         audit ? new { t.TEXTBAN, t.TEXTENG, t.URL, t.PARENTID, t.ORDERCOLUMN, t.ICON, t.MODULEID, By = byStaffId, t.ID }
                               : (object)new { t.TEXTBAN, t.TEXTENG, t.URL, t.PARENTID, t.ORDERCOLUMN, t.ICON, t.MODULEID, t.ID });
        }

        public int DeleteTask(int id)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute("DELETE FROM UC_TASKMANAGER_COPY WHERE ID = :Id", new { Id = id });
        }

        // ============================================================================================
        // TASK PERMISSION (UC_TASKPERMISSION_COPY - Admin24's own menu permissions)
        // ============================================================================================
        public List<TaskTreeNode> GetTaskTree(int roleId)
        {
            using var conn = _databaseService.GetConnection();
            var all = conn.Query<TaskRow>(@"SELECT T.ID, T.TEXTENG, T.PARENTID, T.ORDERCOLUMN, T.MODULEID
                                              FROM UC_TASKMANAGER_COPY T ORDER BY T.MODULEID, T.ORDERCOLUMN").ToList();
            var modules = conn.Query("SELECT ID AS \"Value\", MODULENAME AS \"Text\" FROM UC_MODULE_COPY")
                              .ToDictionary(r => Convert.ToInt32(((IDictionary<string, object?>)r)["Value"]),
                                            r => Convert.ToString(((IDictionary<string, object?>)r)["Text"]) ?? "");
            var permitted = roleId > 0
                ? conn.Query<int>("SELECT TASKID FROM UC_TASKPERMISSION_COPY WHERE ROLEID = :RoleId", new { RoleId = roleId }).ToHashSet()
                : new HashSet<int>();

            var tree = new List<TaskTreeNode>();
            foreach (var root in all.Where(t => t.PARENTID == 0))
            {
                var node = new TaskTreeNode
                {
                    ID = root.ID,
                    Text = root.TEXTENG ?? "",
                    Module = modules.TryGetValue(root.MODULEID, out string? m) ? m : null,
                    Checked = permitted.Contains(root.ID)
                };
                foreach (var child in all.Where(t => t.PARENTID == root.ID))
                    node.Children.Add(new TaskTreeNode { ID = child.ID, Text = child.TEXTENG ?? "", Checked = permitted.Contains(child.ID) });
                tree.Add(node);
            }
            return tree;
        }

        // old btnSave_Click: wipe the role's permissions and insert the checked tasks.
        // UC_TASKPERMISSION_COPY is a copy of YA's UC_TASKPERMISSION: its ID (and CREATEDATE / ISACTIVE) kept NOT NULL
        // but lost the sequence / default, so the insert must give them - like every other _COPY insert here (MAX(ID)+1).
        public void SaveTaskPermissions(int roleId, List<int> taskIds, string byStaffId)
        {
            const string T = "UC_TASKPERMISSION_COPY";
            bool id = Has(T, "ID");
            string cols = (id ? "ID, " : "") + "TASKID, ROLEID"
                        + (NeedsValue(T, "ISACTIVE") ? ", ISACTIVE" : "")
                        + (NeedsValue(T, "ACTIVE") ? ", ACTIVE" : "")
                        + AuditInsertCols(T);
            string vals = (id ? "(SELECT NVL(MAX(ID),0)+1 FROM UC_TASKPERMISSION_COPY), " : "") + ":TaskId, :RoleId"
                        + (NeedsValue(T, "ISACTIVE") ? ", 1" : "")
                        + (NeedsValue(T, "ACTIVE") ? ", 1" : "")
                        + AuditInsertVals(T);
            EnsureCovered(T, cols.Split(','));
            string insert = $"INSERT INTO UC_TASKPERMISSION_COPY ({cols}) VALUES ({vals})";

            using var conn = _databaseService.GetConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();
            conn.Execute("DELETE FROM UC_TASKPERMISSION_COPY WHERE ROLEID = :RoleId", new { RoleId = roleId }, tx);
            foreach (int taskId in taskIds.Distinct())
                conn.Execute(insert, new { TaskId = taskId, RoleId = roleId, By = byStaffId }, tx);
            tx.Commit();
        }
    }
}
