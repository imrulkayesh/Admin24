// Repositories/RegisterKhata/RegisterKhataRepository.cs
// Port of YA App_Code/dal/RegisterKhata + the SQL in the RegisterKhata code-behinds. Same tables, columns, sequences
// (SQ_RK_GUESTKHATA, SQ_RK_DOCUMENT, SQ_RK_VEHICLEINOUT, SQ_RK_ISSUEMASTER, SQ_RK_ISSUEDETAILS) and RK_LASTNO counters
// (ITEMMASTER, ITEMIN, IS) as YA. Differences to the old code:
//   * every value is a bind parameter (the old code concatenated user text into the SQL)
//   * RK_LASTNO is incremented inside the transaction before it is read (the old code read it first: two users could get the same number)
//   * stock rows are locked (FOR UPDATE) while an issue checks and lowers ITEMSTOCK; a short stock rolls the whole issue back
//   * receive: one transaction (YA committed the lines and the status in two)
// NOTE: bind variable names must not be Oracle reserved words (:Date, :Level, :Size, :Start, :Member ... -> ORA-01745)
using AlphaCare.Interface;
using AlphaCare.Models;
using Dapper;
using QCMS.Services;
using System.Data;

namespace Admin360.Repositories
{
    public class RegisterKhataRepository : IRegisterKhataRepository
    {
        private readonly DatabaseService _databaseService;

        public RegisterKhataRepository(DatabaseService databaseService)
        {
            _databaseService = databaseService;
        }

        // ================================================================ PERMISSION / LOOKUPS

        public bool HasMenuPermission(string staffId, string url)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT COUNT(1) FROM UC_USERROLE R
                 WHERE R.STAFFID = :StaffId
                   AND (R.ROLEID = 1 OR R.ROLEID IN (
                        SELECT TP.ROLEID
                          FROM UC_TASKPERMISSION_COPY TP
                         INNER JOIN UC_TASKMANAGER_COPY T ON T.ID = TP.TASKID
                         WHERE LOWER(LTRIM(TRIM(T.URL), '~/')) = :Url))";
            string u = (url ?? "").Trim().TrimStart('~', '/').ToLowerInvariant();
            return conn.ExecuteScalar<int>(sql, new { StaffId = staffId, Url = u }) > 0;
        }

        public List<BusinessCenterModel> GetAllBusinessCenters()
        {
            using var conn = _databaseService.GetConnection();
            return conn.Query<BusinessCenterModel>("SELECT ID, CENTERCODE, CENTERNAME FROM BS_BUSINESSCENTER ORDER BY CENTERNAME").ToList();
        }

        public List<RkOption> GetDesignations()
        {
            using var conn = _databaseService.GetConnection();
            return conn.Query<RkOption>("SELECT ID, DESIGNATIONNAME NAME FROM BS_DESIGNATION ORDER BY DESIGNATIONNAME").ToList();
        }

        // ================================================================ GUEST KHATA (dalGuestKhata)

        public List<RkGuestModel> GetGuests(int locationId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT G.ID, G.LOCATIONID, G.GUESTNAME, G.DESIGNATIONID, G.MOBILE, G.ADDRESS, G.REASON, G.INDATE, G.INTIME,
                       G.OUTDATE, G.OUTTIME, G.REMARKS, G.CREATEDATE, B.CENTERNAME, D.DESIGNATIONNAME
                  FROM RK_GUESTKHATA G
                 INNER JOIN BS_BUSINESSCENTER B ON B.ID = G.LOCATIONID
                 INNER JOIN BS_DESIGNATION D ON D.ID = G.DESIGNATIONID
                 WHERE G.ID > 0 AND B.ID = :Loc
                 ORDER BY G.CREATEDATE DESC";
            return conn.Query<RkGuestModel>(sql, new { Loc = locationId }).ToList();
        }

        public RkGuestModel? GetGuest(long id)
        {
            using var conn = _databaseService.GetConnection();
            return conn.QueryFirstOrDefault<RkGuestModel>(@"
                SELECT ID, LOCATIONID, GUESTNAME, DESIGNATIONID, MOBILE, ADDRESS, REASON, INDATE, INTIME, OUTDATE, OUTTIME, REMARKS, CREATEDATE
                  FROM RK_GUESTKHATA WHERE ID = :Id", new { Id = id });
        }

        public int InsertGuest(RkRegisterSave r, DateTime? inDate, DateTime? outDate, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute(@"
                INSERT INTO RK_GUESTKHATA (ID, LOCATIONID, GUESTNAME, DESIGNATIONID, MOBILE, ADDRESS, REASON, INDATE, INTIME, OUTDATE, OUTTIME, REMARKS, CREATEDBY)
                VALUES (SQ_RK_GUESTKHATA.NEXTVAL, :Loc, :Nm, :Desig, :Mob, :Addr, :Reason, :InD, :InT, :OutD, :OutT, :Rem, :StaffId)",
                new { Loc = r.LocationId, Nm = r.GuestName, Desig = r.DesignationId, Mob = r.Mobile, Addr = r.Address, Reason = r.Reason,
                      InD = inDate, InT = r.InTime, OutD = outDate, OutT = r.OutTime, Rem = r.Remarks, StaffId = staffId });
        }

        public int UpdateGuest(RkRegisterSave r, DateTime? inDate, DateTime? outDate, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute(@"
                UPDATE RK_GUESTKHATA
                   SET LOCATIONID = :Loc, GUESTNAME = :Nm, DESIGNATIONID = :Desig, MOBILE = :Mob, ADDRESS = :Addr, REASON = :Reason,
                       INDATE = :InD, INTIME = :InT, OUTDATE = :OutD, OUTTIME = :OutT, REMARKS = :Rem, UPDATEDBY = :StaffId, UPDATEDATE = SYSDATE
                 WHERE ID = :Id",
                new { Loc = r.LocationId, Nm = r.GuestName, Desig = r.DesignationId, Mob = r.Mobile, Addr = r.Address, Reason = r.Reason,
                      InD = inDate, InT = r.InTime, OutD = outDate, OutT = r.OutTime, Rem = r.Remarks, StaffId = staffId, Id = r.Id });
        }

        // ================================================================ DOCUMENTS (dalDocuments)

        public List<RkDocumentModel> GetDocuments(int locationId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT G.ID, G.LOCATIONID, G.DOCUMENTNAME, G.SENDERNAME, G.SENDERMOBILE, G.RECEIVERNAME, G.RECEIVERMOBILE, G.TRANSPORT,
                       G.INDATE, G.INTIME, G.OUTDATE, G.OUTTIME, G.REMARKS, G.CREATEDATE, B.CENTERNAME
                  FROM RK_DOCUMENT G
                 INNER JOIN BS_BUSINESSCENTER B ON B.ID = G.LOCATIONID
                 WHERE G.ID > 0 AND B.ID = :Loc
                 ORDER BY G.CREATEDATE DESC";
            return conn.Query<RkDocumentModel>(sql, new { Loc = locationId }).ToList();
        }

        public RkDocumentModel? GetDocument(long id)
        {
            using var conn = _databaseService.GetConnection();
            return conn.QueryFirstOrDefault<RkDocumentModel>(@"
                SELECT ID, LOCATIONID, DOCUMENTNAME, SENDERNAME, SENDERMOBILE, RECEIVERNAME, RECEIVERMOBILE, TRANSPORT,
                       INDATE, INTIME, OUTDATE, OUTTIME, REMARKS, CREATEDATE
                  FROM RK_DOCUMENT WHERE ID = :Id", new { Id = id });
        }

        public int InsertDocument(RkRegisterSave r, DateTime? inDate, DateTime? outDate, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute(@"
                INSERT INTO RK_DOCUMENT (ID, LOCATIONID, DOCUMENTNAME, SENDERNAME, SENDERMOBILE, RECEIVERNAME, RECEIVERMOBILE, TRANSPORT,
                                         INDATE, INTIME, OUTDATE, OUTTIME, REMARKS, CREATEDBY)
                VALUES (SQ_RK_DOCUMENT.NEXTVAL, :Loc, :Doc, :Snd, :SndMob, :Rcv, :RcvMob, :Trans, :InD, :InT, :OutD, :OutT, :Rem, :StaffId)",
                new { Loc = r.LocationId, Doc = r.DocumentName, Snd = r.SenderName, SndMob = r.SenderMobile, Rcv = r.ReceiverName,
                      RcvMob = r.ReceiverMobile, Trans = r.Transport, InD = inDate, InT = r.InTime, OutD = outDate, OutT = r.OutTime,
                      Rem = r.Remarks, StaffId = staffId });
        }

        public int UpdateDocument(RkRegisterSave r, DateTime? inDate, DateTime? outDate, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute(@"
                UPDATE RK_DOCUMENT
                   SET LOCATIONID = :Loc, DOCUMENTNAME = :Doc, SENDERNAME = :Snd, SENDERMOBILE = :SndMob, RECEIVERNAME = :Rcv,
                       RECEIVERMOBILE = :RcvMob, TRANSPORT = :Trans, INDATE = :InD, INTIME = :InT, OUTDATE = :OutD, OUTTIME = :OutT,
                       REMARKS = :Rem, UPDATEDBY = :StaffId, UPDATEDATE = SYSDATE
                 WHERE ID = :Id",
                new { Loc = r.LocationId, Doc = r.DocumentName, Snd = r.SenderName, SndMob = r.SenderMobile, Rcv = r.ReceiverName,
                      RcvMob = r.ReceiverMobile, Trans = r.Transport, InD = inDate, InT = r.InTime, OutD = outDate, OutT = r.OutTime,
                      Rem = r.Remarks, StaffId = staffId, Id = r.Id });
        }

        // ================================================================ VEHICLE IN / OUT (dalVehicleInOut)

        private const string VehicleSelect = @"
            SELECT G.ID, G.LOCATIONID, G.VEHICLENO, G.GATENO, G.DRIVERNAME, G.DRIVERMOBILE, G.INDATE, G.INTIME, G.OUTDATE, G.OUTTIME,
                   G.REMARKS, G.CREATEDATE, B.CENTERNAME
              FROM RK_VEHICLEINOUT G
             INNER JOIN BS_BUSINESSCENTER B ON B.ID = G.LOCATIONID";

        public List<RkVehicleModel> GetVehicles(int locationId)
        {
            using var conn = _databaseService.GetConnection();
            // old: TO_DATE(CREATEDATE) BETWEEN SYSDATE - 15 AND SYSDATE (by day)
            string sql = VehicleSelect + @"
                 WHERE G.ID > 0 AND B.ID = :Loc
                   AND G.CREATEDATE >= TRUNC(SYSDATE) - 15 AND G.CREATEDATE < TRUNC(SYSDATE) + 1
                 ORDER BY G.CREATEDATE DESC";
            return conn.Query<RkVehicleModel>(sql, new { Loc = locationId }).ToList();
        }

        public RkVehicleModel? GetVehicle(long id)
        {
            using var conn = _databaseService.GetConnection();
            return conn.QueryFirstOrDefault<RkVehicleModel>(@"
                SELECT ID, LOCATIONID, VEHICLENO, GATENO, DRIVERNAME, DRIVERMOBILE, INDATE, INTIME, OUTDATE, OUTTIME, REMARKS, CREATEDATE
                  FROM RK_VEHICLEINOUT WHERE ID = :Id", new { Id = id });
        }

        public int InsertVehicle(RkRegisterSave r, DateTime? inDate, DateTime? outDate, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute(@"
                INSERT INTO RK_VEHICLEINOUT (ID, LOCATIONID, VEHICLENO, GATENO, DRIVERNAME, DRIVERMOBILE, INDATE, INTIME, OUTDATE, OUTTIME, REMARKS, CREATEDBY)
                VALUES (SQ_RK_VEHICLEINOUT.NEXTVAL, :Loc, :Veh, :Gate, :Drv, :DrvMob, :InD, :InT, :OutD, :OutT, :Rem, :StaffId)",
                new { Loc = r.LocationId, Veh = r.VehicleNo, Gate = r.GateNo, Drv = r.DriverName, DrvMob = r.DriverMobile,
                      InD = inDate, InT = r.InTime, OutD = outDate, OutT = r.OutTime, Rem = r.Remarks, StaffId = staffId });
        }

        public int UpdateVehicle(RkRegisterSave r, DateTime? inDate, DateTime? outDate, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute(@"
                UPDATE RK_VEHICLEINOUT
                   SET LOCATIONID = :Loc, VEHICLENO = :Veh, GATENO = :Gate, DRIVERNAME = :Drv, DRIVERMOBILE = :DrvMob,
                       INDATE = :InD, INTIME = :InT, OUTDATE = :OutD, OUTTIME = :OutT, REMARKS = :Rem, UPDATEDBY = :StaffId, UPDATEDATE = SYSDATE
                 WHERE ID = :Id",
                new { Loc = r.LocationId, Veh = r.VehicleNo, Gate = r.GateNo, Drv = r.DriverName, DrvMob = r.DriverMobile,
                      InD = inDate, InT = r.InTime, OutD = outDate, OutT = r.OutTime, Rem = r.Remarks, StaffId = staffId, Id = r.Id });
        }

        public List<IDictionary<string, object?>> ReportVehicles(int locationId, DateTime from, DateTime to)
        {
            using var conn = _databaseService.GetConnection();
            string sql = VehicleSelect + @"
                 WHERE G.ID > 0 AND B.ID = :Loc AND G.CREATEDATE >= :DFrom AND G.CREATEDATE < :DTo
                 ORDER BY G.CREATEDATE DESC";
            return conn.Query(sql, new { Loc = locationId, DFrom = from.Date, DTo = to.Date.AddDays(1) })
                       .Select(r => (IDictionary<string, object?>)r).ToList();
        }

        // ================================================================ WAREHOUSE LOOKUPS

        // old: EM_EMPLOYEE LEFT JOIN RK_WAREHOUSE where STAFFID = <user> and BUSINESSCENTERID = location (now the session STAFFID)
        public List<RkOption> GetUserWarehouses(string staffId, int centerId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT T2.ID, T2.WAREHOUSENAME NAME
                  FROM EM_EMPLOYEE T1
                  LEFT JOIN RK_WAREHOUSE T2 ON T1.WAREHOUSEID = T2.ID
                 WHERE T1.STAFFID = :StaffId AND T1.BUSINESSCENTERID = :Bc AND T2.ID IS NOT NULL";
            return conn.Query<RkOption>(sql, new { StaffId = staffId, Bc = centerId }).ToList();
        }

        public bool IsUserWarehouse(string staffId, long warehouseId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<int>("SELECT COUNT(1) FROM EM_EMPLOYEE WHERE STAFFID = :StaffId AND WAREHOUSEID = :Wh",
                                           new { StaffId = staffId, Wh = warehouseId }) > 0;
        }

        public List<RkOption> GetUnits()
        {
            using var conn = _databaseService.GetConnection();
            return conn.Query<RkOption>("SELECT ID, UNITNAME NAME FROM RK_UNIT WHERE ACTIVESTATUS = 1 ORDER BY UNITNAME").ToList();
        }

        public List<RkOption> GetItemOptions(long warehouseId, bool activeOnly)
        {
            using var conn = _databaseService.GetConnection();
            string sql = "SELECT ID, ITEMNAME NAME FROM RK_ITEMMASTER WHERE WAREHOUSEID = :Wh" + (activeOnly ? " AND ACTIVESTATUS = 1" : "") + " ORDER BY ITEMNAME";
            return conn.Query<RkOption>(sql, new { Wh = warehouseId }).ToList();
        }

        public RkItemUnitModel? GetItemUnit(long itemId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.QueryFirstOrDefault<RkItemUnitModel>(@"
                SELECT U.ID UNITID, U.UNITNAME, I.ID ITEMID, I.ITEMCODE
                  FROM RK_ITEMMASTER I
                  LEFT JOIN RK_UNIT U ON U.ID = I.UNITID
                 WHERE I.ID = :Item", new { Item = itemId });
        }

        public List<RkOption> GetDepartments(long warehouseId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Query<RkOption>("SELECT ID, DEPARTMENTNAME NAME FROM RK_DEPARTMENT WHERE ACTIVESTATUS = 1 AND WAREHOUSEID = :Wh ORDER BY DEPARTMENTNAME",
                                        new { Wh = warehouseId }).ToList();
        }

        // old LoadItemstock: (total in + received) - issued; when the item was never issued: total in only
        public decimal? GetAvailableQty(long warehouseId, long itemId)
        {
            using var conn = _databaseService.GetConnection();
            const string withIssues = @"
                SELECT (B.TOTALQTY + A.RECQTY) - A.ISSUEQTY ONHANDQTY
                  FROM (SELECT ITEM.ITEMCODE,
                               NVL(SUM(CASE WHEN ID.ISSUETYPE = 1 THEN ID.QUANTITY ELSE 0 END), 0) ISSUEQTY,
                               NVL(SUM(CASE WHEN ID.ISSUETYPE = 2 THEN ID.QUANTITY ELSE 0 END), 0) RECQTY
                          FROM RK_ISSUEDETAILS ID
                         INNER JOIN RK_ITEMMASTER ITEM ON ID.ITEMID = ITEM.ID
                         INNER JOIN RK_ISSUEMASTER IM ON ID.ISSUETRNO = IM.TRNO
                         WHERE ITEM.ACTIVESTATUS = 1 AND IM.ACTIVESTATUS = 1 AND IM.WAREHOUSEID = :Wh1 AND ITEM.ID = :Item1
                         GROUP BY ITEM.ITEMNAME, ITEM.ITEMCODE) A,
                       (SELECT ITEMM.ITEMCODE, SUM(ITEMIN.QUANTITY) TOTALQTY
                          FROM RK_ITEMIN ITEMIN
                         INNER JOIN RK_ITEMMASTER ITEMM ON ITEMIN.ITEMID = ITEMM.ID
                         WHERE ITEMM.ACTIVESTATUS = 1 AND ITEMIN.ACTIVESTATUS = 1 AND ITEMM.WAREHOUSEID = :Wh2 AND ITEMM.ID = :Item2
                         GROUP BY ITEMM.ITEMNAME, ITEMM.ITEMCODE) B
                 WHERE A.ITEMCODE = B.ITEMCODE";
            var rows = conn.Query<decimal?>(withIssues, new { Wh1 = warehouseId, Item1 = itemId, Wh2 = warehouseId, Item2 = itemId }).ToList();
            if (rows.Count > 0) return rows[0];

            const string inOnly = @"
                SELECT SUM(ITEMIN.QUANTITY) ONHANDQTY
                  FROM RK_ITEMIN ITEMIN
                 INNER JOIN RK_ITEMMASTER ITEMM ON ITEMIN.ITEMID = ITEMM.ID
                 WHERE ITEMM.ACTIVESTATUS = 1 AND ITEMIN.ACTIVESTATUS = 1 AND ITEMM.WAREHOUSEID = :Wh AND ITEMM.ID = :Item
                 GROUP BY ITEMM.ITEMNAME, ITEMM.ITEMCODE";
            return conn.Query<decimal?>(inOnly, new { Wh = warehouseId, Item = itemId }).FirstOrDefault();
        }

        // RK_LASTNO counter: increment first (row lock), then read -> the same number as YA's LASTNO + 1, but never twice
        private static long NextLastNo(IDbConnection conn, IDbTransaction tx, string key)
        {
            int changed = conn.Execute("UPDATE RK_LASTNO SET LASTNO = LASTNO + 1 WHERE ID = :K", new { K = key }, tx);
            if (changed != 1) throw new InvalidOperationException($"RK_LASTNO has no row '{key}'.");
            return conn.ExecuteScalar<long>("SELECT LASTNO FROM RK_LASTNO WHERE ID = :K", new { K = key }, tx);
        }

        // ================================================================ ITEM MASTER (dalItemMaster)

        private const string ItemSelect = @"
            SELECT IM.ID, IM.WAREHOUSEID, WAREHOUSE.WAREHOUSENAME, IM.ITEMNAME, IM.ITEMCODE, IM.UNITID, UNIT.UNITNAME, IM.DESCRIPTION, IM.ITEMSTOCK
              FROM RK_ITEMMASTER IM
             INNER JOIN RK_WAREHOUSE WAREHOUSE ON IM.WAREHOUSEID = WAREHOUSE.ID
             INNER JOIN RK_UNIT UNIT ON IM.UNITID = UNIT.ID";

        public List<RkItemModel> GetItems(long warehouseId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Query<RkItemModel>(ItemSelect + " WHERE IM.ACTIVESTATUS = 1 AND IM.WAREHOUSEID = :Wh ORDER BY IM.ITEMNAME",
                                           new { Wh = warehouseId }).ToList();
        }

        public RkItemModel? GetItem(long id)
        {
            using var conn = _databaseService.GetConnection();
            return conn.QueryFirstOrDefault<RkItemModel>(ItemSelect + " WHERE IM.ID = :Id", new { Id = id });
        }

        public bool ItemCodeExists(string itemCode, long warehouseId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<int>("SELECT COUNT(1) FROM RK_ITEMMASTER WHERE ITEMCODE = :Code AND WAREHOUSEID = :Wh",
                                           new { Code = itemCode, Wh = warehouseId }) > 0;
        }

        public long InsertItem(long warehouseId, string itemName, string itemCode, long unitId, string description, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();
            long id = NextLastNo(conn, tx, "ITEMMASTER");
            conn.Execute(@"
                INSERT INTO RK_ITEMMASTER (ID, WAREHOUSEID, ITEMNAME, ITEMCODE, UNITID, DESCRIPTION, CREATEDBY)
                VALUES (:Id, :Wh, :Nm, :Code, :Unit, :Descr, :StaffId)",
                new { Id = id, Wh = warehouseId, Nm = itemName, Code = itemCode, Unit = unitId, Descr = description, StaffId = staffId }, tx);
            tx.Commit();
            return id;
        }

        public int UpdateItem(long id, long unitId, string description, string userId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute(@"
                UPDATE RK_ITEMMASTER SET DESCRIPTION = :Descr, UNITID = :Unit, UPDATEDBY = :UserId, UPDATEDATE = SYSDATE WHERE ID = :Id",
                new { Descr = description, Unit = unitId, UserId = userId, Id = id });
        }

        // ================================================================ ITEM IN (dalItemIn)

        private const string ItemInSelect = @"
            SELECT ITEMIN.ID, ITEMIN.WAREHOUSEID, WAREHOUSE.WAREHOUSENAME, ITEMIN.ITEMID, IM.ITEMNAME, IM.ITEMCODE, ITEMIN.QUANTITY,
                   ITEMIN.UNITID, UNIT.UNITNAME, ITEMIN.PRICE, ITEMIN.VALIDITYDATE
              FROM RK_ITEMIN ITEMIN
             INNER JOIN RK_WAREHOUSE WAREHOUSE ON ITEMIN.WAREHOUSEID = WAREHOUSE.ID
             INNER JOIN RK_UNIT UNIT ON ITEMIN.UNITID = UNIT.ID
             INNER JOIN RK_ITEMMASTER IM ON ITEMIN.ITEMID = IM.ID";

        public List<RkItemInModel> GetItemIns(long warehouseId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Query<RkItemInModel>(ItemInSelect + " WHERE ITEMIN.ACTIVESTATUS = 1 AND ITEMIN.WAREHOUSEID = :Wh ORDER BY ITEMIN.ID DESC",
                                             new { Wh = warehouseId }).ToList();
        }

        public RkItemInModel? GetItemIn(long id)
        {
            using var conn = _databaseService.GetConnection();
            return conn.QueryFirstOrDefault<RkItemInModel>(ItemInSelect + " WHERE ITEMIN.ID = :Id", new { Id = id });
        }

        public long InsertItemIn(long warehouseId, long itemId, decimal quantity, long? unitId, decimal? price, DateTime? validityDate, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();
            long id = NextLastNo(conn, tx, "ITEMIN");
            conn.Execute(@"
                INSERT INTO RK_ITEMIN (ID, WAREHOUSEID, ITEMID, QUANTITY, UNITID, PRICE, VALIDITYDATE, CREATEDBY)
                VALUES (:Id, :Wh, :Item, :Qty, :Unit, :Price, :Valid, :StaffId)",
                new { Id = id, Wh = warehouseId, Item = itemId, Qty = quantity, Unit = unitId, Price = price, Valid = validityDate, StaffId = staffId }, tx);
            conn.Execute("UPDATE RK_ITEMMASTER SET ITEMSTOCK = NVL(ITEMSTOCK, 0) + :Qty WHERE ID = :Item",
                         new { Qty = quantity, Item = itemId }, tx);
            tx.Commit();
            return id;
        }

        private sealed class ItemInQty { public decimal? QUANTITY { get; set; } public long? ITEMID { get; set; } }

        public int UpdateItemIn(long id, decimal quantity, decimal? price, string userId)
        {
            using var conn = _databaseService.GetConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();
            // old quantity from the row itself (YA kept it in the ViewState of the page)
            var old = conn.QueryFirstOrDefault<ItemInQty>(
                "SELECT QUANTITY, ITEMID FROM RK_ITEMIN WHERE ID = :Id FOR UPDATE", new { Id = id }, tx);
            if (old == null || old.ITEMID == null) { tx.Rollback(); return 0; }

            conn.Execute("UPDATE RK_ITEMIN SET QUANTITY = :Qty, PRICE = :Price, UPDATEDBY = :UserId, UPDATEDATE = SYSDATE WHERE ID = :Id",
                         new { Qty = quantity, Price = price, UserId = userId, Id = id }, tx);
            conn.Execute("UPDATE RK_ITEMMASTER SET ITEMSTOCK = (NVL(ITEMSTOCK, 0) - :OldQty) + :NewQty WHERE ID = :Item",
                         new { OldQty = old.QUANTITY ?? 0, NewQty = quantity, Item = old.ITEMID }, tx);
            tx.Commit();
            return 1;
        }

        // ================================================================ ISSUE (Issue.aspx btnSave_OnClick)

        public (string? TrNo, string? Error) SaveIssue(RkIssueSave r, List<RkIssueLineChecked> lines, string staffId, Func<string, string?>? beforeCommit)
        {
            using var conn = _databaseService.GetConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();

            // TRNO = 'IS' + yyMMddHHmmss + RK_LASTNO 'IS' (6 digits, as YA's PadLeft)
            long n = NextLastNo(conn, tx, "IS");
            string stamp = conn.ExecuteScalar<string>("SELECT TO_CHAR(SYSDATE, 'YYMMDDHH24MISS') FROM DUAL", transaction: tx) ?? DateTime.Now.ToString("yyMMddHHmmss");
            string trNo = "IS" + stamp + n.ToString().PadLeft(6, '0');

            DateTime? returnDate = DateTime.TryParse(r.ReturnDate, out var rd) ? rd.Date : null;
            conn.Execute(@"
                INSERT INTO RK_ISSUEMASTER (ID, LOCATIONID, WAREHOUSEID, DEPARTMENTID, STAFFID, TRNO, ISSUEDATE, PURPOSE, RETURNDATE, CREATEDBY)
                VALUES (SQ_RK_ISSUEMASTER.NEXTVAL, :Loc, :Wh, :Dept, :Staff, :TrNo, TRUNC(SYSDATE), :Purpose, :RetD, :StaffId)",
                new { Loc = r.LocationId, Wh = r.WarehouseId, Dept = r.DepartmentId, Staff = r.StaffId, TrNo = trNo, Purpose = r.Purpose,
                      RetD = returnDate, StaffId = staffId }, tx);

            foreach (var line in lines)
            {
                // on-hand stock check (old: SUM(ITEMSTOCK - qty) >= 0), row locked until commit
                decimal stock = conn.ExecuteScalar<decimal?>("SELECT NVL(ITEMSTOCK, 0) FROM RK_ITEMMASTER WHERE ID = :Item FOR UPDATE",
                                                             new { Item = line.ItemId }, tx) ?? 0;
                if (stock - line.Qty < 0)
                {
                    tx.Rollback();
                    return (null, $"Item issue quantity cannot be greater than the available stock for Item: {line.ItemName} (stock {stock:0.##}).");
                }

                conn.Execute(@"
                    INSERT INTO RK_ISSUEDETAILS (ID, ISSUETRNO, ITEMID, UNITID, QUANTITY, ISSUETYPE, REMARKS, CREATEDATE, CREATEDBY)
                    VALUES (SQ_RK_ISSUEDETAILS.NEXTVAL, :TrNo, :Item, :Unit, :Qty, 1, :Rem, TRUNC(SYSDATE), :StaffId)",
                    new { TrNo = trNo, Item = line.ItemId, Unit = line.UnitId, Qty = line.Qty, Rem = line.Remarks, StaffId = staffId }, tx);
                conn.Execute("UPDATE RK_ITEMMASTER SET ITEMSTOCK = NVL(ITEMSTOCK, 0) - :Qty WHERE ID = :Item",
                             new { Qty = line.Qty, Item = line.ItemId }, tx);
            }

            // old: the dispatch (objDispatch.Update) ran before the issue was committed - a failure undoes the issue
            if (beforeCommit != null)
            {
                string? error = beforeCommit(trNo);
                if (error != null) { tx.Rollback(); return (null, error); }
            }

            tx.Commit();
            return (trNo, null);
        }

        // ================================================================ RECEIVE (dalReceive + Receive.aspx)

        public List<RkOption> GetOpenIssues(long warehouseId, long departmentId)
        {
            using var conn = _databaseService.GetConnection();
            // old: warehouse list, or the department's list once a department is chosen
            string where = departmentId > 0 ? "DEPARTMENTID = :Dept" : "WAREHOUSEID = :Wh";
            string sql = $@"
                SELECT ID, TRNO || '--' || STAFFID NAME
                  FROM RK_ISSUEMASTER
                 WHERE ISSUESTATUS = 1 AND ACTIVESTATUS = 1 AND {where}
                 ORDER BY ID DESC";
            return conn.Query<RkOption>(sql, new { Dept = departmentId, Wh = warehouseId }).ToList();
        }

        public RkIssueHeaderModel? GetIssueHeader(long issueId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.QueryFirstOrDefault<RkIssueHeaderModel>(@"
                SELECT ID, TRNO, STAFFID, PURPOSE, ISSUEDATE, RETURNDATE, WAREHOUSEID
                  FROM RK_ISSUEMASTER WHERE ID = :Id AND ISSUESTATUS = 1 AND ACTIVESTATUS = 1", new { Id = issueId });
        }

        private const string PendingSelect = @"
            SELECT ID.ID, ID.ITEMID, ITEM.ITEMNAME, ITEM.ITEMCODE, ID.UNITID, UNIT.UNITNAME, NVL(ID.QUANTITY, 0) ISSQTY,
                   COALESCE(ID.QUANTITY, 0) - COALESCE(ID.RECTOTALQTY, 0) RECQTY, ID.REMARKS
              FROM RK_ISSUEDETAILS ID
             INNER JOIN RK_ISSUEMASTER IM ON ID.ISSUETRNO = IM.TRNO
             INNER JOIN RK_ITEMMASTER ITEM ON ID.ITEMID = ITEM.ID
             INNER JOIN RK_UNIT UNIT ON ID.UNITID = UNIT.ID
             WHERE ID.ISSUETYPE = 1 AND IM.ISSUESTATUS = 1 AND IM.ACTIVESTATUS = 1
               AND (COALESCE(ID.QUANTITY, 0) - COALESCE(ID.RECTOTALQTY, 0)) > 0
               AND IM.TRNO = :TrNo";

        public List<RkPendingLineModel> GetPendingLines(string trNo)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Query<RkPendingLineModel>(PendingSelect + " ORDER BY ID.ID", new { TrNo = trNo }).ToList();
        }

        public (bool Closed, string? Error) SaveReceive(long issueId, List<RkReceiveLine> lines, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();

            string? trNo = conn.ExecuteScalar<string>(
                "SELECT TRNO FROM RK_ISSUEMASTER WHERE ID = :Id AND ISSUESTATUS = 1 AND ACTIVESTATUS = 1 FOR UPDATE", new { Id = issueId }, tx);
            if (trNo == null) { tx.Rollback(); return (false, "This issue is not open any more. Please reload the page."); }

            var pending = conn.Query<RkPendingLineModel>(PendingSelect, new { TrNo = trNo }, tx).ToDictionary(p => p.ID);
            foreach (var line in lines)
            {
                if (!pending.TryGetValue(line.LineId, out var p)) { tx.Rollback(); return (false, "An item of this issue was already received. Please reload."); }
                if (!decimal.TryParse(line.Qty, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal qty)
                    || qty <= 0 || qty > p.RECQTY)
                {
                    tx.Rollback();
                    return (false, $"Receive quantity of {p.ITEMNAME} must be more than 0 and not more than {p.RECQTY:0.##}.");
                }

                // receive row (ISSUETYPE 2) + issue line received total + stock back (old btnSave_OnClick)
                conn.Execute(@"
                    INSERT INTO RK_ISSUEDETAILS (ID, ISSUETRNO, ITEMID, UNITID, QUANTITY, ISSUETYPE, REMARKS, CREATEDATE, CREATEDBY)
                    VALUES (SQ_RK_ISSUEDETAILS.NEXTVAL, :TrNo, :Item, :Unit, :Qty, 2, :Rem, TRUNC(SYSDATE), :StaffId)",
                    new { TrNo = trNo, Item = p.ITEMID, Unit = p.UNITID, Qty = qty, Rem = p.REMARKS, StaffId = staffId }, tx);
                conn.Execute("UPDATE RK_ISSUEDETAILS SET RECTOTALQTY = NVL(RECTOTALQTY, 0) + :Qty WHERE ID = :LineId",
                             new { Qty = qty, LineId = p.ID }, tx);
                conn.Execute("UPDATE RK_ITEMMASTER SET ITEMSTOCK = NVL(ITEMSTOCK, 0) + :Qty WHERE ID = :Item",
                             new { Qty = qty, Item = p.ITEMID }, tx);
            }

            // old: issued - received = 0 -> ISSUESTATUS = 0 (closed)
            decimal left = conn.ExecuteScalar<decimal?>(@"
                SELECT NVL(SUM(CASE WHEN ISSUETYPE = 1 THEN QUANTITY ELSE 0 END), 0) - NVL(SUM(CASE WHEN ISSUETYPE = 2 THEN QUANTITY ELSE 0 END), 0)
                  FROM RK_ISSUEDETAILS WHERE ISSUETRNO = :TrNo", new { TrNo = trNo }, tx) ?? 0;
            bool closed = left == 0;
            if (closed) conn.Execute("UPDATE RK_ISSUEMASTER SET ISSUESTATUS = 0 WHERE TRNO = :TrNo", new { TrNo = trNo }, tx);

            tx.Commit();
            return (closed, null);
        }

        // ================================================================ WAREHOUSE PERMISSION (dalWarehouse)

        public List<RkOption> GetAllWarehouses()
        {
            using var conn = _databaseService.GetConnection();
            return conn.Query<RkOption>("SELECT ID, WAREHOUSENAME NAME FROM RK_WAREHOUSE ORDER BY WAREHOUSENAME").ToList();
        }

        public long? GetEmployeeWarehouse(string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Query<long?>(@"
                SELECT W.ID FROM RK_WAREHOUSE W, EM_EMPLOYEE E
                 WHERE E.WAREHOUSEID = W.ID AND W.ACTIVESTATUS = 1 AND E.STAFFID = :StaffId", new { StaffId = staffId }).FirstOrDefault();
        }

        public int SetEmployeeWarehouse(string staffId, long warehouseId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute("UPDATE EM_EMPLOYEE SET WAREHOUSEID = :Wh WHERE STAFFID = :StaffId", new { Wh = warehouseId, StaffId = staffId });
        }

        // ================================================================ REPORT (dalRkReport.GetWarehousewiseItemReport)

        public List<IDictionary<string, object?>> ReportStock(long warehouseId)
        {
            var p = new DynamicParameters();
            string criteria = "", criteria2 = "";
            if (warehouseId > 0)
            {
                criteria = " AND IM.WAREHOUSEID = :Wh1";
                criteria2 = " AND ITEMM.WAREHOUSEID = :Wh2";
                p.Add("Wh1", warehouseId);
                p.Add("Wh2", warehouseId);
            }
            // same calculation as YA; NVL so an item that was never issued shows its stock instead of an empty cell
            string sql = $@"
                SELECT B.ITEMNAME, B.ITEMCODE, B.TOTALQTY,
                       NVL(A.ISSUEQTY, 0) ISSUEQTY, NVL(A.RECQTY, 0) RECQTY, NVL(A.DAMAGEQTY, 0) DAMAGEQTY,
                       (B.TOTALQTY + NVL(A.RECQTY, 0)) - NVL(A.ISSUEQTY, 0) ONHANDQTY,
                       NVL(A.PENDINGRECEIVEQTYWITHDAMAGE, 0) - NVL(A.DAMAGEQTY, 0) PENDINGRECEIVEQTY
                  FROM (SELECT ITEM.ITEMNAME, ITEM.ITEMCODE,
                               NVL(SUM(CASE WHEN ID.ISSUETYPE = 1 THEN ID.QUANTITY ELSE 0 END), 0) ISSUEQTY,
                               NVL(SUM(CASE WHEN ID.ISSUETYPE = 1 AND DEPT.DEPARTMENTTYPE = 1 THEN ID.QUANTITY ELSE 0 END), 0) DAMAGEQTY,
                               NVL(SUM(CASE WHEN ID.ISSUETYPE = 2 THEN ID.QUANTITY ELSE 0 END), 0) RECQTY,
                               NVL(SUM(CASE WHEN ID.ISSUETYPE = 1 THEN ID.QUANTITY ELSE 0 END), 0)
                               - NVL(SUM(CASE WHEN ID.ISSUETYPE = 2 THEN ID.QUANTITY ELSE 0 END), 0) PENDINGRECEIVEQTYWITHDAMAGE
                          FROM RK_ISSUEDETAILS ID
                         INNER JOIN RK_ITEMMASTER ITEM ON ID.ITEMID = ITEM.ID
                         INNER JOIN RK_ISSUEMASTER IM ON ID.ISSUETRNO = IM.TRNO
                         INNER JOIN RK_DEPARTMENT DEPT ON IM.DEPARTMENTID = DEPT.ID
                         WHERE ITEM.ACTIVESTATUS = 1 AND IM.ACTIVESTATUS = 1 {criteria}
                         GROUP BY ITEM.ITEMNAME, ITEM.ITEMCODE) A
                 RIGHT JOIN (SELECT ITEMM.ITEMNAME, ITEMM.ITEMCODE, SUM(ITEMIN.QUANTITY) TOTALQTY
                               FROM RK_ITEMIN ITEMIN
                              INNER JOIN RK_ITEMMASTER ITEMM ON ITEMIN.ITEMID = ITEMM.ID
                              WHERE ITEMM.ACTIVESTATUS = 1 AND ITEMIN.ACTIVESTATUS = 1 {criteria2}
                              GROUP BY ITEMM.ITEMNAME, ITEMM.ITEMCODE) B ON A.ITEMCODE = B.ITEMCODE
                 ORDER BY B.ITEMCODE";
            using var conn = _databaseService.GetConnection();
            return conn.Query(sql, p).Select(r => (IDictionary<string, object?>)r).ToList();
        }
    }
}
