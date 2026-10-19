using System.Text;
using System.Text.Json;

namespace AlphaCare.Services
{
    /// <summary>
    /// URL / keys come from appsettings.json -> "HrisApi" section.
    /// </summary>
    public class ApiService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;
        private readonly ILogger<ApiService> _logger;

        public ApiService(HttpClient httpClient, IConfiguration config, ILogger<ApiService> logger)
        {
            _httpClient = httpClient;
            _config = config;
            _logger = logger;
        }

        public async Task<HrisLoginResult> HrisLoginAsync(string username, string password)
        {
            try
            {
                var url = _config["HrisApi:LoginUrl"];       // http://hrisapi.prangroup.com:8083/v1/Login/HrisLogin
                var sKeyL = _config["HrisApi:S_KEYL"];
                var basicAuth = _config["HrisApi:BasicAuth"]; // the value AFTER "Basic "

                var json = JsonSerializer.Serialize(new { username, password });

                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                request.Headers.TryAddWithoutValidation("S_KEYL", sKeyL);
                request.Headers.TryAddWithoutValidation("Authorization", "Basic " + basicAuth);

                using var response = await _httpClient.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode && string.IsNullOrWhiteSpace(body))
                    return HrisLoginResult.Fail($"HRIS API error: {(int)response.StatusCode}");

                using var doc = JsonDocument.Parse(body);
                var code = GetPropertyIgnoreCase(doc.RootElement, "succesS_CODE");
                var message = GetPropertyIgnoreCase(doc.RootElement, "message")
                              ?? GetPropertyIgnoreCase(doc.RootElement, "errorMessage");

                if (code == "2000")
                    return HrisLoginResult.Ok(body);

                return HrisLoginResult.Fail(message ?? "Invalid Username or Password.");
            }
            catch (TaskCanceledException)
            {
                return HrisLoginResult.Fail("HRIS server timeout. Please try again.");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HRIS API network error");
                return HrisLoginResult.Fail("Cannot reach HRIS server. Please try again.");
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "HRIS API invalid JSON");
                return HrisLoginResult.Fail("Invalid response from HRIS server.");
            }
        }

        // Reads a JSON property case-insensitively and returns it as string (works for "2000" and 2000)
        private static string? GetPropertyIgnoreCase(JsonElement root, string name)
        {
            if (root.ValueKind != JsonValueKind.Object) return null;
            foreach (var p in root.EnumerateObject())
            {
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return p.Value.ValueKind switch
                    {
                        JsonValueKind.String => p.Value.GetString(),
                        JsonValueKind.Null => null,
                        _ => p.Value.ToString()
                    };
                }
            }
            return null;
        }
    }

    public class HrisLoginResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? RawResponse { get; set; }

        public static HrisLoginResult Ok(string raw) => new() { Success = true, RawResponse = raw };
        public static HrisLoginResult Fail(string msg) => new() { Success = false, Message = msg };
    }
}
