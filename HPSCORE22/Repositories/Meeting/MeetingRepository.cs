// Repositories/MeetingRepository.cs
// Program.cs:  builder.Services.AddScoped<IMeetingRepository, MeetingRepository>();
using Admin360.Services;          // DatabaseService (change namespace if yours differs)
using AlphaCare.Interface;
using AlphaCare.Models;
using Dapper;
using QCMS.Services;
using System.Data;
using System.Security.Cryptography;

namespace Admin360.Repositories
{
    public class MeetingRepository : IMeetingRepository
    {
        private readonly DatabaseService _databaseService;

        public MeetingRepository(DatabaseService databaseService)
        {
            _databaseService = databaseService;
        }

        // = old dalMTMeeting.GetMeetingDetails
        public MeetingDetailModel? GetMeetingDetails(int meetingId)
        {
            using var conn = _databaseService.GetConnection();

            const string sql = @"
                SELECT M.ID, M.MROOMID, MR.BUSINESSCENTERID, M.MEETINGTITLE, M.AGENDA, M.STARTTIME, M.ENDTIME,
                       M.MEETINGTYPEID, M.CHAIRPERSONSTAFFID, M.CREATEDBY,
                       M.MEETINGCALLEDBY, M.ACTUALPERTICIPANT,
                       B.CENTERNAME, MR.ROOMTITLE, T.MTYPENAME
                  FROM MT_MEETING M
                 INNER JOIN MT_MEETINGROOM MR ON MR.ID = M.MROOMID
                 INNER JOIN BS_BUSINESSCENTER B ON B.ID = MR.BUSINESSCENTERID
                  LEFT OUTER JOIN MT_MEETINGTYPE T ON T.ID = M.MEETINGTYPEID
                 WHERE M.ID = :MeetingId";

            return conn.QueryFirstOrDefault<MeetingDetailModel>(sql, new { MeetingId = meetingId });
        }

        // LOCATION  (old: Select * FROM BS_BUSINESSCENTER)
        public List<BusinessCenterModel> GetBusinessCenters()
        {
            using var conn = _databaseService.GetConnection();

            const string sql = @"
                SELECT B.ID, B.CENTERCODE, B.CENTERNAME
                  FROM BS_BUSINESSCENTER B WHERE B.LOCATIONSTATUS =1
                 ORDER BY B.CENTERNAME";

            return conn.Query<BusinessCenterModel>(sql).ToList();
        }

        // ROOM  (old GetAll(BusinessCenterId): rooms of one location + room admin)
        // The label "ROOMTITLE @Capacity:-SEATS" is built in MeetingRoomModel.DisplayName
        public List<MeetingRoomModel> GetRoomsByCenter(int businessCenterId)
        {
            using var conn = _databaseService.GetConnection();

            const string sql = @"
                SELECT MR.ID, MR.BUSINESSCENTERID, MR.ROOMTITLE, MR.SEATS,
                       E.EMPLOYEENAME AS ADMINNAME, E.OFFICEMOBILE AS ADMINMOBILE, E.OFFICEMAIL AS ADMINMAIL
                  FROM MT_MEETINGROOM MR
                 INNER JOIN BS_BUSINESSCENTER B ON B.ID = MR.BUSINESSCENTERID
                 INNER JOIN EM_EMPLOYEE E ON E.STAFFID = MR.ADMINSTAFFID
                 WHERE MR.BUSINESSCENTERID = :BusinessCenterId
                 ORDER BY MR.ROOMTITLE";

            return conn.Query<MeetingRoomModel>(sql, new { BusinessCenterId = businessCenterId }).ToList();
        }

        // another meeting in the same room with overlapping time
        public bool IsRoomBooked(int roomId, DateTime start, DateTime end, int excludeMeetingId)
        {
            using var conn = _databaseService.GetConnection();

            const string sql = @"
                SELECT COUNT(1)
                  FROM MT_MEETING M
                 WHERE M.MROOMID = :RoomId
                   AND M.ID <> :MeetingId
                   AND NVL(M.ISAPPROVED, 0) NOT IN (2, 3)      -- rejected / cancelled meetings do not block the room
                   AND M.STARTTIME < :EndTime
                   AND M.ENDTIME > :StartTime";

            return conn.ExecuteScalar<int>(sql, new { RoomId = roomId, MeetingId = excludeMeetingId, StartTime = start, EndTime = end }) > 0;
        }

        // bookings of one room on one day (rejected / cancelled ones are not shown - they do not block the room)
        public List<RoomBookingModel> GetRoomBookings(int roomId, DateTime day, int excludeMeetingId)
        {
            using var conn = _databaseService.GetConnection();

            const string sql = @"
                SELECT M.ID, M.MEETINGTITLE, M.STARTTIME, M.ENDTIME,
                       NVL(M.ISAPPROVED, 0) AS ISAPPROVED,
                       E.EMPLOYEENAME AS CREATORNAME
                  FROM MT_MEETING M
                  LEFT OUTER JOIN EM_EMPLOYEE E ON E.STAFFID = M.CREATEDBY
                 WHERE M.MROOMID = :RoomId
                   AND M.ID <> :MeetingId
                   AND NVL(M.ISAPPROVED, 0) NOT IN (2, 3)
                   AND M.STARTTIME < :DayEnd
                   AND M.ENDTIME > :DayStart
                 ORDER BY M.STARTTIME";

            return conn.Query<RoomBookingModel>(sql, new
            {
                RoomId = roomId,
                MeetingId = excludeMeetingId,
                DayStart = day.Date,
                DayEnd = day.Date.AddDays(1)
            }).ToList();
        }

        // MEETING SCHEDULE report  (old Default.aspx "Schedule" calendar, read only)
        // Only the status filter is put into the SQL text (a fixed value chosen here), everything else is a bind parameter.
        public List<MeetingScheduleRowModel> GetMeetingSchedule(int centerId, int roomId, DateTime from, DateTime to, int status)
        {
            using var conn = _databaseService.GetConnection();

            string statusSql = status >= 0 && status <= 3 ? "= " + status : "IN (0, 1, 2, 3)";      // 0 waiting, 1 approved, 2 rejected, 3 cancelled, -1 all

            string sql = $@"
                SELECT M.ID, M.MEETINGTITLE, M.STARTTIME, M.ENDTIME, NVL(M.ISAPPROVED, 0) AS ISAPPROVED,
                       M.MEETINGCALLEDBY, B.CENTERCODE, B.CENTERNAME, MR.ROOMTITLE,
                       CR.EMPLOYEENAME AS CREATORNAME,
                       AD.EMPLOYEENAME AS ADMINNAME, AD.OFFICEMOBILE AS ADMINMOBILE
                  FROM MT_MEETING M
                 INNER JOIN MT_MEETINGROOM MR ON MR.ID = M.MROOMID
                 INNER JOIN BS_BUSINESSCENTER B ON B.ID = MR.BUSINESSCENTERID
                  LEFT OUTER JOIN EM_EMPLOYEE CR ON CR.STAFFID = M.CREATEDBY
                  LEFT OUTER JOIN EM_EMPLOYEE AD ON AD.STAFFID = MR.ADMINSTAFFID
                 WHERE NVL(M.ISAPPROVED, 0) {statusSql}
                   AND M.STARTTIME >= :FromDate
                   AND M.STARTTIME <  :ToDate
                   AND (:CenterId1 = 0 OR MR.BUSINESSCENTERID = :CenterId2)
                   AND (:RoomId1   = 0 OR MR.ID = :RoomId2)
                 ORDER BY M.STARTTIME, B.CENTERNAME, MR.ROOMTITLE";

            return conn.Query<MeetingScheduleRowModel>(sql, new
            {
                FromDate = from.Date,
                ToDate = to.Date.AddDays(1),               // "to" date is included
                CenterId1 = centerId,
                CenterId2 = centerId,
                RoomId1 = roomId,
                RoomId2 = roomId
            }).ToList();
        }

        // ROOM WISE TOTAL USES TIME  (new report)
        // Rooms with no meeting are listed too (0 time). The status is a fixed value chosen here, everything else is a bind parameter.
        public List<RoomUsageRowModel> GetRoomUsage(int centerId, int roomId, DateTime from, DateTime to, int status)
        {
            using var conn = _databaseService.GetConnection();

            string statusSql = status >= 0 && status <= 3 ? "= " + status : "IN (0, 1, 2, 3)";

            string sql = $@"
                SELECT MR.ID AS ROOMID, B.CENTERCODE, B.CENTERNAME, MR.ROOMTITLE, MR.SEATS,
                       COUNT(M.ID) AS TOTALMEETINGS,
                       COUNT(DISTINCT TRUNC(M.STARTTIME)) AS DAYSUSED,
                       NVL(ROUND(SUM((M.ENDTIME - M.STARTTIME) * 24 * 60)), 0) AS TOTALMINUTES,
                       NVL(ROUND(MAX((M.ENDTIME - M.STARTTIME) * 24 * 60)), 0) AS LONGESTMINUTES
                  FROM MT_MEETINGROOM MR
                 INNER JOIN BS_BUSINESSCENTER B ON B.ID = MR.BUSINESSCENTERID
                  LEFT OUTER JOIN MT_MEETING M
                    ON M.MROOMID = MR.ID
                   AND NVL(M.ISAPPROVED, 0) {statusSql}
                   AND M.STARTTIME >= :FromDate
                   AND M.STARTTIME <  :ToDate
                 WHERE (:CenterId1 = 0 OR MR.BUSINESSCENTERID = :CenterId2)
                   AND (:RoomId1   = 0 OR MR.ID = :RoomId2)
                 GROUP BY MR.ID, B.CENTERCODE, B.CENTERNAME, MR.ROOMTITLE, MR.SEATS
                 ORDER BY TOTALMINUTES DESC, B.CENTERNAME, MR.ROOMTITLE";

            return conn.Query<RoomUsageRowModel>(sql, new
            {
                FromDate = from.Date,
                ToDate = to.Date.AddDays(1),               // "to" date is included
                CenterId1 = centerId,
                CenterId2 = centerId,
                RoomId1 = roomId,
                RoomId2 = roomId
            }).ToList();
        }

        // ROOM DOOR DISPLAY: the room + location
        public RoomDisplayInfoModel? GetRoomDisplayInfo(int roomId)
        {
            using var conn = _databaseService.GetConnection();

            const string sql = @"
                SELECT MR.ID AS ROOMID, MR.ROOMTITLE, MR.SEATS, B.CENTERCODE, B.CENTERNAME
                  FROM MT_MEETINGROOM MR
                 INNER JOIN BS_BUSINESSCENTER B ON B.ID = MR.BUSINESSCENTERID
                 WHERE MR.ID = :RoomId";

            return conn.QueryFirstOrDefault<RoomDisplayInfoModel>(sql, new { RoomId = roomId });
        }

        // ROOM DOOR DISPLAY: approved meetings of the day (also a meeting that started yesterday and is still running)
        public List<RoomDisplayMeetingModel> GetRoomMeetingsOfDay(int roomId, DateTime day)
        {
            using var conn = _databaseService.GetConnection();

            const string sql = @"
                SELECT M.ID, M.MEETINGTITLE, M.STARTTIME, M.ENDTIME, M.MEETINGCALLEDBY,
                       CR.EMPLOYEENAME AS CREATORNAME
                  FROM MT_MEETING M
                  LEFT OUTER JOIN EM_EMPLOYEE CR ON CR.STAFFID = M.CREATEDBY
                 WHERE M.MROOMID = :RoomId
                   AND NVL(M.ISAPPROVED, 0) = 1
                   AND M.STARTTIME < :DayEnd
                   AND M.ENDTIME   > :DayStart
                 ORDER BY M.STARTTIME, M.ENDTIME";

            return conn.Query<RoomDisplayMeetingModel>(sql, new
            {
                RoomId = roomId,
                DayStart = day.Date,
                DayEnd = day.Date.AddDays(1)
            }).ToList();
        }

        public List<MeetingTypeModel> GetMeetingTypes()
        {
            using var conn = _databaseService.GetConnection();
            const string sql = "SELECT T.ID, T.MTYPENAME FROM MT_MEETINGTYPE T ORDER BY T.MTYPENAME";
            return conn.Query<MeetingTypeModel>(sql).ToList();
        }

        // = old dalMTMeeting.Update  (+ MROOMID because Location/Room can now be changed)
        // + MEETINGCALLEDBY and ACTUALPERTICIPANT.  CHAIRPERSONSTAFFID is NOT touched any more (old meetings keep their value).
        // TODO: keep any audit columns (UPDATEDBY / UPDATEDATE) your old Update set.
        public int UpdateMeeting(int meetingId, int roomId, string title, string agenda, string? meetingCalledBy, int? actualParticipant, int meetingTypeId, string updatedBy)
        {
            using var conn = _databaseService.GetConnection();

            const string sql = @"
                UPDATE MT_MEETING
                   SET MROOMID            = :RoomId,
                       MEETINGTITLE       = :Title,
                       AGENDA             = :Agenda,
                       MEETINGCALLEDBY    = :MeetingCalledBy,
                       ACTUALPERTICIPANT  = :ActualParticipant,
                       MEETINGTYPEID      = :MeetingTypeId,
                       ISOPEN             = 0,
                       UPDATEDBY          = :UpdatedBy,
                       UPDATEDATE         = SYSDATE
                 WHERE ID = :MeetingId";

            return conn.Execute(sql, new
            {
                RoomId = roomId,
                Title = title,
                Agenda = agenda,
                MeetingCalledBy = meetingCalledBy,
                ActualParticipant = actualParticipant ?? 0,        // INTEGER column, default 0
                MeetingTypeId = meetingTypeId,
                UpdatedBy = updatedBy,
                MeetingId = meetingId
            });
        }

        // NEW meeting = old INSERT INTO MT_MEETING(ID,MROOMID,CREATEDBY,MEETINGTITLE,AGENDA,STARTTIME,ENDTIME,ALLDAY)
        //   + MEETINGTYPEID, MEETINGCALLEDBY, ACTUALPERTICIPANT, MEETINGCODE.  Bind parameters + real DateTime values instead of TO_DATE strings.
        public int InsertMeeting(NewMeetingModel m, string createdBy)
        {
            using var conn = _databaseService.GetConnection();
            conn.Open();

            int newId = conn.ExecuteScalar<int>("SELECT SQ_MT_MEETING.NEXTVAL FROM DUAL");
            string code = GenerateMeetingCode(conn);                 // 6 digit MEETINGCODE

            const string sql = @"
                INSERT INTO MT_MEETING
                       (ID, MROOMID, CREATEDBY, MEETINGTITLE, AGENDA, STARTTIME, ENDTIME, ALLDAY,
                        MEETINGTYPEID, MEETINGCALLEDBY, ACTUALPERTICIPANT, MEETINGCODE)
                VALUES (:Id, :RoomId, :CreatedBy, :Title, :Agenda, :StartTime, :EndTime, :AllDay,
                        :MeetingTypeId, :MeetingCalledBy, :ActualParticipant, :MeetingCode)";

            conn.Execute(sql, new
            {
                Id = newId,
                RoomId = m.RoomId,
                CreatedBy = createdBy,
                Title = m.Title,
                Agenda = m.Agenda,
                StartTime = m.StartTime,
                EndTime = m.EndTime,
                AllDay = m.AllDay ? 1 : 0,
                MeetingTypeId = m.MeetingTypeId,
                MeetingCalledBy = m.MeetingCalledBy,
                ActualParticipant = m.ActualParticipant ?? 0,      // INTEGER column, default 0
                MeetingCode = code
            });

            return newId;
        }

        // = old dalMTMeeting.GetMeetingList(status)  (bind parameters instead of string concatenation)
        // Meetings you CREATED are included too, because Chairperson is now optional
        // and a creator would otherwise never see his own meeting.
        public List<MeetingListItemModel> GetMeetingList(int approvalStatus, string staffId)
        {
            using var conn = _databaseService.GetConnection();

            const string sql = @"
                SELECT M.ID, M.MEETINGTITLE, M.STARTTIME, M.ENDTIME, M.CREATEDBY, M.CHAIRPERSONSTAFFID,
                       B.CENTERCODE, MR.ROOMTITLE,
                       E.EMPLOYEENAME AS ADMINNAME, E.OFFICEMOBILE AS ADMINMOBILE, E.OFFICEMAIL AS ADMINMAIL
                  FROM MT_MEETING M
                 INNER JOIN MT_MEETINGROOM MR ON MR.ID = M.MROOMID
                 INNER JOIN EM_EMPLOYEE E ON E.STAFFID = MR.ADMINSTAFFID
                 INNER JOIN BS_BUSINESSCENTER B ON B.ID = MR.BUSINESSCENTERID
                 WHERE NVL(M.ISAPPROVED, 0) = :Status
                   AND NVL(M.STATUS, 0) = 0
                   AND ( EXISTS (SELECT 1 FROM MT_PERTICIPANT MP
                                  WHERE MP.MEETINGID = M.ID AND MP.STAFFID = :StaffId1)
                         OR M.CHAIRPERSONSTAFFID = :StaffId2
                         OR M.CREATEDBY = :StaffId3 )
                 ORDER BY M.STARTTIME DESC";

            return conn.Query<MeetingListItemModel>(sql, new
            {
                Status = approvalStatus,
                StaffId1 = staffId,
                StaffId2 = staffId,
                StaffId3 = staffId
            }).ToList();
        }

        // = old dalMTMeeting.GetRoomAdminMeetingList(status)
        // Columns of the page: room, date/time, title, requested by (creator), meeting called by (MT_MEETING.MEETINGCALLEDBY)
        public List<RoomAdminMeetingModel> GetRoomAdminMeetingList(int approvalStatus, string adminStaffId)
        {
            using var conn = _databaseService.GetConnection();

            const string sql = @"
                SELECT M.ID, M.MEETINGTITLE, M.STARTTIME, M.ENDTIME, M.CREATEDBY, M.MEETINGCALLEDBY,
                       CR.EMPLOYEENAME AS CREATORNAME, CR.OFFICEMOBILE AS CREATORMOBILE,
                       B.CENTERCODE, MR.ROOMTITLE
                  FROM MT_MEETING M
                 INNER JOIN MT_MEETINGROOM MR ON MR.ID = M.MROOMID
                 INNER JOIN BS_BUSINESSCENTER B ON B.ID = MR.BUSINESSCENTERID
                  LEFT OUTER JOIN EM_EMPLOYEE CR ON CR.STAFFID = M.CREATEDBY
                 WHERE NVL(M.ISAPPROVED, 0) = :Status
                   AND MR.ADMINSTAFFID = :AdminStaffId
                 ORDER BY M.STARTTIME ASC";

            var rows = conn.Query<RoomAdminMeetingModel>(sql, new { Status = approvalStatus, AdminStaffId = adminStaffId }).ToList();

            // TODAY first (earliest first), then the coming days (nearest first), then the past (latest first)
            var today = DateTime.Today;
            int Group(RoomAdminMeetingModel r) =>
                r.STARTTIME == null ? 2 : r.STARTTIME.Value.Date == today ? 0 : r.STARTTIME.Value.Date > today ? 1 : 2;

            return rows.OrderBy(Group)
                       .ThenBy(r => Group(r) == 2 ? 0 : (r.STARTTIME?.Ticks ?? 0))
                       .ThenByDescending(r => Group(r) == 2 ? (r.STARTTIME?.Ticks ?? 0) : 0)
                       .ToList();
        }

        // = old dalMTMeeting.ApproveRejectMeeting (ISAPPROVED, APPROVERSTAFFID, UPDATEDATE)
        // Only the ADMIN OF THE ROOM can change it, and only from a valid state:
        //   approve (1): from waiting (0) or rejected (2)      reject (2): from waiting (0)
        // On APPROVE and REJECT the remarks of the room admin are saved in MT_MEETING.APPROVERREMARKS (VARCHAR2(100)).
        public int SetMeetingApproval(int meetingId, int newStatus, string adminStaffId, string? remarks = null)
        {
            using var conn = _databaseService.GetConnection();

            string fromStates = newStatus == 1 ? "(0, 2)" : "(0)";
            string remarksSql = ", M.APPROVERREMARKS = :Remarks";      // approve AND reject

            string sql = $@"
                UPDATE MT_MEETING M
                   SET M.ISAPPROVED = :NewStatus,
                       M.APPROVERSTAFFID = :Approver,
                       M.UPDATEDATE = SYSDATE{remarksSql}
                 WHERE M.ID = :MeetingId
                   AND NVL(M.ISAPPROVED, 0) IN {fromStates}
                   AND EXISTS (SELECT 1 FROM MT_MEETINGROOM MR
                                WHERE MR.ID = M.MROOMID AND MR.ADMINSTAFFID = :AdminStaffId)";

            return conn.Execute(sql, new
            {
                NewStatus = newStatus,
                Approver = adminStaffId,
                MeetingId = meetingId,
                AdminStaffId = adminStaffId,
                Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim()
            });
        }

        // CANCEL REQUEST: only the creator, only while waiting / approved, only before the meeting ends
        // ISAPPROVED values: 0 waiting, 1 approved, 2 rejected, 3 cancelled
        // The reason of the creator is saved in MT_MEETING.CANCELREASON (VARCHAR2(200)).
        public int CancelMeeting(int meetingId, string creatorStaffId, string? reason = null)
        {
            using var conn = _databaseService.GetConnection();

            const string sql = @"
                UPDATE MT_MEETING M
                   SET M.ISAPPROVED = 3,
                       M.CANCELREASON = :Reason,
                       M.UPDATEDBY = :UpdatedBy,
                       M.UPDATEDATE = SYSDATE
                 WHERE M.ID = :MeetingId
                   AND M.CREATEDBY = :Creator
                   AND NVL(M.ISAPPROVED, 0) IN (0, 1)
                   AND NVL(M.STATUS, 0) = 0
                   AND M.ENDTIME >= SYSDATE";

            return conn.Execute(sql, new
            {
                Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
                UpdatedBy = creatorStaffId,
                MeetingId = meetingId,
                Creator = creatorStaffId
            });
        }

        // mail addresses come from EM_EMPLOYEE.OFFICEMAIL
        public MeetingMailInfoModel? GetMeetingMailInfo(int meetingId)
        {
            using var conn = _databaseService.GetConnection();

            const string sql = @"
                SELECT M.ID, M.MEETINGTITLE, M.AGENDA, M.STARTTIME, M.ENDTIME,
                       M.MEETINGCODE, M.APPROVERREMARKS,
                       B.CENTERNAME, MR.ROOMTITLE, T.MTYPENAME,
                       CH.EMPLOYEENAME AS CHAIRNAME,
                       CR.EMPLOYEENAME AS CREATORNAME, CR.OFFICEMAIL AS CREATORMAIL, CR.OFFICEMOBILE AS CREATORMOBILE,
                       AD.EMPLOYEENAME AS ADMINNAME,   AD.OFFICEMAIL AS ADMINMAIL,   AD.OFFICEMOBILE AS ADMINMOBILE,
                       AP.EMPLOYEENAME AS APPROVERNAME
                  FROM MT_MEETING M
                 INNER JOIN MT_MEETINGROOM MR ON MR.ID = M.MROOMID
                 INNER JOIN BS_BUSINESSCENTER B ON B.ID = MR.BUSINESSCENTERID
                  LEFT OUTER JOIN MT_MEETINGTYPE T ON T.ID = M.MEETINGTYPEID
                  LEFT OUTER JOIN EM_EMPLOYEE CH ON CH.STAFFID = M.CHAIRPERSONSTAFFID
                  LEFT OUTER JOIN EM_EMPLOYEE CR ON CR.STAFFID = M.CREATEDBY
                  LEFT OUTER JOIN EM_EMPLOYEE AD ON AD.STAFFID = MR.ADMINSTAFFID
                  LEFT OUTER JOIN EM_EMPLOYEE AP ON AP.STAFFID = M.APPROVERSTAFFID
                 WHERE M.ID = :MeetingId";

            return conn.QueryFirstOrDefault<MeetingMailInfoModel>(sql, new { MeetingId = meetingId });
        }

        public bool CanViewMeeting(int meetingId, string staffId)
        {
            using var conn = _databaseService.GetConnection();

            const string sql = @"
                SELECT COUNT(1)
                  FROM MT_MEETING M
                 WHERE M.ID = :MeetingId
                   AND ( M.CREATEDBY = :StaffId1
                         OR M.CHAIRPERSONSTAFFID = :StaffId2
                         OR EXISTS (SELECT 1 FROM MT_PERTICIPANT MP
                                     WHERE MP.MEETINGID = M.ID AND MP.STAFFID = :StaffId3)
                         OR EXISTS (SELECT 1 FROM MT_MEETINGROOM MR
                                     WHERE MR.ID = M.MROOMID AND MR.ADMINSTAFFID = :StaffId4) )";

            return conn.ExecuteScalar<int>(sql, new { MeetingId = meetingId, StaffId1 = staffId, StaffId2 = staffId, StaffId3 = staffId, StaffId4 = staffId }) > 0;
        }

        public EmployeeLookupModel? GetEmployeeByStaffId(string staffId)
        {
            using var conn = _databaseService.GetConnection();

            const string sql = @"
                SELECT E.STAFFID, E.EMPLOYEENAME, E.OFFICEMOBILE, E.OFFICEMAIL
                  FROM EM_EMPLOYEE E
                 WHERE E.STAFFID = :StaffId";

            return conn.QueryFirstOrDefault<EmployeeLookupModel>(sql, new { StaffId = staffId });
        }

        public List<EmployeeLookupModel> SearchEmployees(string term, int max = 10)
        {
            using var conn = _databaseService.GetConnection();

            const string sql = @"
                SELECT E.STAFFID, E.EMPLOYEENAME, E.OFFICEMOBILE, E.OFFICEMAIL
                  FROM EM_EMPLOYEE E
                 WHERE (E.STAFFID LIKE :Term OR UPPER(E.EMPLOYEENAME) LIKE :TermUpper)
                   AND ROWNUM <= :Max
                 ORDER BY E.STAFFID";

            string like = term.Trim() + "%";
            return conn.Query<EmployeeLookupModel>(sql, new { Term = like, TermUpper = like.ToUpper(), Max = max }).ToList();
        }

        public int CreateMeetingFromCalendar(CalendarEventRequest r, string createdBy)
        {
            using var conn = _databaseService.GetConnection();
            conn.Open();

            int newId = conn.ExecuteScalar<int>("SELECT SQ_MT_MEETING.NEXTVAL FROM DUAL");
            string code = GenerateMeetingCode(conn);                 // 6 digit MEETINGCODE

            const string sql = @"
                INSERT INTO MT_MEETING (ID, MROOMID, CREATEDBY, MEETINGTITLE, AGENDA, STARTTIME, ENDTIME, ALLDAY, MEETINGCODE)
                VALUES (:Id, :RoomId, :CreatedBy, :Title, :Agenda, :StartTime, :EndTime, :AllDay, :MeetingCode)";

            conn.Execute(sql, new
            {
                Id = newId,
                RoomId = r.RoomId,
                CreatedBy = createdBy,
                Title = r.Title,
                Agenda = r.Description,
                StartTime = r.Start,
                EndTime = r.End,
                AllDay = r.AllDay ? 1 : 0,
                MeetingCode = code
            });

            return newId;
        }

        // ---------------------------------------------------------
        // MEETING CODE: 6 digits (100000 - 999999), random and not used by another meeting.
        // Change the 100000 / 1000000 numbers here if you want another look (e.g. 000001 style).
        // The connection must be open (the same one that inserts the meeting).
        private static string GenerateMeetingCode(IDbConnection conn)
        {
            for (int i = 0; i < 20; i++)
            {
                string code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
                int used = conn.ExecuteScalar<int>("SELECT COUNT(1) FROM MT_MEETING WHERE MEETINGCODE = :Code", new { Code = code });
                if (used == 0) return code;
            }
            throw new InvalidOperationException("Could not make a free meeting code. Please try again.");
        }
    }
}
