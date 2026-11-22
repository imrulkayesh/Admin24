// Models/RegisterKhataModels.cs
// Register Khata module (port of YA Pages/RegisterKhata + App_Code/dal/RegisterKhata). Column names = YA tables RK_*.
// Registers: RK_GUESTKHATA, RK_DOCUMENT, RK_VEHICLEINOUT. Warehouse: RK_WAREHOUSE, RK_DEPARTMENT, RK_UNIT, RK_ITEMMASTER,
// RK_ITEMIN, RK_ISSUEMASTER, RK_ISSUEDETAILS (ISSUETYPE 1 = issue, 2 = receive), RK_LASTNO (ITEMMASTER / ITEMIN / IS).
namespace AlphaCare.Models
{
    public class RkOption
    {
        public long ID { get; set; }
        public string? NAME { get; set; }
    }

    // ======================================================================= registers
    public class RkGuestModel
    {
        public long ID { get; set; }
        public int? LOCATIONID { get; set; }
        public string? GUESTNAME { get; set; }
        public int? DESIGNATIONID { get; set; }
        public string? MOBILE { get; set; }
        public string? ADDRESS { get; set; }
        public string? REASON { get; set; }
        public DateTime? INDATE { get; set; }
        public string? INTIME { get; set; }
        public DateTime? OUTDATE { get; set; }
        public string? OUTTIME { get; set; }
        public string? REMARKS { get; set; }
        public DateTime? CREATEDATE { get; set; }
        public string? CENTERNAME { get; set; }
        public string? DESIGNATIONNAME { get; set; }
    }

    public class RkDocumentModel
    {
        public long ID { get; set; }
        public int? LOCATIONID { get; set; }
        public string? DOCUMENTNAME { get; set; }
        public string? SENDERNAME { get; set; }
        public string? SENDERMOBILE { get; set; }
        public string? RECEIVERNAME { get; set; }
        public string? RECEIVERMOBILE { get; set; }
        public string? TRANSPORT { get; set; }
        public DateTime? INDATE { get; set; }
        public string? INTIME { get; set; }
        public DateTime? OUTDATE { get; set; }
        public string? OUTTIME { get; set; }
        public string? REMARKS { get; set; }
        public DateTime? CREATEDATE { get; set; }
        public string? CENTERNAME { get; set; }
    }

    public class RkVehicleModel
    {
        public long ID { get; set; }
        public int? LOCATIONID { get; set; }
        public string? VEHICLENO { get; set; }
        public string? GATENO { get; set; }
        public string? DRIVERNAME { get; set; }
        public string? DRIVERMOBILE { get; set; }
        public DateTime? INDATE { get; set; }
        public string? INTIME { get; set; }
        public DateTime? OUTDATE { get; set; }
        public string? OUTTIME { get; set; }
        public string? REMARKS { get; set; }
        public DateTime? CREATEDATE { get; set; }
        public string? CENTERNAME { get; set; }
    }

    /// <summary>Posted form of the three registers (fields not used by a register stay empty).</summary>
    public class RkRegisterSave
    {
        public long Id { get; set; }
        public int LocationId { get; set; }
        // guest khata
        public string? GuestName { get; set; }
        public int DesignationId { get; set; }
        public string? Mobile { get; set; }
        public string? Address { get; set; }
        public string? Reason { get; set; }
        // documents
        public string? DocumentName { get; set; }
        public string? SenderName { get; set; }
        public string? SenderMobile { get; set; }
        public string? ReceiverName { get; set; }
        public string? ReceiverMobile { get; set; }
        public string? Transport { get; set; }
        // vehicle in / out
        public string? VehicleNo { get; set; }
        public string? GateNo { get; set; }
        public string? DriverName { get; set; }
        public string? DriverMobile { get; set; }
        // all three
        public string? InDate { get; set; }      // yyyy-MM-dd (date box)
        public string? InTime { get; set; }      // free text, as in YA (RK_*.INTIME is text)
        public string? OutDate { get; set; }
        public string? OutTime { get; set; }
        public string? Remarks { get; set; }
    }

    public class RkRegisterPageModel
    {
        public int LocationId { get; set; }
        public List<BusinessCenterModel> Locations { get; set; } = new();
        public List<RkOption> Designations { get; set; } = new();
        public List<RkGuestModel> Guests { get; set; } = new();
        public List<RkDocumentModel> Documents { get; set; } = new();
        public List<RkVehicleModel> Vehicles { get; set; } = new();
    }

    // ======================================================================= warehouse
    public class RkItemModel
    {
        public long ID { get; set; }
        public long? WAREHOUSEID { get; set; }
        public string? WAREHOUSENAME { get; set; }
        public string? ITEMNAME { get; set; }
        public string? ITEMCODE { get; set; }
        public long? UNITID { get; set; }
        public string? UNITNAME { get; set; }
        public string? DESCRIPTION { get; set; }
        public decimal? ITEMSTOCK { get; set; }
    }

    public class RkItemInModel
    {
        public long ID { get; set; }
        public long? WAREHOUSEID { get; set; }
        public string? WAREHOUSENAME { get; set; }
        public long? ITEMID { get; set; }
        public string? ITEMNAME { get; set; }
        public string? ITEMCODE { get; set; }
        public decimal? QUANTITY { get; set; }
        public long? UNITID { get; set; }
        public string? UNITNAME { get; set; }
        public decimal? PRICE { get; set; }
        public DateTime? VALIDITYDATE { get; set; }
    }

    // old GetUnitItemWise
    public class RkItemUnitModel
    {
        public long? UNITID { get; set; }
        public string? UNITNAME { get; set; }
        public long ITEMID { get; set; }
        public string? ITEMCODE { get; set; }
    }

    public class RkStorePageModel
    {
        public int CenterId { get; set; }
        public long WarehouseId { get; set; }
        public List<BusinessCenterModel> Locations { get; set; } = new();
        public List<RkOption> Warehouses { get; set; } = new();
        public List<RkOption> Units { get; set; } = new();
        public List<RkOption> Items { get; set; } = new();
        public List<RkOption> Departments { get; set; } = new();
        public List<RkItemModel> ItemRows { get; set; } = new();
        public List<RkItemInModel> ItemInRows { get; set; } = new();
    }

    // ---------- issue ----------
    public class RkIssueLine
    {
        public long ItemId { get; set; }
        public string? Qty { get; set; }
        public string? Remarks { get; set; }
    }

    public class RkIssueSave
    {
        public int LocationId { get; set; }
        public long WarehouseId { get; set; }
        public long DepartmentId { get; set; }
        public string? StaffId { get; set; }
        public string? Purpose { get; set; }
        public string? ReturnDate { get; set; }   // yyyy-MM-dd, optional
        public bool Dispatch { get; set; }         // old chkDispatch "Dispatch Send"
        public List<RkIssueLine> Lines { get; set; } = new();
    }

    /// <summary>A checked issue line (after validation): unit from RK_ITEMMASTER, quantity parsed.</summary>
    public class RkIssueLineChecked
    {
        public long ItemId { get; set; }
        public long? UnitId { get; set; }
        public string ItemName { get; set; } = "";
        public string? UnitName { get; set; }
        public decimal Qty { get; set; }
        public string Remarks { get; set; } = "";
    }

    // ---------- receive ----------
    public class RkIssueHeaderModel
    {
        public long ID { get; set; }
        public string? TRNO { get; set; }
        public string? STAFFID { get; set; }
        public string? PURPOSE { get; set; }
        public DateTime? ISSUEDATE { get; set; }
        public DateTime? RETURNDATE { get; set; }
        public long? WAREHOUSEID { get; set; }
    }

    // old ddlIssueTrNo_SelectedIndexChanged query: issue lines with something left to receive
    public class RkPendingLineModel
    {
        public long ID { get; set; }               // RK_ISSUEDETAILS.ID of the issue line
        public long ITEMID { get; set; }
        public string? ITEMNAME { get; set; }
        public string? ITEMCODE { get; set; }
        public long? UNITID { get; set; }
        public string? UNITNAME { get; set; }
        public decimal ISSQTY { get; set; }
        public decimal RECQTY { get; set; }        // still to receive (QUANTITY - RECTOTALQTY)
        public string? REMARKS { get; set; }
    }

    public class RkReceiveLine
    {
        public long LineId { get; set; }            // RK_ISSUEDETAILS.ID of the issue line
        public string? Qty { get; set; }
    }

    // ---------- warehouse permission ----------
    public class RkPermissionPageModel
    {
        public List<RkOption> Warehouses { get; set; } = new();
    }

    // ======================================================================= reports
    public class RkReportViewModel
    {
        public string Action { get; set; } = "";
        public string Title { get; set; } = "";
        public string Icon { get; set; } = "fa-table";
        public string? Note { get; set; }
        public bool UsesDates { get; set; }
        public bool UsesWarehouse { get; set; }
        public bool LockLocation { get; set; }         // old WarehouseItemRpt: ddlBusinessCenter.Enabled = false
        public int CenterId { get; set; }
        public long WarehouseId { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public List<BusinessCenterModel> Locations { get; set; } = new();
        public List<RkOption> Warehouses { get; set; } = new();

        public bool Searched { get; set; }
        public string? Message { get; set; }
        public List<DispatchReportColumn> Columns { get; set; } = new();
        public List<IDictionary<string, object?>> Rows { get; set; } = new();
        public bool Wide { get; set; } = true;          // scroll box in both directions (same as the Dispatch / Service reports)
    }
}
