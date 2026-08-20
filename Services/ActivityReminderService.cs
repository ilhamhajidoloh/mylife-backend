using Microsoft.EntityFrameworkCore;
using back_mylife.Data;
using back_mylife.Models;

namespace back_mylife.Services
{
    public class ActivityReminderService
    {
        private readonly AppDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ActivityReminderService> _logger;

        public ActivityReminderService(
            AppDbContext context,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<ActivityReminderService> logger)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task CheckAndSendReminders()
        {
            try
            {
                var thaiTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                var nowUtc = DateTime.UtcNow;
                var nowThai = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, thaiTimeZone);

                _logger.LogInformation($"Checking activity reminders at {nowThai:yyyy-MM-dd HH:mm:ss} Thai time");

                // หาผู้ใช้ที่เปิดการแจ้งเตือน LINE
                var usersWithNotifications = await _context.LineConnections
                    .Where(lc => lc.NotificationsEnabled)
                    .Include(lc => lc.User)
                        .ThenInclude(u => u.Activities)
                    .ToListAsync();

                _logger.LogInformation($"Found {usersWithNotifications.Count} users with LINE notifications enabled");

                foreach (var lineConnection in usersWithNotifications)
                {
                    if (lineConnection.User == null) continue;

                    // หากิจกรรมที่มี ReminderMinutes และยังไม่ได้ส่งแจ้งเตือน
                    var activitiesNeedingReminder = lineConnection.User.Activities
                        .Where(a =>
                            a.ReminderMinutes.HasValue &&
                            a.ReminderMinutes.Value > 0 &&
                            !a.ReminderSentAt.HasValue &&
                            a.StartTime.HasValue &&
                            !a.IsIndefinite)
                        .ToList();

                    foreach (var activity in activitiesNeedingReminder)
                    {
                        if (!activity.StartTime.HasValue) continue;

                        // แปลงเวลาจาก UTC เป็น Thai time
                        var activityStartThai = TimeZoneInfo.ConvertTimeFromUtc(activity.StartTime.Value, thaiTimeZone);
                        var reminderTime = activityStartThai.AddMinutes(-activity.ReminderMinutes.Value);

                        // ตรวจสอบว่าถึงเวลาแจ้งเตือนหรือยัง (ภายใน 5 นาทีหลังจากเวลาที่กำหนด)
                        var timeDifference = (nowThai - reminderTime).TotalMinutes;

                        if (timeDifference >= 0 && timeDifference <= 5)
                        {
                            // ส่งการแจ้งเตือน
                            var sent = await SendActivityReminder(
                                lineConnection.LineUserId,
                                activity,
                                activity.ReminderMinutes.Value
                            );

                            if (sent)
                            {
                                // อัปเดตว่าส่งแล้ว
                                activity.ReminderSentAt = nowUtc;
                                await _context.SaveChangesAsync();

                                _logger.LogInformation(
                                    $"Sent activity reminder to user {lineConnection.UserId} for activity {activity.Title}"
                                );
                            }
                        }
                    }
                }

                // Reset ReminderSentAt สำหรับกิจกรรมที่ผ่านไปแล้ว (เพื่อรองรับ recurring activities ในอนาคต)
                var pastActivities = await _context.Activities
                    .Where(a =>
                        a.ReminderSentAt.HasValue &&
                        a.StartTime.HasValue &&
                        a.StartTime.Value < nowUtc.AddDays(-1))
                    .ToListAsync();

                if (pastActivities.Any())
                {
                    foreach (var activity in pastActivities)
                    {
                        activity.ReminderSentAt = null;
                    }
                    await _context.SaveChangesAsync();
                    _logger.LogInformation($"Reset {pastActivities.Count} past activity reminder flags");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CheckAndSendReminders for activities");
            }
        }

        private async Task<bool> SendActivityReminder(string lineUserId, Activity activity, int minutesBefore)
        {
            try
            {
                var lineMessagingApiUrl = _configuration["LINE_MESSAGING_API_URL"];
                var lineChannelAccessToken = _configuration["LINE_CHANNEL_ACCESS_TOKEN"];

                if (string.IsNullOrEmpty(lineMessagingApiUrl) || string.IsNullOrEmpty(lineChannelAccessToken))
                {
                    _logger.LogWarning("LINE messaging configuration is missing");
                    return false;
                }

                var thaiTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                var startTimeThai = activity.StartTime.HasValue
                    ? TimeZoneInfo.ConvertTimeFromUtc(activity.StartTime.Value, thaiTimeZone)
                    : DateTime.MinValue;

                var endTimeThai = activity.EndTime.HasValue
                    ? TimeZoneInfo.ConvertTimeFromUtc(activity.EndTime.Value, thaiTimeZone)
                    : DateTime.MinValue;

                string timeText;
                if (activity.IsAllDay)
                {
                    timeText = $"{startTimeThai:dd/MM/yyyy} (ตลอดวัน)";
                }
                else if (activity.IsMultiDay)
                {
                    timeText = $"{startTimeThai:dd/MM/yyyy HH:mm} ถึง {endTimeThai:dd/MM/yyyy HH:mm}";
                }
                else
                {
                    timeText = $"{startTimeThai:dd/MM/yyyy HH:mm}";
                    if (activity.EndTime.HasValue)
                    {
                        timeText += $" - {endTimeThai:HH:mm}";
                    }
                }

                var locationText = !string.IsNullOrEmpty(activity.Location) ? $"\n📍 สถานที่: {activity.Location}" : "";

                var message = $@"🔔 แจ้งเตือนกิจกรรม

📅 {activity.Title}
🕐 {timeText}{locationText}

⏰ กิจกรรมจะเริ่มในอีก {minutesBefore} นาที";

                var httpClient = _httpClientFactory.CreateClient();
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {lineChannelAccessToken}");

                var payload = new
                {
                    to = lineUserId,
                    messages = new[]
                    {
                        new
                        {
                            type = "text",
                            text = message
                        }
                    }
                };

                var response = await httpClient.PostAsJsonAsync($"{lineMessagingApiUrl}/message/push", payload);

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning($"Failed to send LINE message. Status: {response.StatusCode}, Error: {errorContent}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending activity reminder to LINE user {lineUserId}");
                return false;
            }
        }
    }
}
