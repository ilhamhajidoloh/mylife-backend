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

        public LineController(AppDbContext context)
        {
            _context = context;
        }

        public record LineConnectDto(string LineUserId, bool NotificationsEnabled);
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
                .Where(c => c.NotificationsEnabled)
                .Select(c => new { userId = c.UserId, lineUserId = c.LineUserId })
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
                    ConnectedAt = DateTime.UtcNow,
                };
                _context.LineConnections.Add(connection);
            }
            else
            {
                connection.LineUserId = dto.LineUserId;
                connection.NotificationsEnabled = dto.NotificationsEnabled;
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
    }
}
