// Interface/Service/IServiceRepository.Reports.cs
// Service reports (read only). Each list returns at most ServiceRepository.ReportMaxRows rows (column name -> value).
using AlphaCare.Models;

namespace AlphaCare.Interface
{
    public partial interface IServiceRepository
    {
        /// <summary>All service centers as "CENTERCODE-NAME" (report filter)</summary>
        List<ServiceOption> GetAllServiceCenterOptions();

        /// <summary>Old Reports/Service/Default.aspx: VW_SERVICECENTER (same view, same numbers as YA)</summary>
        List<ServiceCenterSummaryRow> GetServiceCenterSummary();
        /// <summary>Old Reports/Service/ServiceCenter.aspx: VW_SERVICECENTER as a table</summary>
        List<IDictionary<string, object?>> ReportServiceCenter();
        /// <summary>Old Reports/Service/ServiceRequest.aspx (VW_SERVICEREQUEST) with its search filters working</summary>
        List<IDictionary<string, object?>> ReportServiceRequests(ServiceReportFilter f, string fromStaff, string servicePerson);
        /// <summary>New: status breakdown per service center for a date range</summary>
        List<IDictionary<string, object?>> ReportCenterStatus(ServiceReportFilter f);
        /// <summary>New: service person performance (assigned / completed / rating / days) for a date range</summary>
        List<IDictionary<string, object?>> ReportServicePerson(ServiceReportFilter f, string servicePerson);
        /// <summary>New: required accessories (spare parts) per request for a date range</summary>
        List<IDictionary<string, object?>> ReportAccessories(ServiceReportFilter f);
        /// <summary>New: machine breakdown time (SE Complete stop / start) for a date range</summary>
        List<IDictionary<string, object?>> ReportMachineBreakdown(ServiceReportFilter f);
    }
}
