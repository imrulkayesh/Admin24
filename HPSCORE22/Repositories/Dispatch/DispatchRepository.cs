// Repositories/Dispatch/DispatchRepository.cs
// Port of YA App_Code/dal/Dispatch/dalDispatch.cs, dalDispatchGlobal.cs and dalApprover.cs (phase 1: create + approval).
// Same tables, columns, sequences and status numbers as YA. Differences to the old code:
//   * every value is a bind parameter (the old code concatenated user text into the SQL)
//   * one version only: times use the UTC_OFFSET of the sender location ("Global" pages). Offset 6 (Bangladesh) = SYSDATE,
//     so Bangladesh locations get exactly the old values. The time is taken from the DATABASE clock, not the web server.
//   * a dispatch can only be changed by the person allowed to change it (checked in the SQL, not only in the list)
//   * several statements that belong together run in one transaction
// NOTE: bind variable names must not be Oracle reserved words (:By, :Date, :Level, :Size ... -> ORA-01745)
using AlphaCare.Interface;
using AlphaCare.Models;
using Dapper;
using Oracle.ManagedDataAccess.Client;
using QCMS.Services;
using System.Data;

namespace Admin360.Repositories
{
    public partial class DispatchRepository : IDispatchRepository
    {
        private readonly DatabaseService _databaseService;
        private readonly ILogger<DispatchRepository> _logger;

        public DispatchRepository(DatabaseService databaseService, ILogger<DispatchRepository> logger)
        {
            _databaseService = databaseService;
            _logger = logger;
        }

        // ================================================================ LOOKUPS

        public List<DispatchIssueTypeModel> GetIssueTypes()
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT ID, ISSUETYPENAME, NVL(RETURNABLE, 0) RETURNABLE, NVL(ISEXTERNAL, 0) ISEXTERNAL, NVL(ISPERSONAL, 0) ISPERSONAL
                  FROM DP_ISSUETYPE
                 WHERE ISACTIVE = 1
                 ORDER BY ID DESC";
            return conn.Query<DispatchIssueTypeModel>(sql).ToList();
        }

        public DispatchIssueTypeModel? GetIssueType(int issueTypeId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT ID, ISSUETYPENAME, NVL(RETURNABLE, 0) RETURNABLE, NVL(ISEXTERNAL, 0) ISEXTERNAL, NVL(ISPERSONAL, 0) ISPERSONAL
                  FROM DP_ISSUETYPE
                 WHERE ID = :Id AND ISACTIVE = 1";
            return conn.QueryFirstOrDefault<DispatchIssueTypeModel>(sql, new { Id = issueTypeId });
        }

        public List<BusinessCenterModel> GetBusinessCenters()
        {
            using var conn = _databaseService.GetConnection();
            const string sql = "SELECT ID, CENTERCODE, CENTERNAME FROM BS_BUSINESSCENTER WHERE LOCATIONSTATUS = 1 ORDER BY CENTERNAME ASC";
            return conn.Query<BusinessCenterModel>(sql).ToList();
        }

        public List<DepartmentModel> GetDepartments()
        {
            using var conn = _databaseService.GetConnection();
            const string sql = "SELECT ID, DEPARTMENTNAME, DEPARTMENTCODE FROM BS_DEPARTMENT ORDER BY DEPARTMENTNAME";
            return conn.Query<DepartmentModel>(sql).ToList();
        }

        public DispatchEmployeeModel? GetEmployee(string staffId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT E.STAFFID, E.EMPLOYEENAME, E.OFFICEMOBILE, E.OFFICEMAIL, E.BUSINESSCENTERID, E.DEPARTMENTID, E.COMPANYID,
                       B.CENTERNAME, B.CENTERCODE, D.DEPARTMENTCODE, DE.DESIGNATIONNAME
                  FROM EM_EMPLOYEE E
                 INNER JOIN BS_BUSINESSCENTER B ON B.ID = E.BUSINESSCENTERID
                 INNER JOIN BS_LOCATIONS L ON L.ID = B.LOCATIONID
                 INNER JOIN BS_COUNTRY C ON C.ID = L.COUNTRYID
                 INNER JOIN BS_DEPARTMENT D ON D.ID = E.DEPARTMENTID
                 INNER JOIN BS_DESIGNATION DE ON DE.ID = E.DESIGNATIONID
                 WHERE E.STAFFID = :StaffId";
            return conn.QueryFirstOrDefault<DispatchEmployeeModel>(sql, new { StaffId = staffId });
        }

        public List<DispatchEmployeeModel> GetEmployeesByDepartment(int businessCenterId, int departmentId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT STAFFID, EMPLOYEENAME, OFFICEMOBILE, BUSINESSCENTERID, DEPARTMENTID
                  FROM EM_EMPLOYEE
                 WHERE BUSINESSCENTERID = :CenterId AND DEPARTMENTID = :DepartmentId
                 ORDER BY EMPLOYEENAME";
            return conn.Query<DispatchEmployeeModel>(sql, new { CenterId = businessCenterId, DepartmentId = departmentId }).ToList();
        }

        public List<string> SearchEmployees(string term)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT E.STAFFID || '-' || B.CENTERCODE || '-' || D.DEPARTMENTCODE || '-' || DE.DESIGNATIONNAME || '-' || E.EMPLOYEENAME
                  FROM EM_EMPLOYEE E
                 INNER JOIN BS_BUSINESSCENTER B ON B.ID = E.BUSINESSCENTERID
                 INNER JOIN BS_LOCATIONS L ON L.ID = B.LOCATIONID
                 INNER JOIN BS_COUNTRY C ON C.ID = L.COUNTRYID
                 INNER JOIN BS_DEPARTMENT D ON D.ID = E.DEPARTMENTID
                 INNER JOIN BS_DESIGNATION DE ON DE.ID = E.DESIGNATIONID
                 WHERE LOWER(E.STAFFID || '-' || B.CENTERCODE || '-' || D.DEPARTMENTCODE || '-' || DE.DESIGNATIONNAME || '-' || E.EMPLOYEENAME)
                       LIKE '%' || :Term || '%'
                   AND ROWNUM <= 20";
            return conn.Query<string>(sql, new { Term = term.Trim().ToLowerInvariant() }).ToList();
        }

        public List<string> SearchItems(string term)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT DISTINCT ITEMNAME FROM DP_DISPATCHITEMS
                 WHERE LOWER(ITEMNAME) LIKE '%' || :Term || '%' AND ROWNUM <= 20";
            return conn.Query<string>(sql, new { Term = term.Trim().ToLowerInvariant() }).ToList();
        }

        // ================================================================ CREATE (DRAFT)

        // old CheckDispatch: one open draft (ISOPEN = 1) per user collects the items until Save
        public long GetOrCreateDraft(string staffId)
        {
            using var conn = _databaseService.GetConnection();
            const string find = "SELECT MIN(ID) FROM DP_DISPATCH WHERE ISOPEN = 1 AND CREATEDBY = :StaffId";

            long? id = conn.ExecuteScalar<long?>(find, new { StaffId = staffId });
            if (id.HasValue) return id.Value;

            conn.Execute("INSERT INTO DP_DISPATCH (ID, ISOPEN, CREATEDBY) VALUES (SQ_DP_DISPATCH.NEXTVAL, 1, :StaffId)", new { StaffId = staffId });
            return conn.ExecuteScalar<long>(find, new { StaffId = staffId });
        }

        public bool IsOwnDraft(long dispatchId, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<int>("SELECT COUNT(1) FROM DP_DISPATCH WHERE ID = :Id AND ISOPEN = 1 AND CREATEDBY = :StaffId",
                                           new { Id = dispatchId, StaffId = staffId }) > 0;
        }

        public List<DispatchItemModel> GetItems(long dispatchId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT ID, DISPATCHID, ITEMNAME, QUANTITY, UNIT, NOTES, ATTACHMENT
                  FROM DP_DISPATCHITEMS
                 WHERE DISPATCHID = :DispatchId
                 ORDER BY ID";
            return conn.Query<DispatchItemModel>(sql, new { DispatchId = dispatchId }).ToList();
        }

        public DispatchItemModel? GetItem(long itemId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = "SELECT ID, DISPATCHID, ITEMNAME, QUANTITY, UNIT, NOTES, ATTACHMENT FROM DP_DISPATCHITEMS WHERE ID = :Id";
            return conn.QueryFirstOrDefault<DispatchItemModel>(sql, new { Id = itemId });
        }

        public bool DraftHasItem(long dispatchId, string itemName)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = "SELECT COUNT(1) FROM DP_DISPATCHITEMS WHERE DISPATCHID = :DispatchId AND ITEMNAME = :ItemName";
            return conn.ExecuteScalar<int>(sql, new { DispatchId = dispatchId, ItemName = itemName }) > 0;
        }

        // old InsertDispatchItems: an item with the same name in this dispatch only gets more quantity
        // returns 1 = new row, 2 = quantity added to the existing row, 0 = not the user's open draft
        public int AddDraftItem(long dispatchId, string staffId, string itemName, decimal quantity, string unit, string notes, string attachment)
        {
            using var conn = _databaseService.GetConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();

            if (!IsOwnOpenDraft(conn, tran, dispatchId, staffId)) { tran.Rollback(); return 0; }

            long? existing = conn.ExecuteScalar<long?>(
                "SELECT MIN(ID) FROM DP_DISPATCHITEMS WHERE DISPATCHID = :DispatchId AND ITEMNAME = :ItemName",
                new { DispatchId = dispatchId, ItemName = itemName }, tran);

            if (existing.HasValue)
            {
                conn.Execute("UPDATE DP_DISPATCHITEMS SET QUANTITY = NVL(QUANTITY, 0) + :Qty WHERE ID = :Id",
                    new { Qty = quantity, Id = existing.Value }, tran);
                tran.Commit();
                return 2;
            }

            conn.Execute(@"
                INSERT INTO DP_DISPATCHITEMS (ID, ITEMID, QUANTITY, UNIT, ITEMNAME, DISPATCHID, ATTACHMENT, NOTES)
                VALUES (SQ_DP_DISPATCHITEMS.NEXTVAL, 0, :Qty, :Unit, :ItemName, :DispatchId, :Attachment, :Notes)",
                new { Qty = quantity, Unit = unit, ItemName = itemName, DispatchId = dispatchId, Attachment = attachment, Notes = notes }, tran);
            tran.Commit();
            return 1;
        }

        public int UpdateDraftItem(long itemId, string staffId, decimal quantity, string unit, string notes)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                UPDATE DP_DISPATCHITEMS I
                   SET I.QUANTITY = :Qty, I.UNIT = :Unit, I.NOTES = :Notes
                 WHERE I.ID = :Id
                   AND EXISTS (SELECT 1 FROM DP_DISPATCH D WHERE D.ID = I.DISPATCHID AND D.ISOPEN = 1 AND D.CREATEDBY = :StaffId)";
            return conn.Execute(sql, new { Qty = quantity, Unit = unit, Notes = notes, Id = itemId, StaffId = staffId });
        }

        // only an item of the user's OWN OPEN draft (the old page deleted any DP_DISPATCHITEMS row by id)
        public int DeleteDraftItem(long itemId, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                DELETE FROM DP_DISPATCHITEMS I
                 WHERE I.ID = :Id
                   AND EXISTS (SELECT 1 FROM DP_DISPATCH D WHERE D.ID = I.DISPATCHID AND D.ISOPEN = 1 AND D.CREATEDBY = :StaffId)";
            return conn.Execute(sql, new { Id = itemId, StaffId = staffId });
        }

        // old dalDispatchGlobal.Update: the draft becomes a real dispatch.
        // TRANSACTIONID = 'DG' + location of the logged in user (3 digits) + yy + MM (local time of the sender location) + 6 digit number.
        // Returns the TRANSACTIONID, or null when the draft is not the user's open draft / has no item.
        public string? SubmitDispatch(SubmitDispatchModel m, string staffId, string userId, int userBusinessCenterId)
        {
            // same padding as the old code: >= 100 as is, 10..99 -> "0" + id, below 10 -> "00" + id
            string office = userBusinessCenterId >= 100 ? userBusinessCenterId.ToString()
                          : userBusinessCenterId >= 10 ? "0" + userBusinessCenterId
                          : "00" + userBusinessCenterId;

            // two people can get the same next number at the same moment -> the unique index UK_TRANSACTIONID refuses one: try again
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    return SubmitOnce(m, staffId, userId, office);
                }
                catch (OracleException ex) when (ex.Number == 1 && attempt < 3)
                {
                    _logger.LogWarning("TRANSACTIONID already used, trying again. DispatchId={DispatchId}, Attempt={Attempt}", m.DispatchId, attempt);
                }
            }
        }

        private string? SubmitOnce(SubmitDispatchModel m, string staffId, string userId, string office)
        {
            using var conn = _databaseService.GetConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();

            if (!IsOwnOpenDraft(conn, tran, m.DispatchId, staffId)) { tran.Rollback(); return null; }
            if (conn.ExecuteScalar<int>("SELECT COUNT(1) FROM DP_DISPATCHITEMS WHERE DISPATCHID = :Id", new { Id = m.DispatchId }, tran) == 0)
            {
                tran.Rollback(); return null;
            }

            DateTime local = LocalTimeOfCenter(conn, tran, m.SenderCenterId);
            string yy = local.ToString("yy"), mm = local.ToString("MM");

            // old code: "delete from dp_transits where DISPATCHID = id" (a draft has no real step yet)
            conn.Execute("DELETE FROM DP_TRANSITS WHERE DISPATCHID = :Id", new { Id = m.DispatchId }, tran);

            const string update = @"
                UPDATE DP_DISPATCH
                   SET TOUSER = :ToUser, FROMUSER = :FromUser, SENDNAME = :SendName, SENDMOBILE = :SendMobile,
                       SENDDEPARTMENTID = :SendDept, SENDBUSINESSCENTERID = :SendCenter,
                       RECDEPARTMENTID = :RecDept, RECBUSINESSCENTERID = :RecCenter, DIRECTSEND = :DirectSend,
                       ISOPEN = 0, STATUSID = 1,
                       RECEIVERNAME = :RecName, ISSUETYPEID = :IssueType, RECEIVERMOBILE = :RecMobile, RECEIVERADDRESS = :RecAddress,
                       RECEIVERDEPARTMENT = :RecDeptText, BUSINESSCENTER = :RecCenterText,
                       UPDATEDBY = :UpdatedBy, UPDATEDATE = :Local1, CREATEDATE = :Local2,
                       TRANSACTIONID = (SELECT :Prefix || TRIM(TO_CHAR((NVL(MAX(TO_NUMBER(SUBSTR(X.TRANSACTIONID, 10, 6))), 0) + 1), '000000'))
                                          FROM DP_DISPATCH X
                                         WHERE TRIM(TO_CHAR(NVL(SUBSTR(X.TRANSACTIONID, 3, 3), 0), '000')) = :Office
                                           AND TRIM(TO_CHAR(NVL(SUBSTR(X.TRANSACTIONID, 6, 2), 0), '00')) = :Yy
                                           AND TRIM(TO_CHAR(NVL(SUBSTR(X.TRANSACTIONID, 8, 2), 0), '00')) = :Mm)
                 WHERE ID = :Id AND ISOPEN = 1 AND CREATEDBY = :StaffId";

            int changed = conn.Execute(update, new
            {
                ToUser = m.ReceiverStaffId,
                FromUser = m.SenderStaffId,
                SendName = m.SenderName,
                SendMobile = m.SenderMobile,
                SendDept = m.SenderDepartmentId,
                SendCenter = m.SenderCenterId,
                RecDept = m.ReceiverDepartmentId,
                RecCenter = m.ReceiverCenterId,
                DirectSend = m.DirectSend,
                RecName = m.ReceiverName,
                IssueType = m.IssueTypeId,
                RecMobile = m.ReceiverMobile,
                RecAddress = m.ReceiverAddress,
                RecDeptText = m.ReceiverDepartment,
                RecCenterText = m.ReceiverCenter,
                UpdatedBy = userId,                          // old code: SessionName.UserId (UC_USERS.ID)
                Local1 = local,
                Local2 = local,
                Prefix = "DG" + office + yy + mm,
                Office = office,
                Yy = yy,
                Mm = mm,
                Id = m.DispatchId,
                StaffId = staffId
            }, tran);

            if (changed != 1) { tran.Rollback(); return null; }

            conn.Execute(@"INSERT INTO DP_TRANSITS (ID, RECEIVEDATE, DISPATCHID, STEP)
                           VALUES (SQ_DP_TRANSITS.NEXTVAL, :Local, :Id, 'Final Approver')",
                         new { Local = local, Id = m.DispatchId }, tran);

            string? txId = conn.ExecuteScalar<string>("SELECT TRANSACTIONID FROM DP_DISPATCH WHERE ID = :Id", new { Id = m.DispatchId }, tran);
            tran.Commit();
            return txId;
        }

        public List<DispatchListItemModel> GetMyWaitingList(string staffId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = ListSelect + @"
                 WHERE D.STATUSID = 1 AND D.GATEPASS >= 0
                   AND (D.CREATEDBY = :StaffId1 OR D.FROMUSER = :StaffId2)
                 ORDER BY D.CREATEDATE DESC";
            return conn.Query<DispatchListItemModel>(sql, new { StaffId1 = staffId, StaffId2 = staffId }).ToList();
        }

        // ================================================================ DETAILS

        public DispatchDetailModel? GetDispatchDetails(long dispatchId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT D.ID, D.TRANSACTIONID, D.STATUSID, D.ISOPEN, D.ISSUETYPEID, I.ISSUETYPENAME,
                       NVL(I.ISEXTERNAL, 0) ISEXTERNAL, NVL(I.ISPERSONAL, 0) ISPERSONAL, NVL(I.RETURNABLE, 0) RETURNABLE,
                       D.DIRECTSEND, D.CREATEDBY, D.FROMUSER, D.TOUSER, D.CREATEDATE, D.SENDNAME, D.SENDMOBILE,
                       D.RECEIVERNAME, D.RECEIVERMOBILE, D.RECEIVERADDRESS, D.RECEIVERDEPARTMENT, D.BUSINESSCENTER, D.NOOFPRINT, D.GATEPASS,
                       E.EMPLOYEENAME, E.OFFICEMOBILE, E.OFFICEMAIL,
                       B.CENTERCODE, B.CENTERNAME, B.ADDRESS SENDERADDRESS,
                       BD.DEPARTMENTCODE, BD.DEPARTMENTNAME, DE.DESIGNATIONNAME,
                       TB.CENTERCODE || '-' || TB.ADDRESS ADDRESS
                  FROM DP_DISPATCH D
                 INNER JOIN DP_ISSUETYPE I ON I.ID = D.ISSUETYPEID
                 INNER JOIN EM_EMPLOYEE E ON E.USERID = D.FROMUSER
                 INNER JOIN BS_BUSINESSCENTER B ON B.ID = E.BUSINESSCENTERID
                 INNER JOIN BS_DEPARTMENT BD ON BD.ID = E.DEPARTMENTID
                 INNER JOIN BS_DESIGNATION DE ON DE.ID = E.DESIGNATIONID
                  LEFT OUTER JOIN BS_BUSINESSCENTER TB ON TB.ID = D.RECBUSINESSCENTERID
                 WHERE D.ID = :Id";
            return conn.QueryFirstOrDefault<DispatchDetailModel>(sql, new { Id = dispatchId });
        }

        // old GetTransitDetails used INNER JOIN on the employee, so the open step (no UPDATEDBY yet) was not shown.
        // LEFT JOIN shows it too (read only, nothing else changes).
        public List<DispatchTransitModel> GetTransits(long dispatchId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT T.ID, T.STEP, T.RECEIVEDATE, T.RELEASEDATE, T.REMARKS, T.UPDATEDBY, T.ISACTIVE,
                       E.EMPLOYEENAME, B.CENTERCODE, BD.DEPARTMENTCODE
                  FROM DP_TRANSITS T
                  LEFT OUTER JOIN EM_EMPLOYEE E ON E.USERID = T.UPDATEDBY
                  LEFT OUTER JOIN BS_BUSINESSCENTER B ON B.ID = E.BUSINESSCENTERID
                  LEFT OUTER JOIN BS_DEPARTMENT BD ON BD.ID = E.DEPARTMENTID
                 WHERE T.DISPATCHID = :Id
                 ORDER BY T.ID ASC";
            return conn.Query<DispatchTransitModel>(sql, new { Id = dispatchId }).ToList();
        }

        public List<DispatchTransportModel> GetTransports(long dispatchId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT T.ID, TR.TRANSPORTNAME, T.DRIVER, T.DELIVERYMAN, T.MOBILE, T.COST, C.CURRENCYCODE
                  FROM DP_TRANSPORTDETAILS T
                 INNER JOIN DP_TRANSPORT TR ON TR.ID = T.TRANSPORTID
                 INNER JOIN ASSET_CURRENCY C ON C.ID = T.CURRENCYID
                 WHERE T.DISPATCHID = :Id
                 ORDER BY T.ID";
            return conn.Query<DispatchTransportModel>(sql, new { Id = dispatchId }).ToList();
        }

        public bool CanViewDispatch(long dispatchId, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT COUNT(1)
                  FROM DP_DISPATCH D
                 WHERE D.ID = :Id
                   AND NVL(D.ISOPEN, 0) = 0
                   AND ( D.CREATEDBY = :S1 OR D.FROMUSER = :S2 OR D.TOUSER = :S3
                         OR EXISTS (SELECT 1
                                      FROM EM_EMPLOYEE E
                                     INNER JOIN DP_APPROVER A
                                        ON A.BUSINESSCENTERID = E.BUSINESSCENTERID
                                       AND A.DEPARTMENTID = E.DEPARTMENTID
                                       AND A.COMPANYID = E.COMPANYID
                                     WHERE E.USERID = D.CREATEDBY
                                       AND A.APPROVERID = :S4) )";
            return conn.ExecuteScalar<int>(sql, new { Id = dispatchId, S1 = staffId, S2 = staffId, S3 = staffId, S4 = staffId }) > 0;
        }

        // ================================================================ APPROVAL

        // the approver of a dispatch = DP_APPROVER row with the creator's location + department + company
        private const string ApproverOfCreator = @"
                 EXISTS (SELECT 1 FROM DP_APPROVER A
                          WHERE A.APPROVERID = :Approver
                            AND A.BUSINESSCENTERID = E.BUSINESSCENTERID
                            AND A.DEPARTMENTID = E.DEPARTMENTID
                            AND A.COMPANYID = E.COMPANYID";

        private const string ActiveTransit = " EXISTS (SELECT 1 FROM DP_TRANSITS DT WHERE DT.DISPATCHID = D.ID AND DT.ISACTIVE = 1) ";

        // columns of all lists (old repeaters: TRANSACTIONID, ISSUETYPENAME, sender EMPLOYEENAME, receiver ..., CREATEDUSER)
        private const string ListSelect = @"
                SELECT D.ID, D.TRANSACTIONID, D.CREATEDATE, D.STATUSID, IT.ISSUETYPENAME,
                       SE.EMPLOYEENAME, D.RECEIVERNAME, D.RECEIVERMOBILE, D.RECEIVERDEPARTMENT, D.BUSINESSCENTER, D.RECEIVERADDRESS,
                       E.EMPLOYEENAME CREATEDUSER
                  FROM DP_DISPATCH D
                 INNER JOIN EM_EMPLOYEE E ON E.USERID = D.CREATEDBY
                 INNER JOIN EM_EMPLOYEE SE ON SE.USERID = D.FROMUSER
                 INNER JOIN DP_ISSUETYPE IT ON IT.ID = D.ISSUETYPEID";

        // old GetApprovalList: waiting at my level (STATUSID = my PRIORITY)
        public List<DispatchListItemModel> GetPendingApprovals(string approverStaffId)
        {
            using var conn = _databaseService.GetConnection();
            string sql = ListSelect + @"
                 WHERE NVL(D.ISOPEN, 0) = 0 AND" + ActiveTransit + "AND" + ApproverOfCreator + @"
                            AND A.PRIORITY = D.STATUSID)
                 ORDER BY D.UPDATEDATE DESC";
            return conn.Query<DispatchListItemModel>(sql, new { Approver = approverStaffId }).ToList();
        }

        // old GetApprovedList: approved (STATUSID = 2), a step updated by me, created in the last 3 days
        public List<DispatchListItemModel> GetApprovedByMe(string approverStaffId)
        {
            using var conn = _databaseService.GetConnection();
            string sql = ListSelect + @"
                 WHERE D.STATUSID = 2
                   AND TRUNC(D.CREATEDATE) BETWEEN TRUNC(SYSDATE) - 3 AND TRUNC(SYSDATE)
                   AND EXISTS (SELECT 1 FROM DP_TRANSITS DT WHERE DT.DISPATCHID = D.ID AND DT.UPDATEDBY = :Me)
                   AND" + ApproverOfCreator + @")
                 ORDER BY D.TRANSACTIONID DESC";
            return conn.Query<DispatchListItemModel>(sql, new { Approver = approverStaffId, Me = approverStaffId }).ToList();
        }

        // old GetRejectedList: rejected at my level (STATUSID = PRIORITY + 98), created in the last 180 days
        public List<DispatchListItemModel> GetRejectedForMe(string approverStaffId)
        {
            using var conn = _databaseService.GetConnection();
            string sql = ListSelect + @"
                 WHERE TRUNC(D.CREATEDATE) BETWEEN TRUNC(SYSDATE) - 180 AND TRUNC(SYSDATE)
                   AND" + ActiveTransit + "AND" + ApproverOfCreator + @"
                            AND A.PRIORITY + 98 = D.STATUSID)
                 ORDER BY D.TRANSACTIONID DESC";
            return conn.Query<DispatchListItemModel>(sql, new { Approver = approverStaffId }).ToList();
        }

        public bool IsPendingForApprover(long dispatchId, string approverStaffId)
        {
            using var conn = _databaseService.GetConnection();
            return PendingStatus(conn, null, dispatchId, approverStaffId).HasValue;
        }

        public bool IsRejectedForApprover(long dispatchId, string approverStaffId)
        {
            using var conn = _databaseService.GetConnection();
            return RejectedStatus(conn, null, dispatchId, approverStaffId).HasValue;
        }

        // old ApproveDispatch(Global): close the open step, new step = DispatchStatus text, STATUSID = 2
        public string? Approve(long dispatchId, string approverStaffId, string remarks)
        {
            using var conn = _databaseService.GetConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();

            int? status = PendingStatus(conn, tran, dispatchId, approverStaffId);
            if (!status.HasValue) { tran.Rollback(); return null; }

            DateTime local = LocalTimeOfDispatch(conn, tran, dispatchId);

            conn.Execute(@"
                UPDATE DP_TRANSITS
                   SET RELEASEDATE = :Local1, REMARKS = :Remarks, UPDATEDATE = :Local2, ISACTIVE = 0, UPDATEDBY = :ActBy
                 WHERE ISACTIVE = 1 AND DISPATCHID = :Id",
                new { Local1 = local, Remarks = remarks, Local2 = local, ActBy = approverStaffId, Id = dispatchId }, tran);

            string step = StatusText(conn, tran, dispatchId);           // computed while STATUSID is still the old value (like the old code)

            conn.Execute(@"INSERT INTO DP_TRANSITS (ID, RECEIVEDATE, DISPATCHID, STEP)
                           VALUES (SQ_DP_TRANSITS.NEXTVAL, :Local, :Id, :Step)",
                         new { Local = local, Id = dispatchId, Step = step }, tran);

            int changed = conn.Execute(@"
                UPDATE DP_DISPATCH SET STATUSID = 2, UPDATEDBY = :ActBy, UPDATEDATE = :Local
                 WHERE ID = :Id AND STATUSID = :OldStatus",
                new { ActBy = approverStaffId, Local = local, Id = dispatchId, OldStatus = status.Value }, tran);

            if (changed != 1) { tran.Rollback(); return null; }
            tran.Commit();
            return step;
        }

        // old RejectDispatch(Global): STATUSID + 98, step "Rejected"
        public bool Reject(long dispatchId, string approverStaffId, string remarks)
        {
            using var conn = _databaseService.GetConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();

            int? status = PendingStatus(conn, tran, dispatchId, approverStaffId);
            if (!status.HasValue) { tran.Rollback(); return false; }

            DateTime local = LocalTimeOfDispatch(conn, tran, dispatchId);
            CloseStepsAndAdd(conn, tran, dispatchId, approverStaffId, remarks, local, "Rejected");

            int changed = conn.Execute(@"
                UPDATE DP_DISPATCH SET STATUSID = STATUSID + 98, UPDATEDBY = :ActBy, UPDATEDATE = :Local
                 WHERE ID = :Id AND STATUSID = :OldStatus",
                new { ActBy = approverStaffId, Local = local, Id = dispatchId, OldStatus = status.Value }, tran);

            if (changed != 1) { tran.Rollback(); return false; }
            tran.Commit();
            return true;
        }

        // old ApproveRejected: STATUSID - 97 (99 -> 2), new step = DispatchStatus text ("Approved" for a rejected dispatch)
        public string? ApproveRejected(long dispatchId, string approverStaffId, string remarks)
        {
            using var conn = _databaseService.GetConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();

            int? status = RejectedStatus(conn, tran, dispatchId, approverStaffId);
            if (!status.HasValue) { tran.Rollback(); return null; }

            DateTime local = LocalTimeOfDispatch(conn, tran, dispatchId);
            string step = StatusText(conn, tran, dispatchId);
            CloseStepsAndAdd(conn, tran, dispatchId, approverStaffId, remarks, local, step);

            int changed = conn.Execute(@"
                UPDATE DP_DISPATCH SET STATUSID = STATUSID - 97, UPDATEDBY = :ActBy, UPDATEDATE = :Local
                 WHERE ID = :Id AND STATUSID = :OldStatus",
                new { ActBy = approverStaffId, Local = local, Id = dispatchId, OldStatus = status.Value }, tran);

            if (changed != 1) { tran.Rollback(); return null; }
            tran.Commit();
            return step;
        }

        // old UpdateTransitRemarks(Global)
        public int UpdateTransitRemarks(long dispatchId, string staffId, string remarks)
        {
            using var conn = _databaseService.GetConnection();
            conn.Open();
            DateTime local = LocalTimeOfDispatch(conn, null, dispatchId);
            return conn.Execute(@"
                UPDATE DP_TRANSITS SET REMARKS = :Remarks, UPDATEDATE = :Local, UPDATEDBY = :ActBy
                 WHERE ISACTIVE = 1 AND DISPATCHID = :Id",
                new { Remarks = remarks, Local = local, ActBy = staffId, Id = dispatchId });
        }

        public int DeleteItemAsApprover(long itemId, string approverStaffId)
        {
            using var conn = _databaseService.GetConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();

            long? dispatchId = conn.ExecuteScalar<long?>("SELECT DISPATCHID FROM DP_DISPATCHITEMS WHERE ID = :Id", new { Id = itemId }, tran);
            if (!dispatchId.HasValue || !PendingStatus(conn, tran, dispatchId.Value, approverStaffId).HasValue) { tran.Rollback(); return 0; }

            int n = conn.Execute("DELETE FROM DP_DISPATCHITEMS WHERE ID = :Id AND DISPATCHID = :DispatchId",
                                 new { Id = itemId, DispatchId = dispatchId.Value }, tran);
            tran.Commit();
            return n;
        }

        // ================================================================ MAIL

        public DispatchMailInfoModel? GetMailInfo(long dispatchId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT D.ID, D.TRANSACTIONID, D.CREATEDBY, D.BUSINESSCENTER, D.RECEIVERNAME, D.RECEIVERMOBILE,
                       FE.EMPLOYEENAME, FE.OFFICEMAIL, FE.OFFICEMOBILE, TE.OFFICEMAIL TOMAIL
                  FROM DP_DISPATCH D
                 INNER JOIN EM_EMPLOYEE FE ON FE.STAFFID = D.FROMUSER
                  LEFT OUTER JOIN EM_EMPLOYEE TE ON TE.STAFFID = D.TOUSER
                 WHERE D.ID = :Id";
            var info = conn.QueryFirstOrDefault<DispatchMailInfoModel>(sql, new { Id = dispatchId });
            if (info != null) info.Items = GetItems(dispatchId);
            return info;
        }

        public List<string> GetApproverMails(string createdByStaffId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT DISTINCT AE.OFFICEMAIL
                  FROM EM_EMPLOYEE E
                 INNER JOIN DP_APPROVER A ON A.DEPARTMENTID = E.DEPARTMENTID
                 INNER JOIN EM_EMPLOYEE AE ON AE.STAFFID = A.APPROVERID
                 WHERE A.COMPANYID = E.COMPANYID
                   AND A.BUSINESSCENTERID = E.BUSINESSCENTERID
                   AND E.STAFFID = :CreatedBy
                   AND AE.OFFICEMAIL IS NOT NULL";
            return conn.Query<string>(sql, new { CreatedBy = createdByStaffId }).ToList();
        }

        // ================================================================ PHASE 2: PRINT LABEL

        // old ApprovedList: D.STATUSID > 1 AND D.GATEPASS >= 0 AND (CREATEDBY = me OR FROMUSER = me), last 180 days
        private const string PrintRule = " D.STATUSID > 1 AND D.GATEPASS >= 0 AND (D.CREATEDBY = :S1 OR D.FROMUSER = :S2) ";

        public List<DispatchListItemModel> GetPrintList(string staffId)
        {
            using var conn = _databaseService.GetConnection();
            string sql = @"
                SELECT D.ID, D.TRANSACTIONID, D.CREATEDATE, D.STATUSID, IT.ISSUETYPENAME,
                       SE.EMPLOYEENAME, D.RECEIVERNAME, D.RECEIVERMOBILE, D.RECEIVERDEPARTMENT, D.BUSINESSCENTER, D.RECEIVERADDRESS,
                       E.EMPLOYEENAME CREATEDUSER, NVL(D.NOOFPRINT, 0) NOOFPRINT
                  FROM DP_DISPATCH D
                 INNER JOIN EM_EMPLOYEE E ON E.USERID = D.CREATEDBY
                 INNER JOIN EM_EMPLOYEE SE ON SE.USERID = D.FROMUSER
                 INNER JOIN DP_ISSUETYPE IT ON IT.ID = D.ISSUETYPEID
                 WHERE" + PrintRule + @"
                   AND TRUNC(D.CREATEDATE) BETWEEN TRUNC(SYSDATE) - 180 AND TRUNC(SYSDATE)
                 ORDER BY D.CREATEDATE DESC";
            return conn.Query<DispatchListItemModel>(sql, new { S1 = staffId, S2 = staffId }).ToList();
        }

        public bool CanPrint(long dispatchId, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            string sql = "SELECT COUNT(1) FROM DP_DISPATCH D WHERE D.ID = :Id AND" + PrintRule;
            return conn.ExecuteScalar<int>(sql, new { Id = dispatchId, S1 = staffId, S2 = staffId }) > 0;
        }

        // old UpdatePrint: NOOFPRINT = NOOFPRINT + 1 (only for a dispatch the user may print)
        public int MarkPrinted(long dispatchId, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            string sql = "UPDATE DP_DISPATCH D SET D.NOOFPRINT = NVL(D.NOOFPRINT, 0) + 1 WHERE D.ID = :Id AND" + PrintRule;
            return conn.Execute(sql, new { Id = dispatchId, S1 = staffId, S2 = staffId });
        }

        // ================================================================ PHASE 2: GATE CHECK

        // old dalCommon.IsPermitted(url): the page is in one of the user's role menus.
        // Menu urls can be "Dispatch/GateCheck", "/Dispatch/GateCheck" or "~/Dispatch/GateCheck" -> compared without "~" and "/".
        public bool HasMenuPermission(string staffId, string url)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT COUNT(1)
                  FROM UC_TASKPERMISSION_COPY TP
                 INNER JOIN UC_TASKMANAGER_COPY T ON T.ID = TP.TASKID
                 WHERE TP.ROLEID IN (SELECT ROLEID FROM UC_USERROLE WHERE STAFFID = :StaffId)
                   AND LOWER(LTRIM(TRIM(T.URL), '~/')) = :Url";
            string u = (url ?? "").Trim().TrimStart('~', '/').ToLowerInvariant();
            return conn.ExecuteScalar<int>(sql, new { StaffId = staffId, Url = u }) > 0;
        }

        // old GetGatemen: GATENO of the user in DP_ASSIGNGATEMAN, "1" when the user has no row.
        // (an active row is preferred; the old code took any row)
        public int GetGateNo(string staffId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT GATENO FROM (
                    SELECT GATENO FROM DP_ASSIGNGATEMAN
                     WHERE STAFFID = :StaffId
                     ORDER BY NVL(ISACTIVE, 0) DESC, ID DESC)
                 WHERE ROWNUM = 1";
            var rows = conn.Query<int?>(sql, new { StaffId = staffId }).ToList();
            if (rows.Count == 0) return 1;                 // old default
            return rows[0] ?? 0;                           // row without a gate number -> no gate (old: nothing happened)
        }

        public List<DispatchListItemModel> GetGateCheckList(int businessCenterId)
        {
            using var conn = _databaseService.GetConnection();
            string sql = @"
                SELECT D.ID, D.CREATEDATE, D.TRANSACTIONID, D.STATUSID, IT.ISSUETYPENAME,
                       SE.EMPLOYEENAME, D.RECEIVERNAME, D.RECEIVERMOBILE, D.RECEIVERDEPARTMENT, D.BUSINESSCENTER, D.RECEIVERADDRESS,
                       E.EMPLOYEENAME CREATEDUSER
                  FROM DP_DISPATCH D
                 INNER JOIN EM_EMPLOYEE SE ON SE.STAFFID = D.FROMUSER
                 INNER JOIN EM_EMPLOYEE E ON E.STAFFID = D.CREATEDBY
                  LEFT OUTER JOIN DP_ISSUETYPE IT ON IT.ID = D.ISSUETYPEID
                 WHERE" + ActiveTransit + @"
                   AND D.STATUSID IN (2, 3, 6, 7) AND D.ISOPEN = 0 AND D.GATEPASS = 0
                   AND E.BUSINESSCENTERID = :CenterId
                   AND TRUNC(D.CREATEDATE) BETWEEN TRUNC(SYSDATE) - 10 AND TRUNC(SYSDATE)
                 ORDER BY D.CREATEDATE DESC";
            return conn.Query<DispatchListItemModel>(sql, new { CenterId = businessCenterId }).ToList();
        }

        // old dalDispatch.GateCheck(TransactionID): STATUSID > 1 AND STATUSID < 14 AND ISOPEN = 0
        public long? FindDispatchForGate(string transactionId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT ID FROM (
                    SELECT D.ID
                      FROM DP_DISPATCH D
                     INNER JOIN EM_EMPLOYEE E ON E.USERID = D.CREATEDBY
                     INNER JOIN EM_EMPLOYEE SE ON SE.USERID = D.FROMUSER
                     INNER JOIN DP_ISSUETYPE I ON I.ID = D.ISSUETYPEID
                     WHERE D.STATUSID > 1 AND D.STATUSID < 14 AND D.ISOPEN = 0 AND D.TRANSACTIONID = :Tx
                     ORDER BY D.ID DESC)
                 WHERE ROWNUM = 1";
            return conn.ExecuteScalar<long?>(sql, new { Tx = transactionId });
        }

        // old InsertGatepass (Global): GATEPASS = 1, a closed step "<gate man's CENTERCODE>-Gate#<n>" with remarks "Gate Checked",
        // and a DP_GATECHECKING row. Time = local time of the sender location (UTC_OFFSET).
        // New: only while GATEPASS = 0, so the same leg is not gate passed twice.
        public bool GatePassDispatch(long dispatchId, string staffId, int gateNo)
        {
            using var conn = _databaseService.GetConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();

            int changed = conn.Execute(@"
                UPDATE DP_DISPATCH SET GATEPASS = 1
                 WHERE ID = :Id AND ISOPEN = 0 AND STATUSID > 1 AND STATUSID < 14 AND NVL(GATEPASS, 0) = 0",
                new { Id = dispatchId }, tran);
            if (changed != 1) { tran.Rollback(); return false; }

            DateTime local = LocalTimeOfDispatch(conn, tran, dispatchId);
            string? centerCode = conn.ExecuteScalar<string?>(@"
                SELECT B.CENTERCODE FROM EM_EMPLOYEE E
                 INNER JOIN BS_BUSINESSCENTER B ON B.ID = E.BUSINESSCENTERID
                 WHERE E.STAFFID = :StaffId AND ROWNUM = 1",
                new { StaffId = staffId }, tran);
            string step = (centerCode ?? "") + "-Gate#" + gateNo;

            conn.Execute(@"
                INSERT INTO DP_TRANSITS (ID, DISPATCHID, STEP, RECEIVEDATE, RELEASEDATE, REMARKS, UPDATEDATE, ISACTIVE, UPDATEDBY)
                VALUES (SQ_DP_TRANSITS.NEXTVAL, :Id, :Step, :Local1, :Local2, 'Gate Checked', :Local3, 0, :ActBy)",
                new { Id = dispatchId, Step = step, Local1 = local, Local2 = local, Local3 = local, ActBy = staffId }, tran);

            conn.Execute(@"
                INSERT INTO DP_GATECHECKING (ID, STAFFID, DISPATCHID, GATENO, PASSTIME)
                VALUES (SQ_DP_GATECHECKING.NEXTVAL, :StaffId, :Id, :GateNo, :Local1)",
                new { StaffId = staffId, Id = dispatchId, GateNo = gateNo, Local1 = local }, tran);

            tran.Commit();
            return true;
        }

        // old RejectDispatchGate(Global) from the gate list: close the open step ("Gate Rejected"), all steps inactive,
        // new step "Gate Rejected", STATUSID + 98. Only a dispatch that is in the gate list rule of the user's location.
        public bool GateRejectDispatch(long dispatchId, string staffId, int businessCenterId)
        {
            using var conn = _databaseService.GetConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();

            int? status = conn.ExecuteScalar<int?>(@"
                SELECT D.STATUSID
                  FROM DP_DISPATCH D
                 INNER JOIN EM_EMPLOYEE E ON E.STAFFID = D.CREATEDBY
                 WHERE D.ID = :Id AND D.STATUSID IN (2, 3, 6, 7) AND D.ISOPEN = 0 AND NVL(D.GATEPASS, 0) = 0
                   AND E.BUSINESSCENTERID = :CenterId AND ROWNUM = 1",
                new { Id = dispatchId, CenterId = businessCenterId }, tran);
            if (!status.HasValue) { tran.Rollback(); return false; }

            DateTime local = LocalTimeOfDispatch(conn, tran, dispatchId);
            CloseStepsAndAdd(conn, tran, dispatchId, staffId, "Gate Rejected", local, "Gate Rejected");

            int changed = conn.Execute(@"
                UPDATE DP_DISPATCH SET STATUSID = STATUSID + 98, UPDATEDBY = :ActBy, UPDATEDATE = :Local
                 WHERE ID = :Id AND STATUSID = :OldStatus",
                new { ActBy = staffId, Local = local, Id = dispatchId, OldStatus = status.Value }, tran);

            if (changed != 1) { tran.Rollback(); return false; }
            tran.Commit();
            return true;
        }

        // old dalGateEntry.GateCheck(GateEntryId): an entry that has not passed the gate yet
        public GateEntryModel? FindGateEntry(string gateEntryId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT G.ID, G.GATEENTRYID, G.VISTORNAME, G.VISTORMOBILE, G.VISITORADDRESS, G.VISTORCARDNO, G.INDATETIME, G.SCHEDULETIME,
                       E.EMPLOYEENAME, E.OFFICEMOBILE, B.CENTERNAME, D.DEPARTMENTCODE
                  FROM DP_GATEENTRY G
                  LEFT OUTER JOIN EM_EMPLOYEE E ON E.STAFFID = G.STAFFID
                  LEFT OUTER JOIN BS_BUSINESSCENTER B ON B.ID = E.BUSINESSCENTERID
                  LEFT OUTER JOIN BS_DEPARTMENT D ON D.ID = E.DEPARTMENTID
                 WHERE G.GATEPASSED = 0 AND G.GATEENTRYID = :Tx AND ROWNUM = 1";
            return conn.QueryFirstOrDefault<GateEntryModel>(sql, new { Tx = gateEntryId });
        }

        public List<GateEntryItemModel> GetGateEntryItems(long gateEntryId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT ID, ITEMNAME, QUANTITY, UNIT, REMARKS, ISPERSONAL, ISRETURNABLE, ATTACHMENTS
                  FROM DP_GATEENTRYITEMS
                 WHERE GATEENTRYID = :Id
                 ORDER BY ID";
            return conn.Query<GateEntryItemModel>(sql, new { Id = gateEntryId }).ToList();
        }

        // old dalGateEntry.GatePass (database time, as the old code)
        public bool GatePassEntry(string gateEntryId, string staffId, int gateNo)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                UPDATE DP_GATEENTRY
                   SET GATEPASSED = 1, GATENO = :GateNo, OUTDATETIME = SYSDATE, UPDATEDBY = :ActBy, UPDATEDATE = SYSDATE
                 WHERE GATEENTRYID = :Tx AND GATEPASSED = 0";
            return conn.Execute(sql, new { GateNo = gateNo, ActBy = staffId, Tx = gateEntryId }) > 0;
        }

        // ================================================================ PHASE 3: DISPATCH DESK

        // old GetDispatchSendingList: approved / returning (STATUSID 2, 6), not personal, not direct send,
        // created by a user of my location. (D.CREATEDBY -> E in ListSelect)
        private const string DeskSendRule = @"
                 NVL(D.ISOPEN, 0) = 0 AND" + ActiveTransit + @"AND D.STATUSID IN (2, 6) AND IT.ISPERSONAL = 0 AND D.DIRECTSEND = 0
                   AND E.BUSINESSCENTERID = :CenterId ";

        // old GetDispatchReceiveList (two parts of the UNION):
        //   TOUSER is a staff (TOUSER > 0)  -> the receiver works at my location
        //   no receiver staff (TOUSER = 0)  -> the sender (FROMUSER) works at my location
        // TOUSER is NVARCHAR2: compared as text ('0'), so a non numeric value cannot raise ORA-01722.
        private const string DeskReceiveRule = @"
                 NVL(D.ISOPEN, 0) = 0 AND" + ActiveTransit + @"AND D.STATUSID IN (3, 7) AND D.DIRECTSEND = 0
                   AND ( (D.TOUSER <> '0' AND EXISTS (SELECT 1 FROM EM_EMPLOYEE RE WHERE RE.USERID = D.TOUSER AND RE.BUSINESSCENTERID = :CenterId))
                      OR (D.TOUSER = '0' AND SE.BUSINESSCENTERID = :CenterId2) ) ";

        public List<DispatchListItemModel> GetDeskSendList(int businessCenterId)
        {
            using var conn = _databaseService.GetConnection();
            string sql = ListSelect + @"
                 WHERE" + DeskSendRule + @"
                   AND TRUNC(D.CREATEDATE) BETWEEN TRUNC(SYSDATE) - 7 AND TRUNC(SYSDATE)
                 ORDER BY D.CREATEDATE DESC";
            return conn.Query<DispatchListItemModel>(sql, new { CenterId = businessCenterId }).ToList();
        }

        public List<DispatchListItemModel> GetDeskReceiveList(int businessCenterId)
        {
            using var conn = _databaseService.GetConnection();
            string sql = ListSelect + @"
                 WHERE" + DeskReceiveRule + @"
                 ORDER BY D.CREATEDATE DESC";
            return conn.Query<DispatchListItemModel>(sql, new { CenterId = businessCenterId, CenterId2 = businessCenterId }).ToList();
        }

        // which desk action the user's location may do on this dispatch: "send", "receive" or null
        // (same rules as the two lists, without the 7 day window of the send list)
        public string? GetDeskAction(long dispatchId, int businessCenterId)
        {
            using var conn = _databaseService.GetConnection();
            return DeskActionOf(conn, null, dispatchId, businessCenterId);
        }

        // old dalApprover.UpdateTransit(id, remarks) from the desk (Send "OK" / Receive "Received" / remarks typed on the view):
        // close the open step, new step = DispatchStatus text, STATUSID + 1 (GATEPASS = 0 when the old status was 5).
        // Local time of the sender location ("one version with offset"). Returns the new step, or null when not allowed any more.
        public string? DeskForward(long dispatchId, string staffId, int businessCenterId, string expectedAction, string remarks)
        {
            using var conn = _databaseService.GetConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();

            if (DeskActionOf(conn, tran, dispatchId, businessCenterId) != expectedAction) { tran.Rollback(); return null; }
            string? step = ForwardOneStep(conn, tran, dispatchId, staffId, remarks);
            if (step == null) { tran.Rollback(); return null; }
            tran.Commit();
            return step;
        }

        // the body of old UpdateTransit (caller checks the rule and commits)
        private static string? ForwardOneStep(IDbConnection conn, IDbTransaction tran, long dispatchId, string staffId, string remarks)
        {
            int status = conn.ExecuteScalar<int>("SELECT STATUSID FROM DP_DISPATCH WHERE ID = :Id", new { Id = dispatchId }, tran);

            DateTime local = LocalTimeOfDispatch(conn, tran, dispatchId);

            conn.Execute(@"
                UPDATE DP_TRANSITS
                   SET RELEASEDATE = :Local1, REMARKS = :Remarks, ISACTIVE = 0, UPDATEDATE = :Local2, UPDATEDBY = :ActBy
                 WHERE ISACTIVE = 1 AND DISPATCHID = :Id",
                new { Local1 = local, Remarks = remarks, Local2 = local, ActBy = staffId, Id = dispatchId }, tran);

            string step = StatusText(conn, tran, dispatchId);            // from the old status, as the old code

            conn.Execute(@"INSERT INTO DP_TRANSITS (ID, RECEIVEDATE, DISPATCHID, STEP)
                           VALUES (SQ_DP_TRANSITS.NEXTVAL, :Local, :Id, :Step)",
                         new { Local = local, Id = dispatchId, Step = step }, tran);

            string gatePass = status == 5 ? ", GATEPASS = 0" : "";
            int changed = conn.Execute(@"
                UPDATE DP_DISPATCH SET STATUSID = STATUSID + 1" + gatePass + @", UPDATEDBY = :ActBy, UPDATEDATE = :Local
                 WHERE ID = :Id AND STATUSID = :OldStatus",
                new { ActBy = staffId, Local = local, Id = dispatchId, OldStatus = status }, tran);

            return changed == 1 ? step : null;
        }

        // old DispatchDesk btnSearch (InsertReceive): a closed step "<my CENTERCODE>-Dispatch", remarks "Received".
        // Only a log line on the dispatch - the status does not change (as the old code). Returns the dispatch id or null.
        public long? DeskScanReceive(string transactionId, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();

            long? id = conn.ExecuteScalar<long?>(@"
                SELECT ID FROM (SELECT ID FROM DP_DISPATCH WHERE TRANSACTIONID = :Tx AND NVL(ISOPEN, 0) = 0 ORDER BY ID DESC)
                 WHERE ROWNUM = 1", new { Tx = transactionId }, tran);
            if (!id.HasValue) { tran.Rollback(); return null; }

            string? centerCode = conn.ExecuteScalar<string?>(@"
                SELECT B.CENTERCODE FROM EM_EMPLOYEE E
                 INNER JOIN BS_BUSINESSCENTER B ON B.ID = E.BUSINESSCENTERID
                 WHERE E.STAFFID = :StaffId AND ROWNUM = 1", new { StaffId = staffId }, tran);
            DateTime local = LocalTimeOfDispatch(conn, tran, id.Value);

            conn.Execute(@"
                INSERT INTO DP_TRANSITS (ID, DISPATCHID, STEP, RECEIVEDATE, RELEASEDATE, REMARKS, UPDATEDATE, ISACTIVE, UPDATEDBY)
                VALUES (SQ_DP_TRANSITS.NEXTVAL, :Id, :Step, :Local1, :Local2, 'Received', :Local3, 0, :ActBy)",
                new { Id = id.Value, Step = (centerCode ?? "") + "-Dispatch", Local1 = local, Local2 = local, Local3 = local, ActBy = staffId }, tran);

            tran.Commit();
            return id;
        }

        // ================================================================ PHASE 4: RECEIVE + RETURN

        // old GetReceiveList: I am the receiver (TOUSER), STATUSID 2 / 4 / 8, not personal
        private const string MyReceiveRule = @"
                 NVL(D.ISOPEN, 0) = 0 AND" + ActiveTransit + @"AND D.STATUSID IN (2, 4, 8) AND IT.ISPERSONAL = 0 AND D.TOUSER = :Me1 ";

        // old GetReturnList: I am the sender (FROMUSER), returnable, not personal, and
        //   STATUSID 5 + not direct  |  STATUSID 3 + direct send
        private const string MyReturnRule = @"
                 NVL(D.ISOPEN, 0) = 0 AND" + ActiveTransit + @"AND IT.ISPERSONAL = 0 AND IT.RETURNABLE = 1 AND D.FROMUSER = :Me2
                   AND ( (D.STATUSID = 5 AND D.DIRECTSEND = 0) OR (D.STATUSID = 3 AND D.DIRECTSEND = 1) ) ";

        // old list also had: TRUNC(D.CREATEDATE) >= 01/08/2024 (start date of this flow), oldest first
        public List<DispatchListItemModel> GetMyReceiveList(string staffId)
        {
            using var conn = _databaseService.GetConnection();
            string sql = ListSelect + @"
                 WHERE" + MyReceiveRule + @"
                   AND TRUNC(D.CREATEDATE) >= DATE '2024-08-01'
                 ORDER BY D.CREATEDATE";
            return conn.Query<DispatchListItemModel>(sql, new { Me1 = staffId }).ToList();
        }

        public List<DispatchListItemModel> GetMyReturnList(string staffId)
        {
            using var conn = _databaseService.GetConnection();
            string sql = ListSelect + @"
                 WHERE" + MyReturnRule + @"
                 ORDER BY D.CREATEDATE";
            return conn.Query<DispatchListItemModel>(sql, new { Me2 = staffId }).ToList();
        }

        // "receive", "return" or null for this dispatch and this user
        public string? GetMyAction(long dispatchId, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return MyActionOf(conn, null, dispatchId, staffId);
        }

        // old Receive.aspx btnReceiveEdit ("Received") / ReceiverView btnSaveandSend (remarks typed) = UpdateTransit
        public string? MyReceive(long dispatchId, string staffId, string remarks)
        {
            using var conn = _databaseService.GetConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();

            if (MyActionOf(conn, tran, dispatchId, staffId) != "receive") { tran.Rollback(); return null; }
            string? step = ForwardOneStep(conn, tran, dispatchId, staffId, remarks);
            if (step == null) { tran.Rollback(); return null; }
            tran.Commit();
            return step;
        }

        // old NormalReturn  : step = DirectSendStatus(id, 5), DIRECTSEND = 0, STATUSID = 6, GATEPASS = 0
        // old DirectReturn  : step = DirectSendStatus(id, 3), DIRECTSEND = 1, STATUSID = 4, GATEPASS = 0
        // Both: close the open step with the remarks, all steps inactive, add the new step. Returns the new step or null.
        public string? ReturnDispatch(long dispatchId, string staffId, string remarks, bool direct)
        {
            using var conn = _databaseService.GetConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();

            if (MyActionOf(conn, tran, dispatchId, staffId) != "return") { tran.Rollback(); return null; }
            int status = conn.ExecuteScalar<int>("SELECT STATUSID FROM DP_DISPATCH WHERE ID = :Id", new { Id = dispatchId }, tran);

            DateTime local = LocalTimeOfDispatch(conn, tran, dispatchId);
            string step = StatusText(conn, tran, dispatchId, direct ? 3 : 5);   // before DIRECTSEND changes, as the old code
            CloseStepsAndAdd(conn, tran, dispatchId, staffId, remarks, local, step);

            int changed = conn.Execute(@"
                UPDATE DP_DISPATCH SET DIRECTSEND = :Direct, STATUSID = :NewStatus, GATEPASS = 0, UPDATEDBY = :ActBy, UPDATEDATE = :Local
                 WHERE ID = :Id AND STATUSID = :OldStatus",
                new { Direct = direct ? 1 : 0, NewStatus = direct ? 4 : 6, ActBy = staffId, Local = local, Id = dispatchId, OldStatus = status }, tran);

            if (changed != 1) { tran.Rollback(); return null; }
            tran.Commit();
            return step;
        }

        private static string? MyActionOf(IDbConnection conn, IDbTransaction? tran, long dispatchId, string staffId)
        {
            const string from = @"
                SELECT COUNT(1)
                  FROM DP_DISPATCH D
                 INNER JOIN EM_EMPLOYEE E ON E.USERID = D.CREATEDBY
                 INNER JOIN EM_EMPLOYEE SE ON SE.USERID = D.FROMUSER
                 INNER JOIN DP_ISSUETYPE IT ON IT.ID = D.ISSUETYPEID
                 WHERE D.ID = :Id AND";
            var p = new { Id = dispatchId, Me1 = staffId, Me2 = staffId };
            if (conn.ExecuteScalar<int>(from + MyReceiveRule, p, tran) > 0) return "receive";
            if (conn.ExecuteScalar<int>(from + MyReturnRule, p, tran) > 0) return "return";
            return null;
        }

        // ================================================================ PHASE 5: TRACKING

        // old dalDispatch.GetTrackingList: my dispatches (sender or receiver) that are not completed, not personal
        public List<TrackingListItemModel> GetTrackingList(string staffId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT D.ID, D.TRANSACTIONID, D.CREATEDATE, D.STATUSID, IT.ISSUETYPENAME,
                       E.EMPLOYEENAME, E.OFFICEMOBILE, B.CENTERCODE,
                       D.BUSINESSCENTER, D.RECEIVERNAME, D.RECEIVERMOBILE,
                       (SELECT MAX(T.STEP) KEEP (DENSE_RANK LAST ORDER BY T.ID)
                          FROM DP_TRANSITS T WHERE T.DISPATCHID = D.ID AND T.ISACTIVE = 1) CURRENTSTEP
                  FROM DP_DISPATCH D
                 INNER JOIN EM_EMPLOYEE E ON E.STAFFID = D.FROMUSER
                 INNER JOIN BS_BUSINESSCENTER B ON B.ID = E.BUSINESSCENTERID
                 INNER JOIN BS_DEPARTMENT DE ON DE.ID = E.DEPARTMENTID
                 INNER JOIN DP_ISSUETYPE IT ON IT.ID = D.ISSUETYPEID
                 WHERE D.ISOPEN = 0 AND D.COMPLETED = 0 AND IT.ISPERSONAL = 0
                   AND (D.FROMUSER = :S1 OR D.TOUSER = :S2) AND D.CREATEDATE >= SYSDATE - 30
                 ORDER BY D.CREATEDATE DESC";
            return conn.Query<TrackingListItemModel>(sql, new { S1 = staffId, S2 = staffId }).ToList();
        }

        // old dalDispatch.TrakckingDetails(TransactionId): the dispatch + the first box "<send CENTERCODE>-<send DEPARTMENTCODE>-<SENDNAME>"
        // (send location / department of the dispatch itself, as the old query)
        public TrackingHeaderModel? FindForTracking(string transactionId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT ID, TRANSACTIONID, STATUSID, CREATEDATE, ISSUETYPENAME, SENDNAME, CENTERCODE, DEPARTMENTCODE,
                       BUSINESSCENTER, RECEIVERNAME, COMPLETED
                  FROM (SELECT D.ID, D.TRANSACTIONID, D.STATUSID, D.CREATEDATE, IT.ISSUETYPENAME, D.SENDNAME, B.CENTERCODE, DP.DEPARTMENTCODE,
                               D.BUSINESSCENTER, D.RECEIVERNAME, D.COMPLETED
                          FROM DP_DISPATCH D
                         INNER JOIN DP_ISSUETYPE IT ON IT.ID = D.ISSUETYPEID
                         INNER JOIN BS_BUSINESSCENTER B ON B.ID = D.SENDBUSINESSCENTERID
                         INNER JOIN BS_DEPARTMENT DP ON DP.ID = D.SENDDEPARTMENTID
                         WHERE D.TRANSACTIONID = :Tx AND NVL(D.ISOPEN, 0) = 0
                         ORDER BY D.ID DESC)
                 WHERE ROWNUM = 1";
            return conn.QueryFirstOrDefault<TrackingHeaderModel>(sql, new { Tx = transactionId });
        }

        // ================================================================ PHASE 3: TRANSPORT

        public List<TransportModel> GetTransportTypes()
        {
            using var conn = _databaseService.GetConnection();
            return conn.Query<TransportModel>("SELECT ID, TRANSPORTNAME, ISEXTERNAL FROM DP_TRANSPORT ORDER BY TRANSPORTNAME").ToList();
        }

        public TransportModel? GetTransportType(int id)
        {
            using var conn = _databaseService.GetConnection();
            return conn.QueryFirstOrDefault<TransportModel>("SELECT ID, TRANSPORTNAME, ISEXTERNAL FROM DP_TRANSPORT WHERE ID = :Id", new { Id = id });
        }

        public List<CurrencyModel> GetCurrencies()
        {
            using var conn = _databaseService.GetConnection();
            return conn.Query<CurrencyModel>(
                "SELECT ID, CURRENCYNAME, CURRENCYCODE, NVL(ISDEFAULT, 0) ISDEFAULT FROM ASSET_CURRENCY ORDER BY NVL(ISDEFAULT, 0) DESC, CURRENCYNAME").ToList();
        }

        // old CheckDuplicate (exact name); exceptId > 0 -> ignore that row (edit)
        public bool TransportNameExists(string name, int exceptId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<int>("SELECT COUNT(1) FROM DP_TRANSPORT WHERE TRANSPORTNAME = :Name AND ID <> :ExceptId",
                                           new { Name = name, ExceptId = exceptId }) > 0;
        }

        // old dalTransport.Insert / Update (ISEXTERNAL is NVARCHAR2: '0' / '1' as the old code wrote it)
        public int InsertTransport(string name, bool isExternal)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute("INSERT INTO DP_TRANSPORT (ID, TRANSPORTNAME, ISEXTERNAL) VALUES (SQ_DP_TRANSPORT.NEXTVAL, :Name, :Ext)",
                                new { Name = name, Ext = isExternal ? "1" : "0" });
        }

        public int UpdateTransport(int id, string name, bool isExternal)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute("UPDATE DP_TRANSPORT SET TRANSPORTNAME = :Name, ISEXTERNAL = :Ext WHERE ID = :Id",
                                new { Name = name, Ext = isExternal ? "1" : "0", Id = id });
        }

        // old InsertTransportDetails (send view of the desk). COST is INTEGER in DP_TRANSPORTDETAILS.
        // Only while the dispatch is in the send rule of the user's location.
        public bool AddTransportDetail(long dispatchId, int businessCenterId, int transportId, int currencyId, long cost,
                                       string driver, string deliveryMan, string mobile)
        {
            using var conn = _databaseService.GetConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();

            if (DeskActionOf(conn, tran, dispatchId, businessCenterId) != "send") { tran.Rollback(); return false; }
            bool known = conn.ExecuteScalar<int>(@"
                SELECT (SELECT COUNT(1) FROM DP_TRANSPORT WHERE ID = :TransportId) * (SELECT COUNT(1) FROM ASSET_CURRENCY WHERE ID = :CurrencyId) FROM DUAL",
                new { TransportId = transportId, CurrencyId = currencyId }, tran) > 0;
            if (!known) { tran.Rollback(); return false; }

            conn.Execute(@"
                INSERT INTO DP_TRANSPORTDETAILS (ID, DISPATCHID, TRANSPORTID, DRIVER, DELIVERYMAN, MOBILE, CURRENCYID, COST)
                VALUES (SQ_DP_TRANSPORTDETAILS.NEXTVAL, :Id, :TransportId, :Driver, :DeliveryMan, :Mobile, :CurrencyId, :Cost)",
                new { Id = dispatchId, TransportId = transportId, Driver = driver, DeliveryMan = deliveryMan, Mobile = mobile, CurrencyId = currencyId, Cost = cost }, tran);
            tran.Commit();
            return true;
        }

        // ================================================================ HELPERS

        private static string? DeskActionOf(IDbConnection conn, IDbTransaction? tran, long dispatchId, int businessCenterId)
        {
            const string from = @"
                SELECT COUNT(1)
                  FROM DP_DISPATCH D
                 INNER JOIN EM_EMPLOYEE E ON E.USERID = D.CREATEDBY
                 INNER JOIN EM_EMPLOYEE SE ON SE.USERID = D.FROMUSER
                 INNER JOIN DP_ISSUETYPE IT ON IT.ID = D.ISSUETYPEID
                 WHERE D.ID = :Id AND";
            var p = new { Id = dispatchId, CenterId = businessCenterId, CenterId2 = businessCenterId };
            if (conn.ExecuteScalar<int>(from + DeskSendRule, p, tran) > 0) return "send";
            if (conn.ExecuteScalar<int>(from + DeskReceiveRule, p, tran) > 0) return "receive";
            return null;
        }

        private static bool IsOwnOpenDraft(IDbConnection conn, IDbTransaction tran, long dispatchId, string staffId) =>
            conn.ExecuteScalar<int>("SELECT COUNT(1) FROM DP_DISPATCH WHERE ID = :Id AND ISOPEN = 1 AND CREATEDBY = :StaffId",
                                    new { Id = dispatchId, StaffId = staffId }, tran) > 0;

        // same filter as the approver's pending list -> STATUSID, or null
        private static int? PendingStatus(IDbConnection conn, IDbTransaction? tran, long dispatchId, string approverStaffId)
        {
            string sql = @"
                SELECT D.STATUSID
                  FROM DP_DISPATCH D
                 INNER JOIN EM_EMPLOYEE E ON E.USERID = D.CREATEDBY
                 INNER JOIN EM_EMPLOYEE SE ON SE.USERID = D.FROMUSER
                 INNER JOIN DP_ISSUETYPE IT ON IT.ID = D.ISSUETYPEID
                 WHERE D.ID = :Id AND NVL(D.ISOPEN, 0) = 0 AND" + ActiveTransit + "AND" + ApproverOfCreator + @"
                            AND A.PRIORITY = D.STATUSID)
                   AND ROWNUM = 1";
            return conn.ExecuteScalar<int?>(sql, new { Id = dispatchId, Approver = approverStaffId }, tran);
        }

        // same filter as the approver's rejected list -> STATUSID, or null
        private static int? RejectedStatus(IDbConnection conn, IDbTransaction? tran, long dispatchId, string approverStaffId)
        {
            string sql = @"
                SELECT D.STATUSID
                  FROM DP_DISPATCH D
                 INNER JOIN EM_EMPLOYEE E ON E.USERID = D.CREATEDBY
                 INNER JOIN EM_EMPLOYEE SE ON SE.USERID = D.FROMUSER
                 INNER JOIN DP_ISSUETYPE IT ON IT.ID = D.ISSUETYPEID
                 WHERE D.ID = :Id AND" + ActiveTransit + "AND" + ApproverOfCreator + @"
                            AND A.PRIORITY + 98 = D.STATUSID)
                   AND ROWNUM = 1";
            return conn.ExecuteScalar<int?>(sql, new { Id = dispatchId, Approver = approverStaffId }, tran);
        }

        // old RejectDispatch / ApproveRejected: close the open step with the remarks, make every step inactive, add the new step
        private static void CloseStepsAndAdd(IDbConnection conn, IDbTransaction tran, long dispatchId, string by, string remarks, DateTime local, string newStep)
        {
            conn.Execute(@"
                UPDATE DP_TRANSITS SET RELEASEDATE = :Local1, REMARKS = :Remarks, UPDATEDATE = :Local2, UPDATEDBY = :ActBy
                 WHERE ISACTIVE = 1 AND DISPATCHID = :Id",
                new { Local1 = local, Remarks = remarks, Local2 = local, ActBy = by, Id = dispatchId }, tran);
            conn.Execute("UPDATE DP_TRANSITS SET ISACTIVE = 0 WHERE DISPATCHID = :Id", new { Id = dispatchId }, tran);
            conn.Execute(@"INSERT INTO DP_TRANSITS (ID, RECEIVEDATE, DISPATCHID, STEP)
                           VALUES (SQ_DP_TRANSITS.NEXTVAL, :Local, :Id, :Step)",
                         new { Local = local, Id = dispatchId, Step = newStep }, tran);
        }

        // "Global" time: database time + (UTC_OFFSET of the location - 6) hours. No offset / no location -> SYSDATE.
        private static DateTime LocalTimeOfCenter(IDbConnection conn, IDbTransaction? tran, int businessCenterId) =>
            conn.ExecuteScalar<DateTime>(
                "SELECT SYSDATE + (NVL(MAX(BC.UTC_OFFSET), 6) - 6) / 24 FROM BS_BUSINESSCENTER BC WHERE BC.ID = :CenterId",
                new { CenterId = businessCenterId }, tran);

        private static DateTime LocalTimeOfDispatch(IDbConnection conn, IDbTransaction? tran, long dispatchId) =>
            conn.ExecuteScalar<DateTime>(@"
                SELECT SYSDATE + (NVL(MAX(BC.UTC_OFFSET), 6) - 6) / 24
                  FROM DP_DISPATCH D
                  LEFT OUTER JOIN BS_BUSINESSCENTER BC ON BC.ID = D.SENDBUSINESSCENTERID
                 WHERE D.ID = :Id",
                new { Id = dispatchId }, tran);

        // statusOverride = old dalApprover.DirectSendStatus(id, status): the same text, built for a given status
        // (all other values, DIRECTSEND too, are the current ones of the dispatch)
        private static string StatusText(IDbConnection conn, IDbTransaction tran, long dispatchId, int? statusOverride = null)
        {
            const string sql = @"
                SELECT NVL(D.STATUSID, 0) STATUSID, NVL(D.DIRECTSEND, 0) DIRECTSEND,
                       NVL(IT.ISPERSONAL, 0) ISPERSONAL, NVL(IT.ISEXTERNAL, 0) ISEXTERNAL, NVL(IT.RETURNABLE, 0) RETURNABLE,
                       D.SENDNAME, B.CENTERCODE, DP.DEPARTMENTCODE, D.BUSINESSCENTER, D.RECEIVERDEPARTMENT, D.RECEIVERNAME
                  FROM DP_DISPATCH D
                 INNER JOIN DP_ISSUETYPE IT ON IT.ID = D.ISSUETYPEID
                 INNER JOIN BS_BUSINESSCENTER B ON B.ID = D.SENDBUSINESSCENTERID
                 INNER JOIN BS_DEPARTMENT DP ON DP.ID = D.SENDDEPARTMENTID
                 WHERE D.ID = :Id";
            var s = conn.QueryFirstOrDefault<DispatchStatusSourceModel>(sql, new { Id = dispatchId }, tran);
            if (s != null && statusOverride.HasValue) s.STATUSID = statusOverride.Value;
            return DispatchStatusText.For(s);
        }
    }

    /// <summary>
    /// Old dalApprover.DispatchStatus(id): the text of the NEXT step, from the CURRENT status of the dispatch.
    /// Kept line by line (also the places where a later "if" overwrites the "Personal-" text, as in the old code).
    /// </summary>
    public static class DispatchStatusText
    {
        public static string For(DispatchStatusSourceModel? d)
        {
            if (d == null) return "";                                         // old code: exception -> ""

            string cc = d.CENTERCODE ?? "", dc = d.DEPARTMENTCODE ?? "", sn = d.SENDNAME ?? "";
            string bc = d.BUSINESSCENTER ?? "", rd = d.RECEIVERDEPARTMENT ?? "", rn = d.RECEIVERNAME ?? "";
            bool personal = d.ISPERSONAL == 1, external = d.ISEXTERNAL == 1, direct = d.DIRECTSEND == 1;
            string status = "";

            switch (d.STATUSID)
            {
                case 0:
                    return "Approved";
                case 1:
                    if (personal) status = "Personal-" + sn;
                    if (external) status = direct ? "External Direct Sent" : cc + "-Dispatch";
                    else status = direct ? bc + "-" + rd + "-" + rn : cc + "-Dispatch";
                    return status;
                case 2:
                    if (personal) return "Personal-" + sn;
                    return direct ? bc + "-" + rd + "-" + rn + " Received" : "Transport";
                case 3:
                    if (personal) status = "Personal-" + sn;
                    if (external) status = cc + "-Dispatch Received";
                    else status = direct ? cc + "-" + dc + "-" + sn : bc + "-Dispatch";
                    return status;
                case 4:
                    if (personal) status = "Personal-" + sn;
                    if (external) status = cc + "-" + dc + "-" + sn;
                    else status = direct ? cc + "-" + dc + "-" + sn + "-Returned" : bc + "-" + rd + "-" + rn;
                    return status;
                case 5:
                    return bc + "-Returning Dispatch";
                case 6:
                    return "Returning Transport";
                case 7:
                    return cc + "- Returned to Dispatch";
                case 8:
                    return cc + "-" + dc + "-" + sn;
                default:
                    return "Approved";
            }
        }
    }
}
