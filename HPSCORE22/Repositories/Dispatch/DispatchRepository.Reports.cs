// Repositories/Dispatch/DispatchRepository.Reports.cs
// Dispatch phase 7 - reports. Port of YA dalDispatchReport, dalLocationWiseReport, dalDispatch.DispatchItemListBySender/Receiver
// and dalApprover.GetGatepassStatusList. Read only.
// Differences to the old code:
//   * every filter is a bind parameter (the old pages concatenated the text boxes into the SQL)
//   * dates: TRUNC(CREATEDATE) BETWEEN from AND to (the old "CREATEDATE <= to" missed the whole "to" day after 00:00,
//     and "CREATEDATE = from" only found rows saved exactly at midnight)
//   * at most ReportMaxRows rows per search (the old pages could load the whole table)
//   * VW_DISPATCHREPORT is not needed: the same SELECT is written here on the base tables
using AlphaCare.Models;
using Dapper;

namespace Admin360.Repositories
{
    public partial class DispatchRepository
    {
        public const int ReportMaxRows = 5000;

        private List<IDictionary<string, object?>> RunReport(string sql, DynamicParameters p)
        {
            using var conn = _databaseService.GetConnection();
            string limited = "SELECT * FROM (" + sql + ") WHERE ROWNUM <= " + ReportMaxRows;
            return conn.Query(limited, p).Select(r => (IDictionary<string, object?>)r).ToList();
        }

        // from + to -> range, only from -> that day, only to -> up to that day
        private static string DateRange(string column, DispatchReportFilter f, DynamicParameters p)
        {
            if (f.From.HasValue) p.Add("DFrom", f.From.Value.Date);
            if (f.To.HasValue) p.Add("DTo", f.To.Value.Date);
            if (f.From.HasValue && f.To.HasValue) return $" AND TRUNC({column}) BETWEEN :DFrom AND :DTo";
            if (f.From.HasValue) return $" AND TRUNC({column}) = :DFrom";
            if (f.To.HasValue) return $" AND TRUNC({column}) <= :DTo";
            return "";
        }

        private static string SendFilters(DispatchReportFilter f, DynamicParameters p, bool department = true, bool issueType = true)
        {
            string w = "";
            if (f.LocationId > 0) { w += " AND D.SENDBUSINESSCENTERID = :LocationId"; p.Add("LocationId", f.LocationId); }
            if (department && f.DepartmentId > 0) { w += " AND D.SENDDEPARTMENTID = :DepartmentId"; p.Add("DepartmentId", f.DepartmentId); }
            if (issueType && f.IssueTypeId > 0) { w += " AND D.ISSUETYPEID = :IssueTypeId"; p.Add("IssueTypeId", f.IssueTypeId); }
            return w;
        }

        // old Pages/Dispatch/DispatchReport.aspx (VW_DISPATCHREPORT): one row per dispatch with its current step
        public List<IDictionary<string, object?>> ReportDispatchList(DispatchReportFilter f)
        {
            var p = new DynamicParameters();
            string sql = @"
                SELECT D.ID, D.TRANSACTIONID, D.CREATEDATE, I.ISSUETYPENAME,
                       FB.CENTERCODE || '-' || FD.DEPARTMENTCODE || '-' || D.SENDNAME SENDER, D.SENDMOBILE,
                       D.BUSINESSCENTER || '-' || D.RECEIVERDEPARTMENT || '-' || D.RECEIVERNAME RECEIVER, D.RECEIVERMOBILE,
                       (SELECT MAX(T.STEP) KEEP (DENSE_RANK LAST ORDER BY T.ID) FROM DP_TRANSITS T
                         WHERE T.DISPATCHID = D.ID AND T.ISACTIVE = 1) STEP,
                       CE.EMPLOYEENAME CREATEDBYNAME
                  FROM DP_DISPATCH D
                 INNER JOIN EM_EMPLOYEE CE ON CE.STAFFID = D.CREATEDBY
                 INNER JOIN BS_BUSINESSCENTER FB ON FB.ID = D.SENDBUSINESSCENTERID
                 INNER JOIN DP_ISSUETYPE I ON I.ID = D.ISSUETYPEID
                 INNER JOIN BS_DEPARTMENT FD ON FD.ID = D.SENDDEPARTMENTID
                 WHERE D.ISOPEN = 0" + DateRange("D.CREATEDATE", f, p) + SendFilters(f, p) + @"
                 ORDER BY D.CREATEDATE DESC";
            return RunReport(sql, p);
        }

        // old DispatchDetails.aspx + LocationWiseDispatchReport.aspx (GetDispatchDetails / GetLocationWiseDispatchDetails):
        // one row per item, sender location = location of the FROMUSER employee
        public List<IDictionary<string, object?>> ReportDispatchItems(DispatchReportFilter f)
        {
            var p = new DynamicParameters();
            string sql = @"
                SELECT D.ID, D.CREATEDATE, RC.CENTERNAME SENDERRLOC, D.TRANSACTIONID, I.ISSUETYPENAME, FB.CENTERNAME RECEIVERLOC,
                       DP.DEPARTMENTNAME, D.FROMUSER, D.SENDNAME, D.SENDMOBILE, D.TOUSER, D.RECEIVERNAME, D.RECEIVERMOBILE,
                       DI.ITEMNAME, DI.QUANTITY, DI.UNIT, DI.NOTES
                  FROM DP_DISPATCH D
                 INNER JOIN DP_DISPATCHITEMS DI ON DI.DISPATCHID = D.ID
                 INNER JOIN EM_EMPLOYEE CE ON CE.STAFFID = D.CREATEDBY
                  LEFT OUTER JOIN BS_BUSINESSCENTER FB ON FB.ID = D.RECBUSINESSCENTERID
                 INNER JOIN EM_EMPLOYEE RV ON D.FROMUSER = RV.USERID
                 INNER JOIN BS_BUSINESSCENTER RC ON RV.BUSINESSCENTERID = RC.ID
                 INNER JOIN BS_DEPARTMENT DP ON DP.ID = D.SENDDEPARTMENTID
                 INNER JOIN DP_ISSUETYPE I ON I.ID = D.ISSUETYPEID
                 WHERE NVL(D.ISOPEN, 0) = 0" + DateRange("D.CREATEDATE", f, p) + SendFilters(f, p) + @"
                 ORDER BY RC.CENTERNAME, D.CREATEDATE, D.ID, DI.ID";
            return RunReport(sql, p);
        }

        // old ItemListBySender.aspx (D.FROMUSER = staff) / ItemListByReceiver.aspx.
        // The old receiver page also filtered D.FROMUSER (copy of the sender page) - here the receiver page uses D.TOUSER.
        public List<IDictionary<string, object?>> ReportItemsByStaff(DispatchReportFilter f, string staffId)
        {
            var p = new DynamicParameters();
            p.Add("Staff", staffId);
            string who = f.Mode == "receiver" ? "D.TOUSER" : "D.FROMUSER";
            string sql = @"
                SELECT D.ID, D.TRANSACTIONID, D.CREATEDATE, D.FROMUSER, D.TOUSER, BC.CENTERNAME, DP.DEPARTMENTNAME,
                       DI.ITEMNAME, DI.QUANTITY, DI.UNIT
                  FROM DP_DISPATCH D
                 INNER JOIN BS_BUSINESSCENTER BC ON BC.ID = D.SENDBUSINESSCENTERID
                 INNER JOIN BS_DEPARTMENT DP ON DP.ID = D.SENDDEPARTMENTID
                 INNER JOIN DP_DISPATCHITEMS DI ON DI.DISPATCHID = D.ID
                 WHERE NVL(D.ISOPEN, 0) = 0 AND " + who + " = :Staff" + DateRange("D.CREATEDATE", f, p) + @"
                 ORDER BY D.CREATEDATE, D.ID, DI.ID";
            return RunReport(sql, p);
        }

        // old LocationWiseDispatchOnlineReport.aspx: items per sender location (opens with yesterday)
        public List<IDictionary<string, object?>> ReportLocationSummary(DispatchReportFilter f)
        {
            var p = new DynamicParameters();
            string sql = @"
                SELECT D.ID, D.TRANSACTIONID, RC.CENTERNAME SENDERRLOC, DP.DEPARTMENTNAME, D.CREATEDATE, D.FROMUSER, D.SENDNAME,
                       D.RECEIVERNAME,
                       CASE WHEN D.RECBUSINESSCENTERID = 0 THEN 'External' ELSE TO_CHAR(FB.CENTERNAME) END RECEIVERLOC,
                       I.ISSUETYPENAME, DI.ITEMNAME, DI.QUANTITY, DI.UNIT
                  FROM DP_DISPATCH D
                 INNER JOIN DP_DISPATCHITEMS DI ON DI.DISPATCHID = D.ID
                 INNER JOIN EM_EMPLOYEE CE ON CE.STAFFID = D.CREATEDBY
                  LEFT OUTER JOIN BS_BUSINESSCENTER FB ON FB.ID = D.RECBUSINESSCENTERID
                 INNER JOIN EM_EMPLOYEE RV ON D.FROMUSER = RV.USERID
                 INNER JOIN BS_BUSINESSCENTER RC ON RV.BUSINESSCENTERID = RC.ID
                 INNER JOIN BS_DEPARTMENT DP ON DP.ID = D.SENDDEPARTMENTID
                 INNER JOIN DP_ISSUETYPE I ON I.ID = D.ISSUETYPEID
                 WHERE NVL(D.ISOPEN, 0) = 0" + DateRange("D.CREATEDATE", f, p) + SendFilters(f, p, department: false, issueType: false) + @"
                 ORDER BY RC.CENTERNAME, D.CREATEDATE, D.ID, DI.ID";
            return RunReport(sql, p);
        }

        // old GatepassStatusReport.aspx: approved (STATUSID 2) more than 7 days ago and still not gate passed,
        // sender location is an active location that is not a depot / sales office. One row per item.
        public List<IDictionary<string, object?>> ReportGatePassPending(DispatchReportFilter f, string createdBy)
        {
            var p = new DynamicParameters();
            string w = "";
            if (f.From.HasValue) { w += " AND TRUNC(D.CREATEDATE) >= :DFrom"; p.Add("DFrom", f.From.Value.Date); }
            if (f.LocationId > 0) { w += " AND D.SENDBUSINESSCENTERID = :LocationId"; p.Add("LocationId", f.LocationId); }
            // one staff member's pending gate passes: the person who created the dispatch (column "Created By ID")
            if (!string.IsNullOrWhiteSpace(createdBy)) { w += " AND D.CREATEDBY = :CreatedBy"; p.Add("CreatedBy", createdBy.Trim()); }
            string sql = @"
                SELECT D.ID, D.TRANSACTIONID, D.CREATEDATE, I.ISSUETYPENAME,
                       SB.CENTERNAME SENDERLOCATION, SDEP.DEPARTMENTNAME SENDERDEPARTMENT, D.FROMUSER SENDERUSER, SE.EMPLOYEENAME SENDERNAME,
                       RB.CENTERNAME RECEIVERLOCATION, RDEP.DEPARTMENTNAME RECEIVERDEPARTMENT, D.TOUSER RECEIVERUSER, D.RECEIVERNAME,
                       D.CREATEDBY CREATEUSER, E.EMPLOYEENAME CREATORNAME,
                       DPITEM.ITEMNAME, DPITEM.QUANTITY, DPITEM.UNIT, DPITEM.NOTES
                  FROM DP_DISPATCH D
                  LEFT JOIN EM_EMPLOYEE E ON D.CREATEDBY = E.STAFFID
                  LEFT JOIN EM_EMPLOYEE SE ON D.FROMUSER = SE.STAFFID
                  LEFT JOIN BS_DEPARTMENT SDEP ON SDEP.ID = D.SENDDEPARTMENTID
                  LEFT JOIN BS_DEPARTMENT RDEP ON RDEP.ID = D.RECDEPARTMENTID
                  LEFT JOIN DP_ISSUETYPE I ON D.ISSUETYPEID = I.ID
                  LEFT JOIN BS_BUSINESSCENTER SB ON D.SENDBUSINESSCENTERID = SB.ID
                  LEFT JOIN BS_BUSINESSCENTER RB ON D.RECBUSINESSCENTERID = RB.ID
                  LEFT JOIN DP_DISPATCHITEMS DPITEM ON D.ID = DPITEM.DISPATCHID
                 WHERE D.CREATEDATE < SYSDATE - 7
                   AND D.STATUSID IN (2)" + w + @"
                   AND D.GATEPASS = 0
                   AND SB.ISDEPOTORSALESOFFICE = 0
                   AND SB.LOCATIONSTATUS = 1
                 ORDER BY SB.CENTERNAME, D.CREATEDATE, D.ID";
            return RunReport(sql, p);
        }

        // old Pages/Dispatch/GatepassStatus.aspx (GetGatepassStatusList): MY approved dispatches that have not passed the gate,
        // created from 05/10/2026 (the start date written in the old query), active non depot sender location
        public List<IDictionary<string, object?>> ReportMyGatePassStatus(string staffId)
        {
            var p = new DynamicParameters();
            p.Add("Me", staffId);
            const string sql = @"
                SELECT D.ID, D.TRANSACTIONID, D.CREATEDATE, I.ISSUETYPENAME,
                       SB.CENTERNAME SENDER_LOCATION, D.FROMUSER SENDER_USER, SE.EMPLOYEENAME SENDER_NAME,
                       RB.CENTERNAME RECEIVER_LOCATION, D.TOUSER RECEIVER_USER, RE.EMPLOYEENAME RECEIVER_NAME,
                       D.CREATEDBY CREAT_USER, E.EMPLOYEENAME CREATOR_NAME
                  FROM DP_DISPATCH D
                  LEFT JOIN EM_EMPLOYEE E ON D.CREATEDBY = E.STAFFID
                  LEFT JOIN EM_EMPLOYEE SE ON D.FROMUSER = SE.STAFFID
                  LEFT JOIN EM_EMPLOYEE RE ON D.TOUSER = RE.STAFFID
                  LEFT JOIN DP_ISSUETYPE I ON D.ISSUETYPEID = I.ID
                  LEFT JOIN BS_BUSINESSCENTER SB ON D.SENDBUSINESSCENTERID = SB.ID
                  LEFT JOIN BS_BUSINESSCENTER RB ON D.RECBUSINESSCENTERID = RB.ID
                 WHERE D.CREATEDATE < SYSDATE
                   AND D.STATUSID IN (2)
                   AND TRUNC(D.CREATEDATE) >= DATE '2026-10-05'
                   AND D.GATEPASS = 0
                   AND SB.ISDEPOTORSALESOFFICE = 0
                   AND SB.LOCATIONSTATUS = 1
                   AND D.CREATEDBY = :Me
                 ORDER BY D.CREATEDATE";
            return RunReport(sql, p);
        }

        private static string EmployeePlaceFilters(string alias, DispatchReportFilter f, DynamicParameters p)
        {
            string w = "";
            if (f.LocationId > 0) { w += $" AND {alias}.BUSINESSCENTERID = :LocationId"; p.Add("LocationId", f.LocationId); }
            if (f.CompanyId > 0) { w += $" AND {alias}.COMPANYID = :CompanyId"; p.Add("CompanyId", f.CompanyId); }
            if (f.DepartmentId > 0) { w += $" AND {alias}.DEPARTMENTID = :DepartmentId"; p.Add("DepartmentId", f.DepartmentId); }
            return w;
        }

        // old LWReport.aspx (dalLocationWiseReport.GetByCriteria): approvers with an active user account
        public List<IDictionary<string, object?>> ReportApprovers(DispatchReportFilter f)
        {
            var p = new DynamicParameters();
            string sql = @"
                SELECT B.CENTERNAME, C.COMPANYNAME, DE.DEPARTMENTNAME, DE.DEPARTMENTCODE, A.PRIORITY, A.ISFINALAPPROVER,
                       E.STAFFID, E.EMPLOYEENAME, DS.DESIGNATIONNAME, E.OFFICEMOBILE, E.OFFICEMAIL
                  FROM DP_APPROVER A
                 INNER JOIN BS_BUSINESSCENTER B ON B.ID = A.BUSINESSCENTERID
                 INNER JOIN BS_COMPANY C ON C.ID = A.COMPANYID
                 INNER JOIN BS_DEPARTMENT DE ON DE.ID = A.DEPARTMENTID
                 INNER JOIN EM_EMPLOYEE E ON E.USERID = A.APPROVERID
                 INNER JOIN UC_USERS U ON U.ID = E.STAFFID
                 INNER JOIN BS_DESIGNATION DS ON DS.ID = E.DESIGNATIONID
                 WHERE LENGTH(E.STAFFID) > 0 AND U.ISACTIVE = 1" + EmployeePlaceFilters("A", f, p) + @"
                 ORDER BY B.CENTERNAME, C.COMPANYNAME, DE.DEPARTMENTCODE, A.PRIORITY";
            return RunReport(sql, p);
        }

        // old LWUserList.aspx (dalLocationWiseReport.GetAllUserList): active users with their role
        public List<IDictionary<string, object?>> ReportUsers(DispatchReportFilter f)
        {
            var p = new DynamicParameters();
            string sql = @"
                SELECT E.STAFFID, E.EMPLOYEENAME, DS.DESIGNATIONNAME, B.CENTERCODE, B.CENTERNAME, C.COMPANYNAME,
                       DE.DEPARTMENTCODE, DE.DEPARTMENTNAME, R.ROLENAME, E.OFFICEMOBILE, E.OFFICEMAIL
                  FROM EM_EMPLOYEE E
                 INNER JOIN UC_USERS U ON U.ID = E.STAFFID
                 INNER JOIN UC_ROLESETUP R ON R.ID = U.ROLEID
                 INNER JOIN BS_BUSINESSCENTER B ON B.ID = E.BUSINESSCENTERID
                 INNER JOIN BS_COMPANY C ON C.ID = E.COMPANYID
                 INNER JOIN BS_DEPARTMENT DE ON DE.ID = E.DEPARTMENTID
                 INNER JOIN BS_DESIGNATION DS ON DS.ID = E.DESIGNATIONID
                 WHERE LENGTH(E.STAFFID) > 0 AND U.ISACTIVE = 1" + EmployeePlaceFilters("E", f, p) + @"
                 ORDER BY B.CENTERNAME, DE.DEPARTMENTCODE, E.EMPLOYEENAME";
            return RunReport(sql, p);
        }
    }
}
