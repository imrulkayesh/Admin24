// Interface/RegisterKhata/IRegisterKhataRepository.cs
// Port of YA App_Code/dal/RegisterKhata (dalGuestKhata, dalDocuments, dalVehicleInOut, dalItemMaster, dalItemIn, dalIssue,
// dalReceive, dalWarehouse, dalRkReport). Same RK_* tables, columns, sequences and RK_LASTNO counters as YA.
using AlphaCare.Models;

namespace AlphaCare.Interface
{
    public interface IRegisterKhataRepository
    {
        // ---------------- permission / lookups ----------------
        /// <summary>Old dalCommon.IsPermitted (UC_*_COPY menu tables, super admin ROLEID 1 always)</summary>
        bool HasMenuPermission(string staffId, string url);
        /// <summary>Old Common().GetAll("BS_BUSINESSCENTER")</summary>
        List<BusinessCenterModel> GetAllBusinessCenters();
        /// <summary>Old Common().GetAll("BS_DESIGNATION")</summary>
        List<RkOption> GetDesignations();

        // ---------------- registers (dalGuestKhata / dalDocuments / dalVehicleInOut) ----------------
        List<RkGuestModel> GetGuests(int locationId);
        RkGuestModel? GetGuest(long id);
        int InsertGuest(RkRegisterSave r, DateTime? inDate, DateTime? outDate, string staffId);
        int UpdateGuest(RkRegisterSave r, DateTime? inDate, DateTime? outDate, string staffId);

        List<RkDocumentModel> GetDocuments(int locationId);
        RkDocumentModel? GetDocument(long id);
        int InsertDocument(RkRegisterSave r, DateTime? inDate, DateTime? outDate, string staffId);
        int UpdateDocument(RkRegisterSave r, DateTime? inDate, DateTime? outDate, string staffId);

        /// <summary>Old dalVehicleInOut.GetALL: the location's entries created in the last 15 days</summary>
        List<RkVehicleModel> GetVehicles(int locationId);
        RkVehicleModel? GetVehicle(long id);
        int InsertVehicle(RkRegisterSave r, DateTime? inDate, DateTime? outDate, string staffId);
        int UpdateVehicle(RkRegisterSave r, DateTime? inDate, DateTime? outDate, string staffId);
        /// <summary>Old dalVehicleInOut.GetALLVehicle (RptVehicleInOut): entries created between two dates</summary>
        List<IDictionary<string, object?>> ReportVehicles(int locationId, DateTime from, DateTime to);

        // ---------------- warehouse lookups ----------------
        /// <summary>Old GetWarehouseByBusinessCenerId: the warehouse of the staff (EM_EMPLOYEE.WAREHOUSEID) when he belongs to that location</summary>
        List<RkOption> GetUserWarehouses(string staffId, int centerId);
        /// <summary>The warehouse is the staff's (EM_EMPLOYEE.WAREHOUSEID) - for edits of rows that are already saved</summary>
        bool IsUserWarehouse(string staffId, long warehouseId);
        /// <summary>Old GetUnitName: active units</summary>
        List<RkOption> GetUnits();
        /// <summary>Old GetItemNameId (activeOnly) / dalReceive.GetItemNameId (all)</summary>
        List<RkOption> GetItemOptions(long warehouseId, bool activeOnly);
        /// <summary>Old GetUnitItemWise</summary>
        RkItemUnitModel? GetItemUnit(long itemId);
        /// <summary>Old GetWarehouseId: active departments of the warehouse</summary>
        List<RkOption> GetDepartments(long warehouseId);
        /// <summary>Old Issue LoadItemstock: GetItemStockId, else GetItemStock (null = no stock row at all)</summary>
        decimal? GetAvailableQty(long warehouseId, long itemId);

        // ---------------- item master (dalItemMaster) ----------------
        List<RkItemModel> GetItems(long warehouseId);
        RkItemModel? GetItem(long id);
        bool ItemCodeExists(string itemCode, long warehouseId);
        /// <summary>Old btnSave_Click: ID from RK_LASTNO 'ITEMMASTER' (one transaction)</summary>
        long InsertItem(long warehouseId, string itemName, string itemCode, long unitId, string description, string staffId);
        /// <summary>Old btnEdit_Click: only DESCRIPTION and UNITID change</summary>
        int UpdateItem(long id, long unitId, string description, string userId);

        // ---------------- item in (dalItemIn) ----------------
        List<RkItemInModel> GetItemIns(long warehouseId);
        RkItemInModel? GetItemIn(long id);
        /// <summary>Old btnSave_Click: ID from RK_LASTNO 'ITEMIN', ITEMSTOCK + quantity (one transaction)</summary>
        long InsertItemIn(long warehouseId, long itemId, decimal quantity, long? unitId, decimal? price, DateTime? validityDate, string staffId);
        /// <summary>Old btnEdit_Click: QUANTITY / PRICE, ITEMSTOCK - old + new (one transaction)</summary>
        int UpdateItemIn(long id, decimal quantity, decimal? price, string userId);

        // ---------------- issue (Issue.aspx) ----------------
        /// <summary>
        /// Old btnSave_OnClick: RK_ISSUEMASTER + RK_ISSUEDETAILS (ISSUETYPE 1) + ITEMSTOCK - qty, TRNO from RK_LASTNO 'IS'.
        /// beforeCommit runs inside the transaction (the dispatch); returning an error text rolls everything back.
        /// </summary>
        (string? TrNo, string? Error) SaveIssue(RkIssueSave r, List<RkIssueLineChecked> lines, string staffId, Func<string, string?>? beforeCommit);

        // ---------------- receive (Receive.aspx / dalReceive) ----------------
        /// <summary>Old GetIssueTrNo / GetDepartmentWiseIssueTrNo: open issues, "TRNO--STAFFID"</summary>
        List<RkOption> GetOpenIssues(long warehouseId, long departmentId);
        RkIssueHeaderModel? GetIssueHeader(long issueId);
        List<RkPendingLineModel> GetPendingLines(string trNo);
        /// <summary>Old btnSave_OnClick: RK_ISSUEDETAILS ISSUETYPE 2, RECTOTALQTY, ITEMSTOCK + qty, ISSUESTATUS 0 when all is back</summary>
        (bool Closed, string? Error) SaveReceive(long issueId, List<RkReceiveLine> lines, string staffId);

        // ---------------- warehouse permission (dalWarehouse) ----------------
        /// <summary>Old Common().GetAll("RK_WAREHOUSE")</summary>
        List<RkOption> GetAllWarehouses();
        /// <summary>Old loadWarehouselist: the user's active warehouse</summary>
        long? GetEmployeeWarehouse(string staffId);
        /// <summary>Old dalWarehouse.Update: EM_EMPLOYEE.WAREHOUSEID</summary>
        int SetEmployeeWarehouse(string staffId, long warehouseId);

        // ---------------- report (dalRkReport) ----------------
        /// <summary>Old GetWarehousewiseItemReport (0 = every warehouse, as YA's "Select")</summary>
        List<IDictionary<string, object?>> ReportStock(long warehouseId);
    }
}
