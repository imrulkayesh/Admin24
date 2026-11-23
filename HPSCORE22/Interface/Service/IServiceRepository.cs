// Interface/Service/IServiceRepository.cs
// Port of YA App_Code/dal/Service (dalServiceRequest, dalServiceCenter, dalServiceTeam, dalServiceCategory,
// dalServiceAccessories, dalAccessoriesCategory, dalMachine). Same SC_* tables, columns, SQ_SC_* sequences and status numbers.
using AlphaCare.Models;

namespace AlphaCare.Interface
{
    public partial interface IServiceRepository
    {
        // ---------------- permission ----------------
        /// <summary>Old dalCommon.IsPermitted: one of the user's roles has the menu task with this url (super admin ROLEID 1 always)</summary>
        bool HasMenuPermission(string staffId, string url);

        // ---------------- lookups ----------------
        /// <summary>Old Common().GetAll("BS_BUSINESSCENTER") - every location</summary>
        List<BusinessCenterModel> GetAllBusinessCenters();
        /// <summary>Old dalServiceCenter.GetByBusinessCenerId</summary>
        List<ServiceOption> GetServiceCentersByCenter(int businessCenterId);
        /// <summary>Old dalServiceCategory.GetServiceCenterId</summary>
        List<ServiceOption> GetCategoriesByServiceCenter(int serviceCenterId);
        /// <summary>Old dalServiceRequest.GetMachineList: machines of the user's location + company, "CODE-NAME"</summary>
        List<ServiceOption> GetMachinesFor(int businessCenterId, int companyId);
        /// <summary>Old Common().GetAll("SC_ACCESSORIESCATEGORY")</summary>
        List<AccessoriesCategoryModel> GetAccessoryCategories();
        /// <summary>Old dalServiceAccessories.GetByServiceCenterAndCategory: accessories of the category in the service centers the user supervises</summary>
        List<ServiceOption> GetAccessoriesForSupervisor(int categoryId, string staffId);
        /// <summary>Subject auto complete: DISTINCT SC_SERVICEDETAILS.SUBJECT (max 20)</summary>
        List<string> SearchSubjects(string term);

        // ---------------- assign / forward lookups (AssignService.aspx) ----------------
        /// <summary>Centers where the user is the center admin or supervises a team</summary>
        List<ServiceOption> GetServiceCentersForAssign(string staffId);
        /// <summary>Center admin: all teams of the center; team supervisor: the teams he supervises</summary>
        List<ServiceOption> GetTeamsForAssign(int serviceCenterId, string staffId);
        /// <summary>Active members + supervisor of a team the user may assign to</summary>
        List<ServiceStaffOption> GetMembersForAssign(int serviceCenterId, int teamId, string staffId);
        /// <summary>Old dalServiceCenter.GetServiceCenterAdminDetails</summary>
        ServiceCenterAdminModel? GetServiceCenterAdmin(int serviceCenterId);
        /// <summary>Old GetServiceCenterDetails(staffId): first active SC_TEAMMEMBER row (instant service)</summary>
        ServiceMembershipModel? GetMembership(string staffId);

        // ---------------- lists ----------------
        List<ServiceListItemModel> GetList(ServiceList list, string staffId, int roleId);
        /// <summary>True when the request is in that list of the user (the rule of the old page that showed the button)</summary>
        bool InList(ServiceList list, long requestId, string staffId, int roleId);

        // ---------------- draft (old CheckRequest: one open request per user, shared by New Service and Instant Service) ----------------
        long GetOrCreateDraft(string staffId);
        bool IsOwnDraft(long requestId, string staffId);
        List<ServiceItemModel> GetItems(long requestId);
        ServiceItemModel? GetItem(long itemId);
        bool DraftHasItem(long requestId, string subject);
        /// <summary>Old InsertServiceDetails: same subject -> quantity added (2), otherwise new row (1); 0 = not the user's open draft</summary>
        int AddDraftItem(long requestId, string staffId, string subject, int quantity, string remarks, string attachment);
        /// <summary>Old UpdateService - only items of the user's own open draft</summary>
        int UpdateDraftItem(long itemId, string staffId, int quantity, string remarks);
        /// <summary>Old dalServiceRequest.Update: the draft becomes a request. Returns SERVICEID, null when it is not the user's open draft</summary>
        string? SubmitRequest(long requestId, string staffId, int serviceCenterId, int categoryId, int machineId, int fromCenterId, int fromDepartmentId);
        /// <summary>Old UpdateInstantService + AssignInstantService (one transaction). Returns SERVICEID, null when not the user's open draft</summary>
        string? SubmitInstant(long requestId, string staffId, string fromStaffId, int serviceCenterId, int categoryId, int machineId,
                              int fromCenterId, int fromDepartmentId, int teamId, DateTime startDate, string startHour, DateTime endDate, string endHour);

        // ---------------- details ----------------
        ServiceRequestDetailModel? GetRequestDetails(long requestId);
        ServiceApproverModel? GetApproverDetails(long requestId);
        ServiceAssignModel? GetActiveAssign(long requestId);
        List<RequiredAccessoryModel> GetRequiredAccessories(long requestId);
        RequiredAccessoryModel? GetRequiredAccessory(long id);
        List<ServiceRemarkModel> GetRemarks(long requestId);
        /// <summary>Requester / creator / approver / center admin / team supervisor / assigned service person</summary>
        bool CanView(long requestId, string staffId);

        // ---------------- actions (callers check InList first) ----------------
        int ApproveRequest(long requestId, string staffId);
        int RejectRequest(long requestId, string staffId);
        int ApproveInstant(long requestId, string staffId);
        /// <summary>Old AssignService: old assignments inactive, new SC_SERVICEASSIGN row, STATUS = 1</summary>
        void AssignService(long requestId, int teamId, string memberStaffId, DateTime startDate, string startHour, DateTime endDate, string endHour);
        /// <summary>Old ForwardService (+ UpdateServiceAssignStatus): STATUS = 0, DISPATCHID, FRDSERVICECENTERID, FRDATE</summary>
        void ForwardService(long requestId, long dispatchId, int serviceCenterId);
        /// <summary>Old UpdateServiceStatus: RELEASEDATE for 3 and 5</summary>
        int UpdateStatus(long requestId, int status, string staffId);
        /// <summary>Old UpdateSEDone: STATUS = 2 + machine stop / start time</summary>
        int CompleteService(long requestId, DateTime? stopTime, DateTime? startTime, string staffId);
        /// <summary>Old UpdateServiceRevision(id, 4): NOOFREVISION + 1</summary>
        int ReviseRequest(long requestId);
        /// <summary>Old RatingFromUser + UpdateServiceStatus(3)</summary>
        void RateByUser(long requestId, int rating, string staffId);
        /// <summary>Old RatingFromServiceMan + UpdateServiceStatus(5)</summary>
        void RateByServiceman(long requestId, int rating, string staffId);
        /// <summary>Old UpdateRemarks: new SC_REQUESTREMARKS row</summary>
        int AddRemark(long requestId, string staffId, string remarks);

        // ---------------- required accessories (old Details.aspx / InstantService.aspx) ----------------
        bool RequiredAccessoryExists(long requestId, int accessoryId, long exceptId);
        int InsertRequiredAccessory(long requestId, int accessoryId, int quantity, int price, int currencyId, int isNew, string staffId);
        int UpdateRequiredAccessory(long id, long requestId, int accessoryId, int quantity, int price, int currencyId, int isNew);
        /// <summary>Old InstantService delete - only rows of the user's own open draft</summary>
        int DeleteDraftAccessory(long id, string staffId);
    }
}
