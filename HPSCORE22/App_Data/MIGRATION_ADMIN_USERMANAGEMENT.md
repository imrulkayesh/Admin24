# Migration Report — YA Admin & UserManagement → Admin24

Date: 2026-11-11 · Source: `G:\Imran\GIT\ya\YA` (Web Forms) · Target: `G:\Imran\GIT\Admin24\HPSCORE22` (ASP.NET Core 8 MVC)
Rules followed: `CLAUDE_CODE_SECURITY_AUDIT.md` — no database was touched; all SQL scripts are for you to run.

## 1. Migration summary

**Admin module (14 pages migrated).** All simple setup screens share one generic page
(`Views/Admin/Setup.cshtml`) driven by a `SetupDef` per screen in `AdminController.Setup.cs` — the same
pattern as the generic dispatch report page. Country, Division, District, Thana, Location,
Business Center, Group of Company, Company, Department, Department Unit, Designation, Gender, Religion,
plus the dedicated **Department Assign** page (checkbox matrix). Cascading dropdowns, duplicate checks,
edit and hard-delete behave as in YA.

**UserManagement module (8 pages migrated).** User Setup, User Status Update (same page + Activate/
Deactivate), User Role (multi-role checkboxes), Role Setup, Module, Task Manager, Task Permission
(two-level checked tree), Change Password. Users/roles use the shared tables; the three menu screens
manage **Admin24's own menu** (`UC_*_COPY`), never YA's.

Every page is `[Authorize]` + server-side menu-permission checked by URL (`UC_TASKMANAGER_COPY` →
`UC_TASKPERMISSION_COPY`, super admin ROLEID=1 bypass) — identical to the Dispatch pages.

## 2. File mapping (YA → Admin24)

| YA source | Admin24 target |
|---|---|
| Pages/Admin/Country·Division·District·Thana·Location·BusinessCenter·GroupofCompany·Company·Department·DepartmentUnit·Designation·Gender·Religion .aspx(.cs) | `Controllers/Admin/AdminController.Setup.cs` (SetupDef each) + `Views/Admin/Setup.cshtml` |
| Pages/Admin/DepartmentAssign.aspx(.cs) | `AdminController.Setup.cs` (DepartmentAssign actions) + `Views/Admin/DepartmentAssign.cshtml` |
| Pages/UserManagement/UserSetup.aspx(.cs) + UserStatusUpdate.aspx(.cs) | `Controllers/Admin/UserManagementController.cs` (UserSetup/UserStatusUpdate/GetUser/SaveUser/SetUserStatus) + `Views/UserManagement/UserSetup.cshtml` |
| Pages/UserManagement/UserRole.aspx(.cs) | UserRole/GetUserRoles/SaveUserRoles + `Views/UserManagement/UserRole.cshtml` |
| Pages/UserManagement/RoleSetup.aspx(.cs) | RoleSetup/SaveRole/DeleteRole + `Views/UserManagement/RoleSetup.cshtml` |
| Pages/UserManagement/Module.aspx(.cs) | Module/SaveModule/DeleteModule + `Views/UserManagement/Module.cshtml` |
| Pages/UserManagement/TaskManager.aspx(.cs) | TaskManager/SaveTask/DeleteTask + `Views/UserManagement/TaskManager.cshtml` |
| Pages/UserManagement/TaskPermission.aspx(.cs) | TaskPermission/SaveTaskPermission + `Views/UserManagement/TaskPermission.cshtml` |
| Pages/UserManagement/ChangePassword.aspx(.cs) | ChangePassword/SaveChangePassword + `Views/UserManagement/ChangePassword.cshtml` |
| App_Code/dal/Settings/dalBusinessCenter·Company·Country·Department·DepartmentUnit·Designation·District·Division·Gender·GroupofCompany·Location·Religion·Thana.cs, dalCommon (GetRolePriority/Delete), Common.cs (GetAll/Delete/LoadDropdown) | `Repositories/Admin/AdminSetupRepository.cs` (whitelisted tables/lookups/grids/records/dups) |
| App_Code/dal/UserControl/dalUser·dalUserRole·dalRole·dalModule·dalTaskManager.cs, dal/Employee/dalEmployee.cs, dal/Dispatch/dalApprover.Insert, Encryption.cs (SHA1) | `Repositories/Admin/UserManagementRepository.cs` (incl. `Sha1Hex` = old `EncryptionWithSh`) |
| dalCommon.IsPermitted | `HasMenuPermission` in both repositories (on `UC_*_COPY`, + super-admin bypass) |

**Excluded (with reason).**
- `Pages/Admin/Dashboard.aspx` — Dispatch/Service charts; Admin24 already has module dashboards (`IModuleDashboardProvider`).
- `Pages/Admin/Default.aspx` (module chooser) and `SiteMap.aspx` (menu listing) — duplicated by Admin24's Home page and sidebar.
- `Pages/Admin/SendSMS.aspx` — dead page (all code commented out in YA).
- `Pages/Admin/Event.aspx` + `JsonResponse.ashx` — FullCalendar demo (EventDAO, student-era leftover), unrelated to the Admin module.
- `Pages/Admin/News.aspx`, `Year.aspx`, and the Group / Profession / Qualification / Section / Shift screens — their DAL calls `USP_*` stored procedures as plain text with the parameter code commented out in `DatabaseManagerORCL`, so these pages are **already non-functional in YA on Oracle**. Nothing working existed to migrate.
- `Pages/UserManagement/ChangeStatus.aspx`, `Default.aspx` — empty pages.

## 3. Database report

- Tables used (existing, unchanged): `BS_COUNTRY, BS_DIVISION, BS_DISTRICT, BS_THANA, BS_LOCATIONS, BS_BUSINESSCENTER, BS_GROUPOFCOMPANY, BS_COMPANY, BS_DEPARTMENT, BS_DEPARTMENTUNIT, BS_DEPARTMENTASSIGN, BS_DESIGNATION, BS_GENDER, BS_RELIGION, UC_USERS, UC_USERROLE, UC_ROLESETUP, EM_EMPLOYEE, DP_APPROVER` and Admin24's `UC_MODULE_COPY, UC_TASKMANAGER_COPY, UC_TASKPERMISSION_COPY`.
- Sequences used: the `SQ_BS_*` / `SQ_UC_ROLESETUP` / `SQ_EM_EMPLOYEE` / `SQ_DP_APPROVER` sequences from YA (note: the division sequence really is spelled `SQ_BS_DIVSION`). The `_COPY` tables get `MAX(ID)+1` because no sequence is known for them.
- **No schema change is required.** No stored procedures are used (the YA `USP_*` calls were dead code).
- To show the pages in the menu: run `App_Data/Sql/AdminUserManagement_Menu.sql` (template — fill in module/order/role ids).
- ⚠ Please verify once that `UC_MODULE_COPY` has the audit columns (`CREATEDBY`, `UPDATEDBY`, `UPDATEDATE`); the `_COPY` tables are not in the YA.txt dump. If a column is missing, remove it from the two `UC_MODULE_COPY` statements in `UserManagementRepository`.

## 4. Deliberate deviations from YA (all justified)

1. **Menu screens write `UC_*_COPY`** — managing Admin24's own menu instead of mutating YA's live menu tables (protects the legacy app; YA keeps its own TaskManager).
2. **Parameterized SQL everywhere** — the YA DAL concatenated user input into SQL strings.
3. **`EM_EMPLOYEE` update no longer does `ID=SQ_EM_EMPLOYEE.NEXTVAL`** — the old DAL replaced the primary key on every employee update.
4. **Edit password only when typed** — YA always overwrote `UC_USERS.PASSWORD` on edit, even with an empty box (storing SHA1 of ""). The hash format (SHA1-hex over ASCII) is unchanged, so YA logins keep working.
5. **User edit upserts the selected role in `UC_USERROLE`** — YA rewrote *every* role row of the user to the selected role (collapsing multi-role users into duplicates). Other roles are managed on the User Role page, as before.
6. **Location edit shows the saved time zone** — YA read a misspelled column (`TIMEZO`) and silently lost the value.
7. **Delete / assignment saves run in a transaction** (DepartmentAssign, UserRole, TaskPermission, user create) — YA ran the delete and the inserts as separate statements.
8. **Resetting another user's password requires the ChangePassword menu permission** — YA only checked login.
9. Department Assign auto-loads the saved checkboxes when Company + Center are chosen (YA needed the extra "Search" click). The save semantics (replace all rows of the pair) are unchanged.

## 5. Changed / created files

**Created:** `Models/Admin/AdminSetupModels.cs`, `Models/Admin/UserManagementModels.cs`,
`Interface/Admin/IAdminSetupRepository.cs`, `Interface/Admin/IUserManagementRepository.cs`,
`Repositories/Admin/AdminSetupRepository.cs`, `Repositories/Admin/UserManagementRepository.cs`,
`Controllers/Admin/AdminController.Setup.cs`, `Controllers/Admin/UserManagementController.cs`,
`Views/Admin/Setup.cshtml`, `Views/Admin/DepartmentAssign.cshtml`,
`Views/UserManagement/{UserSetup, UserRole, RoleSetup, Module, TaskManager, TaskPermission, ChangePassword}.cshtml`,
`App_Data/Sql/AdminUserManagement_Menu.sql`, this report.
**Modified:** `Controllers/Admin/AdminController.cs` ([Authorize] + guards; Index now redirects Home — its view never existed), `Program.cs` (two DI registrations).
**Reused:** `DatabaseService`, Dapper, `_Layout` + `app-standard.js` (SD.post/confirm/toast/initList, Select2/date behaviours), the Dispatch permission pattern, generic page patterns.

## 6. Testing report

- `dotnet build` — **0 errors** (only pre-existing warnings).
- Unauthenticated smoke test (app run locally, no DB touched): `Admin/Country`, `Admin/Location`,
  `Admin/DepartmentAssign`, `UserManagement/UserSetup`, `UserManagement/TaskPermission`,
  `UserManagement/ChangePassword` all return **302 → /Accounts/Login**; existing routes unaffected.
- Not tested (needs a logged-in user + menu rows): CRUD round-trips against the production Oracle DB.
  Per the security rules no data was written. Suggested first manual test: log in as a super admin,
  open `/Admin/Gender` (smallest page), add + edit + delete a test value.

## 6b. Follow-up round (2026-11-11): UI improvements + Task Permission save fix

1. **Task Permission save fix.** The save wrote `CREATEDBY` into `UC_TASKPERMISSION_COPY` (and the
   Module / Task Manager screens wrote `CREATEDBY` / `UPDATEDBY` / `UPDATEDATE` into the other two
   `_COPY` tables). Those tables are not in the YA schema dump, so if a table lacks a column the
   insert dies with ORA-00904 and the old generic "Save failed" hid the reason. All `UC_*_COPY`
   reads/writes in `UserManagementRepository` are now **schema-adaptive**: the available columns are
   read once from `USER_TAB_COLUMNS` (fallback `ALL_TAB_COLUMNS`) and audit columns are only used when
   they exist. The three menu-screen save actions now also return the first line of the real database
   error, and the Task Permission page reloads the tree from the database after a successful save, so
   what you see is what was stored. Save button locks with a spinner during the post.
2. **Searchable dropdowns.** Every `<select>` in the Admin + UserManagement views now goes through the
   standard Select2 transformation of `app-standard.js` (the `data-native` opt-outs were removed) —
   the same searchable dropdowns as the Meeting and Gatepass pages. The Establish Date fields on
   Company / Group of Company get the standard DD/MM/YYYY box + calendar (values posted to the server
   are unchanged: the hidden original input keeps the ISO value).
3. **Modern list polish.** `Module` got the standard search box, sortable columns (Module/URL/Order)
   and a "no match" state; `TaskManager` got sortable columns (Title/URL/Order); `DepartmentAssign`
   got a department search box ("Select all" only ticks the rows matching the search; ticked hidden
   rows stay ticked); `TaskPermission` got a quick filter over the menu tree. The other pages already
   had search/sort/paging. `app-standard.css` now guarantees the left sidebar scrolls independently of
   the page at every screen width (AdminLTE only guaranteed it on desktop).
4. Verified: build 0 errors; app boots; all module routes still answer (302 → login unauthenticated).

## 7. Outstanding issues / follow-ups

1. Run `App_Data/Sql/AdminUserManagement_Menu.sql` (after filling the ids) so the pages appear in menus for non-super-admins.
2. Verify the audit columns on `UC_MODULE_COPY` (see §3).
3. YA's `GetRolesForUserSetup` shows roles `ID > 2` to non-super-admins and *all* roles (incl. Super Admin) to super admins — preserved as-is; tell me if you want Super Admin excluded from assignment.
4. The legacy password hash is unsalted SHA1 (kept for YA compatibility). This is listed as a known audit finding; not remediated per your instruction that findings are not approved for remediation.
