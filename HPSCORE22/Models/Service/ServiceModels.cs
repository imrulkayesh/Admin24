// Models/ServiceModels.cs
// Service module (port of YA Pages/Service + App_Code/dal/Service). Column names = YA tables SC_*.
// Setup: SC_SERVICECENTER, SC_CATEGORY, SC_SERVICETEAM, SC_TEAMMEMBER, SC_MACHINE, SC_ACCESSORIESCATEGORY, SC_ACCESSORIES.
// Workflow: SC_SERVICEREQUEST, SC_SERVICEDETAILS, SC_SERVICEASSIGN, SC_REQUIREDACCESSORIES, SC_REQUESTREMARKS.
namespace AlphaCare.Models
{
    // =========================================================================================== status
    public static class ServiceStatus
    {
        // SC_SERVICEREQUEST.STATUS (old dalServiceRequest.RequestStatus)
        public const int WaitingForAssign = 0;
        public const int OnProcess = 1;
        public const int WaitingForUserFeedback = 2;
        public const int WaitingForServicePersonFeedback = 3;
        public const int WaitingForRevise = 4;
        public const int Completed = 5;
        public const int InstantPending = 100;        // old UpdateInstantService: STATUS = 100 until the center admin approves

        public static string Text(int status) => status switch
        {
            0 => "Waiting for Assign",
            1 => "On Process",
            2 => "Waiting for User Feedback",
            3 => "Waiting for Service Person Feedback",
            4 => "Waiting For Revise",
            5 => "Completed",
            _ => ""
        };

        // old pages: ISAPPROVED 0 -> waiting, 2 -> rejected, 1 -> RequestStatus(STATUS)
        public static string Display(int isApproved, int status) => isApproved switch
        {
            0 => "Waiting for Approval",
            2 => "Rejected",
            1 => Text(status),
            _ => ""
        };
    }

    /// <summary>The lists of the old pages. One SQL each in ServiceRepository; also used to check that a user may act on a request.</summary>
    public enum ServiceList
    {
        ApprovalPending,      // old GetApprovalList(0)              Approver.aspx  "Waiting for Approval"
        ApprovalRejected,     // old GetApprovalList(2)              Approver.aspx  "Rejected Request"
        AdminUnassigned,      // old GetByAdminRequest(0, role)      AssignService  "Un-Assigned Service Request"
        AdminAssigned,        // old GetByAdminAssignedRequest(1, r) AssignService  "Assigned Service Request"
        InstantToApprove,     // old GetInstantServicestoApprove     AssignService  "Instant Service Approval"
        MyRequests,           // old MyAllServicesRequest            MyServiceRequest "My Requests"
        MyRejected,           // old MyServicesRequest(2)            MyServiceRequest "Rejected"
        MyWaitingFeedback,    // old MyServicesReq(2)                MyServiceRequest "Waiting for Feedback"
        MyTasks,              // old MyServices(1)                   MyServiceList  "My Services"
        MyRevise,             // old MyServices(4)                   MyServiceList  "Waiting for Revise"
        MyFeedbackToGive,     // old MyServices(3)                   MyServiceList  "Waiting For Feedback"
        SupervisorServices    // old SuperVisorServices              AddAccessories "My Services"
    }

    // =========================================================================================== lookups
    public class ServiceOption
    {
        public long ID { get; set; }
        public string? NAME { get; set; }
    }

    public class ServiceStaffOption
    {
        public string STAFFID { get; set; } = "";
        public string? EMPLOYEENAME { get; set; }
    }

    // old dalServiceCenter.GetServiceCenterAdminDetails (receiver of a forwarded service)
    public class ServiceCenterAdminModel
    {
        public string STAFFID { get; set; } = "";
        public string? EMPLOYEENAME { get; set; }
        public string? OFFICEMOBILE { get; set; }
        public int BUSINESSCENTERID { get; set; }
        public int DEPARTMENTID { get; set; }
        public string? CENTERCODE { get; set; }
        public string? CENTERNAME { get; set; }
        public string? ADDRESS { get; set; }
        public string? DEPARTMENTCODE { get; set; }
    }

    // old GetServiceCenterDetails(staffId): first active SC_TEAMMEMBER row of the user
    public class ServiceMembershipModel
    {
        public int SERVICECENTERID { get; set; }
        public int TEAMID { get; set; }
    }

    // =========================================================================================== setup rows
    public class ServiceCenterModel
    {
        public int ID { get; set; }
        public string? SERVICECENTERNAME { get; set; }
        public int? BUSINESSCENTERID { get; set; }
        public string? CENTERADMIN { get; set; }
        public string? CENTERNAME { get; set; }
        public string? DESIGNATIONNAME { get; set; }
        public string? DEPARTMENTCODE { get; set; }
        public string? EMPLOYEENAME { get; set; }
        public string? OFFICEMOBILE { get; set; }
        public string? OFFICEMAIL { get; set; }
    }

    public class ServiceCategoryModel
    {
        public int ID { get; set; }
        public string? CATEGORYNAME { get; set; }
        public int? SERVICECENTERID { get; set; }
        public int? BUSINESSCENTERID { get; set; }
    }

    public class ServiceTeamModel
    {
        public int ID { get; set; }
        public string? TEAMNAME { get; set; }
        public int? BUSINESSCENTERID { get; set; }
        public int? SERVICECENTERID { get; set; }
        public string? SUPERVISOR { get; set; }
        public string? SERVICECENTERNAME { get; set; }
        public string? CENTERNAME { get; set; }
        public string? DESIGNATIONNAME { get; set; }
        public string? DEPARTMENTCODE { get; set; }
        public string? EMPLOYEENAME { get; set; }
        public string? OFFICEMOBILE { get; set; }
        public string? OFFICEMAIL { get; set; }
    }

    public class ServiceTeamMemberModel
    {
        public int ID { get; set; }
        public string? STAFFID { get; set; }
        public int? TEAMID { get; set; }
        public int? SERVICECENTERID { get; set; }
        public int? BUSINESSCENTERID { get; set; }
        public int? ISACTIVE { get; set; }
        public string? TEAMNAME { get; set; }
        public string? SERVICECENTERNAME { get; set; }
        public string? CENTERNAME { get; set; }
        public string? DESIGNATIONNAME { get; set; }
        public string? DEPARTMENTCODE { get; set; }
        public string? EMPLOYEENAME { get; set; }
        public string? OFFICEMOBILE { get; set; }
        public string? OFFICEMAIL { get; set; }
    }

    public class ServiceMachineModel
    {
        public int ID { get; set; }
        public string? MACHINENAME { get; set; }
        public string? MACHINECODE { get; set; }
        public int? BUSINESSCENTERID { get; set; }
        public int? COMPANYID { get; set; }
        public int? DEPARTMENTID { get; set; }
        public int? ISACTIVE { get; set; }
        public string? CENTERNAME { get; set; }
        public string? COMPANYNAME { get; set; }
        public string? DEPARTMENTNAME { get; set; }
    }

    public class AccessoriesCategoryModel
    {
        public int ID { get; set; }
        public string? ACCESSORIESCATEGORY { get; set; }
    }

    public class ServiceAccessoryModel
    {
        public int ID { get; set; }
        public int? CATEGORYID { get; set; }
        public string? ACCESSORIESNAME { get; set; }
        public int? STOCK { get; set; }
        public int? BUSINESSCENTERID { get; set; }
        public int? SERVICECENTERID { get; set; }
        public string? CENTERCODE { get; set; }
        public string? SERVICECENTERNAME { get; set; }
        public string? ACCESSORIESCATEGORY { get; set; }
    }

    // =========================================================================================== workflow rows
    /// <summary>One row of every request list (columns of the old repeaters).</summary>
    public class ServiceListItemModel
    {
        public long ID { get; set; }
        public string? SERVICEID { get; set; }
        public DateTime? CREATEDATE { get; set; }
        public int STATUS { get; set; }
        public int ISAPPROVED { get; set; }
        public int INSTANTSERVICE { get; set; }
        public int NOOFREVISION { get; set; }
        public string? FROMSTAFFID { get; set; }
        public string? EMPLOYEENAME { get; set; }            // requester
        public string? OFFICEMOBILE { get; set; }
        public string? OFFICEMAIL { get; set; }
        public string? CENTERCODE { get; set; }              // requester location
        public string? CENTERNAME { get; set; }
        public string? DEPARTMENTCODE { get; set; }
        public string? DESIGNATIONNAME { get; set; }
        public string? SERVICECENTERNAME { get; set; }
        public string? SERVICECENTERLOCATION { get; set; }
        public string? CATEGORYNAME { get; set; }
        public string? MACHINE_OUTLET { get; set; }
        public string? SERVICEMAN { get; set; }
        public string? SERVICEMOBILE { get; set; }
        public string? SERVICEEMAIL { get; set; }
        public string? TEAMNAME { get; set; }

        public string StatusText => ServiceStatus.Display(ISAPPROVED, STATUS);
    }

    // old dalServiceRequest.GetRequestDetails
    public class ServiceRequestDetailModel
    {
        public long ID { get; set; }
        public string? SERVICEID { get; set; }
        public DateTime? CREATEDATE { get; set; }
        public int STATUS { get; set; }
        public int ISAPPROVED { get; set; }
        public DateTime? APPROVEDATE { get; set; }
        public int NOOFREVISION { get; set; }
        public int INSTANTSERVICE { get; set; }
        public string? FROMSTAFFID { get; set; }
        public string? CREATEDBY { get; set; }
        public int? SERVICECENTERID { get; set; }
        public int? FRDSERVICECENTERID { get; set; }
        public DateTime? FRDATE { get; set; }
        public long? DISPATCHID { get; set; }
        public string? TRANSACTIONID { get; set; }           // DP_DISPATCH of a forwarded request
        public DateTime? RELEASEDATE { get; set; }
        public DateTime? BTIMEFROM { get; set; }             // machine stop time (SE Complete)
        public DateTime? BTIMETO { get; set; }               // machine start time
        public string? EMPLOYEENAME { get; set; }
        public string? OFFICEMAIL { get; set; }
        public string? OFFICEMOBILE { get; set; }
        public string? CENTERCODE { get; set; }
        public string? DEPARTMENTCODE { get; set; }
        public string? DEPARTMENTNAME { get; set; }
        public string? DESIGNATIONNAME { get; set; }
        public string? CENTERADMIN { get; set; }             // name of the service center admin
        public string? ADMINMOBILE { get; set; }
        public string? SERVICECENTERLOCATIONCODE { get; set; }
        public string? SERVICECENTERLOCATION { get; set; }
        public string? CATEGORYNAME { get; set; }
        public string? SERVICECENTERNAME { get; set; }
        public string? MACHINE_OUTLET { get; set; }
        public string? FRDSERVICECENTERNAME { get; set; }

        public string StatusText => ServiceStatus.Display(ISAPPROVED, STATUS);
    }

    // old GetApproverDetails
    public class ServiceApproverModel
    {
        public string? EMPLOYEENAME { get; set; }
        public string? OFFICEMOBILE { get; set; }
        public string? CENTERCODE { get; set; }
        public string? DEPARTMENTCODE { get; set; }
        public string? DESIGNATIONNAME { get; set; }
        public DateTime? APPROVEDATE { get; set; }
    }

    // SC_SERVICEDETAILS
    public class ServiceItemModel
    {
        public long ID { get; set; }
        public long REQUESTID { get; set; }
        public string? SUBJECT { get; set; }
        public int? QUANTITY { get; set; }
        public string? REMARKS { get; set; }
        public string? ATTACHMENT { get; set; }
    }

    // SC_REQUIREDACCESSORIES + accessory / currency (old GetAccessories / GetAccessoriesByID)
    public class RequiredAccessoryModel
    {
        public long ID { get; set; }
        public long REQUESTID { get; set; }
        public int ACCESSORIESID { get; set; }
        public int? CATEGORYID { get; set; }
        public int? QUANTITY { get; set; }
        public int? PRICE { get; set; }
        public int? CURRENCYID { get; set; }
        public int ISNEW { get; set; }
        public string? ACCESSORIESNAME { get; set; }
        public string? CURRENCYCODE { get; set; }
        public string? NEWITEM { get; set; }
    }

    // SC_REQUESTREMARKS + employee (old GetRemarks)
    public class ServiceRemarkModel
    {
        public long ID { get; set; }
        public string? STAFFID { get; set; }
        public string? REMARKS { get; set; }
        public DateTime? CREATEDATE { get; set; }
        public string? EMPLOYEENAME { get; set; }
        public string? OFFICEMOBILE { get; set; }
        public string? CENTERCODE { get; set; }
        public string? DEPARTMENTCODE { get; set; }
    }

    // SC_SERVICEIMAGE: images of a request (new table - App_Data/Sql/Service_Images.sql)
    public class ServiceImageModel
    {
        public long ID { get; set; }
        public long REQUESTID { get; set; }
        public string? FILENAME { get; set; }          // stored file name in Service:AttachmentFolder
        public string? ORIGINALNAME { get; set; }
        public string? CONTENTTYPE { get; set; }
        public long? FILESIZE { get; set; }
        public string? CREATEDBY { get; set; }
        public DateTime? CREATEDATE { get; set; }
        public string? EMPLOYEENAME { get; set; }      // uploaded by
    }

    // active SC_SERVICEASSIGN row (old GetRatings) + the service person
    public class ServiceAssignModel
    {
        public long ID { get; set; }
        public int? TEAMID { get; set; }
        public string? TEAMMEMBERID { get; set; }
        public DateTime? ASSIGNDATE { get; set; }
        public DateTime? STARTDATE { get; set; }
        public string? STARTTIME { get; set; }
        public DateTime? ENDDATE { get; set; }
        public string? ENDTIME { get; set; }
        public int RATINGFROMUSER { get; set; }
        public int RATINGFROMSERVICEMAN { get; set; }
        public string? EMPLOYEENAME { get; set; }
        public string? OFFICEMOBILE { get; set; }
        public string? TEAMNAME { get; set; }
    }

    // =========================================================================================== view models
    public class ServiceSetupViewModel
    {
        public int CenterId { get; set; }                     // BS_BUSINESSCENTER filter of the list
        public int ServiceCenterId { get; set; }              // SC_SERVICECENTER filter of the list
        public int TeamId { get; set; }
        public List<BusinessCenterModel> BusinessCenters { get; set; } = new();
        public List<ServiceOption> ServiceCenters { get; set; } = new();
        public List<ServiceOption> Teams { get; set; } = new();
        public List<AccessoriesCategoryModel> AccessoryCategories { get; set; } = new();
        public List<DispatchCompanyModel> Companies { get; set; } = new();
        public List<DepartmentModel> Departments { get; set; } = new();
        public int DefaultCompanyId { get; set; }

        public List<ServiceCenterModel> Centers { get; set; } = new();
        public List<ServiceCategoryModel> Categories { get; set; } = new();
        public List<ServiceTeamModel> TeamRows { get; set; } = new();
        public List<ServiceTeamMemberModel> Members { get; set; } = new();
        public List<ServiceMachineModel> Machines { get; set; } = new();
        public List<ServiceAccessoryModel> Accessories { get; set; } = new();
    }

    /// <summary>One tab of a list page (rendered by Views/Service/_ServiceList.cshtml).</summary>
    public class ServiceListSection
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Icon { get; set; } = "fa-list";
        public string Color { get; set; } = "#4f46e5";
        public List<ServiceListItemModel> Rows { get; set; } = new();
        /// <summary>Row buttons, e.g. "approve", "reject", "assign", "forward". "view" is always shown.</summary>
        public List<string> Actions { get; set; } = new();
        public bool ShowServiceman { get; set; }
    }

    public class ServiceListPageViewModel
    {
        public string Title { get; set; } = "";
        public string Icon { get; set; } = "fa-list";
        public List<ServiceListSection> Sections { get; set; } = new();
        public string? Note { get; set; }

        // AssignService pop-ups (old mpAssign / mpForward)
        public List<ServiceOption> AssignServiceCenters { get; set; } = new();
        public List<BusinessCenterModel> BusinessCenters { get; set; } = new();
        public List<DispatchIssueTypeModel> IssueTypes { get; set; } = new();
    }

    public class NewServiceViewModel
    {
        public long RequestId { get; set; }
        public int CenterId { get; set; }
        public List<BusinessCenterModel> BusinessCenters { get; set; } = new();
        public List<ServiceOption> Machines { get; set; } = new();
        public List<ServiceItemModel> Items { get; set; } = new();
        public List<ServiceImageModel> Images { get; set; } = new();
        public bool AttachmentsEnabled { get; set; }
    }

    public class InstantServiceViewModel
    {
        public long RequestId { get; set; }
        public ServiceMembershipModel? Membership { get; set; }
        public List<ServiceOption> Categories { get; set; } = new();
        public List<ServiceOption> Machines { get; set; } = new();
        public List<BusinessCenterModel> BusinessCenters { get; set; } = new();
        public List<DepartmentModel> Departments { get; set; } = new();
        public List<AccessoriesCategoryModel> AccessoryCategories { get; set; } = new();
        public List<CurrencyModel> Currencies { get; set; } = new();
        public List<ServiceItemModel> Items { get; set; } = new();
        public List<RequiredAccessoryModel> Accessories { get; set; } = new();
        public DispatchEmployeeModel? Me { get; set; }
        public bool AttachmentsEnabled { get; set; }
    }

    public class ServiceDetailsViewModel
    {
        public ServiceRequestDetailModel Request { get; set; } = new();
        public ServiceApproverModel? Approver { get; set; }
        public ServiceAssignModel? Assign { get; set; }
        public List<ServiceItemModel> Items { get; set; } = new();
        public List<RequiredAccessoryModel> Accessories { get; set; } = new();
        public List<ServiceRemarkModel> Remarks { get; set; } = new();
        public List<ServiceImageModel> Images { get; set; } = new();
        public bool CanAddImages { get; set; }                // people on the request, until it is completed

        // what the logged in user may do here (each one = the list rule of the old page that offered the button)
        public bool CanApprove { get; set; }                  // approver: pending or rejected (re-approve)
        public bool CanRejectApproval { get; set; }           // approver: pending only
        public bool CanAccept { get; set; }                   // requester, STATUS 2: Accept / Reject (revise) / rate the service
        public bool CanComplete { get; set; }                 // service person, STATUS 1: old SEComplete.aspx
        public bool CanRateServiceman { get; set; }           // service person, STATUS 3: old Ratings.aspx
        public bool CanDone { get; set; }                     // old ServiceRemarks.aspx "Done"
        public bool CanEditAccessories { get; set; }          // team supervisor: old Details.aspx
        public bool CanApproveInstant { get; set; }           // center admin: old InstantServiceDetails.aspx
        public bool CanRemark { get; set; } = true;           // old "Update Remarks"

        public List<AccessoriesCategoryModel> AccessoryCategories { get; set; } = new();
        public List<CurrencyModel> Currencies { get; set; } = new();
        public bool AttachmentsEnabled { get; set; }
    }
}
