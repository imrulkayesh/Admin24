// Repositories/Service/ServiceRepository.Setup.cs
// Port of YA dalServiceCenter, dalServiceCategory, dalServiceTeam, dalMachine, dalAccessoriesCategory, dalServiceAccessories (setup part).
// Same columns + SQ_SC_* sequences; CREATEDBY / UPDATEDBY get the same value as in YA (USERID or STAFFID, per table).
// Duplicate checks: same columns as the old CheckDuplicate; on edit the row itself is ignored (the old edit had no check at all).
using AlphaCare.Models;
using Dapper;

namespace Admin360.Repositories
{
    public partial class ServiceRepository
    {
        // ================================================================ SERVICE CENTER (old dalServiceCenter)

        private const string ServiceCenterSelect = @"
            SELECT SC.ID, SC.SERVICECENTERNAME, SC.BUSINESSCENTERID, SC.CENTERADMIN, B.CENTERNAME, D.DESIGNATIONNAME, DE.DEPARTMENTCODE,
                   E.EMPLOYEENAME, E.OFFICEMOBILE, E.OFFICEMAIL
              FROM SC_SERVICECENTER SC
             INNER JOIN EM_EMPLOYEE E ON E.STAFFID = SC.CENTERADMIN
             INNER JOIN BS_DESIGNATION D ON D.ID = E.DESIGNATIONID
             INNER JOIN BS_DEPARTMENT DE ON DE.ID = E.DEPARTMENTID
             INNER JOIN BS_BUSINESSCENTER B ON B.ID = SC.BUSINESSCENTERID";

        public List<ServiceCenterModel> GetServiceCenters()
        {
            using var conn = _databaseService.GetConnection();
            return conn.Query<ServiceCenterModel>(ServiceCenterSelect + " ORDER BY B.CENTERNAME, SC.SERVICECENTERNAME").ToList();
        }

        public ServiceCenterModel? GetServiceCenter(int id)
        {
            using var conn = _databaseService.GetConnection();
            return conn.QueryFirstOrDefault<ServiceCenterModel>(ServiceCenterSelect + " WHERE SC.ID = :Id", new { Id = id });
        }

        public bool ServiceCenterNameExists(string name, int businessCenterId, int exceptId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM SC_SERVICECENTER WHERE SERVICECENTERNAME = :Nm AND BUSINESSCENTERID = :Bc AND ID <> :Ex",
                new { Nm = name, Bc = businessCenterId, Ex = exceptId }) > 0;
        }

        public int InsertServiceCenter(string name, int businessCenterId, string adminStaffId, string userId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute(@"
                INSERT INTO SC_SERVICECENTER (ID, SERVICECENTERNAME, BUSINESSCENTERID, CENTERADMIN, CREATEDBY)
                VALUES (SQ_SC_SERVICECENTER.NEXTVAL, :Nm, :Bc, :Adm, :UserId)",
                new { Nm = name, Bc = businessCenterId, Adm = adminStaffId, UserId = userId });
        }

        public int UpdateServiceCenter(int id, string name, int businessCenterId, string adminStaffId, string userId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute(@"
                UPDATE SC_SERVICECENTER
                   SET SERVICECENTERNAME = :Nm, BUSINESSCENTERID = :Bc, CENTERADMIN = :Adm, UPDATEDBY = :UserId, UPDATEDATE = SYSDATE
                 WHERE ID = :Id",
                new { Nm = name, Bc = businessCenterId, Adm = adminStaffId, UserId = userId, Id = id });
        }

        // ================================================================ CATEGORY (old dalServiceCategory)

        public List<ServiceCategoryModel> GetCategories(int serviceCenterId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT C.ID, C.CATEGORYNAME, C.SERVICECENTERID, SC.BUSINESSCENTERID
                  FROM SC_CATEGORY C
                  LEFT JOIN SC_SERVICECENTER SC ON SC.ID = C.SERVICECENTERID
                 WHERE C.SERVICECENTERID = :Sc
                 ORDER BY C.CATEGORYNAME";
            return conn.Query<ServiceCategoryModel>(sql, new { Sc = serviceCenterId }).ToList();
        }

        public ServiceCategoryModel? GetCategory(int id)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT C.ID, C.CATEGORYNAME, C.SERVICECENTERID, SC.BUSINESSCENTERID
                  FROM SC_CATEGORY C
                 INNER JOIN SC_SERVICECENTER SC ON SC.ID = C.SERVICECENTERID
                 WHERE C.ID = :Id";
            return conn.QueryFirstOrDefault<ServiceCategoryModel>(sql, new { Id = id });
        }

        public bool CategoryNameExists(string name, int serviceCenterId, int exceptId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM SC_CATEGORY WHERE CATEGORYNAME = :Nm AND SERVICECENTERID = :Sc AND ID <> :Ex",
                new { Nm = name, Sc = serviceCenterId, Ex = exceptId }) > 0;
        }

        public int InsertCategory(string name, int serviceCenterId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute("INSERT INTO SC_CATEGORY (ID, CATEGORYNAME, SERVICECENTERID) VALUES (SQ_SC_CATEGORY.NEXTVAL, :Nm, :Sc)",
                                new { Nm = name, Sc = serviceCenterId });
        }

        public int UpdateCategory(int id, string name, int serviceCenterId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute("UPDATE SC_CATEGORY SET CATEGORYNAME = :Nm, SERVICECENTERID = :Sc WHERE ID = :Id",
                                new { Nm = name, Sc = serviceCenterId, Id = id });
        }

        // ================================================================ SERVICE TEAM (old dalServiceTeam)

        private const string TeamSelect = @"
            SELECT T.ID, T.TEAMNAME, T.BUSINESSCENTERID, T.SERVICECENTERID, T.SUPERVISOR, SC.SERVICECENTERNAME, B.CENTERNAME,
                   D.DESIGNATIONNAME, DE.DEPARTMENTCODE, E.EMPLOYEENAME, E.OFFICEMOBILE, E.OFFICEMAIL
              FROM SC_SERVICETEAM T
             INNER JOIN SC_SERVICECENTER SC ON SC.ID = T.SERVICECENTERID
             INNER JOIN EM_EMPLOYEE E ON E.STAFFID = T.SUPERVISOR
             INNER JOIN BS_DESIGNATION D ON D.ID = E.DESIGNATIONID
             INNER JOIN BS_DEPARTMENT DE ON DE.ID = E.DEPARTMENTID
             INNER JOIN BS_BUSINESSCENTER B ON B.ID = T.BUSINESSCENTERID";

        public List<ServiceTeamModel> GetTeams(int serviceCenterId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Query<ServiceTeamModel>(TeamSelect + " WHERE T.SERVICECENTERID = :Sc ORDER BY T.TEAMNAME", new { Sc = serviceCenterId }).ToList();
        }

        public ServiceTeamModel? GetTeam(int id)
        {
            using var conn = _databaseService.GetConnection();
            return conn.QueryFirstOrDefault<ServiceTeamModel>(TeamSelect + " WHERE T.ID = :Id", new { Id = id });
        }

        public List<ServiceOption> GetTeamOptions(int serviceCenterId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Query<ServiceOption>("SELECT ID, TEAMNAME NAME FROM SC_SERVICETEAM WHERE SERVICECENTERID = :Sc ORDER BY TEAMNAME",
                                             new { Sc = serviceCenterId }).ToList();
        }

        public bool TeamNameExists(string name, int serviceCenterId, int exceptId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM SC_SERVICETEAM WHERE TEAMNAME = :Nm AND SERVICECENTERID = :Sc AND ID <> :Ex",
                new { Nm = name, Sc = serviceCenterId, Ex = exceptId }) > 0;
        }

        public int InsertTeam(string name, int serviceCenterId, int businessCenterId, string supervisorStaffId, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute(@"
                INSERT INTO SC_SERVICETEAM (ID, TEAMNAME, SERVICECENTERID, BUSINESSCENTERID, SUPERVISOR, CREATEDBY)
                VALUES (SQ_SC_SERVICETEAM.NEXTVAL, :Nm, :Sc, :Bc, :Sup, :StaffId)",
                new { Nm = name, Sc = serviceCenterId, Bc = businessCenterId, Sup = supervisorStaffId, StaffId = staffId });
        }

        public int UpdateTeam(int id, string name, int serviceCenterId, int businessCenterId, string supervisorStaffId, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute(@"
                UPDATE SC_SERVICETEAM
                   SET TEAMNAME = :Nm, SERVICECENTERID = :Sc, BUSINESSCENTERID = :Bc, SUPERVISOR = :Sup, UPDATEDBY = :StaffId, UPDATEDATE = SYSDATE
                 WHERE ID = :Id",
                new { Nm = name, Sc = serviceCenterId, Bc = businessCenterId, Sup = supervisorStaffId, StaffId = staffId, Id = id });
        }

        // ================================================================ TEAM MEMBER (old dalServiceTeam ...TeamMember)

        private const string MemberSelect = @"
            SELECT TM.ID, TM.STAFFID, TM.TEAMID, TM.SERVICECENTERID, TM.BUSINESSCENTERID, TM.ISACTIVE, T.TEAMNAME, SC.SERVICECENTERNAME,
                   B.CENTERNAME, D.DESIGNATIONNAME, DE.DEPARTMENTCODE, E.EMPLOYEENAME, E.OFFICEMOBILE, E.OFFICEMAIL
              FROM SC_TEAMMEMBER TM
             INNER JOIN SC_SERVICETEAM T ON T.ID = TM.TEAMID
             INNER JOIN SC_SERVICECENTER SC ON SC.ID = T.SERVICECENTERID
             INNER JOIN EM_EMPLOYEE E ON E.STAFFID = TM.STAFFID
             INNER JOIN BS_DESIGNATION D ON D.ID = E.DESIGNATIONID
             INNER JOIN BS_DEPARTMENT DE ON DE.ID = E.DEPARTMENTID
             INNER JOIN BS_BUSINESSCENTER B ON B.ID = TM.BUSINESSCENTERID";

        public List<ServiceTeamMemberModel> GetTeamMembers(int teamId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Query<ServiceTeamMemberModel>(MemberSelect + " WHERE TM.TEAMID = :Team ORDER BY E.EMPLOYEENAME", new { Team = teamId }).ToList();
        }

        public ServiceTeamMemberModel? GetTeamMember(int id)
        {
            using var conn = _databaseService.GetConnection();
            return conn.QueryFirstOrDefault<ServiceTeamMemberModel>(MemberSelect + " WHERE TM.ID = :Id", new { Id = id });
        }

        public int InsertTeamMember(int teamId, int serviceCenterId, int businessCenterId, string memberStaffId, string userId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute(@"
                INSERT INTO SC_TEAMMEMBER (ID, TEAMID, SERVICECENTERID, BUSINESSCENTERID, STAFFID, CREATEDBY)
                VALUES (SQ_SC_TEAMMEMBER.NEXTVAL, :Team, :Sc, :Bc, :Mem, :UserId)",
                new { Team = teamId, Sc = serviceCenterId, Bc = businessCenterId, Mem = memberStaffId, UserId = userId });
        }

        public int UpdateTeamMember(int id, int teamId, int serviceCenterId, int businessCenterId, string memberStaffId, string userId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute(@"
                UPDATE SC_TEAMMEMBER
                   SET TEAMID = :Team, SERVICECENTERID = :Sc, BUSINESSCENTERID = :Bc, STAFFID = :Mem, UPDATEDBY = :UserId, UPDATEDATE = SYSDATE
                 WHERE ID = :Id",
                new { Team = teamId, Sc = serviceCenterId, Bc = businessCenterId, Mem = memberStaffId, UserId = userId, Id = id });
        }

        // ================================================================ MACHINE (old dalMachine)

        public List<ServiceMachineModel> GetMachines(int businessCenterId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT M.ID, M.MACHINENAME, M.MACHINECODE, M.BUSINESSCENTERID, M.COMPANYID, M.DEPARTMENTID, M.ISACTIVE,
                       B.CENTERNAME, C.COMPANYNAME, D.DEPARTMENTNAME
                  FROM SC_MACHINE M
                 INNER JOIN BS_BUSINESSCENTER B ON B.ID = M.BUSINESSCENTERID
                 INNER JOIN BS_COMPANY C ON C.ID = M.COMPANYID
                 INNER JOIN BS_DEPARTMENT D ON D.ID = M.DEPARTMENTID
                 WHERE M.BUSINESSCENTERID = :Bc
                 ORDER BY M.MACHINENAME";
            return conn.Query<ServiceMachineModel>(sql, new { Bc = businessCenterId }).ToList();
        }

        public ServiceMachineModel? GetMachine(int id)
        {
            using var conn = _databaseService.GetConnection();
            return conn.QueryFirstOrDefault<ServiceMachineModel>(
                "SELECT ID, MACHINENAME, MACHINECODE, BUSINESSCENTERID, COMPANYID, DEPARTMENTID, ISACTIVE FROM SC_MACHINE WHERE ID = :Id",
                new { Id = id });
        }

        public bool MachineNameExists(string name, int businessCenterId, int companyId, int exceptId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM SC_MACHINE WHERE MACHINENAME = :Nm AND BUSINESSCENTERID = :Bc AND COMPANYID = :Comp AND ID <> :Ex",
                new { Nm = name, Bc = businessCenterId, Comp = companyId, Ex = exceptId }) > 0;
        }

        public int InsertMachine(int businessCenterId, int companyId, int departmentId, string name, string code, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute(@"
                INSERT INTO SC_MACHINE (ID, BUSINESSCENTERID, COMPANYID, DEPARTMENTID, MACHINENAME, MACHINECODE, CREATEDBY)
                VALUES (SQ_SC_MACHINE.NEXTVAL, :Bc, :Comp, :Dept, :Nm, :Cd, :StaffId)",
                new { Bc = businessCenterId, Comp = companyId, Dept = departmentId, Nm = name, Cd = code, StaffId = staffId });
        }

        public int UpdateMachine(int id, int businessCenterId, int companyId, int departmentId, string name, string code, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute(@"
                UPDATE SC_MACHINE
                   SET BUSINESSCENTERID = :Bc, COMPANYID = :Comp, DEPARTMENTID = :Dept, MACHINENAME = :Nm, MACHINECODE = :Cd,
                       UPDATEDATE = SYSDATE, UPDATEDBY = :StaffId
                 WHERE ID = :Id",
                new { Bc = businessCenterId, Comp = companyId, Dept = departmentId, Nm = name, Cd = code, StaffId = staffId, Id = id });
        }

        // ================================================================ ACCESSORIES CATEGORY (old dalAccessoriesCategory)

        public AccessoriesCategoryModel? GetAccessoryCategory(int id)
        {
            using var conn = _databaseService.GetConnection();
            return conn.QueryFirstOrDefault<AccessoriesCategoryModel>(
                "SELECT ID, ACCESSORIESCATEGORY FROM SC_ACCESSORIESCATEGORY WHERE ID = :Id", new { Id = id });
        }

        public bool AccessoryCategoryNameExists(string name, int exceptId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<int>("SELECT COUNT(1) FROM SC_ACCESSORIESCATEGORY WHERE ACCESSORIESCATEGORY = :Nm AND ID <> :Ex",
                                           new { Nm = name, Ex = exceptId }) > 0;
        }

        public int InsertAccessoryCategory(string name)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute("INSERT INTO SC_ACCESSORIESCATEGORY (ID, ACCESSORIESCATEGORY) VALUES (SQ_SC_ACCESSORIESCATEGORY.NEXTVAL, :Nm)",
                                new { Nm = name });
        }

        public int UpdateAccessoryCategory(int id, string name)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute("UPDATE SC_ACCESSORIESCATEGORY SET ACCESSORIESCATEGORY = :Nm WHERE ID = :Id", new { Nm = name, Id = id });
        }

        // ================================================================ ACCESSORIES (old dalServiceAccessories)

        public List<ServiceAccessoryModel> GetAccessories(int serviceCenterId)
        {
            using var conn = _databaseService.GetConnection();
            const string sql = @"
                SELECT AC.ID, AC.CATEGORYID, AC.ACCESSORIESNAME, AC.STOCK, AC.BUSINESSCENTERID, AC.SERVICECENTERID,
                       B.CENTERCODE, S.SERVICECENTERNAME, C.ACCESSORIESCATEGORY
                  FROM SC_ACCESSORIES AC
                 INNER JOIN BS_BUSINESSCENTER B ON B.ID = AC.BUSINESSCENTERID
                 INNER JOIN SC_SERVICECENTER S ON S.ID = AC.SERVICECENTERID
                 INNER JOIN SC_ACCESSORIESCATEGORY C ON C.ID = AC.CATEGORYID
                 WHERE AC.SERVICECENTERID = :Sc
                 ORDER BY C.ACCESSORIESCATEGORY, AC.ACCESSORIESNAME";
            return conn.Query<ServiceAccessoryModel>(sql, new { Sc = serviceCenterId }).ToList();
        }

        public ServiceAccessoryModel? GetAccessory(int id)
        {
            using var conn = _databaseService.GetConnection();
            return conn.QueryFirstOrDefault<ServiceAccessoryModel>(
                "SELECT ID, CATEGORYID, ACCESSORIESNAME, STOCK, BUSINESSCENTERID, SERVICECENTERID FROM SC_ACCESSORIES WHERE ID = :Id",
                new { Id = id });
        }

        public bool AccessoryNameExists(string name, int categoryId, int serviceCenterId, int exceptId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM SC_ACCESSORIES WHERE ACCESSORIESNAME = :Nm AND SERVICECENTERID = :Sc AND CATEGORYID = :Cat AND ID <> :Ex",
                new { Nm = name, Sc = serviceCenterId, Cat = categoryId, Ex = exceptId }) > 0;
        }

        public int InsertAccessory(int categoryId, string name, int stock, int businessCenterId, int serviceCenterId, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute(@"
                INSERT INTO SC_ACCESSORIES (ID, CATEGORYID, ACCESSORIESNAME, STOCK, BUSINESSCENTERID, SERVICECENTERID, CREATEDBY)
                VALUES (SQ_SC_ACCESSORIES.NEXTVAL, :Cat, :Nm, :Stk, :Bc, :Sc, :StaffId)",
                new { Cat = categoryId, Nm = name, Stk = stock, Bc = businessCenterId, Sc = serviceCenterId, StaffId = staffId });
        }

        public int UpdateAccessory(int id, int categoryId, string name, int stock, int businessCenterId, int serviceCenterId, string staffId)
        {
            using var conn = _databaseService.GetConnection();
            return conn.Execute(@"
                UPDATE SC_ACCESSORIES
                   SET CATEGORYID = :Cat, ACCESSORIESNAME = :Nm, STOCK = :Stk, BUSINESSCENTERID = :Bc, SERVICECENTERID = :Sc,
                       UPDATEDATE = SYSDATE, UPDATEDBY = :StaffId
                 WHERE ID = :Id",
                new { Cat = categoryId, Nm = name, Stk = stock, Bc = businessCenterId, Sc = serviceCenterId, StaffId = staffId, Id = id });
        }
    }
}
