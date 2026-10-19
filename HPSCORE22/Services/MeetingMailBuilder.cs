// Services/MeetingMailBuilder.cs
// Builds subject + HTML body of the four meeting mails. Every value is HTML-encoded
// (the old mail code pasted raw text into the HTML).
using System.Net;
using AlphaCare.Models;
using Ganss.Xss;

namespace AlphaCare.Services
{
    public static class MeetingMailBuilder
    {
        private static readonly HtmlSanitizer _sanitizer = new();

        private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

        // 1) after CREATE -> to the ROOM ADMIN
        public static (string Subject, string Body) NewRequest(MeetingMailInfoModel m, string link)
        {
            string intro = $"Dear {E(m.ADMINNAME)},<br><br>A new meeting room request is waiting for your approval.";
            string body = Wrap("New Meeting Request", "#4f46e5", intro, Table(m, showRequester: true, showAgenda: true),
                               link, "Open Room Admin to approve or reject");
            return ($"Meeting Request - {m.MEETINGTITLE}", body);
        }

        // 2) after ROOM ADMIN APPROVES -> to the CREATOR
        public static (string Subject, string Body) Approved(MeetingMailInfoModel m, string link)
        {
            string intro = $"Dear {E(m.CREATORNAME)},<br><br>Your meeting request has been <b style=\"color:#16a34a\">APPROVED</b>" +
                           (string.IsNullOrWhiteSpace(m.APPROVERNAME) ? "." : $" by {E(m.APPROVERNAME)}.");
            // the secret MEETING CODE and the remarks of the room admin are shown ONLY in this mail (to the creator)
            string extra = "";
            if (!string.IsNullOrWhiteSpace(m.MEETINGCODE))
                extra += Row("Meeting Code", $"<b style=\"font-size:20px;letter-spacing:4px;color:#16a34a\">{E(m.MEETINGCODE)}</b>" +
                                             "<br><span style=\"color:#64748b;font-size:12px\">Keep this code private.</span>");
            if (!string.IsNullOrWhiteSpace(m.APPROVERREMARKS))
                extra += Row("Approver Remarks", E(m.APPROVERREMARKS));
            string body = Wrap("Meeting Approved", "#16a34a", intro, Table(m, showRequester: false, showAgenda: false, extraRows: extra),
                               link, "Open My Meeting List");
            return ($"Meeting Approved - {m.MEETINGTITLE}", body);
        }

        // 2b) after ROOM ADMIN REJECTS -> to the CREATOR
        public static (string Subject, string Body) Rejected(MeetingMailInfoModel m, string link)
        {
            string intro = $"Dear {E(m.CREATORNAME)},<br><br>Your meeting request has been <b style=\"color:#dc2626\">REJECTED</b>" +
                           (string.IsNullOrWhiteSpace(m.APPROVERNAME) ? "." : $" by {E(m.APPROVERNAME)}.") +
                           "<br>Please contact the room admin for details.";
            string extra = string.IsNullOrWhiteSpace(m.APPROVERREMARKS) ? "" : Row("Approver Remarks", E(m.APPROVERREMARKS));
            string body = Wrap("Meeting Rejected", "#dc2626", intro, Table(m, showRequester: false, showAgenda: false, extraRows: extra),
                               link, "Open My Meeting List");
            return ($"Meeting Rejected - {m.MEETINGTITLE}", body);
        }

        // 3) after CANCEL REQUEST by the creator -> to the ROOM ADMIN
        public static (string Subject, string Body) Cancelled(MeetingMailInfoModel m, string? reason, string link)
        {
            string intro = $"Dear {E(m.ADMINNAME)},<br><br>The meeting below has been <b style=\"color:#dc2626\">CANCELLED</b> by the requester. " +
                           "The room is free again for this time.";
            string extra = string.IsNullOrWhiteSpace(reason) ? "" : Row("Cancel Reason", E(reason));
            string body = Wrap("Meeting Cancelled", "#f59e0b", intro, Table(m, showRequester: true, showAgenda: false, extraRows: extra),
                               link, "Open Room Admin");
            return ($"Meeting Cancelled - {m.MEETINGTITLE}", body);
        }

        // ---------------------------------------------------------------- helpers
        private static string Row(string label, string htmlValue) =>
            $"<tr><td style=\"padding:8px 12px;border:1px solid #e2e8f0;background:#f8fafc;width:150px;font-weight:600\">{E(label)}</td>" +
            $"<td style=\"padding:8px 12px;border:1px solid #e2e8f0\">{htmlValue}</td></tr>";

        private static string Table(MeetingMailInfoModel m, bool showRequester, bool showAgenda, string extraRows = "")
        {
            string date = m.STARTTIME?.ToString("dd MMM yyyy") ?? "-";
            string time = $"{m.STARTTIME?.ToString("hh:mm tt")} to {m.ENDTIME?.ToString("hh:mm tt")}";

            var sb = new System.Text.StringBuilder();
            sb.Append("<table cellspacing=\"0\" cellpadding=\"0\" style=\"border-collapse:collapse;width:100%;max-width:560px;font-size:14px\">");
            sb.Append(Row("Meeting Title", E(m.MEETINGTITLE)));
            if (!string.IsNullOrWhiteSpace(m.MTYPENAME)) sb.Append(Row("Meeting Type", E(m.MTYPENAME)));
            sb.Append(Row("Location", E(m.CENTERNAME)));
            sb.Append(Row("Meeting Room", E(m.ROOMTITLE)));
            sb.Append(Row("Date", E(date)));
            sb.Append(Row("Time", E(time)));
            if (!string.IsNullOrWhiteSpace(m.CHAIRNAME)) sb.Append(Row("Chairperson", E(m.CHAIRNAME)));
            if (showRequester)
                sb.Append(Row("Requested By", E(m.CREATORNAME) + (string.IsNullOrWhiteSpace(m.CREATORMOBILE) ? "" : " (" + E(m.CREATORMOBILE) + ")")));
            sb.Append(extraRows);
            if (showAgenda && !string.IsNullOrWhiteSpace(m.AGENDA))
                sb.Append(Row("Agenda", _sanitizer.Sanitize(m.AGENDA)));
            sb.Append("</table>");
            return sb.ToString();
        }

        private static string Wrap(string heading, string color, string introHtml, string tableHtml, string link, string linkText)
        {
            return
                "<div style=\"font-family:Segoe UI,Arial,sans-serif;color:#1e293b;font-size:14px;line-height:1.5\">" +
                $"<div style=\"max-width:600px;border:1px solid #e2e8f0;border-radius:12px;overflow:hidden\">" +
                $"<div style=\"background:{color};color:#fff;padding:14px 18px;font-size:18px;font-weight:700\">{E(heading)}</div>" +
                $"<div style=\"padding:18px\">{introHtml}<br><br>{tableHtml}<br>" +
                $"<a href=\"{E(link)}\" style=\"display:inline-block;background:{color};color:#fff;text-decoration:none;padding:10px 18px;border-radius:8px;font-weight:600\">{E(linkText)}</a>" +
                "<br><br><span style=\"color:#64748b;font-size:12px\">This is an automatic message from Admin Management Software. Please do not reply.</span>" +
                "</div></div></div>";
        }
    }
}
