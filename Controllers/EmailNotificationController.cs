using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using back_mylife.Data;
using back_mylife.Models;

namespace back_mylife.Controllers
{
    [Route("api/[controller]")]
    public class EmailNotificationController : AuthorizedApiController
    {
        private readonly AppDbContext _context;

        public EmailNotificationController(AppDbContext context)
        {
            _context = context;
        }

        public record EmailNotificationPreferencesDto(
            bool Enabled,
            string? RecipientEmail,
            bool ClassReminders,
            int ClassReminderMinutes,
            bool EventReminders,
            bool TaskReminders,
            bool BillReminders);

        public record ReminderUserDto(
            Guid UserId,
            string Email,
            string? LineUserId,
            bool LineNotificationsEnabled,
            bool LineClassRemindersEnabled,
            int LineClassReminderMinutes,
            bool EmailNotificationsEnabled,
            string? EmailRecipientEmail,
            bool EmailClassRemindersEnabled,
            int EmailClassReminderMinutes,
            bool EmailEventRemindersEnabled,
            bool EmailTaskRemindersEnabled,
            bool EmailBillRemindersEnabled);

        private static EmailNotificationPreferencesDto ToDto(EmailNotificationPreference preference)
        {
            return new EmailNotificationPreferencesDto(
                preference.Enabled,
                preference.RecipientEmail,
                preference.ClassRemindersEnabled,
                preference.ClassReminderMinutes,
                preference.EventRemindersEnabled,
                preference.TaskRemindersEnabled,
                preference.BillRemindersEnabled);
        }

        private static EmailNotificationPreferencesDto DefaultDto(User user)
        {
            return new EmailNotificationPreferencesDto(
                true,
                user.Email,
                true,
                15,
                true,
                true,
                true);
        }

        private static int NormalizeReminderMinutes(int minutes)
        {
            return Math.Clamp(minutes <= 0 ? 15 : minutes, 1, 10080);
        }

        [HttpGet("{userId}/preferences")]
        public async Task<IActionResult> GetPreferences(Guid userId)
        {
            if (!IsCurrentUser(userId)) return Forbid();

            var user = await _context.Users
                .Include(u => u.EmailNotificationPreference)
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return NotFound();

            return Ok(user.EmailNotificationPreference == null
                ? DefaultDto(user)
                : ToDto(user.EmailNotificationPreference));
        }

        [HttpPut("{userId}/preferences")]
        public async Task<IActionResult> UpdatePreferences(Guid userId, [FromBody] EmailNotificationPreferencesDto dto)
        {
            if (!IsCurrentUser(userId)) return Forbid();

            var user = await _context.Users.FindAsync(userId);
            if (user == null) return NotFound();

            var preference = await _context.EmailNotificationPreferences
                .FirstOrDefaultAsync(p => p.UserId == userId);

            if (preference == null)
            {
                preference = new EmailNotificationPreference
                {
                    UserId = userId,
                    CreatedAt = DateTime.UtcNow,
                };
                _context.EmailNotificationPreferences.Add(preference);
            }

            var recipientEmail = dto.RecipientEmail?.Trim();
            preference.Enabled = dto.Enabled;
            preference.RecipientEmail = string.IsNullOrWhiteSpace(recipientEmail) ? null : recipientEmail;
            preference.ClassRemindersEnabled = dto.ClassReminders;
            preference.ClassReminderMinutes = NormalizeReminderMinutes(dto.ClassReminderMinutes);
            preference.EventRemindersEnabled = dto.EventReminders;
            preference.TaskRemindersEnabled = dto.TaskReminders;
            preference.BillRemindersEnabled = dto.BillReminders;
            preference.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(ToDto(preference));
        }

        [HttpGet("reminder-users")]
        [AllowAnonymous]
        [RequireServiceKey]
        public async Task<IActionResult> GetReminderUsers()
        {
            var users = await _context.Users
                .Include(u => u.LineConnection)
                .Include(u => u.EmailNotificationPreference)
                .AsNoTracking()
                .Where(u =>
                    (u.LineConnection != null &&
                        (u.LineConnection.NotificationsEnabled || u.LineConnection.ClassRemindersEnabled)) ||
                    (u.EmailNotificationPreference != null &&
                        u.EmailNotificationPreference.Enabled &&
                        (u.EmailNotificationPreference.EventRemindersEnabled ||
                         u.EmailNotificationPreference.TaskRemindersEnabled ||
                         u.EmailNotificationPreference.ClassRemindersEnabled ||
                         u.EmailNotificationPreference.BillRemindersEnabled)))
                .Select(u => new ReminderUserDto(
                    u.Id,
                    u.Email,
                    u.LineConnection == null ? null : u.LineConnection.LineUserId,
                    u.LineConnection != null && u.LineConnection.NotificationsEnabled,
                    u.LineConnection != null && u.LineConnection.ClassRemindersEnabled,
                    u.LineConnection == null ? 15 : u.LineConnection.ClassReminderMinutes,
                    u.EmailNotificationPreference != null && u.EmailNotificationPreference.Enabled,
                    u.EmailNotificationPreference == null ? null : u.EmailNotificationPreference.RecipientEmail,
                    u.EmailNotificationPreference != null && u.EmailNotificationPreference.ClassRemindersEnabled,
                    u.EmailNotificationPreference == null ? 15 : u.EmailNotificationPreference.ClassReminderMinutes,
                    u.EmailNotificationPreference != null && u.EmailNotificationPreference.EventRemindersEnabled,
                    u.EmailNotificationPreference != null && u.EmailNotificationPreference.TaskRemindersEnabled,
                    u.EmailNotificationPreference != null && u.EmailNotificationPreference.BillRemindersEnabled))
                .ToListAsync();

            return Ok(users);
        }
    }
}
