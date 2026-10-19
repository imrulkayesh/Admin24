using Dapper;
using QCMS.Models;
using QCMS.Services;
using RetailCare.Models;
using System.Data;

namespace QCMS.Repositories
{
    public class UserRepository
    {
        private readonly DatabaseService _databaseService;

        public UserRepository(DatabaseService databaseService)
        {
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        }

        /// <summary>
        /// Active user + all companies assigned to him (one row per company).
        /// Same idea as old dalUser.GetLoginInfoByUserID(). Password is NOT checked here (HRIS API does it).
        /// </summary>
        public List<UserInfoModel> GetUserInfo(string username)
        {
            using var conn = _databaseService.GetConnection();

            const string sql = @"Select * from UC_USERS where ISACTIVE=1 AND UPPER(USERNAME) = UPPER(:Username)";

            return conn.Query<UserInfoModel>(sql, new { Username = username }).ToList();
        }

        public UserInfoModel? CheckUser(string username)
        {
            using var conn = _databaseService.GetConnection();

            // BUG FIXED: old query used "U.ISACTIVE" without alias U
            const string sql = @"
                SELECT * FROM USERINFO U
                 WHERE UPPER(U.USERID) = UPPER(:Username)
                   AND U.ISACTIVE = '1'";

            return conn.QueryFirstOrDefault<UserInfoModel>(sql, new { Username = username });
        }

        public async Task<List<CompanyModel>> GetCompany(string username)
        {
            using var conn = _databaseService.GetConnection();

            const string sql = @"
                SELECT C.COMPANYID, C.COMPANYNAME
                  FROM USERCOMPANY UC
                  JOIN COMPANY C ON C.COMPANYID = UC.COMPANYID
                 WHERE UPPER(UC.USERID) = UPPER(:Username)
                 ORDER BY C.COMPANYNAME";

            var list = await conn.QueryAsync<CompanyModel>(sql, new { Username = username });
            return list.ToList();
        }
        public DataTable GetLoginInfoByUserID(string userId)
        {
            using var conn = _databaseService.GetConnection();

            const string sql = @"

        SELECT U.ID, U.USERNAME, U.ROLEID
          FROM UC_USERS U
         WHERE UPPER(U.USERNAME) = UPPER(:UserId) AND U.ISACTIVE = 1";

            var dt = new DataTable();
            using var reader = conn.ExecuteReader(sql, new { UserId = userId });
            dt.Load(reader);
            return dt;
        }

        public DataTable GetEmployeeInfo(string userId)
        {
            using var conn = _databaseService.GetConnection();

            const string sql = @"

        SELECT E.EMPLOYEENAME, E.BUSINESSCENTERID, E.COMPANYID, E.DEPARTMENTID, E.DESIGNATIONID, E.STAFFID
          FROM EM_EMPLOYEE E
         WHERE E.STAFFID = :UserId";

            var dt = new DataTable();
            using var reader = conn.ExecuteReader(sql, new { UserId = userId });
            dt.Load(reader);
            return dt;
        }
    }
}
