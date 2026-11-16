// Repositories/Dispatch/DispatchRepository.GateEntry.cs
// Dispatch phase 8 - visitor gate entry + notice board. Port of YA dalGateEntry, dalNoticeBorad and
// dalDispatchReport.GetByGeteEntryDetails / GetByGeteEntryReturnItem. Same tables, columns and sequences as YA.
// Differences to the old code:
//   * every value is a bind parameter (the old code concatenated the text boxes into the SQL)
//   * a draft / item can only be changed by the user who owns the open draft (the old pages deleted any row by id)
//   * the notice search "a OR b OR c" is in brackets (the old criteria ignored the date / type filters for most rows)
//   * reports: TRUNC(date) BETWEEN from AND to (whole "to" day), drafts (ISOPEN = 1) are not listed
// NOTE: bind variable names must not be Oracle reserved words, and every occurrence gets its own name (file convention).
using AlphaCare.Models;
using Dapper;
using System.Data;

namespace Admin360.Repositories
{
    public partial class DispatchRepository
    {
        // ================================================================ VISITOR GATE ENTRY

        // old CheckGateEntry: one open entry (ISOPEN = 1) per user collects the items until Save
        public long GetOrCreateGateEntryDraft(string staffId)
        {
            using var conn = _databaseService.GetConnection();
            const string find = "SELECT MIN(ID) FROM DP_GATEENTRY WHERE ISOPEN = 1 AND CREATEDBY = :StaffId";

            long? id = conn.ExecuteScalar<long?>(find, new { StaffId = staffId });
            if (id.HasValue) return id.Value;

            conn.Execute("INSERT INTO DP_GATEENTRY (ID, ISOPEN, CREATEDBY) VALUES (SQ_DP_GATEENTRY.NEXTVAL, 1, :StaffId)", new { StaffId = staffId });
            return conn.ExecuteScalar<long>(find, new { StaffId = staffId });
        }

        public bool IsOwnGateEntryDraft(long entryId, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<int>("SELECT COUNT(1) FROM DP_GATEENTRY WHERE ID = :Id AND ISOPEN = 1 AND CREATEDBY = :StaffId",
                                           new { Id = entryId, StaffId = staffId }) > 0;
        }

        public GateEntryItemModel? GetGateEntryItem(long itemId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT ID, ITEMNAME, QUANTITY, UNIT, REMARKS, ISPERSONAL, ISRETURNABLE, ATTACHMENTS
                  FROM DP_GATEENTRYITEMS WHERE ID = :Id";
            return conn.QueryFirstOrDefault<GateEntryItemModel>(sql, new { Id = itemId });
        }

        public long? GetGateEntryIdOfItem(long itemId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<long?>("SELECT MAX(GATEENTRYID) FROM DP_GATEENTRYITEMS WHERE ID = :Id", new { Id = itemId });
        }

        // old InsertGateEntryItems (no merge of equal names, as the old page). 1 = added, 0 = not the user's open draft
        public int AddGateEntryItem(long entryId, string staffId, string itemName, decimal quantity, string unit, string remarks,
                                    bool sample, bool returnable, string attachment)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                INSERT INTO DP_GATEENTRYITEMS (ID, QUANTITY, UNIT, ITEMNAME, GATEENTRYID, ATTACHMENTS, REMARKS, ISRETURNABLE, ISPERSONAL)
                SELECT SQ_DP_GATEENTRYITEMS.NEXTVAL, :Qty, :Unit, :ItemName, :EntryId, :Attachment, :Remarks, :Returnable, :Sample
                  FROM DUAL
                 WHERE EXISTS (SELECT 1 FROM DP_GATEENTRY G WHERE G.ID = :EntryId2 AND G.ISOPEN = 1 AND G.CREATEDBY = :StaffId)";
            return conn.Execute(sql, new
            {
                Qty = quantity, Unit = unit, ItemName = itemName, EntryId = entryId, Attachment = attachment, Remarks = remarks,
                Returnable = returnable ? 1 : 0, Sample = sample ? 1 : 0, EntryId2 = entryId, StaffId = staffId
            });
        }

        // only an item of the user's OWN OPEN entry (the old page deleted any DP_GATEENTRYITEMS row by id)
        public int DeleteGateEntryItem(long itemId, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                DELETE FROM DP_GATEENTRYITEMS I
                 WHERE I.ID = :Id
                   AND EXISTS (SELECT 1 FROM DP_GATEENTRY G WHERE G.ID = I.GATEENTRYID AND G.ISOPEN = 1 AND G.CREATEDBY = :StaffId)";
            return conn.Execute(sql, new { Id = itemId, StaffId = staffId });
        }

        public List<string> SearchGateEntryItems(string term)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT DISTINCT ITEMNAME FROM DP_GATEENTRYITEMS
                 WHERE LOWER(ITEMNAME) LIKE '%' || :Term || '%' AND ROWNUM <= 20";
            return conn.Query<string>(sql, new { Term = term.Trim().ToLowerInvariant() }).ToList();
        }

        // old dalGateEntry.Update. GATEENTRYID = "R" + location of the logged in user (3 digits, old padding) + yy + MM + dd
        // + a 6 digit number per location and day. Database time (the old page used the web server clock + SYSDATE).
        // DP_GATEENTRY has no unique index on GATEENTRYID, so after the commit a number that two people got at the same
        // moment is given again to the later entry.
        public string? SubmitGateEntry(SaveGateEntryRequest r, string staffId, string userId, int userBusinessCenterId)
        {
            string office = userBusinessCenterId >= 100 ? userBusinessCenterId.ToString()
                          : userBusinessCenterId >= 10 ? "0" + userBusinessCenterId
                          : "00" + userBusinessCenterId;

            using var conn = _databaseService.GetConnection();
            conn.Open();
            string prefix;
            using (var tran = conn.BeginTransaction())
            {
                prefix = "R" + office + conn.ExecuteScalar<string>("SELECT TO_CHAR(SYSDATE, 'YYMMDD') FROM DUAL", transaction: tran);

                string update = @"
                    UPDATE DP_GATEENTRY
                       SET STAFFID = :HostStaffId, BUSINESSCENTERID = :HostCenter, DEPARTMENTID = :HostDept, SCHEDULETIME = NULL,
                           VISITORSTAFFID = :VisitorStaffId, VISTORNAME = :VisitorName, VISITORADDRESS = :VisitorAddress,
                           VISTORMOBILE = :VisitorMobile, GATENO = 1, VISTORCARDNO = :CardNo, ISOPEN = 0,
                           UPDATEDBY = :UpdatedBy, UPDATEDATE = SYSDATE, INDATETIME = SYSDATE, ATTACHMENTS = NULL,
                           GATEENTRYID = " + NextGateEntryNumber("P1", "P2", "P3", "P4") + @"
                     WHERE ID = :Id AND ISOPEN = 1 AND CREATEDBY = :StaffId";

                int changed = conn.Execute(update, new
                {
                    HostStaffId = r.HostStaffId, HostCenter = r.HostCenterId, HostDept = r.HostDepartmentId,
                    VisitorStaffId = r.VisitorStaffId, VisitorName = r.VisitorName, VisitorAddress = r.VisitorAddress,
                    VisitorMobile = r.VisitorMobile, CardNo = r.CardNo,
                    UpdatedBy = userId,                       // old code: SessionName.UserId
                    P1 = prefix, P2 = prefix, P3 = prefix, P4 = prefix,
                    Id = r.EntryId, StaffId = staffId
                }, tran);

                if (changed != 1) { tran.Rollback(); return null; }
                tran.Commit();
            }

            // same number given twice at the same moment -> the later entry (higher ID) gets the next free number
            for (int attempt = 0; attempt < 3; attempt++)
            {
                string? tx = conn.ExecuteScalar<string?>("SELECT GATEENTRYID FROM DP_GATEENTRY WHERE ID = :Id", new { Id = r.EntryId });
                if (tx == null) return null;
                bool clash = conn.ExecuteScalar<int>(
                    "SELECT COUNT(1) FROM DP_GATEENTRY WHERE GATEENTRYID = :Tx AND ID < :Id", new { Tx = tx, Id = r.EntryId }) > 0;
                if (!clash) return tx;

                _logger.LogWarning("GATEENTRYID {Tx} used twice, giving entry {Id} a new number.", tx, r.EntryId);
                conn.Execute("UPDATE DP_GATEENTRY SET GATEENTRYID = " + NextGateEntryNumber("P1", "P2", "P3", "P4") + " WHERE ID = :Id",
                             new { P1 = prefix, P2 = prefix, P3 = prefix, P4 = prefix, Id = r.EntryId });
            }
            return conn.ExecuteScalar<string?>("SELECT GATEENTRYID FROM DP_GATEENTRY WHERE ID = :Id", new { Id = r.EntryId });
        }

        // prefix + (highest 6 digit number of the same prefix + 1)
        private static string NextGateEntryNumber(string p1, string p2, string p3, string p4) => $@"
            (SELECT :{p1} || TRIM(TO_CHAR(NVL(MAX(TO_NUMBER(SUBSTR(X.GATEENTRYID, LENGTH(:{p2}) + 1, 6))), 0) + 1, '000000'))
               FROM DP_GATEENTRY X
              WHERE X.GATEENTRYID LIKE :{p3} || '%' AND LENGTH(X.GATEENTRYID) = LENGTH(:{p4}) + 6
                AND REGEXP_LIKE(SUBSTR(X.GATEENTRYID, -6), '^[0-9]{{6}}$'))";

        private const string GateEntrySelect = @"
            SELECT G.ID, G.GATEENTRYID, G.STAFFID, G.VISTORNAME, G.VISTORMOBILE, G.VISITORADDRESS, G.VISTORCARDNO, G.VISITORSTAFFID,
                   G.INDATETIME, G.OUTDATETIME, G.GATENO, NVL(G.GATEPASSED, 0) GATEPASSED, NVL(G.ISRETURNED, 0) ISRETURNED, G.CREATEDBY,
                   E.EMPLOYEENAME, E.OFFICEMOBILE, B.CENTERNAME, D.DEPARTMENTCODE, D.DEPARTMENTNAME, C.EMPLOYEENAME CREATEDBYNAME
              FROM DP_GATEENTRY G
              LEFT OUTER JOIN EM_EMPLOYEE E ON E.STAFFID = G.STAFFID
              LEFT OUTER JOIN BS_BUSINESSCENTER B ON B.ID = NVL(G.BUSINESSCENTERID, E.BUSINESSCENTERID)
              LEFT OUTER JOIN BS_DEPARTMENT D ON D.ID = NVL(G.DEPARTMENTID, E.DEPARTMENTID)
              LEFT OUTER JOIN EM_EMPLOYEE C ON C.STAFFID = G.CREATEDBY ";

        // old GetEntryDetails / GetByGeteEntryDetails (a saved entry; the old print page also needed GATEPASSED = 0)
        public GateEntryDetailModel? GetGateEntry(long entryId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.QueryFirstOrDefault<GateEntryDetailModel>(GateEntrySelect + " WHERE G.ID = :Id AND NVL(G.ISOPEN, 0) = 0",
                                                                  new { Id = entryId });
        }

        public bool IsGateEntryPerson(long entryId, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM DP_GATEENTRY WHERE ID = :Id AND (CREATEDBY = :S1 OR STAFFID = :S2)",
                new { Id = entryId, S1 = staffId, S2 = staffId }) > 0;
        }

        private const string ReturnableSample =
            " EXISTS (SELECT 1 FROM DP_GATEENTRYITEMS I WHERE I.GATEENTRYID = G.ID AND I.ISRETURNABLE = 1 AND I.ISPERSONAL = 1) ";

        // old GetSampleReturnable (= VW_SAMPLERETURNABLE for the logged in host)
        public List<GateEntryListItemModel> GetMyReturnableSamples(string staffId)
        {
            using var conn = _databaseService.GetConnection();
            string sql = @"
                SELECT G.ID, G.GATEENTRYID, G.VISTORNAME, G.VISTORMOBILE, G.VISITORADDRESS, G.INDATETIME,
                       (SELECT COUNT(1) FROM DP_GATEENTRYITEMS I WHERE I.GATEENTRYID = G.ID AND I.ISRETURNABLE = 1 AND I.ISPERSONAL = 1) SAMPLES
                  FROM DP_GATEENTRY G
                 WHERE G.STAFFID = :StaffId AND NVL(G.ISOPEN, 0) = 0 AND NVL(G.ISRETURNED, 0) = 0 AND" + ReturnableSample + @"
                 ORDER BY G.INDATETIME DESC";
            return conn.Query<GateEntryListItemModel>(sql, new { StaffId = staffId }).ToList();
        }

        // old SampleReturn: ISRETURNED = 1. Only the host, only once.
        public bool ReturnSample(long entryId, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            string sql = @"
                UPDATE DP_GATEENTRY G SET G.ISRETURNED = 1, G.UPDATEDBY = :ActBy, G.UPDATEDATE = SYSDATE
                 WHERE G.ID = :Id AND G.STAFFID = :StaffId AND NVL(G.ISOPEN, 0) = 0 AND NVL(G.ISRETURNED, 0) = 0 AND" + ReturnableSample;
            return conn.Execute(sql, new { ActBy = staffId, Id = entryId, StaffId = staffId }) == 1;
        }

        // ================================================================ NOTICE BOARD

        public List<DispatchTypeModel> GetDispatchTypes()
        {
            using var conn = _databaseService.GetConnection();
            return conn.Query<DispatchTypeModel>("SELECT ID, DTYPENAME FROM DP_DISPATCHTYPE ORDER BY ID DESC").ToList();
        }

        // old CheckDispatch: one open notice (ISOPEN = 1) per user collects the items until Save
        public long GetOrCreateNoticeDraft(string staffId)
        {
            using var conn = _databaseService.GetConnection();
            const string find = "SELECT MIN(ID) FROM DP_NOTICEBOARD WHERE ISOPEN = 1 AND CREATEDBY = :StaffId";

            long? id = conn.ExecuteScalar<long?>(find, new { StaffId = staffId });
            if (id.HasValue) return id.Value;

            conn.Execute("INSERT INTO DP_NOTICEBOARD (ID, ISOPEN, CREATEDBY) VALUES (SQ_DP_NOTICEBOARD.NEXTVAL, 1, :StaffId)", new { StaffId = staffId });
            return conn.ExecuteScalar<long>(find, new { StaffId = staffId });
        }

        public bool IsOwnNoticeDraft(long noticeId, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<int>("SELECT COUNT(1) FROM DP_NOTICEBOARD WHERE ID = :Id AND ISOPEN = 1 AND CREATEDBY = :StaffId",
                                           new { Id = noticeId, StaffId = staffId }) > 0;
        }

        public NoticeItemModel? GetNoticeItem(long itemId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.QueryFirstOrDefault<NoticeItemModel>(
                "SELECT ID, DISPATCHID, ITEMNAME, QUANTITY, UNIT, NOTES, ATTACHMENT FROM DP_NOTICEBOARDITEM WHERE ID = :Id", new { Id = itemId });
        }

        public List<NoticeItemModel> GetNoticeItems(long noticeId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Query<NoticeItemModel>(
                "SELECT ID, DISPATCHID, ITEMNAME, QUANTITY, UNIT, NOTES, ATTACHMENT FROM DP_NOTICEBOARDITEM WHERE DISPATCHID = :Id ORDER BY ID",
                new { Id = noticeId }).ToList();
        }

        public bool NoticeDraftHasItem(long noticeId, string itemName)
        {
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<int>("SELECT COUNT(1) FROM DP_NOTICEBOARDITEM WHERE DISPATCHID = :Id AND ITEMNAME = :ItemName",
                                           new { Id = noticeId, ItemName = itemName }) > 0;
        }

        // old InsertDispatchItems: the same item name only gets more quantity. 1 = new row, 2 = quantity added, 0 = not my draft
        public int AddNoticeItem(long noticeId, string staffId, string itemName, decimal quantity, string unit, string notes, string attachment)
        {
            using var conn = _databaseService.GetConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();

            if (conn.ExecuteScalar<int>("SELECT COUNT(1) FROM DP_NOTICEBOARD WHERE ID = :Id AND ISOPEN = 1 AND CREATEDBY = :StaffId",
                                        new { Id = noticeId, StaffId = staffId }, tran) == 0)
            {
                tran.Rollback(); return 0;
            }

            long? existing = conn.ExecuteScalar<long?>(
                "SELECT MIN(ID) FROM DP_NOTICEBOARDITEM WHERE DISPATCHID = :Id AND ITEMNAME = :ItemName",
                new { Id = noticeId, ItemName = itemName }, tran);

            if (existing.HasValue)
            {
                conn.Execute("UPDATE DP_NOTICEBOARDITEM SET QUANTITY = NVL(QUANTITY, 0) + :Qty WHERE ID = :Id",
                             new { Qty = quantity, Id = existing.Value }, tran);
                tran.Commit();
                return 2;
            }

            conn.Execute(@"
                INSERT INTO DP_NOTICEBOARDITEM (ID, ITEMID, QUANTITY, UNIT, ITEMNAME, DISPATCHID, ATTACHMENT, NOTES)
                VALUES (SQ_DP_NOTICEBOARDITEM.NEXTVAL, 0, :Qty, :Unit, :ItemName, :Id, :Attachment, :Notes)",
                new { Qty = quantity, Unit = unit, ItemName = itemName, Id = noticeId, Attachment = attachment, Notes = notes }, tran);
            tran.Commit();
            return 1;
        }

        public int UpdateNoticeItem(long itemId, string staffId, decimal quantity, string unit, string notes)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                UPDATE DP_NOTICEBOARDITEM I SET I.QUANTITY = :Qty, I.UNIT = :Unit, I.NOTES = :Notes
                 WHERE I.ID = :Id
                   AND EXISTS (SELECT 1 FROM DP_NOTICEBOARD N WHERE N.ID = I.DISPATCHID AND N.ISOPEN = 1 AND N.CREATEDBY = :StaffId)";
            return conn.Execute(sql, new { Qty = quantity, Unit = unit, Notes = notes, Id = itemId, StaffId = staffId });
        }

        // only an item of the user's OWN OPEN notice (the old page deleted any DP_NOTICEBOARDITEM row by id)
        public int DeleteNoticeItem(long itemId, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                DELETE FROM DP_NOTICEBOARDITEM I
                 WHERE I.ID = :Id
                   AND EXISTS (SELECT 1 FROM DP_NOTICEBOARD N WHERE N.ID = I.DISPATCHID AND N.ISOPEN = 1 AND N.CREATEDBY = :StaffId)";
            return conn.Execute(sql, new { Id = itemId, StaffId = staffId });
        }

        public List<string> SearchNoticeItems(string term)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT DISTINCT ITEMNAME FROM DP_NOTICEBOARDITEM
                 WHERE LOWER(ITEMNAME) LIKE '%' || :Term || '%' AND ROWNUM <= 20";
            return conn.Query<string>(sql, new { Term = term.Trim().ToLowerInvariant() }).ToList();
        }

        // DP_NOTICEBOARD.TRACKINGNO has the unique index TRACKINGNO_UK
        public bool TrackingNoUsed(string trackingNo, long exceptId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<int>("SELECT COUNT(1) FROM DP_NOTICEBOARD WHERE TRACKINGNO = :Tx AND ID <> :Id",
                                           new { Tx = trackingNo, Id = exceptId }) > 0;
        }

        // old dalNoticeBorad.Update. New: CREATEDATE = the save time (the old row kept the time the empty draft was made,
        // so a notice saved today could appear under an older date in the report).
        public bool SubmitNotice(SaveNoticeRequest r, decimal lcAmount, string staffId, string userId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                UPDATE DP_NOTICEBOARD N
                   SET N.DISPATCHTYPE = :DType, N.LCAMOUNT = :LcAmount, N.DOCFROM = :DocFrom, N.RECDATE = :RecDate,
                       N.TRACKINGNO = :TrackingNo, N.FROMMOBILE = :FromMobile, N.FROMEMAIL = :FromEmail, N.FROMADDRESS = :FromAddress,
                       N.FROMREMARKS = :FromRemarks, N.TSTAFFID = :ToStaffId, N.TNAME = :ToName, N.TLOCATION = :ToLocation,
                       N.TCOMPANY = :ToCompany, N.TDEPARTMENT = :ToDepartment, N.TLEVEL = :ToLevel, N.TMOBILE = :ToMobile,
                       N.TEMAIL = :ToEmail, N.TADDRESS = :ToAddress, N.TREMARKS = :ToRemarks,
                       N.UPDATEDBY = :UpdatedBy, N.ISOPEN = 0, N.UPDATEDATE = SYSDATE, N.CREATEDATE = SYSDATE
                 WHERE N.ID = :Id AND N.ISOPEN = 1 AND N.CREATEDBY = :StaffId
                   AND EXISTS (SELECT 1 FROM DP_NOTICEBOARDITEM I WHERE I.DISPATCHID = N.ID)";
            return conn.Execute(sql, new
            {
                DType = r.DispatchType, LcAmount = lcAmount, DocFrom = r.DocFrom, RecDate = r.RecDate?.Date,
                TrackingNo = r.TrackingNo, FromMobile = r.FromMobile, FromEmail = r.FromEmail, FromAddress = r.FromAddress,
                FromRemarks = r.FromRemarks, ToStaffId = r.ToStaffId, ToName = r.ToName, ToLocation = r.ToLocation,
                ToCompany = r.ToCompany, ToDepartment = r.ToDepartment, ToLevel = r.ToLevel, ToMobile = r.ToMobile,
                ToEmail = r.ToEmail, ToAddress = r.ToAddress, ToRemarks = r.ToRemarks,
                UpdatedBy = userId,                           // old code: SessionName.UserId
                Id = r.NoticeId, StaffId = staffId
            }) == 1;
        }

        public NoticeDetailModel? GetNotice(long noticeId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT N.ID, N.DISPATCHTYPE, DT.DTYPENAME, N.LCAMOUNT, N.DOCFROM, N.RECDATE, N.TRACKINGNO, N.FROMMOBILE, N.FROMEMAIL,
                       N.FROMADDRESS, N.FROMREMARKS, N.TSTAFFID, N.TNAME, N.TLOCATION, N.TCOMPANY, N.TDEPARTMENT, N.TLEVEL, N.TMOBILE,
                       N.TEMAIL, N.TADDRESS, N.TREMARKS, N.RECSTAFFID, N.RECEIVERNAME, N.RECEIVERCOMPANY, N.RECDEPARTMENT, N.RECMOBILE,
                       N.RECEMAIL, N.RECREMARKS, N.RECEIVEDDATE, NVL(N.ACTIVE, 1) ACTIVE, N.CREATEDBY, N.CREATEDATE
                  FROM DP_NOTICEBOARD N
                  LEFT OUTER JOIN DP_DISPATCHTYPE DT ON DT.ID = N.DISPATCHTYPE
                 WHERE N.ID = :Id AND NVL(N.ISOPEN, 0) = 0";
            return conn.QueryFirstOrDefault<NoticeDetailModel>(sql, new { Id = noticeId });
        }

        // old dalNoticeBorad.GetEmployeeDetails
        public NoticeEmployeeModel? GetNoticeEmployee(string staffId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT E.STAFFID, E.EMPLOYEENAME, E.OFFICEMOBILE, E.OFFICEMAIL, B.CENTERNAME, CO.COMPANYNAME, D.DEPARTMENTNAME
                  FROM EM_EMPLOYEE E
                 INNER JOIN BS_BUSINESSCENTER B ON B.ID = E.BUSINESSCENTERID
                  LEFT OUTER JOIN BS_COMPANY CO ON CO.ID = E.COMPANYID
                 INNER JOIN BS_DEPARTMENT D ON D.ID = E.DEPARTMENTID
                 WHERE E.STAFFID = :StaffId";
            return conn.QueryFirstOrDefault<NoticeEmployeeModel>(sql, new { StaffId = staffId });
        }

        // old UpdateReceiver (... ACTIVE = 0 WHERE ACTIVE = 1): a notice is handed over only once
        public bool ReceiveNotice(NoticeReceiveRequest r, string userId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                UPDATE DP_NOTICEBOARD
                   SET RECSTAFFID = :StaffId, RECDEPARTMENT = :Department, RECMOBILE = :Mobile, RECEMAIL = :Email, RECREMARKS = :Remarks,
                       UPDATEDBY = :UpdatedBy, UPDATEDATE = SYSDATE, RECEIVEDDATE = SYSDATE, RECEIVERNAME = :Name,
                       RECEIVERCOMPANY = :Company, ACTIVE = 0
                 WHERE ID = :Id AND NVL(ACTIVE, 1) = 1 AND NVL(ISOPEN, 0) = 0";
            return conn.Execute(sql, new
            {
                StaffId = r.StaffId, Department = r.Department, Mobile = r.Mobile, Email = r.Email, Remarks = r.Remarks,
                UpdatedBy = userId, Name = r.Name, Company = r.Company, Id = r.Id
            }) == 1;
        }

        // ================================================================ REPORTS

        // old GateEntryReport.aspx. The old "Receiver" columns came from the employee who MADE the entry (GE.CREATEDBY);
        // both people are shown now: the host (GE.STAFFID) and the gate user who made the entry.
        public List<IDictionary<string, object?>> ReportGateEntry(DispatchReportFilter f)
        {
            var p = new DynamicParameters();
            string sql = @"
                SELECT GE.ID, GE.GATEENTRYID, GE.INDATETIME, GE.OUTDATETIME, BC.CENTERNAME, D.DEPARTMENTNAME,
                       GE.STAFFID, EM.EMPLOYEENAME, EM.OFFICEMOBILE, GE.VISTORNAME, GE.VISTORMOBILE, GE.VISITORADDRESS, GE.VISTORCARDNO,
                       CASE WHEN NVL(GE.GATEPASSED, 0) = 1 THEN 'Passed' ELSE 'Inside' END GATESTATUS,
                       CR.EMPLOYEENAME CREATEDBYNAME
                  FROM DP_GATEENTRY GE
                  LEFT OUTER JOIN EM_EMPLOYEE EM ON EM.STAFFID = GE.STAFFID
                  LEFT OUTER JOIN EM_EMPLOYEE CR ON CR.STAFFID = GE.CREATEDBY
                 INNER JOIN BS_BUSINESSCENTER BC ON BC.ID = GE.BUSINESSCENTERID
                 INNER JOIN BS_DEPARTMENT D ON D.ID = GE.DEPARTMENTID
                 WHERE NVL(GE.ISOPEN, 0) = 0" + DateRange("GE.INDATETIME", f, p) + GateEntryFilters(f, p) + @"
                 ORDER BY GE.INDATETIME DESC";
            return RunReport(sql, p);
        }

        // old GateEntryReturnItem.aspx: entries whose sample was returned (ISRETURNED = 1), one row per item
        public List<IDictionary<string, object?>> ReportGateEntryReturned(DispatchReportFilter f)
        {
            var p = new DynamicParameters();
            string sql = @"
                SELECT GE.ID, GE.GATEENTRYID, GE.INDATETIME, BC.CENTERNAME, D.DEPARTMENTNAME,
                       GE.STAFFID, EM.EMPLOYEENAME, EM.OFFICEMOBILE, GE.VISTORNAME, GE.VISTORMOBILE,
                       EI.ITEMNAME, EI.QUANTITY, EI.UNIT,
                       CASE WHEN EI.ISPERSONAL = 1 THEN 'Yes' ELSE 'No' END SAMPLE,
                       CASE WHEN EI.ISRETURNABLE = 1 THEN 'Yes' ELSE 'No' END RETURNABLE
                  FROM DP_GATEENTRY GE
                 INNER JOIN DP_GATEENTRYITEMS EI ON EI.GATEENTRYID = GE.ID
                  LEFT OUTER JOIN EM_EMPLOYEE EM ON EM.STAFFID = GE.STAFFID
                 INNER JOIN BS_BUSINESSCENTER BC ON BC.ID = GE.BUSINESSCENTERID
                 INNER JOIN BS_DEPARTMENT D ON D.ID = GE.DEPARTMENTID
                 WHERE GE.ISRETURNED = 1" + DateRange("GE.INDATETIME", f, p) + GateEntryFilters(f, p) + @"
                 ORDER BY GE.INDATETIME DESC, EI.ID";
            return RunReport(sql, p);
        }

        private static string GateEntryFilters(DispatchReportFilter f, DynamicParameters p)
        {
            string w = "";
            if (f.LocationId > 0) { w += " AND GE.BUSINESSCENTERID = :LocationId"; p.Add("LocationId", f.LocationId); }
            if (f.DepartmentId > 0) { w += " AND GE.DEPARTMENTID = :DepartmentId"; p.Add("DepartmentId", f.DepartmentId); }
            return w;
        }

        // old DNoticeBoardView.aspx / RNItems.aspx (GetDispatcNoticehDetails + Criteria)
        public List<IDictionary<string, object?>> ReportNoticeBoard(DispatchReportFilter f)
        {
            var p = new DynamicParameters();
            string w = DateRange("N.CREATEDATE", f, p);

            // old: text before the first "-" (auto complete "ID-...") in from / tracking no / from mobile / to staff / to name / to mobile
            string text = (f.Text ?? "").Trim().Split('-')[0].Trim().ToUpperInvariant();
            if (text.Length > 0)
            {
                w += @" AND (UPPER(N.DOCFROM) LIKE :T1 OR UPPER(N.TRACKINGNO) LIKE :T2 OR UPPER(N.FROMMOBILE) LIKE :T3
                          OR UPPER(N.TSTAFFID) LIKE :T4 OR UPPER(N.TNAME) LIKE :T5 OR UPPER(N.TMOBILE) LIKE :T6)";
                string like = "%" + text + "%";
                for (int i = 1; i <= 6; i++) p.Add("T" + i, like);
            }
            if (f.DispatchType > 0) { w += " AND N.DISPATCHTYPE = :DType"; p.Add("DType", f.DispatchType); }
            // old: LC no. / LC amount only for dispatch type 3 (LC)
            if (f.DispatchType == 3 && !string.IsNullOrWhiteSpace(f.LcNo))
            {
                w += " AND UPPER(N.TRACKINGNO) = :LcNo"; p.Add("LcNo", f.LcNo.Trim().ToUpperInvariant());
            }
            if (f.DispatchType == 3 && f.LcAmountValue.HasValue) { w += " AND N.LCAMOUNT = :LcAmt"; p.Add("LcAmt", f.LcAmountValue.Value); }
            if (f.Status == "waiting") w += " AND NVL(N.ACTIVE, 1) = 1";
            else if (f.Status == "received") w += " AND NVL(N.ACTIVE, 1) = 0";

            string sql = @"
                SELECT N.ID, N.CREATEDATE, N.TRACKINGNO, DT.DTYPENAME, N.LCAMOUNT, N.DOCFROM, N.FROMMOBILE, N.RECDATE,
                       N.TSTAFFID, N.TNAME, N.TMOBILE, N.TDEPARTMENT, N.TLOCATION,
                       CASE WHEN NVL(N.ACTIVE, 1) = 1 THEN 'Waiting' ELSE 'Received' END NOTICESTATUS,
                       N.RECSTAFFID, N.RECEIVERNAME, N.RECEIVERCOMPANY, N.RECMOBILE, N.RECEIVEDDATE
                  FROM DP_NOTICEBOARD N
                  LEFT OUTER JOIN DP_DISPATCHTYPE DT ON DT.ID = N.DISPATCHTYPE
                 WHERE NVL(N.ISOPEN, 0) = 0" + w + @"
                 ORDER BY N.CREATEDATE DESC";
            return RunReport(sql, p);
        }
    }
}
