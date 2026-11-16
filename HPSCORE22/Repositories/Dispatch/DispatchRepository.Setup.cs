// Repositories/Dispatch/DispatchRepository.Setup.cs
// Dispatch phase 6 - setup tables. Port of YA dalIssueType, dalApprover (Insert / Update / GetAll / GetById + Common.Delete),
// dalGateman, dalSteps, dalStatus. Same tables, columns and sequences; every value is a bind parameter.
// CREATEDBY / UPDATEDBY are written exactly as the old code did (approver: CREATEDBY = staff id, UPDATEDBY = user id;
// gate man: both = user id). No delete except the approver (the only delete button that was switched on in YA).
using AlphaCare.Models;
using Dapper;

namespace Admin360.Repositories
{
    public partial class DispatchRepository
    {
        // ================================================================ ISSUE TYPE (old IssueType.aspx)

        public List<IssueTypeSetupModel> GetIssueTypesForSetup()
        {
            using var conn = _databaseService.GetConnection();
            return conn.Query<IssueTypeSetupModel>(@"
                SELECT ID, ISSUETYPENAME, NVL(RETURNABLE, 0) RETURNABLE, NVL(ISEXTERNAL, 0) ISEXTERNAL,
                       NVL(ISPERSONAL, 0) ISPERSONAL, NVL(ISACTIVE, 0) ISACTIVE
                  FROM DP_ISSUETYPE ORDER BY ISSUETYPENAME").ToList();
        }

        public bool IssueTypeExists(int id)
        {
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<int>("SELECT COUNT(1) FROM DP_ISSUETYPE WHERE ID = :Id", new { Id = id }) > 0;
        }

        // old CheckDuplicate (exact name); exceptId > 0 ignores that row
        public bool IssueTypeNameExists(string name, int exceptId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<int>("SELECT COUNT(1) FROM DP_ISSUETYPE WHERE ISSUETYPENAME = :Name AND ID <> :ExceptId",
                                           new { Name = name, ExceptId = exceptId }) > 0;
        }

        // old Insert: ISACTIVE is left to its default (1)
        public int InsertIssueType(string name, bool returnable, bool external, bool personal, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute(@"
                INSERT INTO DP_ISSUETYPE (ID, ISSUETYPENAME, RETURNABLE, ISEXTERNAL, ISPERSONAL, CREATEDBY)
                VALUES (SQ_DP_ISSUETYPE.NEXTVAL, :Name, :Ret, :Ext, :Per, :CreatedBy)",
                new { Name = name, Ret = returnable ? 1 : 0, Ext = external ? 1 : 0, Per = personal ? 1 : 0, CreatedBy = staffId });
        }

        public int UpdateIssueType(int id, string name, bool returnable, bool external, bool personal, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute(@"
                UPDATE DP_ISSUETYPE
                   SET ISSUETYPENAME = :Name, RETURNABLE = :Ret, ISEXTERNAL = :Ext, ISPERSONAL = :Per, UPDATEDBY = :UpdBy, UPDATEDATE = SYSDATE
                 WHERE ID = :Id",
                new { Name = name, Ret = returnable ? 1 : 0, Ext = external ? 1 : 0, Per = personal ? 1 : 0, UpdBy = staffId, Id = id });
        }

        // ================================================================ APPROVER (old Approver.aspx)

        public List<DispatchCompanyModel> GetCompanies()
        {
            using var conn = _databaseService.GetConnection();
            return conn.Query<DispatchCompanyModel>("SELECT ID, COMPANYNAME FROM BS_COMPANY ORDER BY COMPANYNAME").ToList();
        }

        private const string ApproverSelect = @"
                SELECT A.ID, A.APPROVERID, A.PRIORITY, A.BUSINESSCENTERID, A.COMPANYID, A.DEPARTMENTID, A.ISFINALAPPROVER,
                       E.EMPLOYEENAME, E.OFFICEMOBILE, E.OFFICEMAIL, B.CENTERNAME, CO.COMPANYNAME,
                       DE.DEPARTMENTCODE, DE.DEPARTMENTNAME, DS.DESIGNATIONNAME
                  FROM DP_APPROVER A
                 INNER JOIN BS_BUSINESSCENTER B ON B.ID = A.BUSINESSCENTERID
                 INNER JOIN BS_COMPANY CO ON CO.ID = A.COMPANYID
                 INNER JOIN BS_DEPARTMENT DE ON DE.ID = A.DEPARTMENTID
                 INNER JOIN EM_EMPLOYEE E ON E.USERID = A.APPROVERID
                 INNER JOIN BS_DESIGNATION DS ON DS.ID = E.DESIGNATIONID";

        // old GetAll(businessCenterId)
        public List<ApproverSetupModel> GetApprovers(int businessCenterId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Query<ApproverSetupModel>(ApproverSelect + @"
                 WHERE A.BUSINESSCENTERID = :CenterId
                 ORDER BY CO.COMPANYNAME, DE.DEPARTMENTCODE, A.PRIORITY", new { CenterId = businessCenterId }).ToList();
        }

        public bool ApproverRowExists(int id)
        {
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<int>("SELECT COUNT(1) FROM DP_APPROVER WHERE ID = :Id", new { Id = id }) > 0;
        }

        // the same person at the same company / location / department / level already exists (new check)
        public bool ApproverDuplicate(int exceptId, string approverId, int companyId, int centerId, int departmentId, int priority)
        {
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<int>(@"
                SELECT COUNT(1) FROM DP_APPROVER
                 WHERE ID <> :ExceptId AND APPROVERID = :ApproverId AND COMPANYID = :CompanyId
                   AND BUSINESSCENTERID = :CenterId AND DEPARTMENTID = :DepartmentId AND PRIORITY = :Priority",
                new { ExceptId = exceptId, ApproverId = approverId, CompanyId = companyId, CenterId = centerId, DepartmentId = departmentId, Priority = priority }) > 0;
        }

        public int InsertApprover(int companyId, string approverId, int priority, int centerId, int departmentId, bool isFinal, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute(@"
                INSERT INTO DP_APPROVER (ID, COMPANYID, APPROVERID, PRIORITY, BUSINESSCENTERID, DEPARTMENTID, ISFINALAPPROVER, CREATEDBY)
                VALUES (SQ_DP_APPROVER.NEXTVAL, :CompanyId, :ApproverId, :Priority, :CenterId, :DepartmentId, :IsFinal, :CreatedBy)",
                new { CompanyId = companyId, ApproverId = approverId, Priority = priority, CenterId = centerId, DepartmentId = departmentId,
                      IsFinal = isFinal ? 1 : 0, CreatedBy = staffId });
        }

        public int UpdateApprover(int id, int companyId, string approverId, int priority, int centerId, int departmentId, bool isFinal, string userId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute(@"
                UPDATE DP_APPROVER
                   SET COMPANYID = :CompanyId, APPROVERID = :ApproverId, PRIORITY = :Priority, BUSINESSCENTERID = :CenterId,
                       DEPARTMENTID = :DepartmentId, ISFINALAPPROVER = :IsFinal, UPDATEDBY = :UpdBy, UPDATEDATE = SYSDATE
                 WHERE ID = :Id",
                new { CompanyId = companyId, ApproverId = approverId, Priority = priority, CenterId = centerId, DepartmentId = departmentId,
                      IsFinal = isFinal ? 1 : 0, UpdBy = userId, Id = id });
        }

        // old btnDelete_Command -> Common.Delete("DP_APPROVER", id): the row is removed (the table has no ISACTIVE column)
        public int DeleteApprover(int id)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute("DELETE FROM DP_APPROVER WHERE ID = :Id", new { Id = id });
        }

        // ================================================================ GATE MAN (old AssignGateMan.aspx)

        // old GetAll(businessCenterId): active rows of the location
        public List<GateManSetupModel> GetGateMen(int businessCenterId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Query<GateManSetupModel>(@"
                SELECT A.ID, A.STAFFID, A.GATENO, A.BUSINESSCENTERID,
                       E.EMPLOYEENAME, E.OFFICEMOBILE, E.OFFICEMAIL, B.CENTERNAME, DS.DESIGNATIONNAME
                  FROM DP_ASSIGNGATEMAN A
                 INNER JOIN BS_BUSINESSCENTER B ON B.ID = A.BUSINESSCENTERID
                 INNER JOIN EM_EMPLOYEE E ON E.USERID = A.STAFFID
                 INNER JOIN BS_DESIGNATION DS ON DS.ID = E.DESIGNATIONID
                 WHERE A.ISACTIVE = 1 AND A.BUSINESSCENTERID = :CenterId
                 ORDER BY A.GATENO, E.EMPLOYEENAME", new { CenterId = businessCenterId }).ToList();
        }

        // old GetById also required ISACTIVE = 1
        public bool ActiveGateManExists(int id)
        {
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<int>("SELECT COUNT(1) FROM DP_ASSIGNGATEMAN WHERE ID = :Id AND ISACTIVE = 1", new { Id = id }) > 0;
        }

        // the staff already has an active gate assignment (other than this row) - new check, GetGateNo reads one row per staff
        public bool GateManActiveElsewhere(string staffId, int exceptId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<int>("SELECT COUNT(1) FROM DP_ASSIGNGATEMAN WHERE STAFFID = :StaffId AND ISACTIVE = 1 AND ID <> :ExceptId",
                                           new { StaffId = staffId, ExceptId = exceptId }) > 0;
        }

        public int InsertGateMan(string staffId, int gateNo, int centerId, string userId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute(@"
                INSERT INTO DP_ASSIGNGATEMAN (ID, STAFFID, GATENO, BUSINESSCENTERID, CREATEDBY)
                VALUES (SQ_DP_ASSIGNGATEMAN.NEXTVAL, :StaffId, :GateNo, :CenterId, :CreatedBy)",
                new { StaffId = staffId, GateNo = gateNo, CenterId = centerId, CreatedBy = userId });
        }

        public int UpdateGateMan(int id, string staffId, int gateNo, int centerId, string userId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute(@"
                UPDATE DP_ASSIGNGATEMAN
                   SET STAFFID = :StaffId, GATENO = :GateNo, BUSINESSCENTERID = :CenterId, UPDATEDBY = :UpdBy, UPDATEDATE = SYSDATE
                 WHERE ID = :Id AND ISACTIVE = 1",
                new { StaffId = staffId, GateNo = gateNo, CenterId = centerId, UpdBy = userId, Id = id });
        }

        // ================================================================ STEPS (old Steps.aspx)

        public List<StepSetupModel> GetSteps()
        {
            using var conn = _databaseService.GetConnection();
            return conn.Query<StepSetupModel>(@"
                SELECT S.ID, S.STEPNAME, S.PRIORITY, S.ISSUETYPEID, I.ISSUETYPENAME, S.ISFINALSTEP
                  FROM DP_STEPS S
                  LEFT OUTER JOIN DP_ISSUETYPE I ON I.ID = S.ISSUETYPEID
                 ORDER BY I.ISSUETYPENAME, S.PRIORITY").ToList();
        }

        public bool StepExists(int id)
        {
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<int>("SELECT COUNT(1) FROM DP_STEPS WHERE ID = :Id", new { Id = id }) > 0;
        }

        // old CheckDuplicate(name, issueType)
        public bool StepNameExists(string name, int issueTypeId, int exceptId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<int>("SELECT COUNT(1) FROM DP_STEPS WHERE STEPNAME = :Name AND ISSUETYPEID = :IssueTypeId AND ID <> :ExceptId",
                                           new { Name = name, IssueTypeId = issueTypeId, ExceptId = exceptId }) > 0;
        }

        public int InsertStep(string name, int priority, int issueTypeId, bool isFinal, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute(@"
                INSERT INTO DP_STEPS (ID, STEPNAME, PRIORITY, ISSUETYPEID, ISFINALSTEP, CREATEDBY)
                VALUES (SQ_DP_STEPS.NEXTVAL, :Name, :Priority, :IssueTypeId, :IsFinal, :CreatedBy)",
                new { Name = name, Priority = priority, IssueTypeId = issueTypeId, IsFinal = isFinal ? 1 : 0, CreatedBy = staffId });
        }

        public int UpdateStep(int id, string name, int priority, int issueTypeId, bool isFinal, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute(@"
                UPDATE DP_STEPS
                   SET STEPNAME = :Name, PRIORITY = :Priority, ISSUETYPEID = :IssueTypeId, ISFINALSTEP = :IsFinal, UPDATEDBY = :UpdBy, UPDATEDATE = SYSDATE
                 WHERE ID = :Id",
                new { Name = name, Priority = priority, IssueTypeId = issueTypeId, IsFinal = isFinal ? 1 : 0, UpdBy = staffId, Id = id });
        }

        // ================================================================ STATUS (old Status.aspx)

        public List<StatusSetupModel> GetStatuses()
        {
            using var conn = _databaseService.GetConnection();
            return conn.Query<StatusSetupModel>("SELECT ID, STATUSNAME FROM DP_STATUS ORDER BY ID").ToList();
        }

        public bool StatusExists(int id)
        {
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<int>("SELECT COUNT(1) FROM DP_STATUS WHERE ID = :Id", new { Id = id }) > 0;
        }

        public bool StatusNameExists(string name, int exceptId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<int>("SELECT COUNT(1) FROM DP_STATUS WHERE STATUSNAME = :Name AND ID <> :ExceptId",
                                           new { Name = name, ExceptId = exceptId }) > 0;
        }

        public int InsertStatus(string name)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute("INSERT INTO DP_STATUS (ID, STATUSNAME) VALUES (SQ_DP_STATUS.NEXTVAL, :Name)", new { Name = name });
        }

        public int UpdateStatus(int id, string name)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute("UPDATE DP_STATUS SET STATUSNAME = :Name WHERE ID = :Id", new { Name = name, Id = id });
        }
    }
}
