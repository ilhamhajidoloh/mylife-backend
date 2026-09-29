using back_mylife.Data;
using Microsoft.EntityFrameworkCore;

namespace back_mylife.Services;

public class UserEmailNotificationService(AppDbContext context, EmailSenderService sender, ILogger<UserEmailNotificationService> logger)
{
    public async Task NotifyAsync(Guid userId, string type, string title, string body)
    {
        if (!sender.IsConfigured) return;
        try
        {
            var user = await context.Users.Include(u => u.EmailNotificationPreference).AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            var preference = user?.EmailNotificationPreference;
            if (user == null || preference?.Enabled == false) return;
            var allowed = type switch
            {
                "event" => preference?.EventRemindersEnabled ?? true,
                "task" => preference?.TaskRemindersEnabled ?? true,
                _ => true,
            };
            if (!allowed) return;
            await sender.SendAsync(preference?.RecipientEmail ?? user.Email, title, body);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Email notification failed for {UserId}", userId); }
    }
}
