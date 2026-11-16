// Repositories/Admin/AdminSetupRepository.cs
// Admin module setup pages (port of YA Pages/Admin + App_Code/dal/Settings) on the same stack as the
// rest of Admin24: DatabaseService (Oracle) + Dapper, parameterized queries only. The legacy DAL built
// its SQL by string concatenation; here every identifier comes from the compile-time whitelists below
// and every value is a bind parameter.
using AlphaCare.Interface;
using AlphaCare.Models;
using Dapper;
using QCMS.Services;

namespace Admin360.Repositories
{
    public class AdminSetupRepository : IAdminSetupRepository
    {
        private readonly DatabaseService _databaseService;

        public AdminSetupRepository(DatabaseService databaseService) => _databaseService = databaseService;

        // ============================================================================================
        // WHITELISTS - the only place table / column names exist
        // ============================================================================================
        private sealed record TableDef(string Table, string? Sequence, string[] Columns, bool Audit);

        // key -> table meta (Columns = the columns a page may write; Audit = CREATEDBY/UPDATEDBY exist)
        private static readonly Dictionary<string, TableDef> Tables = new()
        {
            ["Country"] = new("BS_COUNTRY", "SQ_BS_COUNTRY", new[] { "COUNTRYNAME", "COUNTRYCODE" }, false),
            ["Division"] = new("BS_DIVISION", "SQ_BS_DIVSION", new[] { "DIVISIONNAME", "COUNTRYID" }, false),
            ["District"] = new("BS_DISTRICT", "SQ_BS_DISTRICT", new[] { "DISTRICTNAME", "DIVISIONID" }, false),
            ["Thana"] = new("BS_THANA", "SQ_BS_THANA", new[] { "THANANAME", "DISTRICTID" }, false),
            ["Location"] = new("BS_LOCATIONS", "SQ_BS_LOCATIONS", new[] { "NAME", "COUNTRYID", "THANAID", "TIMEZONE", "LOCATIONCODE" }, false),
            ["BusinessCenter"] = new("BS_BUSINESSCENTER", "SQ_BS_BUSINESSCENTER", new[] { "CENTERNAME", "CENTERCODE", "ADDRESS", "LOCATIONID" }, true),
            ["GroupofCompany"] = new("BS_GROUPOFCOMPANY", "SQ_BS_GROUPOFCOMPANY", new[] { "COMPANYTITLE", "ESTABLISHDATE" }, true),
            ["Company"] = new("BS_COMPANY", "SQ_BS_COMPANY", new[] { "COMPANYNAME", "GROUPOFCOMPANYID", "COMESTABLISHDATE" }, true),
            ["Department"] = new("BS_DEPARTMENT", "SQ_BS_DEPARTMENT", new[] { "DEPARTMENTNAME", "DEPARTMENTCODE" }, true),
            ["DepartmentUnit"] = new("BS_DEPARTMENTUNIT", "SQ_BS_DEPARTMENTUNIT", new[] { "DEPTUNITNAME", "DEPARTMENTID", "DEPTUNITCODE" }, true),
            ["Designation"] = new("BS_DESIGNATION", "SQ_BS_DESIGNATION", new[] { "DESIGNATIONNAME", "PRIORITY" }, true),
            ["Gender"] = new("BS_GENDER", "SQ_BS_GENDER", new[] { "GENDERNAME" }, false),
            ["Religion"] = new("BS_RELIGION", "SQ_BS_RELIGION", new[] { "RELIGIONNAME" }, false),
        };

        // dropdowns; :P = the parent value (ignored by the ones without cascade)
        private static readonly Dictionary<string, string> Lookups = new()
        {
            ["Country"] = "SELECT ID AS \"Value\", COUNTRYNAME AS \"Text\" FROM BS_COUNTRY ORDER BY COUNTRYNAME",
            ["Division"] = "SELECT ID AS \"Value\", DIVISIONNAME AS \"Text\" FROM BS_DIVISION WHERE COUNTRYID = :P ORDER BY DIVISIONNAME",
            ["District"] = "SELECT ID AS \"Value\", DISTRICTNAME AS \"Text\" FROM BS_DISTRICT WHERE DIVISIONID = :P ORDER BY DISTRICTNAME",
            ["Thana"] = "SELECT ID AS \"Value\", THANANAME AS \"Text\" FROM BS_THANA WHERE DISTRICTID = :P ORDER BY THANANAME",
            ["Location"] = "SELECT ID AS \"Value\", NAME AS \"Text\" FROM BS_LOCATIONS WHERE COUNTRYID = :P ORDER BY NAME",
            ["GroupofCompany"] = "SELECT ID AS \"Value\", COMPANYTITLE AS \"Text\" FROM BS_GROUPOFCOMPANY ORDER BY COMPANYTITLE",
            ["Department"] = "SELECT ID AS \"Value\", DEPARTMENTNAME AS \"Text\" FROM BS_DEPARTMENT ORDER BY DEPARTMENTNAME",
            ["Company"] = "SELECT ID AS \"Value\", COMPANYNAME AS \"Text\" FROM BS_COMPANY ORDER BY COMPANYNAME",
            ["Center"] = "SELECT ID AS \"Value\", CENTERNAME AS \"Text\" FROM BS_BUSINESSCENTER WHERE LOCATIONID = :P ORDER BY CENTERNAME",
        };

        // the grid of each page; :F = the filter value (pages without filter ignore it)
        private static readonly Dictionary<string, string> Grids = new()
        {
            ["Country"] = "SELECT ID, COUNTRYNAME, COUNTRYCODE FROM BS_COUNTRY ORDER BY ID",
            ["Division"] = "SELECT ID, DIVISIONNAME FROM BS_DIVISION WHERE COUNTRYID = :F ORDER BY DIVISIONNAME",
            ["District"] = "SELECT ID, DISTRICTNAME FROM BS_DISTRICT WHERE DIVISIONID = :F ORDER BY DISTRICTNAME",
            ["Thana"] = "SELECT ID, THANANAME FROM BS_THANA WHERE DISTRICTID = :F ORDER BY THANANAME",
            ["Location"] = "SELECT ID, NAME, LOCATIONCODE, TIMEZONE FROM BS_LOCATIONS WHERE THANAID = :F ORDER BY NAME",
            ["BusinessCenter"] = "SELECT ID, CENTERNAME, CENTERCODE, ADDRESS FROM BS_BUSINESSCENTER WHERE LOCATIONID = :F ORDER BY CENTERNAME",
            ["GroupofCompany"] = "SELECT ID, COMPANYTITLE, ESTABLISHDATE, CREATEDBY FROM BS_GROUPOFCOMPANY ORDER BY ID",
            ["Company"] = @"SELECT C.ID, C.COMPANYNAME, G.COMPANYTITLE, C.COMESTABLISHDATE, C.CREATEDBY
                              FROM BS_COMPANY C INNER JOIN BS_GROUPOFCOMPANY G ON G.ID = C.GROUPOFCOMPANYID
                             WHERE C.GROUPOFCOMPANYID = :F ORDER BY C.COMPANYNAME",
            ["Department"] = "SELECT ID, DEPARTMENTNAME, DEPARTMENTCODE, CREATEDBY FROM BS_DEPARTMENT ORDER BY DEPARTMENTNAME",
            ["DepartmentUnit"] = @"SELECT DU.ID, DU.DEPTUNITNAME, DU.DEPTUNITCODE, D.DEPARTMENTNAME
                                     FROM BS_DEPARTMENTUNIT DU INNER JOIN BS_DEPARTMENT D ON D.ID = DU.DEPARTMENTID
                                    WHERE DU.DEPARTMENTID = :F ORDER BY DU.DEPTUNITNAME",
            ["Designation"] = "SELECT ID, DESIGNATIONNAME, PRIORITY, CREATEDBY FROM BS_DESIGNATION ORDER BY DESIGNATIONNAME",
            ["Gender"] = "SELECT ID, GENDERNAME FROM BS_GENDER ORDER BY ID",
            ["Religion"] = "SELECT ID, RELIGIONNAME FROM BS_RELIGION ORDER BY ID",
        };

        // fill the form on Edit, including the ids of the cascade dropdowns (old GetById joins)
        private static readonly Dictionary<string, string> Records = new()
        {
            ["Country"] = "SELECT COUNTRYNAME, COUNTRYCODE FROM BS_COUNTRY WHERE ID = :Id",
            ["Division"] = "SELECT DIVISIONNAME, COUNTRYID FROM BS_DIVISION WHERE ID = :Id",
            ["District"] = @"SELECT D.DISTRICTNAME, D.DIVISIONID, DV.COUNTRYID
                               FROM BS_DISTRICT D INNER JOIN BS_DIVISION DV ON DV.ID = D.DIVISIONID WHERE D.ID = :Id",
            ["Thana"] = @"SELECT T.THANANAME, T.DISTRICTID, D.DIVISIONID, DV.COUNTRYID
                            FROM BS_THANA T INNER JOIN BS_DISTRICT D ON D.ID = T.DISTRICTID
                           INNER JOIN BS_DIVISION DV ON DV.ID = D.DIVISIONID WHERE T.ID = :Id",
            ["Location"] = @"SELECT L.NAME, L.LOCATIONCODE, L.TIMEZONE, L.COUNTRYID, L.THANAID, T.DISTRICTID, D.DIVISIONID
                               FROM BS_LOCATIONS L INNER JOIN BS_THANA T ON T.ID = L.THANAID
                              INNER JOIN BS_DISTRICT D ON D.ID = T.DISTRICTID WHERE L.ID = :Id",
            ["BusinessCenter"] = @"SELECT B.CENTERNAME, B.CENTERCODE, B.ADDRESS, B.LOCATIONID, L.COUNTRYID
                                     FROM BS_BUSINESSCENTER B INNER JOIN BS_LOCATIONS L ON L.ID = B.LOCATIONID WHERE B.ID = :Id",
            ["GroupofCompany"] = "SELECT COMPANYTITLE, ESTABLISHDATE FROM BS_GROUPOFCOMPANY WHERE ID = :Id",
            ["Company"] = "SELECT COMPANYNAME, GROUPOFCOMPANYID, COMESTABLISHDATE FROM BS_COMPANY WHERE ID = :Id",
            ["Department"] = "SELECT DEPARTMENTNAME, DEPARTMENTCODE FROM BS_DEPARTMENT WHERE ID = :Id",
            ["DepartmentUnit"] = "SELECT DEPTUNITNAME, DEPARTMENTID, DEPTUNITCODE FROM BS_DEPARTMENTUNIT WHERE ID = :Id",
            ["Designation"] = "SELECT DESIGNATIONNAME, PRIORITY FROM BS_DESIGNATION WHERE ID = :Id",
            ["Gender"] = "SELECT GENDERNAME FROM BS_GENDER WHERE ID = :Id",
            ["Religion"] = "SELECT RELIGIONNAME FROM BS_RELIGION WHERE ID = :Id",
        };

        // old CheckDuplicate: name column (+ parent column where YA checked inside the parent)
        private static readonly Dictionary<string, (string NameCol, string? ParentCol)> Dups = new()
        {
            ["Country"] = ("COUNTRYNAME", null),
            ["Division"] = ("DIVISIONNAME", "COUNTRYID"),
            ["District"] = ("DISTRICTNAME", "DIVISIONID"),
            ["Thana"] = ("THANANAME", "DISTRICTID"),
            ["Location"] = ("NAME", "THANAID"),
            ["BusinessCenter"] = ("CENTERNAME", "LOCATIONID"),
            ["GroupofCompany"] = ("COMPANYTITLE", null),
            ["Company"] = ("COMPANYNAME", "GROUPOFCOMPANYID"),
            ["Department"] = ("DEPARTMENTNAME", null),
            ["DepartmentUnit"] = ("DEPTUNITNAME", "DEPARTMENTID"),
            ["Designation"] = ("DESIGNATIONNAME", null),
            ["Gender"] = ("GENDERNAME", null),
            ["Religion"] = ("RELIGIONNAME", null),
        };

        private static TableDef Def(string key) =>
            Tables.TryGetValue(key, out var d) ? d : throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown setup page");

        // ============================================================================================
        // PERMISSION (same rule as the Dispatch pages + the super admin bypass of ModuleRepository)
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

        // ============================================================================================
        // GENERIC READS
        // ============================================================================================
        public List<SetupOption> Lookup(string lookupKey, int parentId)
        {
            if (!Lookups.TryGetValue(lookupKey, out string? sql)) throw new ArgumentOutOfRangeException(nameof(lookupKey));
            using var conn = _databaseService.GetConnection();
            var p = new DynamicParameters();
            if (sql.Contains(":P")) p.Add("P", parentId);
            return conn.Query(sql, p)
                       .Select(r => { var d = (IDictionary<string, object?>)r; return new SetupOption { Value = Convert.ToString(d["Value"]) ?? "", Text = Convert.ToString(d["Text"]) ?? "" }; })
                       .ToList();
        }

        public List<IDictionary<string, object?>> GetRows(string key, int filterId)
        {
            if (!Grids.TryGetValue(key, out string? sql)) throw new ArgumentOutOfRangeException(nameof(key));
            using var conn = _databaseService.GetConnection();
            var p = new DynamicParameters();
            if (sql.Contains(":F")) p.Add("F", filterId);
            return conn.Query(sql, p).Select(r => (IDictionary<string, object?>)r).ToList();
        }

        public IDictionary<string, object?>? GetRecord(string key, long id)
        {
            if (!Records.TryGetValue(key, out string? sql)) throw new ArgumentOutOfRangeException(nameof(key));
            using var conn = _databaseService.GetConnection();
            return conn.Query(sql, new { Id = id }).Select(r => (IDictionary<string, object?>)r).FirstOrDefault();
        }

        public bool Duplicate(string key, string name, int parentId, long excludeId)
        {
            if (!Dups.TryGetValue(key, out var d)) throw new ArgumentOutOfRangeException(nameof(key));
            var def = Def(key);
            string sql = $"SELECT COUNT(1) FROM {def.Table} WHERE UPPER({d.NameCol}) = UPPER(:Name) AND ID <> :Ex";
            if (d.ParentCol != null) sql += $" AND {d.ParentCol} = :Parent";
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<int>(sql, new { Name = name, Ex = excludeId, Parent = parentId }) > 0;
        }

        // ============================================================================================
        // GENERIC WRITES (columns are validated against the whitelist; values are bind parameters)
        // ============================================================================================
        public long Insert(string key, Dictionary<string, object?> values, string staffId)
        {
            var def = Def(key);
            var cols = def.Columns.Where(values.ContainsKey).ToList();
            if (cols.Count == 0) throw new InvalidOperationException("Nothing to save.");

            string idExpr = def.Sequence != null ? def.Sequence + ".NEXTVAL" : $"(SELECT NVL(MAX(ID),0)+1 FROM {def.Table})";
            string colList = "ID, " + string.Join(", ", cols);
            string valList = idExpr + ", " + string.Join(", ", cols.Select(c => ":" + c));
            if (def.Audit) { colList += ", CREATEDBY"; valList += ", :AUDITBY"; }

            var p = new DynamicParameters();
            foreach (var c in cols) p.Add(c, values[c]);
            if (def.Audit) p.Add("AUDITBY", staffId);

            using var conn = _databaseService.GetConnection();
            return conn.Execute($"INSERT INTO {def.Table} ({colList}) VALUES ({valList})", p);
        }

        public int Update(string key, long id, Dictionary<string, object?> values, string staffId)
        {
            var def = Def(key);
            var cols = def.Columns.Where(values.ContainsKey).ToList();
            if (cols.Count == 0) throw new InvalidOperationException("Nothing to save.");

            string set = string.Join(", ", cols.Select(c => $"{c} = :{c}"));
            if (def.Audit) set += ", UPDATEDBY = :AUDITBY, UPDATEDATE = SYSDATE";

            var p = new DynamicParameters();
            foreach (var c in cols) p.Add(c, values[c]);
            if (def.Audit) p.Add("AUDITBY", staffId);
            p.Add("Id", id);

            using var conn = _databaseService.GetConnection();
            return conn.Execute($"UPDATE {def.Table} SET {set} WHERE ID = :Id", p);
        }

        // old Common().Delete(table, id) - the setup pages really hard-delete; the database FKs protect the data
        public int Delete(string key, long id)
        {
            var def = Def(key);
            using var conn = _databaseService.GetConnection();
            return conn.Execute($"DELETE FROM {def.Table} WHERE ID = :Id", new { Id = id });
        }

        // ============================================================================================
        // DEPARTMENT ASSIGN (old DepartmentAssign.aspx: full grid of departments + checked = assigned)
        // ============================================================================================
        public List<DepartmentAssignRow> GetDepartmentsWithAssignment(int companyId, int centerId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT D.ID, D.DEPARTMENTNAME, D.DEPARTMENTCODE,
                       CASE WHEN A.DEPARTMENTID IS NULL THEN 0 ELSE 1 END AS ASSIGNED
                  FROM BS_DEPARTMENT D
                  LEFT JOIN BS_DEPARTMENTASSIGN A
                    ON A.DEPARTMENTID = D.ID AND A.COMPANYID = :CompanyId AND A.BUSINESSCENTERID = :CenterId
                 ORDER BY D.DEPARTMENTNAME";
            return conn.Query(sql, new { CompanyId = companyId, CenterId = centerId })
                       .Select(r =>
                       {
                           var d = (IDictionary<string, object?>)r;
                           return new DepartmentAssignRow
                           {
                               ID = Convert.ToInt32(d["ID"]),
                               DEPARTMENTNAME = Convert.ToString(d["DEPARTMENTNAME"]),
                               DEPARTMENTCODE = Convert.ToString(d["DEPARTMENTCODE"]),
                               Assigned = Convert.ToInt32(d["ASSIGNED"]) == 1
                           };
                       }).ToList();
        }

        // old btnSave_Click: delete the pair's rows, insert one row per checked department (same transaction here)
        public void SaveDepartmentAssign(int companyId, int centerId, List<int> departmentIds, string userId)
        {
            using var conn = _databaseService.GetConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();
            conn.Execute("DELETE FROM BS_DEPARTMENTASSIGN WHERE COMPANYID = :CompanyId AND BUSINESSCENTERID = :CenterId",
                         new { CompanyId = companyId, CenterId = centerId }, tx);
            const string ins = @"
                INSERT INTO BS_DEPARTMENTASSIGN (ID, DEPARTMENTID, BUSINESSCENTERID, COMPANYID, CREATEDBY)
                VALUES (SQ_BS_DEPARTMENTASSIGN.NEXTVAL, :DepartmentId, :CenterId, :CompanyId, :UserId)";
            foreach (int depId in departmentIds.Distinct())
                conn.Execute(ins, new { DepartmentId = depId, CenterId = centerId, CompanyId = companyId, UserId = userId }, tx);
            tx.Commit();
        }
    }
}
