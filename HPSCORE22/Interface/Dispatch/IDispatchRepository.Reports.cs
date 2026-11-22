// Interface/Dispatch/IDispatchRepository.Reports.cs
// Dispatch phase 7 - reports (read only). Each returns at most DispatchRepository.ReportMaxRows rows (column name -> value).
using AlphaCare.Models;

namespace AlphaCare.Interface
{
    public partial interface IDispatchRepository
    {
        /// <summary>Old Pages/Dispatch/DispatchReport.aspx (VW_DISPATCHREPORT)</summary>
        List<IDictionary<string, object?>> ReportDispatchList(DispatchReportFilter f);
        /// <summary>Old DispatchDetails.aspx + LocationWiseDispatchReport.aspx</summary>
        List<IDictionary<string, object?>> ReportDispatchItems(DispatchReportFilter f);
        /// <summary>Old ItemListBySender.aspx / ItemListByReceiver.aspx (f.Mode)</summary>
        List<IDictionary<string, object?>> ReportItemsByStaff(DispatchReportFilter f, string staffId);
        /// <summary>Old LocationWiseDispatchOnlineReport.aspx</summary>
        List<IDictionary<string, object?>> ReportLocationSummary(DispatchReportFilter f);
        /// <summary>Old GatepassStatusReport.aspx (+ optional Created By staff id, "" = everybody)</summary>
        List<IDictionary<string, object?>> ReportGatePassPending(DispatchReportFilter f, string createdBy);
        /// <summary>Old Pages/Dispatch/GatepassStatus.aspx (my own dispatches)</summary>
        List<IDictionary<string, object?>> ReportMyGatePassStatus(string staffId);
        /// <summary>Old LWReport.aspx</summary>
        List<IDictionary<string, object?>> ReportApprovers(DispatchReportFilter f);
        /// <summary>Old LWUserList.aspx</summary>
        List<IDictionary<string, object?>> ReportUsers(DispatchReportFilter f);
    }
}
