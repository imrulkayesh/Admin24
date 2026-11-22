// Models/PendingGateCheckModel.cs
// One pending Dispatch Gate Check of a user (approved dispatch, gate pass not done) - shown on the login page when the
// account is inactive because of it (UC_USERS.ISACTIVE = 0, INACTIVETYPE = Dispatch). Column names = the query aliases.
namespace RetailCare.Models
{
    public class PendingGateCheckModel
    {
        public string? SENDER_LOCATION { get; set; }
        public string? SENDER_USER { get; set; }
        public string? SENDER_NAME { get; set; }
        public string? RECEIVER_LOCATION { get; set; }
        public string? RECEIVER_USER { get; set; }
        public string? RECEIVER_NAME { get; set; }
        public string? CREAT_USER { get; set; }
        public string? CREATOR_NAME { get; set; }
        public DateTime? CREATEDATE { get; set; }
        public string? TRANSACTIONID { get; set; }
        public string? ISSUETYPENAME { get; set; }
    }
}
