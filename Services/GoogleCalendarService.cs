using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using back_mylife.Data;
using back_mylife.Models;
using Microsoft.EntityFrameworkCore;

namespace back_mylife.Services
{
    // แลกเปลี่ยน/ต่ออายุ Google OAuth token และสร้าง-แก้ไข-ลบ event ใน Google Calendar
    // ของผู้ใช้ที่เชื่อมต่อไว้ (ผ่าน GoogleCalendarConnection) โดยเรียก REST API ของ
    // Google ตรงๆ แทนการพึ่ง client library เพื่อให้โปรเจกต์เบาเหมือนเดิม
    public class GoogleCalendarService
    {
        private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
        private const string CalendarEventsEndpoint = "https://www.googleapis.com/calendar/v3/calendars/primary/events";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly AppDbContext _context;
        private readonly ILogger<GoogleCalendarService> _logger;

        public GoogleCalendarService(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            AppDbContext context,
            ILogger<GoogleCalendarService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _context = context;
            _logger = logger;
        }

        public bool IsConfigured =>
            !string.IsNullOrEmpty(_configuration["Google:ClientId"]) &&
            !string.IsNullOrEmpty(_configuration["Google:ClientSecret"]);

        public async Task<(string AccessToken, string RefreshToken, DateTime ExpiresAt)> ExchangeAuthCodeAsync(string code, string redirectUri)
        {
            var client = _httpClientFactory.CreateClient();
            var form = new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = _configuration["Google:ClientId"] ?? string.Empty,
                ["client_secret"] = _configuration["Google:ClientSecret"] ?? string.Empty,
                ["redirect_uri"] = redirectUri,
                ["grant_type"] = "authorization_code",
            };

            var response = await client.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form));
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Google token exchange failed: {response.StatusCode} {body}");
            }

            var payload = JsonSerializer.Deserialize<GoogleTokenResponse>(body)
                ?? throw new InvalidOperationException("Google token exchange returned an empty response.");
            return (payload.AccessToken, payload.RefreshToken ?? string.Empty, DateTime.UtcNow.AddSeconds(payload.ExpiresIn));
        }

        // คืน access token ที่ใช้งานได้ทันที ต่ออายุอัตโนมัติ (และบันทึกลง DB) ถ้าใกล้หมดอายุ
        public async Task<string?> GetValidAccessTokenAsync(GoogleCalendarConnection connection)
        {
            if (connection.TokenExpiresAt > DateTime.UtcNow.AddMinutes(2))
            {
                return connection.AccessToken;
            }

            if (string.IsNullOrEmpty(connection.RefreshToken)) return null;

            var client = _httpClientFactory.CreateClient();
            var form = new Dictionary<string, string>
            {
                ["refresh_token"] = connection.RefreshToken,
                ["client_id"] = _configuration["Google:ClientId"] ?? string.Empty,
                ["client_secret"] = _configuration["Google:ClientSecret"] ?? string.Empty,
                ["grant_type"] = "refresh_token",
            };

            var response = await client.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form));
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Google token refresh failed: {Status} {Body}", response.StatusCode, body);
                return null;
            }

            var payload = JsonSerializer.Deserialize<GoogleTokenResponse>(body);
            if (payload == null) return null;

            connection.AccessToken = payload.AccessToken;
            connection.TokenExpiresAt = DateTime.UtcNow.AddSeconds(payload.ExpiresIn);
            connection.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return connection.AccessToken;
        }

        // สร้าง event ใหม่ถ้ายังไม่มี GoogleEventId หรืออัปเดต event เดิมถ้ามีแล้ว
        // คืนค่า GoogleEventId ล่าสุด (เดิมถ้า sync ไม่สำเร็จ)
        public async Task<string?> UpsertEventAsync(GoogleCalendarConnection connection, Activity activity)
        {
            if (activity.StartTime == null) return activity.GoogleEventId;

            var accessToken = await GetValidAccessTokenAsync(connection);
            if (accessToken == null) return activity.GoogleEventId;

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var eventBody = BuildEventPayload(activity);
            HttpResponseMessage response;

            if (string.IsNullOrEmpty(activity.GoogleEventId))
            {
                response = await client.PostAsJsonAsync(CalendarEventsEndpoint, eventBody);
            }
            else
            {
                response = await client.PutAsJsonAsync($"{CalendarEventsEndpoint}/{activity.GoogleEventId}", eventBody);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // event ถูกลบไปจากฝั่ง Google แล้ว ให้สร้างใหม่แทน
                    response = await client.PostAsJsonAsync(CalendarEventsEndpoint, eventBody);
                }
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Google Calendar sync failed for activity {ActivityId}: {Status} {Body}", activity.Id, response.StatusCode, errorBody);
                return activity.GoogleEventId;
            }

            var resultJson = await response.Content.ReadFromJsonAsync<JsonElement>();
            return resultJson.TryGetProperty("id", out var idProp) ? idProp.GetString() : activity.GoogleEventId;
        }

        public async Task DeleteEventAsync(GoogleCalendarConnection connection, string googleEventId)
        {
            var accessToken = await GetValidAccessTokenAsync(connection);
            if (accessToken == null) return;

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var response = await client.DeleteAsync($"{CalendarEventsEndpoint}/{googleEventId}");
            if (!response.IsSuccessStatusCode
                && response.StatusCode != System.Net.HttpStatusCode.NotFound
                && response.StatusCode != System.Net.HttpStatusCode.Gone)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Google Calendar delete failed for event {EventId}: {Status} {Body}", googleEventId, response.StatusCode, body);
            }
        }

        private static object BuildEventPayload(Activity activity)
        {
            var start = activity.StartTime!.Value;
            var end = activity.EndTime ?? start.AddHours(1);

            if (activity.IsAllDay)
            {
                return new
                {
                    summary = activity.Title,
                    description = activity.Description,
                    location = activity.Location,
                    start = new { date = start.ToString("yyyy-MM-dd") },
                    end = new { date = end.ToString("yyyy-MM-dd") },
                };
            }

            return new
            {
                summary = activity.Title,
                description = activity.Description,
                location = activity.Location,
                start = new { dateTime = start.ToString("yyyy-MM-ddTHH:mm:ss"), timeZone = "Asia/Bangkok" },
                end = new { dateTime = end.ToString("yyyy-MM-ddTHH:mm:ss"), timeZone = "Asia/Bangkok" },
            };
        }

        private class GoogleTokenResponse
        {
            [JsonPropertyName("access_token")]
            public string AccessToken { get; set; } = string.Empty;

            [JsonPropertyName("refresh_token")]
            public string? RefreshToken { get; set; }

            [JsonPropertyName("expires_in")]
            public int ExpiresIn { get; set; }
        }
    }
}
