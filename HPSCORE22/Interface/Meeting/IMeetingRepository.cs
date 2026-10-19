// Interface/IMeetingRepository.cs
using AlphaCare.Models;

namespace AlphaCare.Interface
{
    public interface IMeetingRepository
    {
        /// <summary>Old dalMTMeeting.GetMeetingDetails(meetingId)</summary>
        MeetingDetailModel? GetMeetingDetails(int meetingId);

        /// <summary>Location drop down: old "Select * FROM BS_BUSINESSCENTER"</summary>
        List<BusinessCenterModel> GetBusinessCenters();

        /// <summary>Room drop down: old GetAll(BusinessCenterId) on MT_MEETINGROOM</summary>
        List<MeetingRoomModel> GetRoomsByCenter(int businessCenterId);

        /// <summary>True when another meeting already uses the room in this time range</summary>
        /// <summary>Bookings (waiting + approved) of one room on one day - for the availability calendar</summary>
        List<RoomBookingModel> GetRoomBookings(int roomId, DateTime day, int excludeMeetingId);

        bool IsRoomBooked(int roomId, DateTime start, DateTime end, int excludeMeetingId);

        /// <summary>Meeting SCHEDULE report (old Default.aspx Schedule): meetings of a location / room between two dates. centerId / roomId 0 = all. status 0 waiting, 1 approved, 2 rejected, 3 cancelled, -1 all</summary>
        List<MeetingScheduleRowModel> GetMeetingSchedule(int centerId, int roomId, DateTime from, DateTime to, int status);

        /// <summary>ROOM WISE TOTAL USES TIME report: per room number of meetings, days used and total time between two dates. centerId / roomId 0 = all</summary>
        List<RoomUsageRowModel> GetRoomUsage(int centerId, int roomId, DateTime from, DateTime to, int status);

        /// <summary>Door display: room + location (null = room not found)</summary>
        RoomDisplayInfoModel? GetRoomDisplayInfo(int roomId);

        /// <summary>Door display: APPROVED meetings of one room that touch the given day (00:00 - 24:00), oldest first</summary>
        List<RoomDisplayMeetingModel> GetRoomMeetingsOfDay(int roomId, DateTime day);

        /// <summary>Meeting type drop down (MT_MEETINGTYPE)</summary>
        List<MeetingTypeModel> GetMeetingTypes();

        /// <summary>Old dalMTMeeting.Update(...) + room + Meeting Called By + Participant (number)</summary>
        int UpdateMeeting(int meetingId, int roomId, string title, string agenda, string? meetingCalledBy, int? actualParticipant, int meetingTypeId, string updatedBy);

        /// <summary>Insert a NEW meeting (old: INSERT INTO MT_MEETING ...). Also makes the 6 digit MEETINGCODE. Returns the new meeting ID.</summary>
        int InsertMeeting(NewMeetingModel meeting, string createdBy);

        /// <summary>Old dalMTMeeting.GetMeetingList(status): 0 = waiting, 1 = approved, 2 = rejected</summary>
        List<MeetingListItemModel> GetMeetingList(int approvalStatus, string staffId);

        /// <summary>Old dalMTMeeting.GetRoomAdminMeetingList(status): meetings of the rooms this admin manages (0 = waiting, 2 = rejected)</summary>
        List<RoomAdminMeetingModel> GetRoomAdminMeetingList(int approvalStatus, string adminStaffId);

        /// <summary>Old dalMTMeeting.ApproveRejectMeeting: newStatus 1 = approve, 2 = reject. remarks (approve and reject) -> MT_MEETING.APPROVERREMARKS. Returns rows changed (0 = not allowed / already done)</summary>
        int SetMeetingApproval(int meetingId, int newStatus, string adminStaffId, string? remarks = null);

        /// <summary>Creator cancels his own meeting request (ISAPPROVED = 3). reason -> MT_MEETING.CANCELREASON. Returns rows changed (0 = not allowed)</summary>
        int CancelMeeting(int meetingId, string creatorStaffId, string? reason = null);

        /// <summary>Data for the e-mails: meeting + creator / room admin / approver names and OFFICEMAIL</summary>
        MeetingMailInfoModel? GetMeetingMailInfo(int meetingId);

        /// <summary>User may open a meeting if he created it, chairs it, is a participant or is the room admin</summary>
        bool CanViewMeeting(int meetingId, string staffId);

        EmployeeLookupModel? GetEmployeeByStaffId(string staffId);
        List<EmployeeLookupModel> SearchEmployees(string term, int max = 10);

        int CreateMeetingFromCalendar(CalendarEventRequest request, string createdBy);
    }
}
