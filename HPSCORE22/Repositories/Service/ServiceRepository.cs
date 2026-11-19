// Repositories/Service/ServiceRepository.cs
// Port of YA App_Code/dal/Service/dalServiceRequest.cs (+ the lookups of dalServiceCenter, dalServiceTeam, dalServiceCategory,
// dalServiceAccessories, dalMachine). Same tables, columns, SQ_SC_* sequences and STATUS numbers as YA. Differences to the old code:
//   * every value is a bind parameter (the old code concatenated user text into the SQL)
//   * several statements that belong together run in one transaction (assign, instant service, forward, rating)
//   * a request is only changed by somebody whose list shows it (checked by the caller with InList / IsOwnDraft)
//   * SERVICEID: location number padded to 3 digits for locations 10..99 (old code made 4 digits there - see MIGRATION_SERVICE.md)
// The SELECT lists, joins and WHERE rules of every list are the old ones (incl. INNER JOIN SC_MACHINE, roles 9/51/65).
// NOTE: bind variable names must not be Oracle reserved words (:Date, :Level, :Size, :Start, :Member ... -> ORA-01745)
using AlphaCare.Interface;
using AlphaCare.Models;
using Dapper;
using QCMS.Services;
using System.Data;

namespace Admin360.Repositories
{
    public partial class ServiceRepository : IServiceRepository
    {
        private readonly DatabaseService _databaseService;
        private readonly ILogger<ServiceRepository> _logger;

        // roles of the old code (SessionManager.SessionName.RoleId): team supervisors
        private static readonly int[] SupervisorRoles = { 9, 51, 65 };      // GetByAdminRequest / GetByAdminAssignedRequest / GetAllTeamMember
        private static readonly int[] TeamRoles = { 9, 51 };                // GetServiceCenter / dalServiceTeam.GetByServiceCenterId

        public ServiceRepository(DatabaseService databaseService, ILogger<ServiceRepository> logger)
        {
            _databaseService = databaseService;
            _logger = logger;
        }

        // ================================================================ PERMISSION

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

        // ================================================================ LOOKUPS

        public List<BusinessCenterModel> GetAllBusinessCenters()
        {
            using var conn = _databaseService.GetConnection();
            return conn.Query<BusinessCenterModel>("SELECT ID, CENTERCODE, CENTERNAME FROM BS_BUSINESSCENTER ORDER BY CENTERNAME").ToList();
        }

        public List<ServiceOption> GetServiceCentersByCenter(int businessCenterId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = "SELECT ID, SERVICECENTERNAME NAME FROM SC_SERVICECENTER WHERE BUSINESSCENTERID = :Bc ORDER BY SERVICECENTERNAME";
            return conn.Query<ServiceOption>(sql, new { Bc = businessCenterId }).ToList();
        }

        public List<ServiceOption> GetCategoriesByServiceCenter(int serviceCenterId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = "SELECT ID, CATEGORYNAME NAME FROM SC_CATEGORY WHERE SERVICECENTERID = :Sc ORDER BY CATEGORYNAME";
            return conn.Query<ServiceOption>(sql, new { Sc = serviceCenterId }).ToList();
        }

        public List<ServiceOption> GetMachinesFor(int businessCenterId, int companyId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT ID, MACHINECODE || '-' || MACHINENAME NAME
                  FROM SC_MACHINE
                 WHERE BUSINESSCENTERID = :Bc AND COMPANYID = :Comp
                 ORDER BY MACHINENAME";
            return conn.Query<ServiceOption>(sql, new { Bc = businessCenterId, Comp = companyId }).ToList();
        }

        public List<AccessoriesCategoryModel> GetAccessoryCategories()
        {
            using var conn = _databaseService.GetConnection();
            return conn.Query<AccessoriesCategoryModel>(
                "SELECT ID, ACCESSORIESCATEGORY FROM SC_ACCESSORIESCATEGORY ORDER BY ACCESSORIESCATEGORY").ToList();
        }

        // old: rows repeat when the user supervises more than one team of the center -> DISTINCT (same choices)
        public List<ServiceOption> GetAccessoriesForSupervisor(int categoryId, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT DISTINCT AC.ID, AC.ACCESSORIESNAME NAME
                  FROM SC_ACCESSORIES AC
                 INNER JOIN SC_SERVICETEAM ST ON ST.SERVICECENTERID = AC.SERVICECENTERID
                 WHERE AC.CATEGORYID = :Cat AND ST.SUPERVISOR = :StaffId
                 ORDER BY NAME";
            return conn.Query<ServiceOption>(sql, new { Cat = categoryId, StaffId = staffId }).ToList();
        }

        public List<string> SearchSubjects(string term)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT DISTINCT SUBJECT FROM SC_SERVICEDETAILS
                 WHERE LOWER(SUBJECT) LIKE '%' || :Term || '%' AND ROWNUM <= 20";
            return conn.Query<string>(sql, new { Term = term.Trim().ToLowerInvariant() }).ToList();
        }

        public List<ServiceOption> GetServiceCentersForAssign(string staffId, int roleId)
        {
            using var conn = _databaseService.GetConnection();
            string sql = TeamRoles.Contains(roleId)
                ? @"SELECT ID, SERVICECENTERNAME NAME FROM SC_SERVICECENTER
                     WHERE ID IN (SELECT SERVICECENTERID FROM SC_SERVICETEAM WHERE SUPERVISOR = :StaffId)
                     ORDER BY SERVICECENTERNAME"
                : "SELECT ID, SERVICECENTERNAME NAME FROM SC_SERVICECENTER WHERE CENTERADMIN = :StaffId ORDER BY SERVICECENTERNAME";
            return conn.Query<ServiceOption>(sql, new { StaffId = staffId }).ToList();
        }

        // old code compared SUPERVISOR with SessionName.UserId (UC_USERS.ID) for roles 9/51 - kept (see MIGRATION_SERVICE.md)
        public List<ServiceOption> GetTeamsForAssign(int serviceCenterId, int roleId, string userId)
        {
            using var conn = _databaseService.GetConnection();
            if (TeamRoles.Contains(roleId))
                return conn.Query<ServiceOption>(
                    "SELECT ID, TEAMNAME NAME FROM SC_SERVICETEAM WHERE SERVICECENTERID = :Sc AND SUPERVISOR = :UserId ORDER BY TEAMNAME",
                    new { Sc = serviceCenterId, UserId = userId }).ToList();
            return conn.Query<ServiceOption>(
                "SELECT ID, TEAMNAME NAME FROM SC_SERVICETEAM WHERE SERVICECENTERID = :Sc ORDER BY TEAMNAME",
                new { Sc = serviceCenterId }).ToList();
        }

        public List<ServiceStaffOption> GetMembersForAssign(int serviceCenterId, int teamId, int roleId)
        {
            using var conn = _databaseService.GetConnection();
            if (SupervisorRoles.Contains(roleId))
                return conn.Query<ServiceStaffOption>(@"
                    SELECT TM.STAFFID, E.EMPLOYEENAME
                      FROM SC_TEAMMEMBER TM
                     INNER JOIN EM_EMPLOYEE E ON E.STAFFID = TM.STAFFID
                     WHERE TM.SERVICECENTERID = :Sc AND TM.TEAMID = :Team
                     ORDER BY E.EMPLOYEENAME", new { Sc = serviceCenterId, Team = teamId }).ToList();

            // center admin: the request goes to the team supervisor (old code)
            return conn.Query<ServiceStaffOption>(@"
                SELECT ST.SUPERVISOR STAFFID, E.EMPLOYEENAME
                  FROM SC_SERVICETEAM ST
                 INNER JOIN EM_EMPLOYEE E ON E.STAFFID = ST.SUPERVISOR
                 WHERE ST.SERVICECENTERID = :Sc AND ST.ID = :Team", new { Sc = serviceCenterId, Team = teamId }).ToList();
        }

        public ServiceCenterAdminModel? GetServiceCenterAdmin(int serviceCenterId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT E.STAFFID, E.EMPLOYEENAME, E.OFFICEMOBILE, E.BUSINESSCENTERID, E.DEPARTMENTID,
                       B.CENTERCODE, B.CENTERNAME, B.ADDRESS, D.DEPARTMENTCODE
                  FROM SC_SERVICECENTER S
                 INNER JOIN EM_EMPLOYEE E ON E.STAFFID = S.CENTERADMIN
                 INNER JOIN BS_DEPARTMENT D ON D.ID = E.DEPARTMENTID
                 INNER JOIN BS_BUSINESSCENTER B ON B.ID = E.BUSINESSCENTERID
                 WHERE S.ID = :Sc";
            return conn.QueryFirstOrDefault<ServiceCenterAdminModel>(sql, new { Sc = serviceCenterId });
        }

        public ServiceMembershipModel? GetMembership(string staffId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT SERVICECENTERID, TEAMID FROM (
                    SELECT SERVICECENTERID, TEAMID FROM SC_TEAMMEMBER
                     WHERE STAFFID = :StaffId AND ISACTIVE = 1
                     ORDER BY ID)
                 WHERE ROWNUM = 1";
            return conn.QueryFirstOrDefault<ServiceMembershipModel>(sql, new { StaffId = staffId });
        }

        // ================================================================ LISTS

        // requester part of every list (old S.*, E.EMPLOYEENAME, B.CENTERCODE ...)
        private const string ReqCols = @"
            S.ID, S.SERVICEID, S.CREATEDATE, S.STATUS, S.ISAPPROVED, NVL(S.INSTANTSERVICE, 0) INSTANTSERVICE, NVL(S.NOOFREVISION, 0) NOOFREVISION,
            S.FROMSTAFFID, E.EMPLOYEENAME, E.OFFICEMOBILE, E.OFFICEMAIL, B.CENTERCODE, B.CENTERNAME, D.DEPARTMENTCODE, DE.DESIGNATIONNAME,
            SC.SERVICECENTERNAME";

        private const string FromReq = @"
              FROM SC_SERVICEREQUEST S
             INNER JOIN BS_BUSINESSCENTER B ON B.ID = S.FROMCENTERID
             INNER JOIN BS_DEPARTMENT D ON D.ID = S.FROMDEPARTMENTID
             INNER JOIN EM_EMPLOYEE E ON E.STAFFID = S.FROMSTAFFID
             INNER JOIN BS_DESIGNATION DE ON DE.ID = E.DESIGNATIONID";

        // old code: INNER JOIN SC_MACHINE (a request without machine, MACHINEID = 0, does not appear in these lists)
        private const string MachineJoin = " INNER JOIN SC_MACHINE MCH ON MCH.ID = S.MACHINEID ";
        // lists that had no machine join in YA (same rows; the column is only shown)
        private const string MachineLeft = " LEFT JOIN SC_MACHINE MCH ON MCH.ID = S.MACHINEID ";
        private const string MachineCol = ", MCH.MACHINECODE || ' - ' || MCH.MACHINENAME MACHINE_OUTLET";

        private const string ServicemanCols = ", SAE.EMPLOYEENAME SERVICEMAN, SAE.OFFICEMOBILE SERVICEMOBILE, SAE.OFFICEMAIL SERVICEEMAIL, ST.TEAMNAME";

        private static string AssignJoin(string serviceCenterColumn) => $@"
             INNER JOIN SC_SERVICECENTER SC ON SC.ID = S.{serviceCenterColumn}
             INNER JOIN SC_SERVICEASSIGN SA ON SA.REQUESTID = S.ID
             INNER JOIN EM_EMPLOYEE SAE ON SAE.STAFFID = SA.TEAMMEMBERID
             INNER JOIN SC_SERVICETEAM ST ON ST.ID = SA.TEAMID
             INNER JOIN BS_BUSINESSCENTER BSC ON BSC.ID = ST.BUSINESSCENTERID ";

        /// <summary>The SQL of one list (without ORDER BY) + its parameters.</summary>
        private static (string Sql, DynamicParameters Args) ListQuery(ServiceList list, string staffId, int roleId)
        {
            var p = new DynamicParameters();
            p.Add("S1", staffId);
            bool sup = SupervisorRoles.Contains(roleId);
            string sql;

            switch (list)
            {
                // ---------- old GetApprovalList(0 / 2): approvers of the requester's location + company + department (DP_APPROVER)
                case ServiceList.ApprovalPending:
                case ServiceList.ApprovalRejected:
                    p.Add("Appr", list == ServiceList.ApprovalPending ? 0 : 2);
                    sql = $@"
                        SELECT DISTINCT {ReqCols}, BSC.CENTERCODE SERVICECENTERLOCATION {MachineCol}
                        {FromReq}
                         INNER JOIN SC_SERVICECENTER SC ON SC.ID = S.SERVICECENTERID
                        {MachineJoin}
                         INNER JOIN BS_BUSINESSCENTER BSC ON BSC.ID = SC.BUSINESSCENTERID
                         INNER JOIN DP_APPROVER A ON A.BUSINESSCENTERID = E.BUSINESSCENTERID AND A.COMPANYID = E.COMPANYID
                         WHERE A.DEPARTMENTID = E.DEPARTMENTID AND S.STATUS = 0 AND S.ISOPEN = 0 AND S.ISAPPROVED = :Appr
                           AND A.APPROVERID = :S1";
                    break;

                // ---------- old GetByAdminRequest(0, role)
                case ServiceList.AdminUnassigned:
                    p.Add("S2", staffId);
                    sql = sup
                        ? $@"
                        SELECT {ReqCols}, BSC.CENTERNAME SERVICECENTERLOCATION, C.CATEGORYNAME {MachineCol}
                        {FromReq}
                         INNER JOIN SC_SERVICECENTER SC ON SC.ID = S.SERVICECENTERID
                         INNER JOIN BS_BUSINESSCENTER BSC ON BSC.ID = SC.BUSINESSCENTERID
                         INNER JOIN SC_CATEGORY C ON C.ID = S.CATEGORYID
                         INNER JOIN SC_SERVICETEAM ST ON ST.SERVICECENTERID = S.SERVICECENTERID
                         INNER JOIN SC_SERVICEASSIGN SA ON SA.TEAMMEMBERID = ST.SUPERVISOR AND SA.REQUESTID = S.ID
                        {MachineJoin}
                         WHERE S.STATUS = 1 AND S.ISOPEN = 0 AND S.ISAPPROVED = 1 AND S.FRDSERVICECENTERID = 0 AND SA.ISACTIVE = 1
                           AND ST.SUPERVISOR = :S1
                        UNION
                        SELECT {ReqCols}, BSC.CENTERNAME SERVICECENTERLOCATION, C.CATEGORYNAME {MachineCol}
                        {FromReq}
                         INNER JOIN SC_SERVICECENTER SC ON SC.ID = S.FRDSERVICECENTERID
                         INNER JOIN BS_BUSINESSCENTER BSC ON BSC.ID = SC.BUSINESSCENTERID
                         INNER JOIN SC_CATEGORY C ON C.ID = S.CATEGORYID
                         INNER JOIN SC_SERVICETEAM ST ON ST.SERVICECENTERID = S.SERVICECENTERID
                        {MachineJoin}
                         WHERE S.STATUS = 0 AND S.ISOPEN = 0 AND S.ISAPPROVED = 1 AND S.FRDSERVICECENTERID > 0
                           AND SC.CENTERADMIN = :S2"
                        : $@"
                        SELECT {ReqCols}, BSC.CENTERNAME SERVICECENTERLOCATION, C.CATEGORYNAME {MachineCol}
                        {FromReq}
                         INNER JOIN SC_SERVICECENTER SC ON SC.ID = S.SERVICECENTERID
                         INNER JOIN BS_BUSINESSCENTER BSC ON BSC.ID = SC.BUSINESSCENTERID
                         INNER JOIN SC_CATEGORY C ON C.ID = S.CATEGORYID
                        {MachineJoin}
                         WHERE S.STATUS = 0 AND S.ISOPEN = 0 AND S.ISAPPROVED = 1 AND S.FRDSERVICECENTERID = 0 AND SC.CENTERADMIN = :S1
                        UNION
                        SELECT {ReqCols}, BSC.CENTERNAME SERVICECENTERLOCATION, C.CATEGORYNAME {MachineCol}
                        {FromReq}
                         INNER JOIN SC_SERVICECENTER SC ON SC.ID = S.FRDSERVICECENTERID
                         INNER JOIN BS_BUSINESSCENTER BSC ON BSC.ID = SC.BUSINESSCENTERID
                         INNER JOIN SC_CATEGORY C ON C.ID = S.CATEGORYID
                        {MachineJoin}
                         WHERE S.STATUS = 0 AND S.ISOPEN = 0 AND S.ISAPPROVED = 1 AND S.FRDSERVICECENTERID > 0 AND SC.CENTERADMIN = :S2";
                    break;

                // ---------- old GetByAdminAssignedRequest(1, role)
                // (supervisor branch: SA.ISACTIVE = 0 exactly as in YA; admin branch had no machine join -> LEFT JOIN = same rows)
                case ServiceList.AdminAssigned:
                    p.Add("S2", staffId);
                    string active = sup ? "0" : "1";
                    string who = sup ? "ST.SUPERVISOR" : "SC.CENTERADMIN";
                    string mach = sup ? MachineJoin : MachineLeft;
                    sql = $@"
                        SELECT {ReqCols}, BSC.CENTERCODE SERVICECENTERLOCATION {ServicemanCols} {MachineCol}
                        {FromReq} {AssignJoin("SERVICECENTERID")} {mach}
                         WHERE S.STATUS IN (1, 2, 3, 4) AND SA.ISACTIVE = {active} AND S.FRDSERVICECENTERID = 0
                           AND S.ISAPPROVED = 1 AND S.ISOPEN = 0 AND {who} = :S1
                        UNION
                        SELECT {ReqCols}, BSC.CENTERCODE SERVICECENTERLOCATION {ServicemanCols} {MachineCol}
                        {FromReq} {AssignJoin("FRDSERVICECENTERID")} {mach}
                         WHERE S.STATUS IN (1, 2, 3, 4) AND SA.ISACTIVE = {active} AND S.FRDSERVICECENTERID > 0
                           AND S.ISAPPROVED = 1 AND S.ISOPEN = 0 AND {who} = :S2";
                    break;

                // ---------- old GetInstantServicestoApprove
                case ServiceList.InstantToApprove:
                    sql = $@"
                        SELECT {ReqCols}, BSC.CENTERCODE SERVICECENTERLOCATION {ServicemanCols} {MachineCol}
                        {FromReq} {AssignJoin("SERVICECENTERID")} {MachineLeft}
                         WHERE S.STATUS = 100 AND SA.ISACTIVE = 1 AND S.FRDSERVICECENTERID = 0 AND S.ISAPPROVED = 0 AND S.ISOPEN = 0
                           AND SC.CENTERADMIN = :S1";
                    break;

                // ---------- old MyAllServicesRequest / MyServicesRequest(2)
                case ServiceList.MyRequests:
                case ServiceList.MyRejected:
                    string mine = list == ServiceList.MyRequests ? "S.STATUS < 5" : "S.STATUS = 0 AND S.ISAPPROVED = 2";
                    sql = $@"
                        SELECT {ReqCols}, SB.CENTERCODE SERVICECENTERLOCATION {MachineCol}
                        {FromReq}
                         INNER JOIN SC_SERVICECENTER SC ON SC.ID = S.SERVICECENTERID
                         INNER JOIN BS_BUSINESSCENTER SB ON SB.ID = SC.BUSINESSCENTERID
                        {MachineJoin}
                         WHERE {mine} AND S.ISOPEN = 0 AND S.FROMSTAFFID = :S1";
                    break;

                // ---------- old MyServicesReq(2): my requests the service person finished
                case ServiceList.MyWaitingFeedback:
                    sql = $@"
                        SELECT {ReqCols}, BSC.CENTERCODE SERVICECENTERLOCATION {ServicemanCols} {MachineCol}
                        {FromReq} {AssignJoin("SERVICECENTERID")} {MachineJoin}
                         WHERE S.STATUS = 2 AND SA.ISACTIVE = 1 AND S.ISAPPROVED = 1 AND S.ISOPEN = 0 AND S.FROMSTAFFID = :S1";
                    break;

                // ---------- old MyServices(1 / 4 / 3): requests assigned to me
                case ServiceList.MyTasks:
                case ServiceList.MyRevise:
                case ServiceList.MyFeedbackToGive:
                    p.Add("St", list == ServiceList.MyTasks ? 1 : list == ServiceList.MyRevise ? 4 : 3);
                    sql = $@"
                        SELECT {ReqCols}, BSC.CENTERCODE SERVICECENTERLOCATION {ServicemanCols} {MachineCol}
                        {FromReq} {AssignJoin("SERVICECENTERID")} {MachineJoin}
                         WHERE S.STATUS = :St AND SA.ISACTIVE = 1 AND S.ISAPPROVED = 1 AND S.ISOPEN = 0 AND SA.TEAMMEMBERID = :S1";
                    break;

                // ---------- old SuperVisorServices (Add Accessories)
                case ServiceList.SupervisorServices:
                    sql = $@"
                        SELECT {ReqCols}, BSC.CENTERCODE SERVICECENTERLOCATION {ServicemanCols} {MachineCol}
                        {FromReq} {AssignJoin("SERVICECENTERID")} {MachineLeft}
                         WHERE S.STATUS IN (1, 4) AND SA.ISACTIVE = 1 AND S.ISAPPROVED = 1 AND S.ISOPEN = 0 AND ST.SUPERVISOR = :S1";
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(list));
            }
            return (sql, p);
        }

        public List<ServiceListItemModel> GetList(ServiceList list, string staffId, int roleId)
        {
            var (sql, args) = ListQuery(list, staffId, roleId);
            using var conn = _databaseService.GetConnection();
            return conn.Query<ServiceListItemModel>("SELECT * FROM (" + sql + ") Q ORDER BY Q.CREATEDATE DESC, Q.ID DESC", args).ToList();
        }

        public bool InList(ServiceList list, long requestId, string staffId, int roleId)
        {
            if (requestId <= 0 || string.IsNullOrWhiteSpace(staffId)) return false;
            var (sql, args) = ListQuery(list, staffId, roleId);
            args.Add("Rid", requestId);
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<int>("SELECT COUNT(1) FROM (" + sql + ") Q WHERE Q.ID = :Rid", args) > 0;
        }

        // ================================================================ DRAFT

        // old CheckRequest: one open request (ISOPEN = 1) per user collects the items until Save
        public long GetOrCreateDraft(string staffId)
        {
            using var conn = _databaseService.GetConnection();
            const string find = "SELECT MIN(ID) FROM SC_SERVICEREQUEST WHERE ISOPEN = 1 AND CREATEDBY = :StaffId";

            long? id = conn.ExecuteScalar<long?>(find, new { StaffId = staffId });
            if (id.HasValue) return id.Value;

            conn.Execute("INSERT INTO SC_SERVICEREQUEST (ID, ISOPEN, CREATEDBY) VALUES (SQ_SC_SERVICEREQUEST.NEXTVAL, 1, :StaffId)",
                         new { StaffId = staffId });
            return conn.ExecuteScalar<long>(find, new { StaffId = staffId });
        }

        public bool IsOwnDraft(long requestId, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return IsOwnOpenDraft(conn, null, requestId, staffId);
        }

        private static bool IsOwnOpenDraft(IDbConnection conn, IDbTransaction? tran, long requestId, string staffId) =>
            conn.ExecuteScalar<int>("SELECT COUNT(1) FROM SC_SERVICEREQUEST WHERE ID = :Id AND ISOPEN = 1 AND CREATEDBY = :StaffId",
                                    new { Id = requestId, StaffId = staffId }, tran) > 0;

        public List<ServiceItemModel> GetItems(long requestId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT ID, REQUESTID, SUBJECT, QUANTITY, REMARKS, ATTACHMENT
                  FROM SC_SERVICEDETAILS
                 WHERE REQUESTID = :Rid
                 ORDER BY ID";
            return conn.Query<ServiceItemModel>(sql, new { Rid = requestId }).ToList();
        }

        public ServiceItemModel? GetItem(long itemId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.QueryFirstOrDefault<ServiceItemModel>(
                "SELECT ID, REQUESTID, SUBJECT, QUANTITY, REMARKS, ATTACHMENT FROM SC_SERVICEDETAILS WHERE ID = :Id", new { Id = itemId });
        }

        public bool DraftHasItem(long requestId, string subject)
        {
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<int>("SELECT COUNT(1) FROM SC_SERVICEDETAILS WHERE REQUESTID = :Rid AND SUBJECT = :Subj",
                                           new { Rid = requestId, Subj = subject }) > 0;
        }

        public int AddDraftItem(long requestId, string staffId, string subject, int quantity, string remarks, string attachment)
        {
            using var conn = _databaseService.GetConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();

            if (!IsOwnOpenDraft(conn, tran, requestId, staffId)) { tran.Rollback(); return 0; }

            long? existing = conn.ExecuteScalar<long?>(
                "SELECT MIN(ID) FROM SC_SERVICEDETAILS WHERE REQUESTID = :Rid AND SUBJECT = :Subj",
                new { Rid = requestId, Subj = subject }, tran);

            if (existing.HasValue)
            {
                conn.Execute("UPDATE SC_SERVICEDETAILS SET QUANTITY = NVL(QUANTITY, 0) + :Qty WHERE ID = :Id",
                             new { Qty = quantity, Id = existing.Value }, tran);
                tran.Commit();
                return 2;
            }

            conn.Execute(@"
                INSERT INTO SC_SERVICEDETAILS (ID, QUANTITY, SUBJECT, REQUESTID, ATTACHMENT, REMARKS)
                VALUES (SQ_SC_SERVICEDETAILS.NEXTVAL, :Qty, :Subj, :Rid, :Att, :Rem)",
                new { Qty = quantity, Subj = subject, Rid = requestId, Att = attachment, Rem = remarks }, tran);
            tran.Commit();
            return 1;
        }

        public int UpdateDraftItem(long itemId, string staffId, int quantity, string remarks)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                UPDATE SC_SERVICEDETAILS I
                   SET I.QUANTITY = :Qty, I.REMARKS = :Rem
                 WHERE I.ID = :Id
                   AND EXISTS (SELECT 1 FROM SC_SERVICEREQUEST S WHERE S.ID = I.REQUESTID AND S.ISOPEN = 1 AND S.CREATEDBY = :StaffId)";
            return conn.Execute(sql, new { Qty = quantity, Rem = remarks, Id = itemId, StaffId = staffId });
        }

        // SERVICEID = 'SC' + location (3 digits) + yy + MM + 6 digit number of that location / month (old TransIdQuery)
        private const string NextServiceIdSql = @"
            (SELECT 'SC' || :Office || TO_CHAR(SYSDATE, 'YYMM')
                    || TRIM(TO_CHAR(NVL(MAX(TO_NUMBER(SUBSTR(X.SERVICEID, 10, 6))), 0) + 1, '000000'))
               FROM SC_SERVICEREQUEST X
              WHERE LENGTH(X.SERVICEID) = 15
                AND SUBSTR(X.SERVICEID, 1, 2) = 'SC'
                AND SUBSTR(X.SERVICEID, 3, 3) = :Office2
                AND SUBSTR(X.SERVICEID, 6, 4) = TO_CHAR(SYSDATE, 'YYMM'))";

        // old code: < 100 -> "00" + id (so 10..99 became 4 digits); here 10..99 -> "0" + id like the Dispatch TRANSACTIONID
        private static string Office(int businessCenterId) =>
            businessCenterId >= 100 ? businessCenterId.ToString()
            : businessCenterId >= 10 ? "0" + businessCenterId
            : "00" + businessCenterId;

        public string? SubmitRequest(long requestId, string staffId, int serviceCenterId, int categoryId, int machineId, int fromCenterId, int fromDepartmentId)
        {
            using var conn = _databaseService.GetConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();

            // old dalServiceRequest.Update (second version: FRDSERVICECENTERID = the chosen service center as well)
            string sql = @"
                UPDATE SC_SERVICEREQUEST
                   SET SERVICECENTERID = :Sc, FRDSERVICECENTERID = :Sc2, CATEGORYID = :Cat, FROMSTAFFID = :FromStaff,
                       FROMCENTERID = :FromCenter, FROMDEPARTMENTID = :FromDept, STATUS = 0, ISOPEN = 0, CREATEDATE = SYSDATE,
                       MACHINEID = :Mach, SERVICEID = " + NextServiceIdSql + @"
                 WHERE ID = :Id AND ISOPEN = 1 AND CREATEDBY = :StaffId";

            string office = Office(fromCenterId);
            int changed = conn.Execute(sql, new
            {
                Sc = serviceCenterId,
                Sc2 = serviceCenterId,
                Cat = categoryId,
                FromStaff = staffId,
                FromCenter = fromCenterId,
                FromDept = fromDepartmentId,
                Mach = machineId,
                Office = office,
                Office2 = office,
                Id = requestId,
                StaffId = staffId
            }, tran);

            if (changed != 1) { tran.Rollback(); return null; }
            string? serviceId = conn.ExecuteScalar<string>("SELECT SERVICEID FROM SC_SERVICEREQUEST WHERE ID = :Id", new { Id = requestId }, tran);
            tran.Commit();
            return serviceId;
        }

        public string? SubmitInstant(long requestId, string staffId, string fromStaffId, int serviceCenterId, int categoryId, int machineId,
                                     int fromCenterId, int fromDepartmentId, int teamId, DateTime startDate, string startHour, DateTime endDate, string endHour)
        {
            using var conn = _databaseService.GetConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();

            // old UpdateInstantService: STATUS = 100, INSTANTSERVICE = 1. FROMCENTERID / FROMDEPARTMENTID = the logged in user's (old code)
            string sql = @"
                UPDATE SC_SERVICEREQUEST
                   SET SERVICECENTERID = :Sc, CATEGORYID = :Cat, FROMSTAFFID = :FromStaff, FROMCENTERID = :FromCenter,
                       FROMDEPARTMENTID = :FromDept, STATUS = 100, MACHINEID = :Mach, INSTANTSERVICE = 1, ISOPEN = 0,
                       CREATEDATE = SYSDATE, SERVICEID = " + NextServiceIdSql + @"
                 WHERE ID = :Id AND ISOPEN = 1 AND CREATEDBY = :StaffId";

            string office = Office(fromCenterId);
            int changed = conn.Execute(sql, new
            {
                Sc = serviceCenterId,
                Cat = categoryId,
                FromStaff = fromStaffId,
                FromCenter = fromCenterId,
                FromDept = fromDepartmentId,
                Mach = machineId,
                Office = office,
                Office2 = office,
                Id = requestId,
                StaffId = staffId
            }, tran);
            if (changed != 1) { tran.Rollback(); return null; }

            // old AssignInstantService: assigned to the user himself (his own team)
            InsertAssign(conn, tran, requestId, teamId, staffId, startDate, startHour, endDate, endHour);

            string? serviceId = conn.ExecuteScalar<string>("SELECT SERVICEID FROM SC_SERVICEREQUEST WHERE ID = :Id", new { Id = requestId }, tran);
            tran.Commit();
            return serviceId;
        }

        private static void InsertAssign(IDbConnection conn, IDbTransaction tran, long requestId, int teamId, string memberStaffId,
                                         DateTime startDate, string startHour, DateTime endDate, string endHour)
        {
            conn.Execute("UPDATE SC_SERVICEASSIGN SET ISACTIVE = 0 WHERE REQUESTID = :Rid", new { Rid = requestId }, tran);
            conn.Execute(@"
                INSERT INTO SC_SERVICEASSIGN (ID, TEAMID, TEAMMEMBERID, REQUESTID, ASSIGNDATE, STARTDATE, STARTTIME, ENDDATE, ENDTIME)
                VALUES (SQ_SC_SERVICEASSIGN.NEXTVAL, :Team, :Mem, :Rid, SYSDATE, :Sd, :Sh, :Ed, :Eh)",
                new { Team = teamId, Mem = memberStaffId, Rid = requestId, Sd = startDate.Date, Sh = startHour, Ed = endDate.Date, Eh = endHour }, tran);
        }

        // ================================================================ DETAILS

        public ServiceRequestDetailModel? GetRequestDetails(long requestId)
        {
            using var conn = _databaseService.GetConnection();
            // old GetRequestDetails (same INNER JOINs) + machine, forwarded center and dispatch id for display
            const string sql = @"
                SELECT SR.ID, SR.SERVICEID, SR.CREATEDATE, SR.STATUS, SR.ISAPPROVED, SR.APPROVEDATE, NVL(SR.NOOFREVISION, 0) NOOFREVISION,
                       NVL(SR.INSTANTSERVICE, 0) INSTANTSERVICE, SR.FROMSTAFFID, SR.CREATEDBY, SR.SERVICECENTERID, SR.FRDSERVICECENTERID,
                       SR.FRDATE, SR.DISPATCHID, SR.RELEASEDATE, SR.BTIMEFROM, SR.BTIMETO,
                       E.EMPLOYEENAME, E.OFFICEMAIL, E.OFFICEMOBILE, B.CENTERCODE, D.DEPARTMENTCODE, D.DEPARTMENTNAME, DE.DESIGNATIONNAME,
                       EA.EMPLOYEENAME CENTERADMIN, EA.OFFICEMOBILE ADMINMOBILE, BA.CENTERCODE SERVICECENTERLOCATIONCODE,
                       BA.CENTERNAME SERVICECENTERLOCATION, C.CATEGORYNAME, SC.SERVICECENTERNAME,
                       MCH.MACHINECODE || ' - ' || MCH.MACHINENAME MACHINE_OUTLET,
                       FSC.SERVICECENTERNAME FRDSERVICECENTERNAME, DP.TRANSACTIONID
                  FROM SC_SERVICEREQUEST SR
                 INNER JOIN EM_EMPLOYEE E ON E.STAFFID = SR.FROMSTAFFID
                 INNER JOIN BS_BUSINESSCENTER B ON B.ID = SR.FROMCENTERID
                 INNER JOIN BS_DEPARTMENT D ON D.ID = SR.FROMDEPARTMENTID
                 INNER JOIN SC_SERVICECENTER SC ON SC.ID = SR.SERVICECENTERID
                 INNER JOIN BS_DESIGNATION DE ON DE.ID = E.DESIGNATIONID
                 INNER JOIN EM_EMPLOYEE EA ON EA.STAFFID = SC.CENTERADMIN
                 INNER JOIN BS_BUSINESSCENTER BA ON BA.ID = SC.BUSINESSCENTERID
                 INNER JOIN SC_CATEGORY C ON C.ID = SR.CATEGORYID
                  LEFT JOIN SC_MACHINE MCH ON MCH.ID = SR.MACHINEID
                  LEFT JOIN SC_SERVICECENTER FSC ON FSC.ID = SR.FRDSERVICECENTERID AND SR.FRDSERVICECENTERID <> SR.SERVICECENTERID
                  LEFT JOIN DP_DISPATCH DP ON DP.ID = SR.DISPATCHID AND SR.DISPATCHID > 0
                 WHERE SR.ID = :Id AND SR.ISOPEN = 0";
            return conn.QueryFirstOrDefault<ServiceRequestDetailModel>(sql, new { Id = requestId });
        }

        public ServiceApproverModel? GetApproverDetails(long requestId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT E.EMPLOYEENAME, E.OFFICEMOBILE, B.CENTERCODE, D.DEPARTMENTCODE, DE.DESIGNATIONNAME, SR.APPROVEDATE
                  FROM SC_SERVICEREQUEST SR
                 INNER JOIN EM_EMPLOYEE E ON E.STAFFID = SR.APPROVERID
                 INNER JOIN BS_BUSINESSCENTER B ON B.ID = E.BUSINESSCENTERID
                 INNER JOIN BS_DEPARTMENT D ON D.ID = E.DEPARTMENTID
                 INNER JOIN BS_DESIGNATION DE ON DE.ID = E.DESIGNATIONID
                 WHERE SR.ID = :Id";
            return conn.QueryFirstOrDefault<ServiceApproverModel>(sql, new { Id = requestId });
        }

        public ServiceAssignModel? GetActiveAssign(long requestId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT * FROM (
                    SELECT SA.ID, SA.TEAMID, SA.TEAMMEMBERID, SA.ASSIGNDATE, SA.STARTDATE, SA.STARTTIME, SA.ENDDATE, SA.ENDTIME,
                           NVL(SA.RATINGFROMUSER, 0) RATINGFROMUSER, NVL(SA.RATINGFROMSERVICEMAN, 0) RATINGFROMSERVICEMAN,
                           E.EMPLOYEENAME, E.OFFICEMOBILE, ST.TEAMNAME
                      FROM SC_SERVICEASSIGN SA
                      LEFT JOIN EM_EMPLOYEE E ON E.STAFFID = SA.TEAMMEMBERID
                      LEFT JOIN SC_SERVICETEAM ST ON ST.ID = SA.TEAMID
                     WHERE SA.REQUESTID = :Rid AND SA.ISACTIVE = 1
                     ORDER BY SA.ID DESC)
                 WHERE ROWNUM = 1";
            return conn.QueryFirstOrDefault<ServiceAssignModel>(sql, new { Rid = requestId });
        }

        private const string RequiredAccessorySelect = @"
            SELECT AC.ID, AC.REQUESTID, AC.ACCESSORIESID, SA.CATEGORYID, AC.QUANTITY, AC.PRICE, AC.CURRENCYID, NVL(AC.ISNEW, 0) ISNEW,
                   SA.ACCESSORIESNAME, C.CURRENCYCODE, CASE WHEN AC.ISNEW = 1 THEN 'YES' ELSE 'NO' END NEWITEM
              FROM SC_REQUIREDACCESSORIES AC
             INNER JOIN SC_ACCESSORIES SA ON SA.ID = AC.ACCESSORIESID
              LEFT JOIN ASSET_CURRENCY C ON C.ID = AC.CURRENCYID";

        public List<RequiredAccessoryModel> GetRequiredAccessories(long requestId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Query<RequiredAccessoryModel>(RequiredAccessorySelect + " WHERE AC.REQUESTID = :Rid ORDER BY AC.ID",
                                                      new { Rid = requestId }).ToList();
        }

        public RequiredAccessoryModel? GetRequiredAccessory(long id)
        {
            using var conn = _databaseService.GetConnection();
            return conn.QueryFirstOrDefault<RequiredAccessoryModel>(RequiredAccessorySelect + " WHERE AC.ID = :Id", new { Id = id });
        }

        public List<ServiceRemarkModel> GetRemarks(long requestId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT R.ID, R.STAFFID, R.REMARKS, R.CREATEDATE, E.EMPLOYEENAME, E.OFFICEMOBILE, B.CENTERCODE, D.DEPARTMENTCODE
                  FROM SC_REQUESTREMARKS R
                 INNER JOIN EM_EMPLOYEE E ON E.STAFFID = R.STAFFID
                 INNER JOIN BS_BUSINESSCENTER B ON B.ID = E.BUSINESSCENTERID
                 INNER JOIN BS_DEPARTMENT D ON D.ID = E.DEPARTMENTID
                 WHERE R.REQUESTID = :Rid
                 ORDER BY R.CREATEDATE, R.ID";
            return conn.Query<ServiceRemarkModel>(sql, new { Rid = requestId }).ToList();
        }

        // the old detail pages only needed a login + the request id in the session; here the user must be part of the request
        public bool CanView(long requestId, string staffId)
        {
            if (requestId <= 0 || string.IsNullOrWhiteSpace(staffId)) return false;
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT COUNT(1) FROM SC_SERVICEREQUEST S
                 WHERE S.ID = :Id AND S.ISOPEN = 0
                   AND (S.FROMSTAFFID = :S1 OR S.CREATEDBY = :S2 OR S.APPROVERID = :S3
                        OR EXISTS (SELECT 1 FROM SC_SERVICECENTER C
                                    WHERE C.ID IN (S.SERVICECENTERID, S.FRDSERVICECENTERID) AND C.CENTERADMIN = :S4)
                        OR EXISTS (SELECT 1 FROM SC_SERVICETEAM T
                                    WHERE T.SERVICECENTERID IN (S.SERVICECENTERID, S.FRDSERVICECENTERID) AND T.SUPERVISOR = :S5)
                        OR EXISTS (SELECT 1 FROM SC_SERVICEASSIGN A WHERE A.REQUESTID = S.ID AND A.TEAMMEMBERID = :S6)
                        OR EXISTS (SELECT 1 FROM EM_EMPLOYEE E
                                    INNER JOIN DP_APPROVER P ON P.BUSINESSCENTERID = E.BUSINESSCENTERID AND P.COMPANYID = E.COMPANYID
                                                            AND P.DEPARTMENTID = E.DEPARTMENTID
                                    WHERE E.STAFFID = S.FROMSTAFFID AND P.APPROVERID = :S7))";
            return conn.ExecuteScalar<int>(sql, new { Id = requestId, S1 = staffId, S2 = staffId, S3 = staffId, S4 = staffId, S5 = staffId, S6 = staffId, S7 = staffId }) > 0;
        }

        // ================================================================ ACTIONS

        public int ApproveRequest(long requestId, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute("UPDATE SC_SERVICEREQUEST SET ISAPPROVED = 1, APPROVERID = :StaffId, APPROVEDATE = SYSDATE WHERE ID = :Id",
                                new { StaffId = staffId, Id = requestId });
        }

        public int RejectRequest(long requestId, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute("UPDATE SC_SERVICEREQUEST SET ISAPPROVED = 2, APPROVERID = :StaffId, APPROVEDATE = SYSDATE WHERE ID = :Id",
                                new { StaffId = staffId, Id = requestId });
        }

        public int ApproveInstant(long requestId, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute(@"UPDATE SC_SERVICEREQUEST SET STATUS = 2, APPROVERID = :StaffId, ISAPPROVED = 1, APPROVEDATE = SYSDATE
                                   WHERE ID = :Id AND STATUS = 100", new { StaffId = staffId, Id = requestId });
        }

        public void AssignService(long requestId, int teamId, string memberStaffId, DateTime startDate, string startHour, DateTime endDate, string endHour)
        {
            using var conn = _databaseService.GetConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();
            InsertAssign(conn, tran, requestId, teamId, memberStaffId, startDate, startHour, endDate, endHour);
            conn.Execute("UPDATE SC_SERVICEREQUEST SET STATUS = 1 WHERE ID = :Id", new { Id = requestId }, tran);
            tran.Commit();
        }

        public void ForwardService(long requestId, long dispatchId, int serviceCenterId)
        {
            using var conn = _databaseService.GetConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();
            conn.Execute("UPDATE SC_SERVICEASSIGN SET ISACTIVE = 0 WHERE REQUESTID = :Rid", new { Rid = requestId }, tran);   // old UpdateServiceAssignStatus
            conn.Execute(@"UPDATE SC_SERVICEREQUEST SET STATUS = 0, DISPATCHID = :Disp, FRDSERVICECENTERID = :Sc, FRDATE = SYSDATE
                            WHERE ID = :Id", new { Disp = dispatchId, Sc = serviceCenterId, Id = requestId }, tran);
            tran.Commit();
        }

        public int UpdateStatus(long requestId, int status, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return UpdateStatus(conn, null, requestId, status, staffId);
        }

        private static int UpdateStatus(IDbConnection conn, IDbTransaction? tran, long requestId, int status, string staffId)
        {
            string release = status == 3 || status == 5 ? ", RELEASEDATE = SYSDATE" : "";
            return conn.Execute($"UPDATE SC_SERVICEREQUEST SET STATUS = :St, UPDATEDBY = :StaffId, UPDATEDATE = SYSDATE{release} WHERE ID = :Id",
                                new { St = status, StaffId = staffId, Id = requestId }, tran);
        }

        public int CompleteService(long requestId, DateTime? stopTime, DateTime? startTime, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute(@"
                UPDATE SC_SERVICEREQUEST
                   SET STATUS = 2, BTIMEFROM = :Bf, BTIMETO = :Bt, UPDATEDBY = :StaffId, UPDATEDATE = SYSDATE, RELEASEDATE = SYSDATE
                 WHERE ID = :Id", new { Bf = stopTime, Bt = startTime, StaffId = staffId, Id = requestId });
        }

        public int ReviseRequest(long requestId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute("UPDATE SC_SERVICEREQUEST SET NOOFREVISION = NVL(NOOFREVISION, 0) + 1, STATUS = 4 WHERE ID = :Id",
                                new { Id = requestId });
        }

        public void RateByUser(long requestId, int rating, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();
            conn.Execute("UPDATE SC_SERVICEASSIGN SET RATINGFROMUSER = :Rv WHERE ISACTIVE = 1 AND REQUESTID = :Rid",
                         new { Rv = rating, Rid = requestId }, tran);
            UpdateStatus(conn, tran, requestId, 3, staffId);
            tran.Commit();
        }

        public void RateByServiceman(long requestId, int rating, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();
            conn.Execute("UPDATE SC_SERVICEASSIGN SET RATINGFROMSERVICEMAN = :Rv WHERE ISACTIVE = 1 AND REQUESTID = :Rid",
                         new { Rv = rating, Rid = requestId }, tran);
            UpdateStatus(conn, tran, requestId, 5, staffId);
            tran.Commit();
        }

        public int AddRemark(long requestId, string staffId, string remarks)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute(@"INSERT INTO SC_REQUESTREMARKS (ID, REQUESTID, STAFFID, REMARKS)
                                  VALUES (SQ_SC_REQUESTREMARKS.NEXTVAL, :Rid, :StaffId, :Rem)",
                                new { Rid = requestId, StaffId = staffId, Rem = remarks });
        }

        // ================================================================ REQUIRED ACCESSORIES

        public bool RequiredAccessoryExists(long requestId, int accessoryId, long exceptId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM SC_REQUIREDACCESSORIES WHERE ACCESSORIESID = :Acc AND REQUESTID = :Rid AND ID <> :Ex",
                new { Acc = accessoryId, Rid = requestId, Ex = exceptId }) > 0;
        }

        public int InsertRequiredAccessory(long requestId, int accessoryId, int quantity, int price, int currencyId, int isNew, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute(@"
                INSERT INTO SC_REQUIREDACCESSORIES (ID, ACCESSORIESID, QUANTITY, PRICE, CURRENCYID, ISNEW, CREATEDBY, REQUESTID)
                VALUES (SQ_SC_REQUIREDACCESSORIES.NEXTVAL, :Acc, :Qty, :Price, :Cur, :IsNew, :StaffId, :Rid)",
                new { Acc = accessoryId, Qty = quantity, Price = price, Cur = currencyId, IsNew = isNew, StaffId = staffId, Rid = requestId });
        }

        public int UpdateRequiredAccessory(long id, long requestId, int accessoryId, int quantity, int price, int currencyId, int isNew)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute(@"
                UPDATE SC_REQUIREDACCESSORIES
                   SET ACCESSORIESID = :Acc, QUANTITY = :Qty, PRICE = :Price, CURRENCYID = :Cur, ISNEW = :IsNew
                 WHERE ID = :Id AND REQUESTID = :Rid",
                new { Acc = accessoryId, Qty = quantity, Price = price, Cur = currencyId, IsNew = isNew, Id = id, Rid = requestId });
        }

        public int DeleteDraftAccessory(long id, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                DELETE FROM SC_REQUIREDACCESSORIES A
                 WHERE A.ID = :Id
                   AND EXISTS (SELECT 1 FROM SC_SERVICEREQUEST S WHERE S.ID = A.REQUESTID AND S.ISOPEN = 1 AND S.CREATEDBY = :StaffId)";
            return conn.Execute(sql, new { Id = id, StaffId = staffId });
        }
    }
}
