// Services/HrisStaffService.cs
// HRIS staff lookup used by Register Khata - Issue (old Issue.aspx [WebMethod] staffChecker).
// Same HRIS endpoint as YA: GET {HrisApi:StaffUrl}{staffId}  ->  { "StaffResult": "[ { ID, NAME, DESIGNATION, CONTACTNO, EMAIL, ... } ]" }
// (StaffResult is a JSON string that holds the list). Default URL = the one hard-coded in YA.
// Status as YA: 1 = found, 2 = not found / HRIS error, 3 = empty staff id.
using System.Text.Json;

namespace AlphaCare.Services
{
    public class HrisStaffResult
    {
        public int Status { get; set; }
        public string? StaffId { get; set; }
        public string? Name { get; set; }
        public string? Designation { get; set; }
        public string? Contact { get; set; }
        public string? Email { get; set; }
    }

    public class HrisStaffService
    {
        private const string DefaultUrl = "http://hris.prangroup.com:8686/api/hrisapi.svc/Staff/";
        private readonly HttpClient _http;
        private readonly IConfiguration _config;
        private readonly ILogger<HrisStaffService> _logger;

        public HrisStaffService(HttpClient http, IConfiguration config, ILogger<HrisStaffService> logger)
        {
            _http = http;
            _config = config;
            _logger = logger;
        }

        public async Task<HrisStaffResult> FindAsync(string? staffId)
        {
            string id = (staffId ?? "").Trim();
            if (id.Length == 0) return new HrisStaffResult { Status = 3 };

            string baseUrl = _config["HrisApi:StaffUrl"] is { Length: > 0 } u ? u : DefaultUrl;
            try
            {
                using var response = await _http.GetAsync(baseUrl + Uri.EscapeDataString(id));
                if (!response.IsSuccessStatusCode) return new HrisStaffResult { Status = 2 };

                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (!doc.RootElement.TryGetProperty("StaffResult", out var sr)) return new HrisStaffResult { Status = 2 };

                // StaffResult is either a JSON string holding the list (YA) or the list itself
                JsonElement list;
                using JsonDocument? inner = sr.ValueKind == JsonValueKind.String ? JsonDocument.Parse(sr.GetString() ?? "[]") : null;
                list = inner?.RootElement ?? sr;
                if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0) return new HrisStaffResult { Status = 2 };

                var first = list[0];
                string? Get(string name) => first.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : null;
                return new HrisStaffResult
                {
                    Status = 1,
                    StaffId = Get("ID"),
                    Name = Get("NAME"),
                    Designation = Get("DESIGNATION"),
                    Contact = Get("CONTACTNO"),
                    Email = Get("EMAIL")
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "HRIS staff lookup failed for {StaffId}", id);
                return new HrisStaffResult { Status = 2 };
            }
        }
    }
}
