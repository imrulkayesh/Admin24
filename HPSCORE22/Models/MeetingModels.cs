// Models/MeetingModels.cs
namespace AlphaCare.Models
{
    // MT_MEETING + room + business center + type  (old dalMTMeeting.GetMeetingDetails)
    public class MeetingDetailModel
    {
        public int ID { get; set; }
        public int MROOMID { get; set; }
        public int? BUSINESSCENTERID { get; set; }
        public string? MEETINGTITLE { get; set; }
        public string? AGENDA { get; set; }
        public DateTime? STARTTIME { get; set; }
        public DateTime? ENDTIME { get; set; }
        public int? MEETINGTYPEID { get; set; }
        public string? CHAIRPERSONSTAFFID { get; set; }        // old meetings only - not on the entry page any more
        public string? MEETINGCALLEDBY { get; set; }           // MT_MEETING.MEETINGCALLEDBY
        public int? ACTUALPERTICIPANT { get; set; }            // MT_MEETING.ACTUALPERTICIPANT  (INTEGER = number of participants)
        public string? CREATEDBY { get; set; }
        public string? CENTERNAME { get; set; }
        public string? ROOMTITLE { get; set; }
        public string? MTYPENAME { get; set; }
    }

    // BS_BUSINESSCENTER  -> Location drop down
    public class BusinessCenterModel
    {
        public int ID { get; set; }
        public string? CENTERCODE { get; set; }
        public string CENTERNAME { get; set; } = "";
    }

    // MT_MEETINGROOM (+ room admin) -> Room drop down
    public class MeetingRoomModel
    {
        public int ID { get; set; }
        public int? BUSINESSCENTERID { get; set; }
        public string? ROOMTITLE { get; set; }
        public int? SEATS { get; set; }
        public string? ADMINNAME { get; set; }
        public string? ADMINMOBILE { get; set; }
        public string? ADMINMAIL { get; set; }

        // same text as old SQL: ROOMTITLE || ' @Capacity:-' || SEATS
        public string DisplayName => $"{ROOMTITLE} @Capacity:-{SEATS}";
    }

    // MT_MEETINGTYPE
    public class MeetingTypeModel
    {
        public int ID { get; set; }
        public string MTYPENAME { get; set; } = "";
    }

    // EM_EMPLOYEE (chairperson lookup)
    public class EmployeeLookupModel
    {
        public string STAFFID { get; set; } = "";
        public string? EMPLOYEENAME { get; set; }
        public string? OFFICEMOBILE { get; set; }
        public string? OFFICEMAIL { get; set; }
    }

    // one row of the Meeting List page (old dalMTMeeting.GetMeetingList)
    public class MeetingListItemModel
    {
        public int ID { get; set; }
        public string? MEETINGTITLE { get; set; }
        public DateTime? STARTTIME { get; set; }
        public DateTime? ENDTIME { get; set; }
        public string? CREATEDBY { get; set; }
        public string? CHAIRPERSONSTAFFID { get; set; }
        public string? CENTERCODE { get; set; }
        public string? ROOMTITLE { get; set; }
        public string? ADMINNAME { get; set; }
        public string? ADMINMOBILE { get; set; }
        public string? ADMINMAIL { get; set; }

        // old grid text: CENTERCODE-ROOMTITLE
        public string RoomDisplay => $"{CENTERCODE}-{ROOMTITLE}";
    }

    // one row of the MEETING SCHEDULE report (old Default.aspx "Schedule" calendar)
    public class MeetingScheduleRowModel
    {
        public int ID { get; set; }
        public string? MEETINGTITLE { get; set; }
        public DateTime? STARTTIME { get; set; }
        public DateTime? ENDTIME { get; set; }
        public int? ISAPPROVED { get; set; }               // 0 waiting, 1 approved, 2 rejected, 3 cancelled
        public string? CENTERCODE { get; set; }
        public string? CENTERNAME { get; set; }
        public string? ROOMTITLE { get; set; }
        public string? MEETINGCALLEDBY { get; set; }
        public string? CREATORNAME { get; set; }
        public string? ADMINNAME { get; set; }
        public string? ADMINMOBILE { get; set; }

        public string RoomDisplay => $"{CENTERCODE}-{ROOMTITLE}";
    }

    // view model of MeetingSchedule.cshtml (filter values + drop downs + result)
    public class MeetingScheduleViewModel
    {
        public int CenterId { get; set; }                  // 0 = all locations
        public int RoomId { get; set; }                    // 0 = all rooms
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public int Status { get; set; } = 1;               // 0 waiting, 1 approved, 2 rejected, 3 cancelled, -1 all
        public List<BusinessCenterModel> Centers { get; set; } = new();
        public List<MeetingRoomModel> Rooms { get; set; } = new();
        public List<MeetingScheduleRowModel> Rows { get; set; } = new();
    }

    // one row of the ROOM WISE TOTAL USES TIME report (one room)
    public class RoomUsageRowModel
    {
        public int ROOMID { get; set; }
        public string? CENTERCODE { get; set; }
        public string? CENTERNAME { get; set; }
        public string? ROOMTITLE { get; set; }
        public int? SEATS { get; set; }
        public int TOTALMEETINGS { get; set; }
        public int DAYSUSED { get; set; }                  // number of different days the room was used
        public double TOTALMINUTES { get; set; }           // sum of (ENDTIME - STARTTIME)
        public double LONGESTMINUTES { get; set; }

        public string RoomDisplay => $"{CENTERCODE}-{ROOMTITLE}";
        public double TotalHours => Math.Round(TOTALMINUTES / 60.0, 2);
        public double AvgMinutes => TOTALMEETINGS == 0 ? 0 : TOTALMINUTES / TOTALMEETINGS;

        // 125 minutes -> "2h 05m"
        public static string Fmt(double minutes)
        {
            int m = (int)Math.Round(minutes);
            return $"{m / 60}h {m % 60:00}m";
        }
    }

    // view model of RoomUsage.cshtml
    public class RoomUsageViewModel
    {
        public int CenterId { get; set; }                  // 0 = all locations
        public int RoomId { get; set; }                    // 0 = all rooms
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public int Status { get; set; } = 1;               // 0 waiting, 1 approved, 2 rejected, 3 cancelled, -1 all
        public List<BusinessCenterModel> Centers { get; set; } = new();
        public List<MeetingRoomModel> Rooms { get; set; } = new();
        public List<RoomUsageRowModel> Rows { get; set; } = new();
    }

    // ROOM DOOR DISPLAY (tablet at the door of a meeting room)
    public class RoomDisplayInfoModel
    {
        public int ROOMID { get; set; }
        public string? ROOMTITLE { get; set; }
        public int? SEATS { get; set; }
        public string? CENTERCODE { get; set; }
        public string? CENTERNAME { get; set; }
    }

    public class RoomDisplayMeetingModel
    {
        public int ID { get; set; }
        public string? MEETINGTITLE { get; set; }
        public DateTime? STARTTIME { get; set; }
        public DateTime? ENDTIME { get; set; }
        public string? MEETINGCALLEDBY { get; set; }
        public string? CREATORNAME { get; set; }
    }

    // one line of the page that lists the display address of every room
    public class RoomDisplayLinkModel
    {
        public int RoomId { get; set; }
        public string? CenterName { get; set; }
        public string? RoomTitle { get; set; }
        public int? Seats { get; set; }
    }

    // view model of MeetingList.cshtml (3 lists: ISAPPROVED = 0 / 1 / 2)
    public class MeetingListViewModel
    {
        public string StaffId { get; set; } = "";
        public List<MeetingListItemModel> Waiting { get; set; } = new();
        public List<MeetingListItemModel> Approved { get; set; } = new();
        public List<MeetingListItemModel> Rejected { get; set; } = new();
        public List<MeetingListItemModel> Cancelled { get; set; } = new();
    }

    // one row of the Room Admin page (old dalMTMeeting.GetRoomAdminMeetingList)
    public class RoomAdminMeetingModel
    {
        public int ID { get; set; }
        public string? MEETINGTITLE { get; set; }
        public string? CHAIRPERSON { get; set; }        // not used by the Room Admin page any more
        public string? MEETINGCALLEDBY { get; set; }    // MT_MEETING.MEETINGCALLEDBY ("Meeting Call By" column)
        public DateTime? STARTTIME { get; set; }
        public DateTime? ENDTIME { get; set; }
        public string? CENTERCODE { get; set; }
        public string? ROOMTITLE { get; set; }
        public string? CREATEDBY { get; set; }
        public string? CREATORNAME { get; set; }        // who requested the room
        public string? CREATORMOBILE { get; set; }

        public string RoomDisplay => $"{CENTERCODE}-{ROOMTITLE}";
    }

    // one booking shown in the room availability calendar of CreateMeeting.cshtml
    public class RoomBookingModel
    {
        public int ID { get; set; }
        public string? MEETINGTITLE { get; set; }
        public DateTime? STARTTIME { get; set; }
        public DateTime? ENDTIME { get; set; }
        public int ISAPPROVED { get; set; }             // 0 waiting, 1 approved
        public string? CREATORNAME { get; set; }
    }

    // view model of RoomAdmin.cshtml
    public class RoomAdminViewModel
    {
        public List<RoomAdminMeetingModel> Waiting { get; set; } = new();
        public List<RoomAdminMeetingModel> Rejected { get; set; } = new();
    }

    // everything the e-mails need (EM_EMPLOYEE.OFFICEMAIL of creator / room admin, names, meeting data)
    public class MeetingMailInfoModel
    {
        public int ID { get; set; }
        public string? MEETINGTITLE { get; set; }
        public string? AGENDA { get; set; }
        public DateTime? STARTTIME { get; set; }
        public DateTime? ENDTIME { get; set; }
        public string? CENTERNAME { get; set; }
        public string? ROOMTITLE { get; set; }
        public string? MTYPENAME { get; set; }
        public string? CHAIRNAME { get; set; }
        public string? CREATORNAME { get; set; }
        public string? CREATORMAIL { get; set; }
        public string? CREATORMOBILE { get; set; }
        public string? ADMINNAME { get; set; }
        public string? ADMINMAIL { get; set; }
        public string? ADMINMOBILE { get; set; }
        public string? APPROVERNAME { get; set; }
        public string? MEETINGCODE { get; set; }        // secret 6 digit code - only the APPROVED mail to the creator shows it
        public string? APPROVERREMARKS { get; set; }    // remarks written by the room admin when he approves
    }

    // posted by the Save button
    public class SaveMeetingRequest
    {
        public int MeetingId { get; set; }
        public int? BusinessCenterId { get; set; }     // Location
        public int? RoomId { get; set; }               // Room
        public int? MeetingTypeId { get; set; }
        public string? MeetingTitle { get; set; }
        public string? Agenda { get; set; }
        public string? MeetingCalledBy { get; set; }   // input name on the page
        public int? ActualParticipant { get; set; }    // input name on the page (number)
        public DateTime? StartTime { get; set; }       // used only when a NEW meeting is created
        public DateTime? EndTime { get; set; }
    }

    // data of a NEW meeting (old: INSERT INTO MT_MEETING ...)
    public class NewMeetingModel
    {
        public int RoomId { get; set; }
        public string Title { get; set; } = "";
        public string Agenda { get; set; } = "";
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string? MeetingCalledBy { get; set; }       // optional
        public int? ActualParticipant { get; set; }        // optional (number of participants)
        public int MeetingTypeId { get; set; }
        public bool AllDay { get; set; }
    }

    // posted by the calendar page (old: INSERT INTO MT_MEETING ... cevent)
    public class CalendarEventRequest
    {
        public int RoomId { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public bool AllDay { get; set; }
    }

    // view model of CreateMeeting.cshtml
    public class CreateMeetingViewModel
    {
        public MeetingDetailModel Meeting { get; set; } = new();
        public List<BusinessCenterModel> BusinessCenters { get; set; } = new();
        public List<MeetingRoomModel> Rooms { get; set; } = new();
        public List<MeetingTypeModel> MeetingTypes { get; set; } = new();

        public int MeetingId { get; set; }
        public int? BusinessCenterId { get; set; }
        public int? RoomId { get; set; }
        public int? MeetingTypeId { get; set; }
        public string? MeetingTitle { get; set; }
        public string? Agenda { get; set; }
        public string? ChairStaffId { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public bool IsNew => MeetingId <= 0;
        public string? ChairName { get; set; }
        public string? ChairMobile { get; set; }
        public string? MeetingCalledBy { get; set; }
        public int? ActualParticipant { get; set; }
    }
}
