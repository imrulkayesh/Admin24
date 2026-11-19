// Repositories/Service/ServiceRepository.Reports.cs
// Service reports. Port of YA App_Code/Reports/dalRptService.cs + Common.GetAll("VW_SERVICECENTER"), plus new date-range reports.
//   * VW_SERVICECENTER is read as it is (its counting rules live in the view - same numbers as YA).
//   * The request report is written on the base tables (as the Dispatch report replaced VW_DISPATCHREPORT), because its
//     filters must work on columns that are known (YA's search form had empty handlers and always listed the whole view).
//   * every filter is a bind parameter; dates as CREATEDATE >= from AND < to + 1 (whole "to" day, index friendly)
//   * at most ReportMaxRows rows per search
using AlphaCare.Models;
using Dapper;

namespace Admin360.Repositories
{
    public partial class ServiceRepository
    {
        public const int ReportMaxRows = 5000;

        private List<IDictionary<string, object?>> RunReport(string sql, DynamicParameters p)
        {
            using var conn = _databaseService.GetConnection();
            string limited = "SELECT * FROM (" + sql + ") WHERE ROWNUM <= " + ReportMaxRows;
            return conn.Query(limited, p).Select(r => (IDictionary<string, object?>)r).ToList();
        }

        // from + to -> range, only from -> from that day on, only to -> up to that day
        private static string DateRange(string column, ServiceReportFilter f, DynamicParameters p)
        {
            string w = "";
            if (f.From.HasValue) { w += $" AND {column} >= :DFrom"; p.Add("DFrom", f.From.Value.Date); }
            if (f.To.HasValue) { w += $" AND {column} < :DTo"; p.Add("DTo", f.To.Value.Date.AddDays(1)); }
            return w;
        }

        // same texts as ServiceStatus.Display (old pages + RequestStatus)
        private const string StatusText = @"
            CASE WHEN S.ISAPPROVED = 0 AND S.STATUS = 100 THEN 'Instant - Waiting for Approval'
                 WHEN S.ISAPPROVED = 0 THEN 'Waiting for Approval'
                 WHEN S.ISAPPROVED = 2 THEN 'Rejected'
                 WHEN S.STATUS = 0 THEN 'Waiting for Assign'
                 WHEN S.STATUS = 1 THEN 'On Process'
                 WHEN S.STATUS = 2 THEN 'Waiting for User Feedback'
                 WHEN S.STATUS = 3 THEN 'Waiting for Service Person Feedback'
                 WHEN S.STATUS = 4 THEN 'Waiting For Revise'
                 WHEN S.STATUS = 5 THEN 'Completed'
                 ELSE '-' END";

        private static string StatusFilter(ServiceReportFilter f, DynamicParameters p)
        {
            switch (f.Status)
            {
                case "approval": return " AND S.ISAPPROVED = 0 AND S.STATUS <> 100";
                case "rejected": return " AND S.ISAPPROVED = 2";
                case "instant": return " AND S.ISAPPROVED = 0 AND S.STATUS = 100";
                case "0": case "1": case "2": case "3": case "4": case "5":
                    p.Add("St", int.Parse(f.Status));
                    return " AND S.ISAPPROVED = 1 AND S.STATUS = :St";
                default: return "";
            }
        }

        // service center filters: location of the service center + the service center (also when the request was forwarded to it)
        private static string CenterFilters(ServiceReportFilter f, DynamicParameters p, string locationColumn = "SC.BUSINESSCENTERID")
        {
            string w = "";
            if (f.LocationId > 0) { w += $" AND {locationColumn} = :Loc"; p.Add("Loc", f.LocationId); }
            if (f.ServiceCenterId > 0)
            {
                w += " AND (S.SERVICECENTERID = :Sc1 OR S.FRDSERVICECENTERID = :Sc2)";
                p.Add("Sc1", f.ServiceCenterId);
                p.Add("Sc2", f.ServiceCenterId);
            }
            return w;
        }

        private static string InstantFilter(ServiceReportFilter f, DynamicParameters p)
        {
            if (f.Instant != "0" && f.Instant != "1") return "";
            p.Add("Inst", f.Instant == "1" ? 1 : 0);
            return " AND NVL(S.INSTANTSERVICE, 0) = :Inst";
        }

        // ================================================================ LOOKUP

        public List<ServiceOption> GetAllServiceCenterOptions()
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT SC.ID, B.CENTERCODE || '-' || SC.SERVICECENTERNAME NAME
                  FROM SC_SERVICECENTER SC
                  LEFT JOIN BS_BUSINESSCENTER B ON B.ID = SC.BUSINESSCENTERID
                 ORDER BY B.CENTERCODE, SC.SERVICECENTERNAME";
            return conn.Query<ServiceOption>(sql).ToList();
        }

        // ================================================================ YA REPORTS (VW_SERVICECENTER)

        // only the columns the old pages used (the view's other columns are not known)
        private const string CenterViewSelect = @"
            SELECT CENTERCODE, SERVICECENTERNAME, EMPLOYEENAME, OFFICEMOBILE, OFFICEMAIL,
                   SERVICECOUNT, COMPLETED, FORWARDED, PROCESSING, PENDING, INSTANTSERVICE
              FROM VW_SERVICECENTER";

        public List<ServiceCenterSummaryRow> GetServiceCenterSummary()
        {
            using var conn = _databaseService.GetConnection();
            return conn.Query<ServiceCenterSummaryRow>(CenterViewSelect + " ORDER BY CENTERCODE, SERVICECENTERNAME").ToList();
        }

        public List<IDictionary<string, object?>> ReportServiceCenter()
        {
            string sql = @"
                SELECT CENTERCODE || '-' || SERVICECENTERNAME SERVICECENTER, EMPLOYEENAME, OFFICEMOBILE, OFFICEMAIL,
                       SERVICECOUNT, COMPLETED, FORWARDED, PROCESSING, PENDING, INSTANTSERVICE
                  FROM VW_SERVICECENTER
                 ORDER BY CENTERCODE, SERVICECENTERNAME";
            return RunReport(sql, new DynamicParameters());
        }

        // ================================================================ SERVICE REQUEST (old ServiceRequest.aspx)

        public List<IDictionary<string, object?>> ReportServiceRequests(ServiceReportFilter f, string fromStaff, string servicePerson)
        {
            var p = new DynamicParameters();
            string w = DateRange("S.CREATEDATE", f, p) + CenterFilters(f, p) + StatusFilter(f, p) + InstantFilter(f, p);
            if (f.CategoryId > 0) { w += " AND S.CATEGORYID = :Cat"; p.Add("Cat", f.CategoryId); }
            if (!string.IsNullOrWhiteSpace(f.ServiceId)) { w += " AND UPPER(S.SERVICEID) LIKE '%' || :Sid || '%'"; p.Add("Sid", f.ServiceId.Trim().ToUpperInvariant()); }
            if (fromStaff.Length > 0) { w += " AND S.FROMSTAFFID = :Fs"; p.Add("Fs", fromStaff); }
            if (servicePerson.Length > 0) { w += " AND SA.TEAMMEMBERID = :Sp"; p.Add("Sp", servicePerson); }

            string sql = $@"
                SELECT S.ID, S.SERVICEID, S.CREATEDATE, C.CATEGORYNAME, S.FROMSTAFFID, E.EMPLOYEENAME, E.OFFICEMOBILE,
                       B.CENTERCODE REQLOCATION, D.DEPARTMENTCODE,
                       BSC.CENTERCODE || '-' || SC.SERVICECENTERNAME SERVICECENTER,
                       NVL2(MCH.ID, MCH.MACHINECODE || ' - ' || MCH.MACHINENAME, NULL) MACHINE_OUTLET,
                       ST.TEAMNAME, SAE.EMPLOYEENAME SERVICEMAN, SAE.OFFICEMOBILE SERVICEMOBILE,
                       CASE WHEN NVL(S.INSTANTSERVICE, 0) = 1 THEN 'Yes' ELSE 'No' END INSTANT,
                       {StatusText} STATUSTEXT,
                       NVL(S.NOOFREVISION, 0) NOOFREVISION, S.RELEASEDATE,
                       SA.RATINGFROMUSER, SA.RATINGFROMSERVICEMAN
                  FROM SC_SERVICEREQUEST S
                 INNER JOIN EM_EMPLOYEE E ON E.STAFFID = S.FROMSTAFFID
                 INNER JOIN SC_SERVICECENTER SC ON SC.ID = S.SERVICECENTERID
                  LEFT JOIN BS_BUSINESSCENTER BSC ON BSC.ID = SC.BUSINESSCENTERID
                  LEFT JOIN BS_BUSINESSCENTER B ON B.ID = S.FROMCENTERID
                  LEFT JOIN BS_DEPARTMENT D ON D.ID = S.FROMDEPARTMENTID
                  LEFT JOIN SC_CATEGORY C ON C.ID = S.CATEGORYID
                  LEFT JOIN SC_MACHINE MCH ON MCH.ID = S.MACHINEID
                  LEFT JOIN SC_SERVICEASSIGN SA ON SA.REQUESTID = S.ID AND SA.ISACTIVE = 1
                  LEFT JOIN EM_EMPLOYEE SAE ON SAE.STAFFID = SA.TEAMMEMBERID
                  LEFT JOIN SC_SERVICETEAM ST ON ST.ID = SA.TEAMID
                 WHERE S.ISOPEN = 0 {w}
                 ORDER BY S.CREATEDATE DESC, S.ID DESC";
            return RunReport(sql, p);
        }

        // ================================================================ NEW: STATUS PER SERVICE CENTER

        public List<IDictionary<string, object?>> ReportCenterStatus(ServiceReportFilter f)
        {
            var p = new DynamicParameters();
            string w = DateRange("S.CREATEDATE", f, p) + InstantFilter(f, p);
            if (f.LocationId > 0) { w += " AND SC.BUSINESSCENTERID = :Loc"; p.Add("Loc", f.LocationId); }
            if (f.ServiceCenterId > 0) { w += " AND S.SERVICECENTERID = :Sc"; p.Add("Sc", f.ServiceCenterId); }

            string sql = $@"
                SELECT BSC.CENTERCODE || '-' || SC.SERVICECENTERNAME SERVICECENTER, E.EMPLOYEENAME ADMINNAME,
                       COUNT(*) TOTAL,
                       SUM(CASE WHEN S.ISAPPROVED = 0 THEN 1 ELSE 0 END) WAITINGAPPROVAL,
                       SUM(CASE WHEN S.ISAPPROVED = 2 THEN 1 ELSE 0 END) REJECTED,
                       SUM(CASE WHEN S.ISAPPROVED = 1 AND S.STATUS = 0 THEN 1 ELSE 0 END) WAITINGASSIGN,
                       SUM(CASE WHEN S.ISAPPROVED = 1 AND S.STATUS = 1 THEN 1 ELSE 0 END) ONPROCESS,
                       SUM(CASE WHEN S.ISAPPROVED = 1 AND S.STATUS = 2 THEN 1 ELSE 0 END) USERFEEDBACK,
                       SUM(CASE WHEN S.ISAPPROVED = 1 AND S.STATUS = 3 THEN 1 ELSE 0 END) SPFEEDBACK,
                       SUM(CASE WHEN S.ISAPPROVED = 1 AND S.STATUS = 4 THEN 1 ELSE 0 END) REVISE,
                       SUM(CASE WHEN S.ISAPPROVED = 1 AND S.STATUS = 5 THEN 1 ELSE 0 END) COMPLETED,
                       SUM(CASE WHEN NVL(S.DISPATCHID, 0) > 0 THEN 1 ELSE 0 END) FORWARDED,
                       SUM(CASE WHEN NVL(S.INSTANTSERVICE, 0) = 1 THEN 1 ELSE 0 END) INSTANT,
                       ROUND(100 * SUM(CASE WHEN S.ISAPPROVED = 1 AND S.STATUS = 5 THEN 1 ELSE 0 END)
                             / NULLIF(SUM(CASE WHEN S.ISAPPROVED = 1 THEN 1 ELSE 0 END), 0), 1) COMPLETEDPCT,
                       ROUND(AVG(CASE WHEN S.STATUS = 5 AND S.RELEASEDATE IS NOT NULL THEN S.RELEASEDATE - S.CREATEDATE END), 1) AVGDAYS
                  FROM SC_SERVICEREQUEST S
                 INNER JOIN SC_SERVICECENTER SC ON SC.ID = S.SERVICECENTERID
                  LEFT JOIN BS_BUSINESSCENTER BSC ON BSC.ID = SC.BUSINESSCENTERID
                  LEFT JOIN EM_EMPLOYEE E ON E.STAFFID = SC.CENTERADMIN
                 WHERE S.ISOPEN = 0 {w}
                 GROUP BY BSC.CENTERCODE, SC.SERVICECENTERNAME, E.EMPLOYEENAME
                 ORDER BY BSC.CENTERCODE, SC.SERVICECENTERNAME";
            return RunReport(sql, p);
        }

        // ================================================================ NEW: SERVICE PERSON PERFORMANCE

        public List<IDictionary<string, object?>> ReportServicePerson(ServiceReportFilter f, string servicePerson)
        {
            var p = new DynamicParameters();
            string w = DateRange("S.CREATEDATE", f, p) + InstantFilter(f, p);
            if (f.LocationId > 0) { w += " AND SC.BUSINESSCENTERID = :Loc"; p.Add("Loc", f.LocationId); }
            if (f.ServiceCenterId > 0) { w += " AND ST.SERVICECENTERID = :Sc"; p.Add("Sc", f.ServiceCenterId); }
            if (servicePerson.Length > 0) { w += " AND SA.TEAMMEMBERID = :Sp"; p.Add("Sp", servicePerson); }

            // the active assignment of every approved request = the person who has (or had) the job
            string sql = $@"
                SELECT SA.TEAMMEMBERID STAFFID, E.EMPLOYEENAME, ST.TEAMNAME, SC.SERVICECENTERNAME,
                       COUNT(*) ASSIGNED,
                       SUM(CASE WHEN S.STATUS = 1 THEN 1 ELSE 0 END) ONPROCESS,
                       SUM(CASE WHEN S.STATUS = 4 THEN 1 ELSE 0 END) REVISE,
                       SUM(CASE WHEN S.STATUS IN (2, 3) THEN 1 ELSE 0 END) WAITINGFEEDBACK,
                       SUM(CASE WHEN S.STATUS = 5 THEN 1 ELSE 0 END) COMPLETED,
                       SUM(NVL(S.NOOFREVISION, 0)) REVISIONS,
                       COUNT(NULLIF(SA.RATINGFROMUSER, 0)) RATED,
                       ROUND(AVG(NULLIF(SA.RATINGFROMUSER, 0)), 2) AVGRATING,
                       ROUND(AVG(CASE WHEN S.STATUS = 5 AND S.RELEASEDATE IS NOT NULL THEN S.RELEASEDATE - SA.ASSIGNDATE END), 1) AVGDAYS
                  FROM SC_SERVICEASSIGN SA
                 INNER JOIN SC_SERVICEREQUEST S ON S.ID = SA.REQUESTID
                 INNER JOIN EM_EMPLOYEE E ON E.STAFFID = SA.TEAMMEMBERID
                  LEFT JOIN SC_SERVICETEAM ST ON ST.ID = SA.TEAMID
                  LEFT JOIN SC_SERVICECENTER SC ON SC.ID = ST.SERVICECENTERID
                 WHERE SA.ISACTIVE = 1 AND S.ISOPEN = 0 AND S.ISAPPROVED = 1 {w}
                 GROUP BY SA.TEAMMEMBERID, E.EMPLOYEENAME, ST.TEAMNAME, SC.SERVICECENTERNAME
                 ORDER BY SC.SERVICECENTERNAME, ST.TEAMNAME, E.EMPLOYEENAME";
            return RunReport(sql, p);
        }

        // ================================================================ NEW: REQUIRED ACCESSORIES

        public List<IDictionary<string, object?>> ReportAccessories(ServiceReportFilter f)
        {
            var p = new DynamicParameters();
            string w = DateRange("S.CREATEDATE", f, p) + CenterFilters(f, p) + StatusFilter(f, p);

            string sql = $@"
                SELECT S.ID, S.SERVICEID, S.CREATEDATE, BSC.CENTERCODE || '-' || SC.SERVICECENTERNAME SERVICECENTER,
                       E.EMPLOYEENAME REQUESTBY, AC.ACCESSORIESCATEGORY, A.ACCESSORIESNAME,
                       RA.QUANTITY, RA.PRICE, NVL(RA.QUANTITY, 0) * NVL(RA.PRICE, 0) AMOUNT, CU.CURRENCYCODE,
                       CASE WHEN RA.ISNEW = 1 THEN 'Yes' ELSE 'No' END NEWITEM,
                       CE.EMPLOYEENAME ADDEDBY, {StatusText} STATUSTEXT
                  FROM SC_REQUIREDACCESSORIES RA
                 INNER JOIN SC_SERVICEREQUEST S ON S.ID = RA.REQUESTID
                 INNER JOIN SC_ACCESSORIES A ON A.ID = RA.ACCESSORIESID
                 INNER JOIN SC_SERVICECENTER SC ON SC.ID = S.SERVICECENTERID
                  LEFT JOIN BS_BUSINESSCENTER BSC ON BSC.ID = SC.BUSINESSCENTERID
                  LEFT JOIN SC_ACCESSORIESCATEGORY AC ON AC.ID = A.CATEGORYID
                  LEFT JOIN ASSET_CURRENCY CU ON CU.ID = RA.CURRENCYID
                  LEFT JOIN EM_EMPLOYEE E ON E.STAFFID = S.FROMSTAFFID
                  LEFT JOIN EM_EMPLOYEE CE ON CE.STAFFID = RA.CREATEDBY
                 WHERE S.ISOPEN = 0 {w}
                 ORDER BY S.CREATEDATE DESC, S.ID DESC, RA.ID";
            return RunReport(sql, p);
        }

        // ================================================================ NEW: MACHINE BREAKDOWN

        public List<IDictionary<string, object?>> ReportMachineBreakdown(ServiceReportFilter f)
        {
            var p = new DynamicParameters();
            // location = where the machine stands (SC_MACHINE.BUSINESSCENTERID)
            string w = DateRange("S.CREATEDATE", f, p) + CenterFilters(f, p, "MCH.BUSINESSCENTERID") + StatusFilter(f, p);

            string sql = $@"
                SELECT S.ID, S.SERVICEID, S.CREATEDATE, MB.CENTERNAME MACHINELOCATION, MCH.MACHINECODE, MCH.MACHINENAME,
                       C.CATEGORYNAME, BSC.CENTERCODE || '-' || SC.SERVICECENTERNAME SERVICECENTER, SAE.EMPLOYEENAME SERVICEMAN,
                       S.BTIMEFROM, S.BTIMETO,
                       CASE WHEN S.BTIMEFROM IS NOT NULL AND S.BTIMETO >= S.BTIMEFROM
                            THEN ROUND((S.BTIMETO - S.BTIMEFROM) * 24, 2) END DOWNHOURS,
                       {StatusText} STATUSTEXT
                  FROM SC_SERVICEREQUEST S
                 INNER JOIN SC_MACHINE MCH ON MCH.ID = S.MACHINEID
                 INNER JOIN SC_SERVICECENTER SC ON SC.ID = S.SERVICECENTERID
                  LEFT JOIN BS_BUSINESSCENTER MB ON MB.ID = MCH.BUSINESSCENTERID
                  LEFT JOIN BS_BUSINESSCENTER BSC ON BSC.ID = SC.BUSINESSCENTERID
                  LEFT JOIN SC_CATEGORY C ON C.ID = S.CATEGORYID
                  LEFT JOIN SC_SERVICEASSIGN SA ON SA.REQUESTID = S.ID AND SA.ISACTIVE = 1
                  LEFT JOIN EM_EMPLOYEE SAE ON SAE.STAFFID = SA.TEAMMEMBERID
                 WHERE S.ISOPEN = 0 AND S.MACHINEID > 0 {w}
                 ORDER BY MB.CENTERNAME, MCH.MACHINENAME, S.CREATEDATE DESC";
            return RunReport(sql, p);
        }
    }
}
