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

        /// <summary>
        /// Port of old dalUser.CheckInactiveUserStatus(): true when the account was switched off by the
        /// Dispatch module (ISACTIVE=0, INACTIVETYPE='Dispatch' - "Dispatch Gate check pending"), so the
        /// login page can show the specific message instead of "Invalid Username or Password."
        /// </summary>
        public bool IsInactiveForDispatchGate(string username)
        {
            using var conn = _databaseService.GetConnection();

            const string sql = @"
        SELECT COUNT(*)
          FROM UC_USERS U
         WHERE U.ISACTIVE = 0 AND U.INACTIVETYPE = 'Dispatch'
           AND UPPER(U.USERNAME) = UPPER(:Username)";

            return conn.ExecuteScalar<int>(sql, new { Username = username }) > 0;
        }

        // First day the Dispatch Gate Check rule counts from (the TO_DATE('05/10/2026', 'DD/MM/YYYY') of the gate check query)
        private static readonly DateTime GateCheckFromDate = new DateTime(2026, 10, 5);

        /// <summary>
        /// The pending Dispatch Gate Check records of a staff (D.CREATEDBY): approved dispatches (STATUSID 2) without gate pass,
        /// sent from an active location that is not a depot / sales office. Shown on the login page when the account is
        /// inactive for this reason. Same query as "Gate check Pending Query", with bind parameters. Oldest first.
        /// </summary>
        public List<PendingGateCheckModel> GetPendingGateCheckDispatches(string staffId)
        {
            using var conn = _databaseService.GetConnection();

            const string sql = @"
        SELECT SB.CENTERNAME AS SENDER_LOCATION,
               D.FROMUSER AS SENDER_USER,
               SE.EMPLOYEENAME AS SENDER_NAME,
               RB.CENTERNAME AS RECEIVER_LOCATION,
               D.TOUSER AS RECEIVER_USER,
               RE.EMPLOYEENAME AS RECEIVER_NAME,
               D.CREATEDBY AS CREAT_USER,
               E.EMPLOYEENAME AS CREATOR_NAME,
               D.CREATEDATE,
               D.TRANSACTIONID,
               I.ISSUETYPENAME
          FROM DP_DISPATCH D
               LEFT JOIN UC_USERS U ON D.CREATEDBY = U.USERNAME
               LEFT JOIN EM_EMPLOYEE E ON D.CREATEDBY = E.STAFFID
               LEFT JOIN EM_EMPLOYEE SE ON D.FROMUSER = SE.STAFFID
               LEFT JOIN EM_EMPLOYEE RE ON D.TOUSER = RE.STAFFID
               LEFT JOIN BS_BUSINESSCENTER B ON B.ID = E.BUSINESSCENTERID
               LEFT JOIN BS_DEPARTMENT SDEP ON SDEP.ID = D.SENDDEPARTMENTID
               LEFT JOIN BS_DEPARTMENT RDEP ON RDEP.ID = D.RECDEPARTMENTID
               LEFT JOIN DP_STATUS S ON D.STATUSID = S.ID
               LEFT JOIN DP_ISSUETYPE I ON D.ISSUETYPEID = I.ID
               LEFT JOIN BS_BUSINESSCENTER SB ON D.SENDBUSINESSCENTERID = SB.ID
               LEFT JOIN BS_BUSINESSCENTER RB ON D.RECBUSINESSCENTERID = RB.ID
         WHERE D.CREATEDATE < SYSDATE
           AND D.STATUSID IN (2)
           AND TRUNC(D.CREATEDATE) >= :FromDate
           AND D.GATEPASS = 0
           AND SB.ISDEPOTORSALESOFFICE = 0
           AND SB.LOCATIONSTATUS = 1
           AND D.CREATEDBY = :StaffId
         ORDER BY D.CREATEDATE";

            return conn.Query<PendingGateCheckModel>(sql, new { FromDate = GateCheckFromDate, StaffId = (staffId ?? "").Trim() }).ToList();
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
