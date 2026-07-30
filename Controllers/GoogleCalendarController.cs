using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using back_mylife.Data;
using back_mylife.Models;
using back_mylife.Services;

namespace back_mylife.Controllers
{
    [Route("api/[controller]")]
    public class GoogleCalendarController : AuthorizedApiController
    {
        private readonly AppDbContext _context;
        private readonly GoogleCalendarService _googleCalendar;

        public GoogleCalendarController(AppDbContext context, GoogleCalendarService googleCalendar)
        {
            _context = context;
            _googleCalendar = googleCalendar;
        }

        public record GoogleCalendarUpsertDto(string AccessToken, string RefreshToken, DateTime TokenExpiresAt);
        public record GoogleCalendarConnectDto(string Code, string RedirectUri);

        [HttpGet("{userId}")]
        public async Task<IActionResult> GetConnection(Guid userId)
        {
            if (!IsCurrentUser(userId)) return Forbid();

            var connection = await _context.GoogleCalendarConnections
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.UserId == userId);

            if (connection == null) return NotFound();
            return Ok(connection);
        }

        [HttpPut("{userId}")]
        public async Task<IActionResult> UpsertConnection(Guid userId, [FromBody] GoogleCalendarUpsertDto dto)
        {
            if (!IsCurrentUser(userId)) return Forbid();

            var connection = await _context.GoogleCalendarConnections
                .FirstOrDefaultAsync(c => c.UserId == userId);

            if (connection == null)
            {
                connection = new GoogleCalendarConnection
                {
                    UserId = userId,
                    AccessToken = dto.AccessToken,
                    RefreshToken = dto.RefreshToken,
                    TokenExpiresAt = dto.TokenExpiresAt,
                };
                _context.GoogleCalendarConnections.Add(connection);
            }
            else
            {
                connection.AccessToken = dto.AccessToken;
                // Google only returns a refresh_token on first consent; keep the existing one if not resent.
                if (!string.IsNullOrEmpty(dto.RefreshToken))
                {
                    connection.RefreshToken = dto.RefreshToken;
                }
                connection.TokenExpiresAt = dto.TokenExpiresAt;
                connection.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            return Ok(connection);
        }

        // ทางเลือกสำหรับ flow OAuth จริง: client ทำ Google Sign-In พร้อมขอ scope ปฏิทิน
        // แล้วส่ง authorization code มาแลก token ที่นี่แทนการส่ง token มาตรงๆ (ปลอดภัยกว่า
        // เพราะ client secret ไม่ต้องฝังไว้ในแอป)
        [HttpPost("{userId}/connect")]
        public async Task<IActionResult> ConnectWithAuthCode(Guid userId, [FromBody] GoogleCalendarConnectDto dto)
        {
            if (!IsCurrentUser(userId)) return Forbid();

            if (!_googleCalendar.IsConfigured)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    message = "เซิร์ฟเวอร์ยังไม่ได้ตั้งค่า Google OAuth client (GOOGLE_CLIENT_ID / GOOGLE_CLIENT_SECRET)"
                });
            }

            try
            {
                var (accessToken, refreshToken, expiresAt) = await _googleCalendar.ExchangeAuthCodeAsync(dto.Code, dto.RedirectUri);

                var connection = await _context.GoogleCalendarConnections
                    .FirstOrDefaultAsync(c => c.UserId == userId);

                if (connection == null)
                {
                    connection = new GoogleCalendarConnection
                    {
                        UserId = userId,
                        AccessToken = accessToken,
                        RefreshToken = refreshToken,
                        TokenExpiresAt = expiresAt,
                    };
                    _context.GoogleCalendarConnections.Add(connection);
                }
                else
                {
                    connection.AccessToken = accessToken;
                    if (!string.IsNullOrEmpty(refreshToken))
                    {
                        connection.RefreshToken = refreshToken;
                    }
                    connection.TokenExpiresAt = expiresAt;
                    connection.UpdatedAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();
                return Ok(new { message = "เชื่อมต่อ Google Calendar สำเร็จ", connectedAt = connection.UpdatedAt });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "แลกเปลี่ยน authorization code กับ Google ไม่สำเร็จ", detail = ex.Message });
            }
        }

        [HttpDelete("{userId}")]
        public async Task<IActionResult> DeleteConnection(Guid userId)
        {
            if (!IsCurrentUser(userId)) return Forbid();

            var connection = await _context.GoogleCalendarConnections
                .FirstOrDefaultAsync(c => c.UserId == userId);
            if (connection == null) return NotFound();

            _context.GoogleCalendarConnections.Remove(connection);
            await _context.SaveChangesAsync();
            return Ok(new { message = "ยกเลิกการเชื่อมต่อ Google Calendar สำเร็จ" });
        }
    }
}
