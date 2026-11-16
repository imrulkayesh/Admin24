// Repositories/ModuleRepository.cs
using Admin360.Services;          // DatabaseService (change to your project's namespace if different)
using AlphaCare.Interface;
using AlphaCare.Models;
using Dapper;
using QCMS.Services;

namespace Admin360.Repositories
{
    public class ModuleRepository : IModuleRepository
    {
        private readonly DatabaseService _databaseService;

        public ModuleRepository(DatabaseService databaseService)
        {
            _databaseService = databaseService;
        }

        // old code: roleId == 1 => all.  Here: staff has ROLEID 1 in UC_USERROLE
        public bool IsSuperAdmin(string staffId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = "SELECT COUNT(1) FROM UC_USERROLE WHERE STAFFID = :StaffId AND ROLEID = 1";
            return conn.ExecuteScalar<int>(sql, new { StaffId = staffId }) > 0;
        }

        // = old dalTaskManager.GetModule
        public List<ModuleModel> GetModules(string staffId)
        {
            using var conn = _databaseService.GetConnection();

            if (IsSuperAdmin(staffId))
            {
                const string sqlAdmin = @"
                    SELECT M.ID, M.MODULENAME, M.ICON, M.URL
                      FROM UC_MODULE_COPY M WHERE M.ISACTIVE=1
                     ORDER BY M.MODULEORDER ASC";
                return conn.Query<ModuleModel>(sqlAdmin).ToList();
            }

            const string sql = @"
                SELECT DISTINCT M.ID, M.MODULENAME, M.ICON, M.URL, M.MODULEORDER
                  FROM UC_TASKPERMISSION_COPY TP
                 INNER JOIN UC_TASKMANAGER_COPY T ON T.ID = TP.TASKID
                 INNER JOIN UC_MODULE_COPY M ON M.ID = T.MODULEID AND M.ISACTIVE=1
                 WHERE TP.ROLEID IN (SELECT ROLEID FROM UC_USERROLE WHERE STAFFID = :StaffId)
                 ORDER BY M.MODULEORDER ASC";
            return conn.Query<ModuleModel>(sql, new { StaffId = staffId }).ToList();
        }

        // = old dalTaskManager.GetAllByRoleId
        public List<TaskMenuRow> GetModuleTasks(string staffId, int moduleId)
        {
            using var conn = _databaseService.GetConnection();

            if (IsSuperAdmin(staffId))
            {
                const string sqlAdmin = @"
                    SELECT T.ID, T.TEXTBAN, T.TEXTENG, T.URL, T.PARENTID, T.ORDERCOLUMN, T.ICON, T.MODULEID
                      FROM UC_TASKMANAGER_COPY T
                     WHERE T.MODULEID = :ModuleId AND T.ISACTIVE = 1
                     ORDER BY T.ORDERCOLUMN ASC";
                return conn.Query<TaskMenuRow>(sqlAdmin, new { ModuleId = moduleId }).ToList();
            }

            const string sql = @"
                SELECT DISTINCT T.ID, T.TEXTBAN, T.TEXTENG, T.URL, T.PARENTID, T.ORDERCOLUMN, T.ICON, T.MODULEID
                  FROM UC_TASKPERMISSION_COPY TP
                 INNER JOIN UC_TASKMANAGER_COPY T ON T.ID = TP.TASKID
                 WHERE T.MODULEID = :ModuleId
                   AND TP.ROLEID IN (SELECT ROLEID FROM UC_USERROLE WHERE STAFFID = :StaffId)
                 ORDER BY T.ORDERCOLUMN ASC";
            return conn.Query<TaskMenuRow>(sql, new { ModuleId = moduleId, StaffId = staffId }).ToList();
        }

        public int? GetModuleIdByUrl(string url)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = "SELECT MODULEID FROM UC_TASKMANAGER_COPY WHERE URL = :Url AND ROWNUM = 1";
            return conn.QueryFirstOrDefault<int?>(sql, new { Url = url });
        }

        public List<SideMenuItem> BuildMenuTree(List<TaskMenuRow> rows)
        {
            static string Title(TaskMenuRow r) =>
                !string.IsNullOrWhiteSpace(r.TEXTENG) ? r.TEXTENG! : (r.TEXTBAN ?? "");

            var result = new List<SideMenuItem>();

            foreach (var p in rows.Where(r => r.PARENTID == 0).OrderBy(r => r.ORDERCOLUMN))
            {
                var item = new SideMenuItem { Id = p.ID, Title = Title(p), Url = p.URL, Icon = p.ICON };

                item.Children = rows.Where(c => c.PARENTID == p.ID)
                                    .OrderBy(c => c.ORDERCOLUMN)
                                    .Select(c => new SideMenuItem { Id = c.ID, Title = Title(c), Url = c.URL, Icon = c.ICON })
                                    .ToList();

                // parent without children is shown only if it has its own link
                if (item.Children.Count > 0 || (!string.IsNullOrWhiteSpace(p.URL) && p.URL != "#"))
                    result.Add(item);
            }
            return result;
        }
    }
}
