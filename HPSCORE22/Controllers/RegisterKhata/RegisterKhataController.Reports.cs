// Controllers/RegisterKhata/RegisterKhataController.Reports.cs
// Register Khata reports - one page (Views/RegisterKhata/RkReport.cshtml), GET filters, Excel = CSV of the same rows
// (DispatchReportColumn + DispatchReportCsv, as the Dispatch and Service reports):
//   /RegisterKhata/RptVehicleInOut    <- RptVehicleInOut.aspx    (vehicle register of a location between two dates)
//   /RegisterKhata/WarehouseItemRpt   <- WarehouseItemRpt.aspx   (stock per item: in, on hand, pending return, damage)
using AlphaCare.Models;
using AlphaCare.Services;
using Microsoft.AspNetCore.Mvc;
using C = AlphaCare.Models.DispatchReportColumn;

namespace QCMS.Controllers
{
    public partial class RegisterKhataController
    {
        // =====================================================================================================
        // VEHICLE IN / OUT REPORT (old RptVehicleInOut.aspx: location + from / to)
        // =====================================================================================================
        [HttpGet]
        public IActionResult RptVehicleInOut(int locationId = 0, DateTime? from = null, DateTime? to = null, int run = 0, int export = 0)
        {
            if (PageGuard("RegisterKhata/RptVehicleInOut") is { } g) return g;
            var centers = _repo.GetAllBusinessCenters();
            var t = DateTime.Today;
            var vm = new RkReportViewModel
            {
                Action = nameof(RptVehicleInOut), Title = "Vehicle In / Out Report", Icon = "fa-truck",
                Note = "Vehicle entries of the location created between the two dates.",
                UsesDates = true, Locations = centers, CenterId = PickLocation(locationId, centers),
                From = from ?? new DateTime(t.Year, t.Month, 1), To = to ?? t,
                Columns = new()
                {
                    new C("CENTERNAME", "Location"), new C("VEHICLENO", "Vehicle No"), new C("GATENO", "Gate No"),
                    new C("DRIVERNAME", "Driver Name"), new C("DRIVERMOBILE", "Driver Mobile"), new C("INDATE", "In Date", "day"),
                    new C("INTIME", "In Time"), new C("OUTDATE", "Out Date", "day"), new C("OUTTIME", "Out Time"), new C("REMARKS", "Remarks")
                }
            };
            if (vm.From > vm.To) (vm.From, vm.To) = (vm.To, vm.From);

            if (run == 1 || export == 1)
            {
                if (vm.CenterId <= 0) vm.Message = "Select Location";
                else
                {
                    try
                    {
                        vm.Rows = _repo.ReportVehicles(vm.CenterId, vm.From!.Value, vm.To!.Value);
                        vm.Searched = true;
                        if (export == 1) return File(DispatchReportCsv.Build(vm.Title, vm.Columns, vm.Rows), "text/csv", $"VehicleInOut_{DateTime.Now:yyyyMMddHHmm}.csv");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Vehicle report failed.");
                        vm.Message = "The report could not be loaded. Please try again.";
                    }
                }
            }
            ViewData["Title"] = vm.Title;
            return View("RkReport", vm);
        }

        // =====================================================================================================
        // WAREHOUSE ITEM REPORT (old WarehouseItemRpt.aspx: own location only, warehouse "Select" = every warehouse)
        // =====================================================================================================
        [HttpGet]
        public IActionResult WarehouseItemRpt(long warehouseId = 0, int run = 0, int export = 0)
        {
            if (PageGuard("RegisterKhata/WarehouseItemRpt") is { } g) return g;
            var vm = new RkReportViewModel
            {
                Action = nameof(WarehouseItemRpt), Title = "Warehouse Item Report", Icon = "fa-boxes-stacked",
                Note = "On hand = total in + received - issued. Pending receive = issued - received - damage (issues to a damage department).",
                UsesWarehouse = true, LockLocation = true, Locations = _repo.GetAllBusinessCenters(), CenterId = UserCenterId,
                Columns = new()
                {
                    new C("ITEMNAME", "Item Name"), new C("ITEMCODE", "Item Code"), new C("TOTALQTY", "Total Qty", "sum"),
                    new C("ISSUEQTY", "Issue Qty", "sum"), new C("RECQTY", "Receive Qty", "sum"), new C("ONHANDQTY", "Onhand Qty", "sum"),
                    new C("PENDINGRECEIVEQTY", "Pending Receive Qty", "sum"), new C("DAMAGEQTY", "Damage Qty", "sum")
                }
            };
            vm.Warehouses = MyWarehouses(vm.CenterId);
            vm.WarehouseId = warehouseId > 0 && vm.Warehouses.Any(w => w.ID == warehouseId) ? warehouseId : 0;

            if (run == 1 || export == 1)
            {
                try
                {
                    vm.Rows = _repo.ReportStock(vm.WarehouseId);
                    vm.Searched = true;
                    if (export == 1) return File(DispatchReportCsv.Build(vm.Title, vm.Columns, vm.Rows), "text/csv", $"WarehouseItem_{DateTime.Now:yyyyMMddHHmm}.csv");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Warehouse item report failed.");
                    vm.Message = "The report could not be loaded. Please try again.";
                }
            }
            ViewData["Title"] = vm.Title;
            return View("RkReport", vm);
        }
    }
}
