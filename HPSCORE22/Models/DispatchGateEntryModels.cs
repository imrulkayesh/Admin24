// Models/DispatchGateEntryModels.cs
// Dispatch phase 8 - visitor gate entry (YA GateEntry.aspx, GatentryPrint.aspx, GateEntryView.aspx, GateEntryDetails.aspx, MyVisitors.aspx)
// and the notice board (YA DNoticeBoard.aspx, RNItems.aspx, NView.aspx). Same tables as YA:
// DP_GATEENTRY / DP_GATEENTRYITEMS and DP_NOTICEBOARD / DP_NOTICEBOARDITEM / DP_DISPATCHTYPE.
namespace AlphaCare.Models
{
    // ===================================================================== VISITOR GATE ENTRY

    // one DP_GATEENTRY row with the host employee (STAFFID = the person the visitor comes to)
    public class GateEntryDetailModel
    {
        public long ID { get; set; }
        public string? GATEENTRYID { get; set; }
        public string? STAFFID { get; set; }
        public string? VISTORNAME { get; set; }
        public string? VISTORMOBILE { get; set; }
        public string? VISITORADDRESS { get; set; }
        public string? VISTORCARDNO { get; set; }
        public string? VISITORSTAFFID { get; set; }
        public DateTime? INDATETIME { get; set; }
        public DateTime? OUTDATETIME { get; set; }
        public int? GATENO { get; set; }
        public int? GATEPASSED { get; set; }
        public int? ISRETURNED { get; set; }
        public string? CREATEDBY { get; set; }
        // host employee
        public string? EMPLOYEENAME { get; set; }
        public string? OFFICEMOBILE { get; set; }
        public string? CENTERNAME { get; set; }
        public string? DEPARTMENTCODE { get; set; }
        public string? DEPARTMENTNAME { get; set; }
        // the gate man who made the entry
        public string? CREATEDBYNAME { get; set; }
    }

    // a row of "My Visitors" (old MyVisitors.aspx: my entries with a returnable sample that is not returned yet)
    public class GateEntryListItemModel
    {
        public long ID { get; set; }
        public string? GATEENTRYID { get; set; }
        public string? VISTORNAME { get; set; }
        public string? VISTORMOBILE { get; set; }
        public string? VISITORADDRESS { get; set; }
        public DateTime? INDATETIME { get; set; }
        public int SAMPLES { get; set; }                    // returnable sample lines
    }

    public class GateEntryCreateViewModel
    {
        public long EntryId { get; set; }                   // the open draft (ISOPEN = 1)
        public List<BusinessCenterModel> BusinessCenters { get; set; } = new();
        public List<DepartmentModel> Departments { get; set; } = new();
        public List<GateEntryItemModel> Items { get; set; } = new();
        public int UserCenterId { get; set; }               // old: ddlBusinessCenter = the user's location
        public bool AttachmentsEnabled { get; set; }
    }

    // posted by "Save" on the gate entry page
    public class SaveGateEntryRequest
    {
        public long EntryId { get; set; }
        public string? HostStaffId { get; set; }            // old ddlEmployee / tbxStaffID
        public int HostCenterId { get; set; }               // old ddlBusinessCenter
        public int HostDepartmentId { get; set; }           // old ddlDepartment
        public string? VisitorStaffId { get; set; }         // old tbxFromStaffID (an employee as visitor, optional)
        public string? VisitorName { get; set; }
        public string? VisitorAddress { get; set; }
        public string? VisitorMobile { get; set; }
        public string? CardNo { get; set; }
    }

    public class GateEntryDetailsViewModel
    {
        public GateEntryDetailModel Entry { get; set; } = new();
        public List<GateEntryItemModel> Items { get; set; } = new();
        public bool AttachmentsEnabled { get; set; }
        public bool CanReturnSample { get; set; }           // the host: a returnable sample is not returned yet
    }

    public class GateEntryLabelViewModel
    {
        public GateEntryDetailModel Entry { get; set; } = new();
        public List<GateEntryItemModel> Items { get; set; } = new();
        public string CodeImage { get; set; } = "";         // CODE-128 of GATEENTRYID (old Barcode.aspx?d=...)
    }

    // ===================================================================== NOTICE BOARD

    public class DispatchTypeModel
    {
        public int ID { get; set; }
        public string? DTYPENAME { get; set; }
    }

    public class NoticeItemModel
    {
        public long ID { get; set; }
        public long DISPATCHID { get; set; }                // = DP_NOTICEBOARD.ID
        public string? ITEMNAME { get; set; }
        public decimal? QUANTITY { get; set; }
        public string? UNIT { get; set; }
        public string? NOTES { get; set; }
        public string? ATTACHMENT { get; set; }
    }

    public class NoticeDetailModel
    {
        public long ID { get; set; }
        public int? DISPATCHTYPE { get; set; }
        public string? DTYPENAME { get; set; }
        public decimal? LCAMOUNT { get; set; }
        public string? DOCFROM { get; set; }
        public DateTime? RECDATE { get; set; }
        public string? TRACKINGNO { get; set; }
        public string? FROMMOBILE { get; set; }
        public string? FROMEMAIL { get; set; }
        public string? FROMADDRESS { get; set; }
        public string? FROMREMARKS { get; set; }
        public string? TSTAFFID { get; set; }
        public string? TNAME { get; set; }
        public string? TLOCATION { get; set; }
        public string? TCOMPANY { get; set; }
        public string? TDEPARTMENT { get; set; }
        public string? TLEVEL { get; set; }
        public string? TMOBILE { get; set; }
        public string? TEMAIL { get; set; }
        public string? TADDRESS { get; set; }
        public string? TREMARKS { get; set; }
        public string? RECSTAFFID { get; set; }
        public string? RECEIVERNAME { get; set; }
        public string? RECEIVERCOMPANY { get; set; }
        public string? RECDEPARTMENT { get; set; }
        public string? RECMOBILE { get; set; }
        public string? RECEMAIL { get; set; }
        public string? RECREMARKS { get; set; }
        public DateTime? RECEIVEDDATE { get; set; }
        public int? ACTIVE { get; set; }                    // 1 = waiting to be handed over, 0 = received
        public string? CREATEDBY { get; set; }
        public DateTime? CREATEDATE { get; set; }
    }

    public class NoticeCreateViewModel
    {
        public long NoticeId { get; set; }                  // the open draft (ISOPEN = 1)
        public List<DispatchTypeModel> Types { get; set; } = new();
        public List<NoticeItemModel> Items { get; set; } = new();
        public bool AttachmentsEnabled { get; set; }
    }

    // posted by "Save" on the notice board page (old btnSave_Click -> dalNoticeBorad.Update)
    public class SaveNoticeRequest
    {
        public long NoticeId { get; set; }
        public int DispatchType { get; set; }
        public string? LcAmount { get; set; }
        public string? DocFrom { get; set; }
        public DateTime? RecDate { get; set; }
        public string? TrackingNo { get; set; }
        public string? FromMobile { get; set; }
        public string? FromEmail { get; set; }
        public string? FromAddress { get; set; }
        public string? FromRemarks { get; set; }
        public string? ToStaffId { get; set; }
        public string? ToName { get; set; }
        public string? ToLocation { get; set; }
        public string? ToCompany { get; set; }
        public string? ToDepartment { get; set; }
        public string? ToLevel { get; set; }
        public string? ToMobile { get; set; }
        public string? ToEmail { get; set; }
        public string? ToAddress { get; set; }
        public string? ToRemarks { get; set; }
    }

    // posted by "Received" on the hand over page (old RNItems btnAssign_Click -> dalNoticeBorad.UpdateReceiver)
    public class NoticeReceiveRequest
    {
        public long Id { get; set; }
        public string? StaffId { get; set; }
        public string? Name { get; set; }
        public string? Company { get; set; }
        public string? Department { get; set; }
        public string? Mobile { get; set; }
        public string? Email { get; set; }
        public string? Remarks { get; set; }
    }

    public class NoticeDetailsViewModel
    {
        public NoticeDetailModel Notice { get; set; } = new();
        public List<NoticeItemModel> Items { get; set; } = new();
        public bool AttachmentsEnabled { get; set; }
    }

    // employee details for the "To" / receiver boxes (old dalNoticeBorad.GetEmployeeDetails)
    public class NoticeEmployeeModel
    {
        public string? STAFFID { get; set; }
        public string? EMPLOYEENAME { get; set; }
        public string? OFFICEMOBILE { get; set; }
        public string? OFFICEMAIL { get; set; }
        public string? CENTERNAME { get; set; }
        public string? COMPANYNAME { get; set; }
        public string? DEPARTMENTNAME { get; set; }
    }
}
