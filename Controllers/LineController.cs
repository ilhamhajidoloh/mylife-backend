using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using back_mylife.Data;
using back_mylife.Models;

namespace back_mylife.Controllers
{
    [Route("api/[controller]")]
    public class LineController : AuthorizedApiController
    {
        private readonly AppDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        public LineController(AppDbContext context, IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        public record LineConnectDto(string LineUserId, bool NotificationsEnabled, bool ClassRemindersEnabled, int ClassReminderMinutes);
        public record LineSessionDto(string? SessionStateJson, DateTime? SessionExpiresAt);

        [HttpGet("{userId}")]
        public async Task<IActionResult> GetConnection(Guid userId)
        {
            if (!IsCurrentUser(userId)) return Forbid();

            var connection = await _context.LineConnections
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.UserId == userId);

            if (connection == null)
            {
                return Ok(new { connected = false, lineUserId = (string?)null, notificationsEnabled = false, connectedAt = (DateTime?)null });
            }

            return Ok(new
            {
                connected = true,
                lineUserId = connection.LineUserId,
                notificationsEnabled = connection.NotificationsEnabled,
                classRemindersEnabled = connection.ClassRemindersEnabled,
                classReminderMinutes = connection.ClassReminderMinutes,
                connectedAt = (DateTime?)connection.ConnectedAt,
            });
        }

        // เรียกโดยบริการ LINE bot ภายนอกเท่านั้น (ไม่มี user JWT ให้ตรวจสอบ)
        // จึงป้องกันด้วย service API key แทน ไม่ใช่ [Authorize] ผู้ใช้ทั่วไป
        [HttpGet("by-line-user/{lineUserId}")]
        [AllowAnonymous]
        [RequireServiceKey]
        public async Task<IActionResult> GetByLineUserId(string lineUserId)
        {
            var connection = await _context.LineConnections
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.LineUserId == lineUserId);

            if (connection == null) return NotFound();
            return Ok(connection);
        }

        [HttpGet("connected")]
        [AllowAnonymous]
        [RequireServiceKey]
        public async Task<IActionResult> GetConnectedUsers()
        {
            var list = await _context.LineConnections
                .AsNoTracking()
                .Where(c => c.NotificationsEnabled || c.ClassRemindersEnabled)
                .Select(c => new { userId = c.UserId, lineUserId = c.LineUserId, classRemindersEnabled = c.ClassRemindersEnabled, classReminderMinutes = c.ClassReminderMinutes })
                .ToListAsync();
            return Ok(list);
        }

        [HttpPost("{userId}/connect")]
        public async Task<IActionResult> Connect(Guid userId, [FromBody] LineConnectDto dto)
        {
            if (!IsCurrentUser(userId)) return Forbid();

            var connection = await _context.LineConnections
                .FirstOrDefaultAsync(c => c.UserId == userId);

            if (connection == null)
            {
                connection = new LineConnection
                {
                    UserId = userId,
                    LineUserId = dto.LineUserId,
                    NotificationsEnabled = dto.NotificationsEnabled,
                    ClassRemindersEnabled = dto.ClassRemindersEnabled,
                    ClassReminderMinutes = dto.ClassReminderMinutes,
                    ConnectedAt = DateTime.UtcNow,
                };
                _context.LineConnections.Add(connection);
            }
            else
            {
                connection.LineUserId = dto.LineUserId;
                connection.NotificationsEnabled = dto.NotificationsEnabled;
                connection.ClassRemindersEnabled = dto.ClassRemindersEnabled;
                connection.ClassReminderMinutes = dto.ClassReminderMinutes;
                connection.ConnectedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            return Ok(connection);
        }

        [HttpPost("{userId}/disconnect")]
        public async Task<IActionResult> Disconnect(Guid userId)
        {
            if (!IsCurrentUser(userId)) return Forbid();

            var connection = await _context.LineConnections
                .FirstOrDefaultAsync(c => c.UserId == userId);
            if (connection == null) return NotFound();

            _context.LineConnections.Remove(connection);
            await _context.SaveChangesAsync();
            return Ok(new { message = "ยกเลิกการเชื่อมต่อ LINE สำเร็จ" });
        }

        [HttpPut("{userId}/session")]
        public async Task<IActionResult> UpdateSession(Guid userId, [FromBody] LineSessionDto dto)
        {
            if (!IsCurrentUser(userId)) return Forbid();

            var connection = await _context.LineConnections
                .FirstOrDefaultAsync(c => c.UserId == userId);
            if (connection == null) return NotFound();

            connection.SessionStateJson = dto.SessionStateJson;
            connection.SessionExpiresAt = dto.SessionExpiresAt;
            await _context.SaveChangesAsync();
            return Ok(connection);
        }

        [HttpPost("{userId}/test")]
        public async Task<IActionResult> SendTest(Guid userId)
        {
            if (!IsCurrentUser(userId)) return Forbid();
            var connection = await _context.LineConnections.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId);
            if (connection == null) return BadRequest(new { message = "ยังไม่ได้เชื่อมต่อ LINE" });
            var endpoint = _configuration["LINE_MESSAGING_API_URL"];
            var token = _configuration["LINE_CHANNEL_ACCESS_TOKEN"];
            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(token))
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "ยังไม่ได้ตั้งค่า LINE Messaging API บนเซิร์ฟเวอร์" });
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var response = await client.PostAsJsonAsync(
                $"{endpoint.TrimEnd('/')}/message/push",
                new
                {
                    to = connection.LineUserId,
                    messages = new[] { new { type = "text", text = "MyLife: ทดสอบการแจ้งเตือนสำเร็จ" } },
                });
            if (!response.IsSuccessStatusCode) return StatusCode((int)response.StatusCode, new { message = "ส่งข้อความ LINE ไม่สำเร็จ" });
            return Ok(new { message = "ส่งข้อความทดสอบแล้ว" });
        }
    }
}
