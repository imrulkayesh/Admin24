// Interface/Dispatch/IDispatchRepository.cs
// Port of YA App_Code/dal/Dispatch (dalDispatch, dalDispatchGlobal, dalApprover) - phase 1: create + approval.
using AlphaCare.Models;

namespace AlphaCare.Interface
{
    public partial interface IDispatchRepository
    {
        // ---------------- lookups ----------------
        /// <summary>DP_ISSUETYPE WHERE ISACTIVE = 1 ORDER BY ID DESC</summary>
        List<DispatchIssueTypeModel> GetIssueTypes();
        DispatchIssueTypeModel? GetIssueType(int issueTypeId);
        /// <summary>BS_BUSINESSCENTER WHERE LOCATIONSTATUS = 1 ORDER BY CENTERNAME</summary>
        List<BusinessCenterModel> GetBusinessCenters();
        List<DepartmentModel> GetDepartments();
        /// <summary>Old dalDispatch.GetEmployeeDetails(staffId)</summary>
        DispatchEmployeeModel? GetEmployee(string staffId);
        /// <summary>Old dalEmployee.GetEmployeeByDepartment(center, department)</summary>
        List<DispatchEmployeeModel> GetEmployeesByDepartment(int businessCenterId, int departmentId);
        /// <summary>Employee auto complete: "STAFFID-CENTERCODE-DEPARTMENTCODE-DESIGNATION-NAME" (max 20)</summary>
        List<string> SearchEmployees(string term);
        /// <summary>Item auto complete: DISTINCT DP_DISPATCHITEMS.ITEMNAME (max 20)</summary>
        List<string> SearchItems(string term);

        // ---------------- create (draft) ----------------
        /// <summary>Old CheckDispatch: the open draft (ISOPEN = 1) of the user, created when missing</summary>
        long GetOrCreateDraft(string staffId);
        /// <summary>True when the dispatch is the user's own open draft (ISOPEN = 1, CREATEDBY = user)</summary>
        bool IsOwnDraft(long dispatchId, string staffId);
        List<DispatchItemModel> GetItems(long dispatchId);
        DispatchItemModel? GetItem(long itemId);
        /// <summary>True when the dispatch already has an item with this name (then only the quantity is added)</summary>
        bool DraftHasItem(long dispatchId, string itemName);
        /// <summary>Old InsertDispatchItems: same item name in the dispatch -> quantity is added, otherwise a new row</summary>
        int AddDraftItem(long dispatchId, string staffId, string itemName, decimal quantity, string unit, string notes, string attachment);
        /// <summary>Old UpdateDispatchItems - only items of the user's own open draft</summary>
        int UpdateDraftItem(long itemId, string staffId, decimal quantity, string unit, string notes);
        /// <summary>Old Common.Delete("DP_DISPATCHITEMS") - only items of the user's own open draft</summary>
        int DeleteDraftItem(long itemId, string staffId);
        /// <summary>Old dalDispatchGlobal.Update: submit the draft (TRANSACTIONID, STATUSID = 1, first transit "Final Approver")</summary>
        string? SubmitDispatch(SubmitDispatchModel m, string staffId, string userId, int userBusinessCenterId);
        /// <summary>Old NewWaitingApprovalList: STATUSID = 1 created by me or sent by me</summary>
        List<DispatchListItemModel> GetMyWaitingList(string staffId);

        // ---------------- details ----------------
        DispatchDetailModel? GetDispatchDetails(long dispatchId);
        List<DispatchTransitModel> GetTransits(long dispatchId);
        List<DispatchTransportModel> GetTransports(long dispatchId);
        /// <summary>Creator, sender, receiver, or an approver of the creator's location / department / company</summary>
        bool CanViewDispatch(long dispatchId, string staffId);

        // ---------------- approval ----------------
        List<DispatchListItemModel> GetPendingApprovals(string approverStaffId);
        List<DispatchListItemModel> GetApprovedByMe(string approverStaffId);
        List<DispatchListItemModel> GetRejectedForMe(string approverStaffId);
        /// <summary>True when the dispatch is in the approver's pending list (STATUSID = his PRIORITY)</summary>
        bool IsPendingForApprover(long dispatchId, string approverStaffId);
        /// <summary>True when the dispatch is in the approver's rejected list (STATUSID = PRIORITY + 98)</summary>
        bool IsRejectedForApprover(long dispatchId, string approverStaffId);
        /// <summary>Old ApproveDispatch(Global). Returns the step text written to DP_TRANSITS, null when nothing changed</summary>
        string? Approve(long dispatchId, string approverStaffId, string remarks);
        /// <summary>Old RejectDispatch(Global): STATUSID + 98</summary>
        bool Reject(long dispatchId, string approverStaffId, string remarks);
        /// <summary>Old ApproveRejected: STATUSID - 97. Returns the step text, null when nothing changed</summary>
        string? ApproveRejected(long dispatchId, string approverStaffId, string remarks);
        /// <summary>Old UpdateTransitRemarks(Global): remarks of the active step</summary>
        int UpdateTransitRemarks(long dispatchId, string staffId, string remarks);
        /// <summary>Old ApproverView delete item - only while the dispatch waits for this approver</summary>
        int DeleteItemAsApprover(long itemId, string approverStaffId);

        // ---------------- mail ----------------
        DispatchMailInfoModel? GetMailInfo(long dispatchId);
        /// <summary>Old dalApprover.GetByCreatedby: OFFICEMAIL of all approvers of the creator</summary>
        List<string> GetApproverMails(string createdByStaffId);

        // ---------------- phase 2: print label (old PrintLebel.aspx / PrintLebelPOS.aspx) ----------------
        /// <summary>Old dalApprover.ApprovedList: STATUSID > 1, GATEPASS >= 0, created or sent by me, last 180 days</summary>
        List<DispatchListItemModel> GetPrintList(string staffId);
        /// <summary>Same rule as the print list (without the date window)</summary>
        bool CanPrint(long dispatchId, string staffId);
        /// <summary>Old dalDispatch.UpdatePrint: NOOFPRINT + 1</summary>
        int MarkPrinted(long dispatchId, string staffId);

        // ---------------- phase 2: gate check (old GateCheck.aspx / GateCheckGlobal.aspx) ----------------
        /// <summary>Old dalCommon.IsPermitted: one of the user's roles has the menu task with this url (UC_TASKPERMISSION_COPY)</summary>
        bool HasMenuPermission(string staffId, string url);
        /// <summary>Old GetGatemen: DP_ASSIGNGATEMAN.GATENO of the user, 1 when not assigned</summary>
        int GetGateNo(string staffId);
        /// <summary>Old GateCheckList: STATUSID IN (2,3,6,7), not gate passed, creator in my location, last 10 days</summary>
        List<DispatchListItemModel> GetGateCheckList(int businessCenterId);
        /// <summary>Old dalDispatch.GateCheck: dispatch id of a submitted dispatch (STATUSID 2..13), null when not found</summary>
        long? FindDispatchForGate(string transactionId);
        /// <summary>Old InsertGatepass(Global): GATEPASS = 1, step "CENTER-Gate#n" + DP_GATECHECKING row. False when nothing changed</summary>
        bool GatePassDispatch(long dispatchId, string staffId, int gateNo);
        /// <summary>Old RejectDispatchGate(Global): step "Gate Rejected", STATUSID + 98 - only dispatches of the gate list rule</summary>
        bool GateRejectDispatch(long dispatchId, string staffId, int businessCenterId);
        /// <summary>Old dalGateEntry.GateCheck: gate entry ("R...") not passed yet</summary>
        GateEntryModel? FindGateEntry(string gateEntryId);
        List<GateEntryItemModel> GetGateEntryItems(long gateEntryId);
        /// <summary>Old dalGateEntry.GatePass: GATEPASSED = 1, GATENO, OUTDATETIME. False when nothing changed</summary>
        bool GatePassEntry(string gateEntryId, string staffId, int gateNo);

        // ---------- phase 3: dispatch desk (old DispatchDesk.aspx / DispatchView.aspx / Details.aspx) ----------
        /// <summary>Old GetDispatchSendingList: STATUSID 2/6, not personal, not direct, creator at my location, last 7 days</summary>
        List<DispatchListItemModel> GetDeskSendList(int businessCenterId);
        /// <summary>Old GetDispatchReceiveList: STATUSID 3/7, not direct, receiver (or sender when no receiver staff) at my location</summary>
        List<DispatchListItemModel> GetDeskReceiveList(int businessCenterId);
        /// <summary>"send", "receive" or null for this dispatch at the user's location</summary>
        string? GetDeskAction(long dispatchId, int businessCenterId);
        /// <summary>Old UpdateTransit: close the step, add the next step, STATUSID + 1. Returns the new step or null</summary>
        string? DeskForward(long dispatchId, string staffId, int businessCenterId, string expectedAction, string remarks);
        /// <summary>Old InsertReceive (scan on the desk): closed step "&lt;CENTERCODE&gt;-Dispatch" / "Received". Returns the dispatch id or null</summary>
        long? DeskScanReceive(string transactionId, string staffId);

        // ---------- phase 3: transport (old Transport.aspx + transport part of DispatchView.aspx) ----------
        List<TransportModel> GetTransportTypes();
        TransportModel? GetTransportType(int id);
        List<CurrencyModel> GetCurrencies();
        bool TransportNameExists(string name, int exceptId);
        int InsertTransport(string name, bool isExternal);
        int UpdateTransport(int id, string name, bool isExternal);
        /// <summary>Old InsertTransportDetails - only while the dispatch is in the send rule of the user's location</summary>
        bool AddTransportDetail(long dispatchId, int businessCenterId, int transportId, int currencyId, long cost, string driver, string deliveryMan, string mobile);

        // ---------- phase 4: receive + return (old Receive.aspx / ReceiverView.aspx / ReturnView.aspx) ----------
        /// <summary>Old GetReceiveList: TOUSER = me, STATUSID 2/4/8, not personal, created from 01/08/2024</summary>
        List<DispatchListItemModel> GetMyReceiveList(string staffId);
        /// <summary>Old GetReturnList: FROMUSER = me, returnable, STATUSID 5 (not direct) or 3 (direct)</summary>
        List<DispatchListItemModel> GetMyReturnList(string staffId);
        /// <summary>"receive", "return" or null for this dispatch and user</summary>
        string? GetMyAction(long dispatchId, string staffId);
        /// <summary>Old UpdateTransit by the receiver. Returns the new step or null</summary>
        string? MyReceive(long dispatchId, string staffId, string remarks);
        /// <summary>Old NormalReturn (direct = false) / DirectReturn (direct = true). Returns the new step or null</summary>
        string? ReturnDispatch(long dispatchId, string staffId, string remarks, bool direct);

        // ---------- phase 5: tracking (old Tracking.aspx) ----------
        /// <summary>Old GetTrackingList: FROMUSER or TOUSER = me, not completed, not personal</summary>
        List<TrackingListItemModel> GetTrackingList(string staffId);
        /// <summary>Old TrakckingDetails: the submitted dispatch with this transaction id, or null</summary>
        TrackingHeaderModel? FindForTracking(string transactionId);
    }
}
