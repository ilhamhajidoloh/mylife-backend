using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using back_mylife.Data;
using back_mylife.Models;
using back_mylife.Services;

namespace back_mylife.Controllers
{
    [Route("api/[controller]")]
    public class ActivityController : AuthorizedApiController
    {
        private readonly AppDbContext _context;
        private readonly GoogleCalendarService _googleCalendar;
        private readonly ILogger<ActivityController> _logger;

        public ActivityController(AppDbContext context, GoogleCalendarService googleCalendar, ILogger<ActivityController> logger)
        {
            _context = context;
            _googleCalendar = googleCalendar;
            _logger = logger;
        }

        [HttpGet("{userId}")]
        public async Task<IActionResult> GetActivities(Guid userId)
        {
            if (!IsCurrentUser(userId)) return Forbid();

            var list = await _context.Activities
                .Where(a => a.UserId == userId)
                .OrderBy(a => a.StartTime)
                .ToListAsync();
            return Ok(list);
        }

        [HttpGet("single/{id}")]
        public async Task<IActionResult> GetActivity(Guid id)
        {
            var existing = await _context.Activities.FindAsync(id);
            if (existing == null || existing.UserId != CurrentUserId) return NotFound();
            return Ok(existing);
        }

        [HttpPost]
        public async Task<IActionResult> AddActivity([FromBody] Activity item)
        {
            item.Id = Guid.NewGuid();
            item.UserId = CurrentUserId;
            _context.Activities.Add(item);
            await _context.SaveChangesAsync();
            await TrySyncToGoogleAsync(item);
            return Ok(item);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateActivity(Guid id, [FromBody] Activity item)
        {
            var existing = await _context.Activities.FindAsync(id);
            if (existing == null || existing.UserId != CurrentUserId) return NotFound();

            existing.Title = item.Title;
            existing.Description = item.Description;
            existing.StartTime = item.StartTime;
            existing.EndTime = item.EndTime;
            existing.IsAllDay = item.IsAllDay;
            existing.IsMultiDay = item.IsMultiDay;
            existing.IsIndefinite = item.IsIndefinite;
            existing.Recurrence = item.Recurrence;
            existing.Location = item.Location;
            existing.ReminderMinutes = item.ReminderMinutes;

            await _context.SaveChangesAsync();
            await TrySyncToGoogleAsync(existing);
            return Ok(existing);
        }

        public record GoogleSyncUpdate(string? GoogleEventId);

        [HttpPut("{id}/google-sync")]
        public async Task<IActionResult> UpdateGoogleSync(Guid id, [FromBody] GoogleSyncUpdate update)
        {
            var existing = await _context.Activities.FindAsync(id);
            if (existing == null || existing.UserId != CurrentUserId) return NotFound();

            existing.GoogleEventId = update.GoogleEventId;
            await _context.SaveChangesAsync();
            return Ok(existing);
        }

        [HttpPut("{id}/reminder-sent")]
        public async Task<IActionResult> MarkReminderSent(Guid id)
        {
            var existing = await _context.Activities.FindAsync(id);
            if (existing == null || existing.UserId != CurrentUserId) return NotFound();

            existing.ReminderSentAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return Ok(existing);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteActivity(Guid id)
        {
            var existing = await _context.Activities.FindAsync(id);
            if (existing == null || existing.UserId != CurrentUserId) return NotFound();

            var googleEventId = existing.GoogleEventId;
            var userId = existing.UserId;

            _context.Activities.Remove(existing);
            await _context.SaveChangesAsync();

            if (!string.IsNullOrEmpty(googleEventId))
            {
                await TryDeleteFromGoogleAsync(userId, googleEventId);
            }

            return Ok(new { message = "ลบกิจกรรมสำเร็จ" });
        }

        // ซิงก์กิจกรรมไปยัง Google Calendar ของผู้ใช้ (ถ้าเชื่อมต่อไว้) แบบ best-effort —
        // ล้มเหลวได้โดยไม่กระทบการบันทึกกิจกรรมหลัก เพราะปฏิทินเป็นฟีเจอร์เสริม ไม่ใช่ข้อมูลหลัก
        private async Task TrySyncToGoogleAsync(Activity activity)
        {
            if (!_googleCalendar.IsConfigured) return;
            try
            {
                var connection = await _context.GoogleCalendarConnections
                    .FirstOrDefaultAsync(c => c.UserId == activity.UserId);
                if (connection == null) return;

                var eventId = await _googleCalendar.UpsertEventAsync(connection, activity);
                if (eventId != activity.GoogleEventId)
                {
                    activity.GoogleEventId = eventId;
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Google Calendar sync failed for activity {ActivityId}", activity.Id);
            }
        }

        private async Task TryDeleteFromGoogleAsync(Guid userId, string googleEventId)
        {
            if (!_googleCalendar.IsConfigured) return;
            try
            {
                var connection = await _context.GoogleCalendarConnections
                    .FirstOrDefaultAsync(c => c.UserId == userId);
                if (connection == null) return;

                await _googleCalendar.DeleteEventAsync(connection, googleEventId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Google Calendar delete failed for event {EventId}", googleEventId);
            }
        }

        // Timeline: Previous, Current, Next activity with countdown
        [HttpGet("timeline/{userId}")]
        public async Task<IActionResult> GetTimeline(Guid userId)
        {
            if (!IsCurrentUser(userId)) return Forbid();

            var now = DateTime.Now;

            var list = await _context.Activities
                .Where(a => a.UserId == userId && a.StartTime != null)
                .OrderBy(a => a.StartTime)
                .ToListAsync();

            var previous = list.LastOrDefault(a => a.EndTime != null && a.EndTime < now);
            var current = list.FirstOrDefault(a => a.StartTime <= now && (a.EndTime == null || a.EndTime >= now));
            var next = list.FirstOrDefault(a => a.StartTime > now);

            double? countdownSeconds = next?.StartTime != null ? (next.StartTime.Value - now).TotalSeconds : null;

            return Ok(new
            {
                previous,
                current,
                next,
                countdownSeconds = countdownSeconds > 0 ? countdownSeconds : 0
            });
        }
    }
}
