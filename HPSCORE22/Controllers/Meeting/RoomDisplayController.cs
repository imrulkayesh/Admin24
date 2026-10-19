// Controllers/Meeting/RoomDisplayController.cs   (views: Views/Meeting/RoomDisplay.cshtml and Views/Meeting/RoomDisplayLinks.cshtml)
// Tablet at the door of a meeting room:  /RoomDisplay/{roomId}   (no login - the tablet only shows today's approved meetings)
// Address list for the admin (login needed):  /RoomDisplay
using AlphaCare.Interface;
using AlphaCare.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace QCMS.Controllers
{
    public class RoomDisplayController : Controller
    {
        private readonly IMeetingRepository _meetingRepository;
        private readonly ILogger<RoomDisplayController> _logger;

        public RoomDisplayController(IMeetingRepository meetingRepository, ILogger<RoomDisplayController> logger)
        {
            _meetingRepository = meetingRepository;
            _logger = logger;
        }

        // ---------- the door screen ----------
        [AllowAnonymous]
        [HttpGet("RoomDisplay/{roomId:int}")]
        public IActionResult Room(int roomId)
        {
            var info = _meetingRepository.GetRoomDisplayInfo(roomId);
            if (info == null) return NotFound("Meeting room not found.");
            return View("~/Views/Meeting/RoomDisplay.cshtml", info);          // views are in Views/Meeting/
        }

        // data of today (the screen asks again every minute)
        [AllowAnonymous]
        [HttpGet("RoomDisplay/Data/{roomId:int}")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public IActionResult Data(int roomId)
        {
            try
            {
                var now = DateTime.Now;                                   // server time = the same clock as the database
                var list = _meetingRepository.GetRoomMeetingsOfDay(roomId, now.Date)
                    .Where(m => m.STARTTIME.HasValue && m.ENDTIME.HasValue)
                    .Select(m => new
                    {
                        id = m.ID,
                        title = m.MEETINGTITLE,
                        start = m.STARTTIME!.Value.ToString("yyyy-MM-ddTHH:mm:ss"),
                        end = m.ENDTIME!.Value.ToString("yyyy-MM-ddTHH:mm:ss"),
                        calledBy = m.MEETINGCALLEDBY,
                        requestedBy = m.CREATORNAME
                    });
                return Json(new { now = now.ToString("yyyy-MM-ddTHH:mm:ss"), meetings = list });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Room display data failed. RoomId={RoomId}", roomId);
                return StatusCode(500);
            }
        }

        // ---------- admin: the address of every room (to set on each tablet) ----------
        [Authorize]
        [HttpGet("RoomDisplay")]
        public IActionResult Links()
        {
            var rows = new List<RoomDisplayLinkModel>();
            foreach (var c in _meetingRepository.GetBusinessCenters())
                foreach (var r in _meetingRepository.GetRoomsByCenter(c.ID))
                    rows.Add(new RoomDisplayLinkModel { RoomId = r.ID, CenterName = c.CENTERNAME, RoomTitle = r.ROOMTITLE, Seats = r.SEATS });
            return View("~/Views/Meeting/RoomDisplayLinks.cshtml", rows);     // views are in Views/Meeting/
        }
    }
}
