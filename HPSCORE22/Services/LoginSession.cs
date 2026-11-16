// Services/LoginSession.cs      (NEW file - namespace AlphaCare.Services, same as ApiService)
// One place that fills the session after login. Used by AccountsController (login) and by Program.cs (restore when
// the session is gone but the "Keep me signed in" cookie is still valid).
using System.Data;
using System.Text.Json;
using AlphaCare.Interface;
using QCMS.Repositories;

namespace AlphaCare.Services
{
    public static class LoginSession
    {
        public const string RememberClaim = "REMEMBER";         // claim in the login cookie: "1" = Keep me signed in
        public const string LastModuleCookie = "lastmod";       // last opened module (so the sidebar comes back)

        /// <summary>The same session values as the old AccountsController.Login.</summary>
        public static void Fill(ISession s, DataTable login, DataTable emp)
        {
            int roleId = Convert.ToInt32(login.Rows[0]["ROLEID"]);
            s.SetString("USERID", login.Rows[0]["ID"].ToString() ?? "");
            s.SetString("USERNAME", login.Rows[0]["USERNAME"].ToString() ?? "");
            s.SetString("UserFullName", emp.Rows[0]["EMPLOYEENAME"].ToString() ?? "");
            s.SetString("STAFFID", emp.Rows[0]["STAFFID"].ToString() ?? "");
            s.SetInt32("RoleId", roleId);
            s.SetInt32("UserTypeID", roleId);
            s.SetInt32("BusinessCenterID", Convert.ToInt32(emp.Rows[0]["BUSINESSCENTERID"]));
            s.SetInt32("CompanyID", Convert.ToInt32(emp.Rows[0]["COMPANYID"]));
            s.SetInt32("DepartmentID", Convert.ToInt32(emp.Rows[0]["DEPARTMENTID"]));
            s.SetInt32("DesignationID", Convert.ToInt32(emp.Rows[0]["DESIGNATIONID"]));
        }

        /// <summary>
        /// The session is empty (30 min idle, browser closed, app restarted) but the login cookie is valid.
        /// Only for users who ticked "Keep me signed in". Re-reads the user from the database (so a disabled user is NOT restored)
        /// and re-opens the last module. Returns false -> the caller signs the user out.
        /// </summary>
        public static bool TryRestore(HttpContext ctx)
        {
            try
            {
                if (!ctx.User.HasClaim(RememberClaim, "1")) return false;

                var users = ctx.RequestServices.GetRequiredService<UserRepository>();
                string userName = ctx.User.Identity?.Name ?? "";
                string staffId = ctx.User.FindFirst("STAFFID")?.Value ?? "";

                DataTable login = users.GetLoginInfoByUserID(userName);      // only an ACTIVE user comes back
                DataTable emp = users.GetEmployeeInfo(staffId);
                if (login.Rows.Count == 0 || emp.Rows.Count == 0) return false;

                Fill(ctx.Session, login, emp);

                // sidebar: open the module the user used last
                if (int.TryParse(ctx.Request.Cookies[LastModuleCookie], out int moduleId) && moduleId > 0)
                {
                    var modules = ctx.RequestServices.GetRequiredService<IModuleRepository>();
                    var module = modules.GetModules(staffId).FirstOrDefault(m => m.ID == moduleId);
                    if (module != null)
                    {
                        var tree = modules.BuildMenuTree(modules.GetModuleTasks(staffId, moduleId));
                        ctx.Session.SetInt32("ModuleId", module.ID);
                        ctx.Session.SetString("ModuleName", module.MODULENAME ?? "");
                        ctx.Session.SetString("ModuleIcon", module.ICON ?? "fa-folder");
                        ctx.Session.SetString("ModuleMenu", JsonSerializer.Serialize(tree));
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Remember the opened module for a month (called when a module is opened).</summary>
        public static void RememberModule(HttpContext ctx, int moduleId)
        {
            ctx.Response.Cookies.Append(LastModuleCookie, moduleId.ToString(), new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddDays(30),
                Path = ctx.Request.PathBase.HasValue ? ctx.Request.PathBase.Value : "/"
            });
        }
    }
}
