// Services/DispatchMailBuilder.cs
// Subject + HTML body of the dispatch mails. Same mails / subjects as YA (dalDispatch.SendInitialMail, SendUpdateMail,
// dalApprover.SendApprovedMail, SendRejectMail). Every value is HTML-encoded (the old code pasted raw text into the HTML).
using System.Net;
using System.Text;
using AlphaCare.Models;

namespace AlphaCare.Services
{
    public static class DispatchMailBuilder
    {
        private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

        // after SAVE -> to the approvers
        public static (string Subject, string Body) NewForApprover(DispatchMailInfoModel m, string link) =>
            ("New Dispatch for approval",
             Wrap($"<h3>New dispatch-{E(m.TRANSACTIONID)} waiting for approval</h3>" + SenderBlock(m) + "<hr>" + ReceiverBlock(m), m, link));

        // after SAVE -> to the sender
        public static (string Subject, string Body) NewForSender(DispatchMailInfoModel m, string link) =>
            ("New dispatch created for you",
             Wrap($"<h3>Your dispatch-{E(m.TRANSACTIONID)} is waiting for approval</h3>" + ReceiverBlock(m), m, link));

        // old SendUpdateMail (from DispatchStatus) -> to the receiver
        public static (string Subject, string Body) StepForReceiver(DispatchMailInfoModel m, string step, string link) =>
            ("Dispatch for you",
             Wrap($"<h3>Dispatch-{E(m.TRANSACTIONID)} sent to you is in : {E(step)}</h3>" + SenderBlock(m), m, link));

        // old SendUpdateMail -> to the sender
        public static (string Subject, string Body) StepForSender(DispatchMailInfoModel m, string step, string link) =>
            ("Your dispatch status updated",
             Wrap($"<h3>Your dispatch-{E(m.TRANSACTIONID)} is in : {E(step)}</h3>" + ReceiverBlock(m), m, link));

        // old SendApprovedMail / SendRejectMail -> approvers and sender
        public static (string Subject, string Body) DecisionForApprover(DispatchMailInfoModel m, bool approved, string by, string link) =>
            ($"Dispatch-{m.TRANSACTIONID} {(approved ? "Approved" : "rejected")}",
             Wrap($"<h3>Dispatch-{E(m.TRANSACTIONID)} {(approved ? "Approved" : "rejected")} by {E(by)}</h3>" + SenderBlock(m) + "<hr>" + ReceiverBlock(m), m, link));

        public static (string Subject, string Body) DecisionForSender(DispatchMailInfoModel m, bool approved, string by, string link) =>
            ($"Dispatch-{m.TRANSACTIONID} {(approved ? "Approved" : "rejected")}",
             Wrap($"<h3>Your dispatch-{E(m.TRANSACTIONID)} {(approved ? "approved" : "rejected")} by {E(by)}</h3>" + ReceiverBlock(m), m, link));

        // ---------------------------------------------------------------- helpers
        private static string SenderBlock(DispatchMailInfoModel m) =>
            $"<b>Sender : </b>{E(m.EMPLOYEENAME)}<br><b>Mobile : </b>{E(m.OFFICEMOBILE)}";

        private static string ReceiverBlock(DispatchMailInfoModel m) =>
            $"<b>Receiver : </b>{E(m.BUSINESSCENTER)}-{E(m.RECEIVERNAME)}<br><b>Mobile : </b>{E(m.RECEIVERMOBILE)}";

        private static string Wrap(string top, DispatchMailInfoModel m, string link)
        {
            var sb = new StringBuilder();
            sb.Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;color:#1e293b;font-size:14px;line-height:1.5\">");
            sb.Append(top);
            sb.Append("<h4>Item Details :</h4>");
            sb.Append("<table border=\"1\" cellspacing=\"0\" cellpadding=\"4\" style=\"border-collapse:collapse;min-width:400px\">");
            sb.Append("<tr><th>Item</th><th>Quantity</th><th>Unit</th><th>Notes</th></tr>");
            foreach (var i in m.Items)
                sb.Append($"<tr><td>{E(i.ITEMNAME)}</td><td>{E(i.QUANTITY?.ToString("0.##"))}</td><td>{E(i.UNIT)}</td><td>{E(i.NOTES)}</td></tr>");
            sb.Append("</table>");
            sb.Append($"<h3>NB: Please login to <a href=\"{E(link)}\" target=\"_blank\">Admin 24/7</a> for more details</h3>");
            sb.Append("<span style=\"color:#64748b;font-size:12px\">This is an automatic message from Admin Management Software. Please do not reply.</span>");
            sb.Append("</div>");
            return sb.ToString();
        }
    }
}
