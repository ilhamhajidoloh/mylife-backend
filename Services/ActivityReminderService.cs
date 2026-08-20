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
                                TimeZoneInfo thaiTimeZone;
                try
                {
                    thaiTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                }
                catch
                {
                    thaiTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok");
                }
                var nowUtc = DateTime.UtcNow;
                var nowThai = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, thaiTimeZone);

                _logger.LogInformation($"Checking activity reminders at {nowThai:yyyy-MM-dd HH:mm:ss} Thai time");

                // เธซเธฒเธเธนเนเนเธเนเธ—เธตเนเน€เธเธดเธ”เธเธฒเธฃเนเธเนเธเน€เธ•เธทเธญเธ LINE
                var usersWithNotifications = await _context.LineConnections
                    .Where(lc => lc.NotificationsEnabled)
                    .Include(lc => lc.User)
                        .ThenInclude(u => u.Activities)
                    .ToListAsync();

                _logger.LogInformation($"Found {usersWithNotifications.Count} users with LINE notifications enabled");

                foreach (var lineConnection in usersWithNotifications)
                {
                    if (lineConnection.User == null) continue;

                    // เธซเธฒเธเธดเธเธเธฃเธฃเธกเธ—เธตเนเธกเธต ReminderMinutes เนเธฅเธฐเธขเธฑเธเนเธกเนเนเธ”เนเธชเนเธเนเธเนเธเน€เธ•เธทเธญเธ
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

                        // เนเธเธฅเธเน€เธงเธฅเธฒเธเธฒเธ UTC เน€เธเนเธ Thai time
                        var activityStartThai = TimeZoneInfo.ConvertTimeFromUtc(activity.StartTime.Value, thaiTimeZone);
                        var reminderTime = activityStartThai.AddMinutes(-activity.ReminderMinutes.Value);

                        // เธ•เธฃเธงเธเธชเธญเธเธงเนเธฒเธ–เธถเธเน€เธงเธฅเธฒเนเธเนเธเน€เธ•เธทเธญเธเธซเธฃเธทเธญเธขเธฑเธ (เธ เธฒเธขเนเธ 5 เธเธฒเธ—เธตเธซเธฅเธฑเธเธเธฒเธเน€เธงเธฅเธฒเธ—เธตเนเธเธณเธซเธเธ”)
                        var timeDifference = (nowThai - reminderTime).TotalMinutes;

                        if (timeDifference >= 0 && timeDifference <= 5)
                        {
                            // เธชเนเธเธเธฒเธฃเนเธเนเธเน€เธ•เธทเธญเธ
                            var sent = await SendActivityReminder(
                                lineConnection.LineUserId,
                                activity,
                                activity.ReminderMinutes.Value
                            );

                            if (sent)
                            {
                                // เธญเธฑเธเน€เธ”เธ•เธงเนเธฒเธชเนเธเนเธฅเนเธง
                                activity.ReminderSentAt = nowUtc;
                                await _context.SaveChangesAsync();

                                _logger.LogInformation(
                                    $"Sent activity reminder to user {lineConnection.UserId} for activity {activity.Title}"
                                );
                            }
                        }
                    }
                }

                // Reset ReminderSentAt เธชเธณเธซเธฃเธฑเธเธเธดเธเธเธฃเธฃเธกเธ—เธตเนเธเนเธฒเธเนเธเนเธฅเนเธง (เน€เธเธทเนเธญเธฃเธญเธเธฃเธฑเธ recurring activities เนเธเธญเธเธฒเธเธ•)
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

                                TimeZoneInfo thaiTimeZone;
                try
                {
                    thaiTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                }
                catch
                {
                    thaiTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok");
                }
                var startTimeThai = activity.StartTime.HasValue
                    ? TimeZoneInfo.ConvertTimeFromUtc(activity.StartTime.Value, thaiTimeZone)
                    : DateTime.MinValue;

                var endTimeThai = activity.EndTime.HasValue
                    ? TimeZoneInfo.ConvertTimeFromUtc(activity.EndTime.Value, thaiTimeZone)
                    : DateTime.MinValue;

                string timeText;
                if (activity.IsAllDay)
                {
                    timeText = $"{startTimeThai:dd/MM/yyyy} (เธ•เธฅเธญเธ”เธงเธฑเธ)";
                }
                else if (activity.IsMultiDay)
                {
                    timeText = $"{startTimeThai:dd/MM/yyyy HH:mm} เธ–เธถเธ {endTimeThai:dd/MM/yyyy HH:mm}";
                }
                else
                {
                    timeText = $"{startTimeThai:dd/MM/yyyy HH:mm}";
                    if (activity.EndTime.HasValue)
                    {
                        timeText += $" - {endTimeThai:HH:mm}";
                    }
                }

                var locationText = !string.IsNullOrEmpty(activity.Location) ? $"\n๐“ เธชเธ–เธฒเธเธ—เธตเน: {activity.Location}" : "";

                var message = $@"๐”” เนเธเนเธเน€เธ•เธทเธญเธเธเธดเธเธเธฃเธฃเธก

๐“… {activity.Title}
๐• {timeText}{locationText}

โฐ เธเธดเธเธเธฃเธฃเธกเธเธฐเน€เธฃเธดเนเธกเนเธเธญเธตเธ {minutesBefore} เธเธฒเธ—เธต";

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

