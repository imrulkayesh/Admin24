// Models/DispatchModels.cs
// Dispatch module (port of YA Pages/Dispatch + App_Code/dal/Dispatch). Column names = YA tables DP_*.
// Phase 1: Create Dispatch, Approval (single + bulk), Approver view / Dispatch view.
namespace AlphaCare.Models
{
    // DP_ISSUETYPE (drop down + the flags that decide which receiver panel is used)
    public class DispatchIssueTypeModel
    {
        public int ID { get; set; }
        public string ISSUETYPENAME { get; set; } = "";
        public int RETURNABLE { get; set; }
        public int ISEXTERNAL { get; set; }
        public int ISPERSONAL { get; set; }
    }

    // BS_DEPARTMENT drop down
    public class DepartmentModel
    {
        public int ID { get; set; }
        public string? DEPARTMENTNAME { get; set; }
        public string? DEPARTMENTCODE { get; set; }
    }

    // EM_EMPLOYEE (+ center / department) = old dalDispatch.GetEmployeeDetails(staffId)
    public class DispatchEmployeeModel
    {
        public string STAFFID { get; set; } = "";
        public string? EMPLOYEENAME { get; set; }
        public string? OFFICEMOBILE { get; set; }
        public string? OFFICEMAIL { get; set; }
        public int? BUSINESSCENTERID { get; set; }
        public int? DEPARTMENTID { get; set; }
        public int? COMPANYID { get; set; }
        public string? CENTERNAME { get; set; }
        public string? CENTERCODE { get; set; }
        public string? DEPARTMENTCODE { get; set; }
        public string? DESIGNATIONNAME { get; set; }
    }

    // DP_DISPATCHITEMS
    public class DispatchItemModel
    {
        public long ID { get; set; }
        public long DISPATCHID { get; set; }
        public string? ITEMNAME { get; set; }
        public decimal? QUANTITY { get; set; }
        public string? UNIT { get; set; }
        public string? NOTES { get; set; }
        public string? ATTACHMENT { get; set; }
    }

    // one row of the "Waiting for Approval" list on the create page and of the approver lists
    public class DispatchListItemModel
    {
        public long ID { get; set; }
        public string? TRANSACTIONID { get; set; }
        public DateTime? CREATEDATE { get; set; }
        public int? STATUSID { get; set; }
        public string? ISSUETYPENAME { get; set; }
        public string? EMPLOYEENAME { get; set; }          // sender (EM_EMPLOYEE of FROMUSER)
        public string? RECEIVERNAME { get; set; }
        public string? RECEIVERMOBILE { get; set; }
        public string? RECEIVERDEPARTMENT { get; set; }
        public string? BUSINESSCENTER { get; set; }        // receiver location (text)
        public string? RECEIVERADDRESS { get; set; }
        public string? CREATEDUSER { get; set; }           // EM_EMPLOYEE of CREATEDBY
        public int? NOOFPRINT { get; set; }                // print label list only

        public string ReceiverPlace =>
            string.Join(" / ", new[] { BUSINESSCENTER, RECEIVERDEPARTMENT, RECEIVERADDRESS }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    // header of the details page = old dalDispatch.GetDispatchDetails(id)
    public class DispatchDetailModel
    {
        public long ID { get; set; }
        public string? TRANSACTIONID { get; set; }
        public int? STATUSID { get; set; }
        public int? ISOPEN { get; set; }
        public int? ISSUETYPEID { get; set; }
        public string? ISSUETYPENAME { get; set; }
        public int ISEXTERNAL { get; set; }
        public int ISPERSONAL { get; set; }
        public int RETURNABLE { get; set; }
        public int? DIRECTSEND { get; set; }
        public string? CREATEDBY { get; set; }
        public string? FROMUSER { get; set; }
        public string? TOUSER { get; set; }
        public DateTime? CREATEDATE { get; set; }
        public string? SENDNAME { get; set; }
        public string? SENDMOBILE { get; set; }
        public string? RECEIVERNAME { get; set; }
        public string? RECEIVERMOBILE { get; set; }
        public string? RECEIVERADDRESS { get; set; }
        public string? RECEIVERDEPARTMENT { get; set; }
        public string? BUSINESSCENTER { get; set; }
        public int? NOOFPRINT { get; set; }
        public int? GATEPASS { get; set; }                 // 1 = gate checked for the current leg

        // sender employee (EM_EMPLOYEE of FROMUSER) + his center / department
        public string? EMPLOYEENAME { get; set; }
        public string? OFFICEMOBILE { get; set; }
        public string? OFFICEMAIL { get; set; }
        public string? CENTERCODE { get; set; }
        public string? CENTERNAME { get; set; }
        public string? SENDERADDRESS { get; set; }
        public string? DEPARTMENTCODE { get; set; }
        public string? DEPARTMENTNAME { get; set; }
        public string? DESIGNATIONNAME { get; set; }

        // receiver location = TB.CENTERCODE || '-' || TB.ADDRESS
        public string? ADDRESS { get; set; }
    }

    // DP_TRANSITS (+ the employee who updated the step) = old GetTransitDetails
    public class DispatchTransitModel
    {
        public long ID { get; set; }
        public string? STEP { get; set; }
        public DateTime? RECEIVEDATE { get; set; }
        public DateTime? RELEASEDATE { get; set; }
        public string? REMARKS { get; set; }
        public string? UPDATEDBY { get; set; }
        public int? ISACTIVE { get; set; }
        public string? EMPLOYEENAME { get; set; }
        public string? CENTERCODE { get; set; }
        public string? DEPARTMENTCODE { get; set; }
    }

    // DP_TRANSPORTDETAILS + DP_TRANSPORT + ASSET_CURRENCY = old GetTransportDetails(id)
    public class DispatchTransportModel
    {
        public long ID { get; set; }
        public string? TRANSPORTNAME { get; set; }
        public string? DRIVER { get; set; }
        public string? DELIVERYMAN { get; set; }
        public string? MOBILE { get; set; }
        public decimal? COST { get; set; }
        public string? CURRENCYCODE { get; set; }
    }

    // values the old DispatchStatus(...) needed to build the text of the next step
    public class DispatchStatusSourceModel
    {
        public int STATUSID { get; set; }
        public int DIRECTSEND { get; set; }
        public int ISPERSONAL { get; set; }
        public int ISEXTERNAL { get; set; }
        public int RETURNABLE { get; set; }
        public string? SENDNAME { get; set; }
        public string? CENTERCODE { get; set; }           // sender location code
        public string? DEPARTMENTCODE { get; set; }       // sender department code
        public string? BUSINESSCENTER { get; set; }       // receiver location (text)
        public string? RECEIVERDEPARTMENT { get; set; }
        public string? RECEIVERNAME { get; set; }
    }

    // old dalDispatch.DispatchMailDetails (header part)
    public class DispatchMailInfoModel
    {
        public long ID { get; set; }
        public string? TRANSACTIONID { get; set; }
        public string? CREATEDBY { get; set; }
        public string? BUSINESSCENTER { get; set; }
        public string? RECEIVERNAME { get; set; }
        public string? RECEIVERMOBILE { get; set; }
        public string? EMPLOYEENAME { get; set; }        // sender
        public string? OFFICEMAIL { get; set; }          // sender mail
        public string? OFFICEMOBILE { get; set; }        // sender mobile
        public string? TOMAIL { get; set; }              // receiver mail (internal receiver only)
        public List<DispatchItemModel> Items { get; set; } = new();
    }

    // ---------------------------------------------------------------- page models

    public class CreateDispatchViewModel
    {
        public long DispatchId { get; set; }                                    // the open draft (DP_DISPATCH.ISOPEN = 1) of the user
        public List<DispatchIssueTypeModel> IssueTypes { get; set; } = new();
        public List<BusinessCenterModel> BusinessCenters { get; set; } = new();
        public List<DepartmentModel> Departments { get; set; } = new();
        public List<DispatchItemModel> Items { get; set; } = new();
        public List<DispatchListItemModel> Waiting { get; set; } = new();       // old rptApproved (NewWaitingApprovalList)
        public DispatchEmployeeModel? Me { get; set; }                          // default sender = logged in user
        public bool AttachmentsEnabled { get; set; }
    }

    // posted by the Save button of the create page
    public class SaveDispatchRequest
    {
        public long DispatchId { get; set; }
        public int IssueTypeId { get; set; }
        public bool DirectSend { get; set; }

        public string? SenderStaffId { get; set; }
        public int? SenderCenterId { get; set; }
        public int? SenderDepartmentId { get; set; }
        public string? SenderName { get; set; }
        public string? SenderMobile { get; set; }

        // internal receiver
        public string? ReceiverStaffId { get; set; }
        public int? ReceiverCenterId { get; set; }
        public int? ReceiverDepartmentId { get; set; }
        public string? ReceiverName { get; set; }
        public string? ReceiverMobile { get; set; }

        // external receiver
        public string? ExtReceiverName { get; set; }
        public string? ExtReceiverMobile { get; set; }
        public string? ExtReceiverAddress { get; set; }
    }

    // values written by DispatchRepository.SubmitDispatch (= old dalDispatch(Global).Update parameters)
    public class SubmitDispatchModel
    {
        public long DispatchId { get; set; }
        public string ReceiverStaffId { get; set; } = "0";
        public string SenderStaffId { get; set; } = "0";
        public string ReceiverName { get; set; } = "";
        public string ReceiverMobile { get; set; } = "";
        public string ReceiverAddress { get; set; } = "";
        public string ReceiverDepartment { get; set; } = "";
        public string ReceiverCenter { get; set; } = "";
        public int IssueTypeId { get; set; }
        public int SenderDepartmentId { get; set; }
        public int SenderCenterId { get; set; }
        public int ReceiverDepartmentId { get; set; }
        public int ReceiverCenterId { get; set; }
        public int DirectSend { get; set; }
        public string SenderName { get; set; } = "";
        public string SenderMobile { get; set; } = "";
    }

    public class DispatchApprovalViewModel
    {
        public List<DispatchListItemModel> Pending { get; set; } = new();       // old GetApprovalList
        public List<DispatchListItemModel> Approved { get; set; } = new();      // old GetApprovedList (last 3 days, approved by me)
        public List<DispatchListItemModel> Rejected { get; set; } = new();      // old GetRejectedList (last 180 days)
    }

    public class DispatchDetailsViewModel
    {
        public DispatchDetailModel Dispatch { get; set; } = new();
        public List<DispatchItemModel> Items { get; set; } = new();
        public List<DispatchTransitModel> Transits { get; set; } = new();
        public List<DispatchTransportModel> Transports { get; set; } = new();
        public bool CanApprove { get; set; }                 // logged in user is the approver of this pending dispatch
        public bool AttachmentsEnabled { get; set; }

        // phase 3 - opened from the dispatch desk: "send" (old DispatchView.aspx: transport + Send) or "receive" (old Details.aspx: Receive)
        public string? DeskAction { get; set; }
        public List<TransportModel> TransportTypes { get; set; } = new();
        public List<CurrencyModel> Currencies { get; set; } = new();

        // phase 4 - the user's own part: "receive" (old ReceiverView.aspx) or "return" (old ReturnView.aspx: Return / Direct Return)
        public string? MyAction { get; set; }

        // old View/ApproverView: external -> RECEIVERADDRESS, personal -> no receiver box, otherwise ADDRESS
        public bool ShowReceiver => Dispatch.ISPERSONAL != 1 || Dispatch.ISEXTERNAL == 1;
        public string ReceiverLocation => Dispatch.ISEXTERNAL == 1 ? (Dispatch.RECEIVERADDRESS ?? "") : (Dispatch.ADDRESS ?? "");
    }

    // ===================================================================== PHASE 2: PRINT LABEL + GATE CHECK

    // old PrintLebel.aspx / PrintLebelPOS.aspx: the printed label of one dispatch
    public class DispatchLabelViewModel
    {
        public DispatchDetailModel Dispatch { get; set; } = new();
        public List<DispatchItemModel> Items { get; set; } = new();
        public bool WithItems { get; set; }                  // old btnPrintWithItem
        public bool Pos { get; set; }                        // old PrintLebelPOS (small 250px label, QR code)
        public string CodeImage { get; set; } = "";          // data:image/svg+xml;base64,... (CODE-128 or QR)

        // same rule as the details page: external -> RECEIVERADDRESS, personal -> no receiver block, otherwise ADDRESS
        public bool ShowReceiver => Dispatch.ISPERSONAL != 1 || Dispatch.ISEXTERNAL == 1;
        public string ReceiverLocation => Dispatch.ISEXTERNAL == 1 ? (Dispatch.RECEIVERADDRESS ?? "") : (Dispatch.ADDRESS ?? "");
    }

    // old DP_GATEENTRY row (visitor / sample entry, id "R...") as the gate sees it
    public class GateEntryModel
    {
        public long ID { get; set; }
        public string? GATEENTRYID { get; set; }
        public string? VISTORNAME { get; set; }
        public string? VISTORMOBILE { get; set; }
        public string? VISITORADDRESS { get; set; }
        public string? VISTORCARDNO { get; set; }
        public DateTime? INDATETIME { get; set; }
        public string? SCHEDULETIME { get; set; }
        // host employee (EM_EMPLOYEE of STAFFID)
        public string? EMPLOYEENAME { get; set; }
        public string? OFFICEMOBILE { get; set; }
        public string? CENTERNAME { get; set; }
        public string? DEPARTMENTCODE { get; set; }
    }

    public class GateEntryItemModel
    {
        public long ID { get; set; }
        public string? ITEMNAME { get; set; }
        public decimal? QUANTITY { get; set; }
        public string? UNIT { get; set; }
        public string? REMARKS { get; set; }
        public int? ISPERSONAL { get; set; }
        public int? ISRETURNABLE { get; set; }
        public string? ATTACHMENTS { get; set; }
    }

    // old GateCheck.aspx / GateCheckGlobal.aspx
    public class GateCheckViewModel
    {
        public int GateNo { get; set; }                                          // old lblGateNo (DP_ASSIGNGATEMAN.GATENO, default 1)
        public List<DispatchListItemModel> Waiting { get; set; } = new();        // old rptGatePass (GateCheckList)
        public string? Search { get; set; }                                      // the scanned / typed transaction id
        public string? Message { get; set; }                                     // old lblPermissionError

        // a dispatch was found (old pnDispatch)
        public DispatchDetailsViewModel? Found { get; set; }
        // a gate entry was found (old pnGateEntry)
        public GateEntryModel? Entry { get; set; }
        public List<GateEntryItemModel> EntryItems { get; set; } = new();
    }

    // ===================================================================== PHASE 3: DISPATCH DESK + TRANSPORT

    // DP_TRANSPORT (old Transport.aspx). ISEXTERNAL is NVARCHAR2 in the table ('0' / '1').
    public class TransportModel
    {
        public int ID { get; set; }
        public string? TRANSPORTNAME { get; set; }
        public string? ISEXTERNAL { get; set; }
        public bool IsExternal => (ISEXTERNAL ?? "").Trim() == "1";
    }

    // ASSET_CURRENCY (old ddlCurrency)
    public class CurrencyModel
    {
        public int ID { get; set; }
        public string? CURRENCYNAME { get; set; }
        public string? CURRENCYCODE { get; set; }
        public int ISDEFAULT { get; set; }
    }

    // old DispatchDesk.aspx
    public class DispatchDeskViewModel
    {
        public List<DispatchListItemModel> ToSend { get; set; } = new();       // old rptSend (Waiting to Send)
        public List<DispatchListItemModel> ToReceive { get; set; } = new();    // old rptReceive (Waiting to Receive)
    }

    // ===================================================================== PHASE 4: RECEIVE + RETURN

    // old Receive.aspx
    public class DispatchReceiveViewModel
    {
        public List<DispatchListItemModel> ToReceive { get; set; } = new();    // old rptReceive (I am the receiver)
        public List<DispatchListItemModel> ToReturn { get; set; } = new();     // old rptReturn (I am the sender of a returnable item)
    }

    // ===================================================================== PHASE 5: TRACKING

    // a row of the old rptGatePass on Tracking.aspx (GetTrackingList)
    public class TrackingListItemModel
    {
        public long ID { get; set; }
        public string? TRANSACTIONID { get; set; }
        public DateTime? CREATEDATE { get; set; }
        public int? STATUSID { get; set; }
        public string? ISSUETYPENAME { get; set; }
        public string? EMPLOYEENAME { get; set; }        // sender (FROMUSER)
        public string? OFFICEMOBILE { get; set; }        // sender mobile
        public string? CENTERCODE { get; set; }          // sender location code
        public string? BUSINESSCENTER { get; set; }      // receiver location (text)
        public string? RECEIVERNAME { get; set; }
        public string? RECEIVERMOBILE { get; set; }
        public string? CURRENTSTEP { get; set; }         // the open step (DP_TRANSITS.ISACTIVE = 1) - new, read only
    }

    // old TrakckingDetails: the dispatch of the searched id
    public class TrackingHeaderModel
    {
        public long ID { get; set; }
        public string? TRANSACTIONID { get; set; }
        public int? STATUSID { get; set; }
        public DateTime? CREATEDATE { get; set; }
        public string? ISSUETYPENAME { get; set; }
        public string? SENDNAME { get; set; }
        public string? CENTERCODE { get; set; }          // send location code
        public string? DEPARTMENTCODE { get; set; }      // send department code
        public string? BUSINESSCENTER { get; set; }
        public string? RECEIVERNAME { get; set; }
        public int? COMPLETED { get; set; }

        // old lblFirst
        public string FirstStep => $"{CENTERCODE}-{DEPARTMENTCODE}-{SENDNAME}";
    }

    // old Tracking.aspx
    public class TrackingViewModel
    {
        public string Search { get; set; } = "";                                 // old tbxSearchDispatch (pre-filled "DG<office><yy><mm>000")
        public string? Message { get; set; }
        public TrackingHeaderModel? Found { get; set; }
        public List<DispatchTransitModel> Steps { get; set; } = new();           // old rptSteps (all DP_TRANSITS, oldest first)
        public List<TrackingListItemModel> Mine { get; set; } = new();           // old rptGatePass
    }
}
