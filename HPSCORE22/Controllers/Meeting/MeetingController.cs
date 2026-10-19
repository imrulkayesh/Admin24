// Controllers/MeetingController.cs
// NuGet:  dotnet add package HtmlSanitizer      (agenda text is cleaned before saving)
using AlphaCare.Interface;
using AlphaCare.Models;
using AlphaCare.Services;
using Ganss.Xss;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace QCMS.Controllers
{
    [Authorize]
    public class MeetingController : Controller
    {
        private readonly IMeetingRepository _meetingRepository;
        private readonly IEmailService _email;
        private readonly IConfiguration _config;
        private readonly ILogger<MeetingController> _logger;
        private static readonly HtmlSanitizer _sanitizer = new();

        public MeetingController(IMeetingRepository meetingRepository, IEmailService email,
                                 IConfiguration config, ILogger<MeetingController> logger)
        {
            _meetingRepository = meetingRepository;
            _email = email;
            _config = config;
            _logger = logger;
        }

        private string StaffId => HttpContext.Session.GetString("STAFFID") ?? "";

        // only the person who created the meeting may edit it
        private bool CanEdit(MeetingDetailModel m) =>
            string.Equals((m.CREATEDBY ?? "").Trim(), StaffId.Trim(), StringComparison.OrdinalIgnoreCase);

        // =========================
        // MEETING LIST  /Meeting/MeetingList     (old: MeetingList.aspx -> BindData)
        // =========================
        [HttpGet]
        public IActionResult MeetingList()
        {
            var vm = new MeetingListViewModel
            {
                StaffId = StaffId,
                Waiting = _meetingRepository.GetMeetingList(0, StaffId),
                Approved = _meetingRepository.GetMeetingList(1, StaffId),
                Rejected = _meetingRepository.GetMeetingList(2, StaffId),
                Cancelled = _meetingRepository.GetMeetingList(3, StaffId)
            };
            return View("MeetingList", vm);
        }

        // =========================
        // ROOM ADMIN  /Meeting/RoomAdmin     (old: RoomAdmin.aspx) - approve / reject meetings of my rooms
        // =========================
        [HttpGet]
        public IActionResult RoomAdmin()
        {
            var vm = new RoomAdminViewModel
            {
                Waiting = _meetingRepository.GetRoomAdminMeetingList(0, StaffId),
                Rejected = _meetingRepository.GetRoomAdminMeetingList(2, StaffId)
            };
            return View("RoomAdmin", vm);
        }

        // Approve (status = 1) or Reject (status = 2) - old btnApprove_Command / btnReject_Command
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetApproval(int id, int status, string? remarks)
        {
            if (id <= 0 || (status != 1 && status != 2))
                return Json(new { success = false, message = "Invalid request." });

            // remarks of the room admin (saved on APPROVE and REJECT) - MT_MEETING.APPROVERREMARKS = 100 characters
            remarks = (remarks ?? "").Trim();
            if (remarks.Length > 100)
                return Json(new { success = false, message = "Remarks is too long (maximum 100 characters)." });

            try
            {
                int changed = _meetingRepository.SetMeetingApproval(id, status, StaffId, remarks);
                if (changed == 0)
                    return Json(new { success = false, message = "You cannot change this meeting. It may already be processed, or it is not in your room." });

                // mail to the CREATOR (EM_EMPLOYEE.OFFICEMAIL). The APPROVED mail also carries the meeting code and the remarks
                bool mailed = await NotifyAsync(id, status == 1 ? "approved" : "rejected");
                string msg = status == 1 ? "Meeting approved." : "Meeting rejected.";
                return Json(new { success = true, message = msg + (mailed ? " Creator notified by mail." : " (Mail to the creator could not be sent.)") });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Approval failed. MeetingId={MeetingId}, Status={Status}", id, status);
                return Json(new { success = false, message = "Action failed. Please try again." });
            }
        }

        // =========================
        // CANCEL REQUEST  (creator) -> ISAPPROVED = 3, mail goes to the ROOM ADMIN
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelMeeting(int id, string? reason)
        {
            if (id <= 0) return Json(new { success = false, message = "Invalid request." });

            reason = (reason ?? "").Trim();
            if (reason.Length > 200)
                return Json(new { success = false, message = "Reason is too long (maximum 200 characters)." });

            try
            {
                int changed = _meetingRepository.CancelMeeting(id, StaffId, reason);        // reason -> MT_MEETING.CANCELREASON
                if (changed == 0)
                    return Json(new { success = false, message = "You cannot cancel this meeting. Only the person who created it can cancel it, and only before it ends." });

                bool mailed = await NotifyAsync(id, "cancelled", reason);
                return Json(new
                {
                    success = true,
                    message = mailed ? "Meeting cancelled. Room admin notified by mail."
                                     : "Meeting cancelled. (Mail to the room admin could not be sent.)"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cancel failed. MeetingId={MeetingId}", id);
                return Json(new { success = false, message = "Action failed. Please try again." });
            }
        }

        // =========================
        // VIEW DETAILS  /Meeting/ViewMeeting/{id}     (old: btnView_Command -> View.aspx)
        // =========================
        [HttpGet]
        public IActionResult ViewMeeting(int id)
        {
            if (id <= 0) return NotFound();
            if (!_meetingRepository.CanViewMeeting(id, StaffId)) return RedirectToAction("Denied", "Accounts");

            var vm = BuildViewModel(id, null);
            if (vm == null) return NotFound();

            HttpContext.Session.SetInt32("MeetingId", id);                  // old SessionName.MeetingId
            ViewBag.AgendaHtml = _sanitizer.Sanitize(vm.Agenda ?? "");      // old data may contain unsafe html
            ViewBag.CanEdit = CanEdit(vm.Meeting);
            return View("ViewMeeting", vm);
        }

        // =========================
        // OPEN
        //   /Meeting/CreateMeeting        -> blank form = NEW meeting
        //   /Meeting/CreateMeeting/{id}   -> edit an existing meeting (old: BindData + SessionName.MeetingId)
        // =========================
        [HttpGet]
        public IActionResult CreateMeeting(int id = 0)
        {
            if (id <= 0)
                return View("CreateMeeting", BuildViewModel(0, null)!);      // NEW

            var vm = BuildViewModel(id, null);
            if (vm == null) return NotFound();
            if (!CanEdit(vm.Meeting)) return RedirectToAction("Denied", "Accounts");

            HttpContext.Session.SetInt32("MeetingId", id);
            return View("CreateMeeting", vm);
        }

        // =========================
        // SAVE  (MeetingId = 0 -> INSERT new meeting, otherwise UPDATE)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateMeeting(SaveMeetingRequest model)
        {
            bool isNew = model.MeetingId <= 0;

            var vm = BuildViewModel(model.MeetingId, model);
            if (vm == null) return NotFound();
            if (!isNew && !CanEdit(vm.Meeting)) return RedirectToAction("Denied", "Accounts");

            string title = (model.MeetingTitle ?? "").Trim();
            string calledBy = (model.MeetingCalledBy ?? "").Trim();
            int? participant = model.ActualParticipant;          // number of participants (INTEGER column)
            string agenda = _sanitizer.Sanitize(model.Agenda ?? "");

            // time of the meeting: typed by the user (new) or already saved (edit)
            DateTime? start = isNew ? model.StartTime : vm.Meeting.STARTTIME;
            DateTime? end = isNew ? model.EndTime : vm.Meeting.ENDTIME;

            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(StaffId))
                errors.Add("Your staff information was not found. Please log in again.");

            // ---- Location + Room ----
            if (!model.BusinessCenterId.HasValue || model.BusinessCenterId.Value <= 0) errors.Add("Select Location");
            if (!model.RoomId.HasValue || model.RoomId.Value <= 0) errors.Add("Select Room");

            // ---- time (new meeting) ----
            if (isNew)
            {
                if (!start.HasValue) errors.Add("Enter Start Time");
                if (!end.HasValue) errors.Add("Enter End Time");
                if (start.HasValue && end.HasValue && end.Value <= start.Value)
                    errors.Add("End Time must be after Start Time");
                // no meeting in the past (1 minute allowed - the same rule as the page)
                if (start.HasValue && start.Value < DateTime.Now.AddMinutes(-1))
                    errors.Add("Past date or time is not allowed. Please choose a current or future time");
            }

            if (errors.Count == 0)
            {
                // the room must really belong to the selected location
                var rooms = _meetingRepository.GetRoomsByCenter(model.BusinessCenterId!.Value);
                if (!rooms.Any(r => r.ID == model.RoomId!.Value))
                    errors.Add("Selected room does not belong to the selected location");
                else if (start.HasValue && end.HasValue &&
                         _meetingRepository.IsRoomBooked(model.RoomId!.Value, start.Value, end.Value, model.MeetingId))
                    errors.Add("This room is already booked for the meeting time. Please choose another room or time.");
            }

            // ---- other fields ----
            // Meeting Called By / Participant are optional - only the length is checked
            // MT_MEETING.MEETINGCALLEDBY = VARCHAR2(50), MT_MEETING.ACTUALPERTICIPANT = INTEGER
            if (calledBy.Length > 50) errors.Add("Meeting Called By is too long (maximum 50 characters)");
            if (participant.HasValue && (participant.Value < 0 || participant.Value > 9999))
                errors.Add("Participant must be a number between 0 and 9999");
            if (!model.MeetingTypeId.HasValue || model.MeetingTypeId.Value <= 0) errors.Add("Select Meeting Type");
            // MT_MEETING.MEETINGTITLE = 100 characters, MT_MEETING.AGENDA = 1000 characters
            if (string.IsNullOrEmpty(title)) errors.Add("Enter Meeting Title");
            else if (title.Length > 100) errors.Add("Meeting Title is too long (maximum 100 characters)");
            if (string.IsNullOrWhiteSpace(System.Text.RegularExpressions.Regex.Replace(agenda, "<.*?>", "")))
                errors.Add("Enter Agenda");
            else if (agenda.Length > 1000) errors.Add("Agenda is too long (maximum 1000 characters)");

            if (errors.Count > 0)
            {
                ViewBag.Errors = errors;
                return View("CreateMeeting", vm);          // keeps what the user typed
            }

            try
            {
                if (isNew)
                {
                    int newId = _meetingRepository.InsertMeeting(new NewMeetingModel
                    {
                        RoomId = model.RoomId!.Value,
                        Title = title,
                        Agenda = agenda,
                        StartTime = start!.Value,
                        EndTime = end!.Value,
                        MeetingCalledBy = string.IsNullOrEmpty(calledBy) ? null : calledBy,
                        ActualParticipant = participant,
                        MeetingTypeId = model.MeetingTypeId!.Value,
                        AllDay = false
                    }, StaffId);

                    HttpContext.Session.SetInt32("MeetingId", newId);      // old SessionName.MeetingId
                    // mail to the ROOM ADMIN (EM_EMPLOYEE.OFFICEMAIL) - a mail problem must never break the save
                    bool mailed = await NotifyAsync(newId, "created");
                    TempData["MeetingMsg"] = mailed
                        ? "Meeting created successfully. Mail sent to the room admin."
                        : "Meeting created successfully. (Mail to the room admin could not be sent.)";
                    return RedirectToAction(nameof(CreateMeeting), new { id = newId });
                }

                _meetingRepository.UpdateMeeting(model.MeetingId, model.RoomId!.Value, title, agenda,
                    string.IsNullOrEmpty(calledBy) ? null : calledBy,
                    participant,
                    model.MeetingTypeId!.Value, StaffId);
                TempData["MeetingMsg"] = "Meeting saved successfully.";
                return RedirectToAction(nameof(CreateMeeting), new { id = model.MeetingId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Meeting save failed. MeetingId={MeetingId}", model.MeetingId);
                ViewBag.Errors = new List<string> { "Save failed. Please try again." };
                return View("CreateMeeting", vm);
            }
        }

        // =========================
        // MEETING SCHEDULE REPORT  /Meeting/MeetingSchedule   (old: Pages/Meeting/Default.aspx "Schedule")
        // "My Meeting" and "My Task" boxes of the old page are not converted (not needed).
        // =========================
        [HttpGet]
        public IActionResult MeetingSchedule(int? centerId = null, int roomId = 0, string? from = null, string? to = null, int status = 1)
        {
            // first open (no filter in the address) -> the location of the logged in user, like the old page; "All locations" = centerId 0
            int center = centerId ?? HttpContext.Session.GetInt32("BusinessCenterID") ?? 0;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var fromDate = DateTime.TryParseExact(from, "yyyy-MM-dd", ci, System.Globalization.DateTimeStyles.None, out var f) ? f : DateTime.Today;
            var toDate = DateTime.TryParseExact(to, "yyyy-MM-dd", ci, System.Globalization.DateTimeStyles.None, out var t) ? t : fromDate.AddDays(30);
            if (toDate < fromDate) (fromDate, toDate) = (toDate, fromDate);
            if ((toDate - fromDate).TotalDays > 366) toDate = fromDate.AddDays(366);      // keep the report small
            if (status < -1 || status > 3) status = 1;

            var vm = new MeetingScheduleViewModel
            {
                CenterId = center < 0 ? 0 : center,
                RoomId = roomId < 0 ? 0 : roomId,
                From = fromDate,
                To = toDate,
                Status = status,
                Centers = _meetingRepository.GetBusinessCenters()
            };
            if (vm.CenterId > 0) vm.Rooms = _meetingRepository.GetRoomsByCenter(vm.CenterId);
            else vm.RoomId = 0;                                                          // a room needs a location

            vm.Rows = _meetingRepository.GetMeetingSchedule(vm.CenterId, vm.RoomId, fromDate, toDate, status);
            return View("MeetingSchedule", vm);
        }

        // =========================
        // ROOM WISE TOTAL USES TIME  /Meeting/RoomUsage   (report)
        // Default: this month, the location of the logged in user, approved meetings
        // =========================
        [HttpGet]
        public IActionResult RoomUsage(int? centerId = null, int roomId = 0, string? from = null, string? to = null, int status = 1)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var fromDate = DateTime.TryParseExact(from, "yyyy-MM-dd", ci, System.Globalization.DateTimeStyles.None, out var f) ? f : monthStart;
            var toDate = DateTime.TryParseExact(to, "yyyy-MM-dd", ci, System.Globalization.DateTimeStyles.None, out var t) ? t : monthStart.AddMonths(1).AddDays(-1);
            if (toDate < fromDate) (fromDate, toDate) = (toDate, fromDate);
            if ((toDate - fromDate).TotalDays > 731) toDate = fromDate.AddDays(731);          // maximum 2 years
            if (status < -1 || status > 3) status = 1;
            int center = centerId ?? HttpContext.Session.GetInt32("BusinessCenterID") ?? 0;

            var vm = new RoomUsageViewModel
            {
                CenterId = center < 0 ? 0 : center,
                RoomId = roomId < 0 ? 0 : roomId,
                From = fromDate,
                To = toDate,
                Status = status,
                Centers = _meetingRepository.GetBusinessCenters()
            };
            if (vm.CenterId > 0) vm.Rooms = _meetingRepository.GetRoomsByCenter(vm.CenterId);
            else vm.RoomId = 0;

            vm.Rows = _meetingRepository.GetRoomUsage(vm.CenterId, vm.RoomId, fromDate, toDate, status);
            return View("RoomUsage", vm);
        }

        // =========================
        // MEETING CALENDAR  /Meeting/MeetingCalendar   (old Default.aspx FullCalendar: month / week / day at a glance)
        // =========================
        [HttpGet]
        public IActionResult MeetingCalendar(int? centerId = null, int roomId = 0, int status = 1)
        {
            int center = centerId ?? HttpContext.Session.GetInt32("BusinessCenterID") ?? 0;
            if (status < -1 || status > 3) status = 1;

            var vm = new MeetingScheduleViewModel
            {
                CenterId = center < 0 ? 0 : center,
                RoomId = roomId < 0 ? 0 : roomId,
                Status = status,
                Centers = _meetingRepository.GetBusinessCenters()
            };
            if (vm.CenterId > 0) vm.Rooms = _meetingRepository.GetRoomsByCenter(vm.CenterId);
            else vm.RoomId = 0;
            return View("MeetingCalendar", vm);
        }

        // events of the visible calendar range (FullCalendar sends start / end like 2026-09-27T00:00:00+06:00)
        [HttpGet]
        public IActionResult GetCalendarEvents(int centerId, int roomId, int status, string? start, string? end)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var from = DateTimeOffset.TryParse(start, ci, System.Globalization.DateTimeStyles.None, out var s) ? s.DateTime : DateTime.Today.AddDays(-7);
            var to = DateTimeOffset.TryParse(end, ci, System.Globalization.DateTimeStyles.None, out var e) ? e.DateTime : from.AddDays(42);
            if (to <= from) to = from.AddDays(1);
            if ((to - from).TotalDays > 100) to = from.AddDays(100);                       // keep it small
            if (status < -1 || status > 3) status = 1;

            // the repository takes an inclusive "to" date -> give the last visible day
            var rows = _meetingRepository.GetMeetingSchedule(Math.Max(centerId, 0), Math.Max(roomId, 0), from, to.AddSeconds(-1), status);

            var list = rows.Where(r => r.STARTTIME.HasValue).Select(r => new
            {
                id = r.ID,
                title = roomId > 0 ? r.MEETINGTITLE : $"{r.MEETINGTITLE} ({r.RoomDisplay})",   // all rooms: show the room too
                start = r.STARTTIME!.Value.ToString("yyyy-MM-ddTHH:mm:ss"),
                end = (r.ENDTIME ?? r.STARTTIME.Value.AddHours(1)).ToString("yyyy-MM-ddTHH:mm:ss"),
                status = r.ISAPPROVED ?? 0,
                room = r.RoomDisplay,
                calledBy = r.MEETINGCALLEDBY,
                requestedBy = r.CREATORNAME,
                roomAdmin = r.ADMINNAME,
                fullTitle = r.MEETINGTITLE
            });
            return Json(list);
        }

        // =========================
        // ROOMS of a Location (Location drop down change)
        // =========================
        [HttpGet]
        public IActionResult GetRooms(int businessCenterId)
        {
            if (businessCenterId <= 0) return Json(new List<object>());

            var rooms = _meetingRepository.GetRoomsByCenter(businessCenterId)
                .Select(r => new
                {
                    id = r.ID,
                    name = r.DisplayName,
                    adminName = r.ADMINNAME,
                    adminMobile = r.ADMINMOBILE,
                    adminMail = r.ADMINMAIL
                });
            return Json(rooms);
        }

        // =========================
        // ROOM AVAILABILITY of one day (booked / free calendar in CreateMeeting.cshtml)
        // =========================
        [HttpGet]
        public IActionResult GetRoomBookings(int roomId, string? date, int meetingId = 0)
        {
            if (roomId <= 0 || !DateTime.TryParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                                                         System.Globalization.DateTimeStyles.None, out var day))
                return Json(new List<object>());

            var list = _meetingRepository.GetRoomBookings(roomId, day, meetingId)
                .Select(b => new
                {
                    id = b.ID,
                    title = b.MEETINGTITLE,
                    start = b.STARTTIME?.ToString("yyyy-MM-ddTHH:mm"),
                    end = b.ENDTIME?.ToString("yyyy-MM-ddTHH:mm"),
                    status = b.ISAPPROVED,
                    requestedBy = b.CREATORNAME
                });
            return Json(list);
        }

        // =========================
        // CHAIRPERSON lookup + auto complete
        // =========================
        [HttpGet]
        public IActionResult GetEmployee(string staffId)
        {
            if (string.IsNullOrWhiteSpace(staffId)) return Json(new { found = false });

            var e = _meetingRepository.GetEmployeeByStaffId(staffId.Trim());
            return e == null
                ? Json(new { found = false })
                : Json(new { found = true, staffId = e.STAFFID, name = e.EMPLOYEENAME, mobile = e.OFFICEMOBILE });
        }

        [HttpGet]
        public IActionResult SearchEmployee(string term)
        {
            if (string.IsNullOrWhiteSpace(term)) return Json(new List<object>());

            var list = _meetingRepository.SearchEmployees(term, 10)
                .Select(e => new { staffId = e.STAFFID, name = e.EMPLOYEENAME });
            return Json(list);
        }

        // =========================
        // CALENDAR SAVE (kept for the calendar page)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateEvent([FromBody] CalendarEventRequest request)
        {
            if (request == null || request.RoomId <= 0 || string.IsNullOrWhiteSpace(request.Title) || request.End <= request.Start)
                return Json(new { success = false, message = "Invalid meeting data." });

            try
            {
                if (_meetingRepository.IsRoomBooked(request.RoomId, request.Start, request.End, 0))
                    return Json(new { success = false, message = "This room is already booked for the selected time." });

                request.Title = request.Title.Trim();
                request.Description = _sanitizer.Sanitize(request.Description ?? "");

                if (request.Title.Length > 100)
                    return Json(new { success = false, message = "Meeting Title is too long (maximum 100 characters)." });
                if (request.Description.Length > 1000)
                    return Json(new { success = false, message = "Agenda is too long (maximum 1000 characters)." });

                int id = _meetingRepository.CreateMeetingFromCalendar(request, StaffId);
                bool mailed = await NotifyAsync(id, "created");        // mail to the room admin
                return Json(new { success = true, id, mailed, redirect = Url.Action(nameof(CreateMeeting), new { id }) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Create meeting failed");
                return Json(new { success = false, message = "Save failed. Please try again." });
            }
        }

        // ---------------------------------------------------------
        // MAIL  kind: created (-> room admin) | approved / rejected (-> creator) | cancelled (-> room admin)
        // returns false when the mail was not sent; the caller only reports it, it never fails the action
        private async Task<bool> NotifyAsync(int meetingId, string kind, string? reason = null)
        {
            try
            {
                var info = _meetingRepository.GetMeetingMailInfo(meetingId);
                if (info == null) return false;

                string? to;
                (string Subject, string Body) mail;

                switch (kind)
                {
                    case "created":
                        to = info.ADMINMAIL;
                        mail = MeetingMailBuilder.NewRequest(info, AbsoluteUrl("RoomAdmin"));
                        break;
                    case "approved":
                        to = info.CREATORMAIL;
                        mail = MeetingMailBuilder.Approved(info, AbsoluteUrl("MeetingList"));
                        break;
                    case "rejected":
                        to = info.CREATORMAIL;
                        mail = MeetingMailBuilder.Rejected(info, AbsoluteUrl("MeetingList"));
                        break;
                    case "cancelled":
                        to = info.ADMINMAIL;
                        mail = MeetingMailBuilder.Cancelled(info, reason, AbsoluteUrl("RoomAdmin"));
                        break;
                    default:
                        return false;
                }

                if (string.IsNullOrWhiteSpace(to))
                {
                    _logger.LogWarning("No OFFICEMAIL found, mail not sent. MeetingId={MeetingId}, Kind={Kind}", meetingId, kind);
                    return false;
                }

                return await _email.SendAsync(to.Trim(), mail.Subject, mail.Body);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Mail failed. MeetingId={MeetingId}, Kind={Kind}", meetingId, kind);
                return false;
            }
        }

        // full link for the mail: App:BaseUrl from appsettings.json (recommended), otherwise the current request
        private string AbsoluteUrl(string action)
        {
            string path = Url.Action(action, "Meeting") ?? "/";
            string baseUrl = (_config["App:BaseUrl"] ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl)) baseUrl = $"{Request.Scheme}://{Request.Host}";
            return baseUrl + path;
        }

        // ---------------------------------------------------------
        // meetingId = 0 -> blank NEW form
        private CreateMeetingViewModel? BuildViewModel(int meetingId, SaveMeetingRequest? posted)
        {
            MeetingDetailModel? meeting = meetingId > 0
                ? _meetingRepository.GetMeetingDetails(meetingId)
                : new MeetingDetailModel();
            if (meeting == null) return null;

            // after a failed save show what the user typed, otherwise what is in the database
            int? centerId = posted != null ? posted.BusinessCenterId : meeting.BUSINESSCENTERID;
            int? roomId = posted != null ? posted.RoomId : (meeting.MROOMID > 0 ? meeting.MROOMID : null);

            var vm = new CreateMeetingViewModel
            {
                Meeting = meeting,
                MeetingId = meetingId,
                BusinessCenters = _meetingRepository.GetBusinessCenters(),
                MeetingTypes = _meetingRepository.GetMeetingTypes(),
                BusinessCenterId = centerId,
                RoomId = roomId,
                Rooms = centerId.HasValue && centerId.Value > 0
                            ? _meetingRepository.GetRoomsByCenter(centerId.Value)
                            : new List<MeetingRoomModel>(),
                MeetingTypeId = posted != null ? posted.MeetingTypeId : meeting.MEETINGTYPEID,
                MeetingTitle = posted != null ? posted.MeetingTitle : meeting.MEETINGTITLE,
                Agenda = posted != null ? posted.Agenda : meeting.AGENDA,
                ChairStaffId = meeting.CHAIRPERSONSTAFFID,      // not on the entry page any more (old meetings / ViewMeeting only)
                MeetingCalledBy = posted != null ? posted.MeetingCalledBy : meeting.MEETINGCALLEDBY,
                ActualParticipant = posted != null ? posted.ActualParticipant : meeting.ACTUALPERTICIPANT,
                StartTime = posted != null && meetingId <= 0 ? posted.StartTime : meeting.STARTTIME,
                EndTime = posted != null && meetingId <= 0 ? posted.EndTime : meeting.ENDTIME
            };

            if (!string.IsNullOrWhiteSpace(vm.ChairStaffId))
            {
                var e = _meetingRepository.GetEmployeeByStaffId(vm.ChairStaffId.Trim());
                vm.ChairName = e?.EMPLOYEENAME;
                vm.ChairMobile = e?.OFFICEMOBILE;
            }
            return vm;
        }
    }
}
