using Microsoft.EntityFrameworkCore;
using back_mylife.Data;
using back_mylife.Models;
using System.Globalization;

namespace back_mylife.Services
{
    public class ClassReminderService
    {
        private readonly AppDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ClassReminderService> _logger;

        private static readonly Dictionary<DayOfWeek, string> DayOfWeekThai = new()
        {
            { DayOfWeek.Monday, "เธเธฑเธเธ—เธฃเน" },
            { DayOfWeek.Tuesday, "เธญเธฑเธเธเธฒเธฃ" },
            { DayOfWeek.Wednesday, "เธเธธเธ" },
            { DayOfWeek.Thursday, "เธเธคเธซเธฑเธชเธเธ”เธต" },
            { DayOfWeek.Friday, "เธจเธธเธเธฃเน" },
            { DayOfWeek.Saturday, "เน€เธชเธฒเธฃเน" },
            { DayOfWeek.Sunday, "เธญเธฒเธ—เธดเธ•เธขเน" }
        };

        public ClassReminderService(
            AppDbContext context,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<ClassReminderService> logger)
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
                var todayThai = nowThai.Date;
                var todayThaiUtc = DateTime.SpecifyKind(todayThai, DateTimeKind.Utc);
                var currentDayOfWeek = nowThai.DayOfWeek;

                _logger.LogInformation($"Checking class reminders at {nowThai:yyyy-MM-dd HH:mm:ss} Thai time");

                // เธซเธฒเธเธนเนเนเธเนเธ—เธตเนเน€เธเธดเธ”เธเธฒเธฃเนเธเนเธเน€เธ•เธทเธญเธเธเธฒเธเน€เธฃเธตเธขเธ
                var usersWithReminders = await _context.LineConnections
                    .Where(lc => lc.ClassRemindersEnabled)
                    .Include(lc => lc.User)
                        .ThenInclude(u => u.AcademicTerms)
                            .ThenInclude(t => t.Courses)
                    .ToListAsync();

                _logger.LogInformation($"Found {usersWithReminders.Count} users with class reminders enabled");

                foreach (var lineConnection in usersWithReminders)
                {
                    if (lineConnection.User == null) continue;

                    // เธซเธฒ Academic Terms เธ—เธตเน active เนเธเธงเธฑเธเธเธตเน
                    var activeTerms = lineConnection.User.AcademicTerms
                        .Where(t => t.StartDate.Date <= todayThai && t.EndDate.Date >= todayThai)
                        .ToList();

                    foreach (var term in activeTerms)
                    {
                        // เธซเธฒเธเธฒเธเน€เธฃเธตเธขเธเนเธเธงเธฑเธเธเธตเน
                        var todayClasses = term.Courses
                            .Where(c => c.DayOfWeek == currentDayOfWeek)
                            .ToList();

                        foreach (var course in todayClasses)
                        {
                            // เธเธณเธเธงเธ“เน€เธงเธฅเธฒเธ—เธตเนเธ•เนเธญเธเนเธเนเธเน€เธ•เธทเธญเธ
                            var classStartTime = todayThai.Add(course.StartTime);
                            var reminderTime = classStartTime.AddMinutes(-lineConnection.ClassReminderMinutes);

                            // เธ•เธฃเธงเธเธชเธญเธเธงเนเธฒเธ–เธถเธเน€เธงเธฅเธฒเนเธเนเธเน€เธ•เธทเธญเธเธซเธฃเธทเธญเธขเธฑเธ (เธ เธฒเธขเนเธ 5 เธเธฒเธ—เธตเธซเธฅเธฑเธเธเธฒเธเน€เธงเธฅเธฒเธ—เธตเนเธเธณเธซเธเธ”)
                            var timeDifference = (nowThai - reminderTime).TotalMinutes;

                            if (timeDifference >= 0 && timeDifference <= 5)
                            {
                                // เธ•เธฃเธงเธเธชเธญเธเธงเนเธฒเนเธเนเธเน€เธ•เธทเธญเธเนเธเนเธฅเนเธงเธซเธฃเธทเธญเธขเธฑเธ
                                var alreadySent = await _context.ClassRemindersSent
                                    .AnyAsync(crs =>
                                        crs.UserId == lineConnection.UserId &&
                                        crs.CourseId == course.Id &&
                                        crs.ClassDate == todayThaiUtc);

                                if (!alreadySent)
                                {
                                    // เธชเนเธเธเธฒเธฃเนเธเนเธเน€เธ•เธทเธญเธ
                                    var sent = await SendClassReminder(
                                        lineConnection.LineUserId,
                                        course,
                                        lineConnection.ClassReminderMinutes
                                    );

                                    if (sent)
                                    {
                                        // เธเธฑเธเธ—เธถเธเธงเนเธฒเธชเนเธเนเธฅเนเธง
                                        var reminderSent = new ClassReminderSent
                                        {
                                            UserId = lineConnection.UserId,
                                            CourseId = course.Id,
                                            ClassDate = todayThaiUtc,
                                            SentAt = nowUtc
                                        };
                                        _context.ClassRemindersSent.Add(reminderSent);
                                        await _context.SaveChangesAsync();

                                        _logger.LogInformation(
                                            $"Sent class reminder to user {lineConnection.UserId} for course {course.CourseName}"
                                        );
                                    }
                                }
                            }
                        }
                    }
                }

                // เธฅเธ records เธ—เธตเนเน€เธเนเธฒเน€เธเธดเธ 7 เธงเธฑเธ
                var sevenDaysAgo = DateTime.SpecifyKind(todayThai.AddDays(-7), DateTimeKind.Utc);
                var oldReminders = await _context.ClassRemindersSent
                    .Where(crs => crs.ClassDate < sevenDaysAgo)
                    .ToListAsync();

                if (oldReminders.Any())
                {
                    _context.ClassRemindersSent.RemoveRange(oldReminders);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation($"Cleaned up {oldReminders.Count} old reminder records");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CheckAndSendReminders");
            }
        }

        private async Task<bool> SendClassReminder(string lineUserId, Course course, int minutesBefore)
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

                var dayLabel = DayOfWeekThai.GetValueOrDefault(course.DayOfWeek, course.DayOfWeek.ToString());
                var locationText = !string.IsNullOrEmpty(course.Room) ? $"\n๐“ เธชเธ–เธฒเธเธ—เธตเน: {course.Room}" : "";

                var message = $@"๐”” เนเธเนเธเน€เธ•เธทเธญเธเธเธฒเธเน€เธฃเธตเธขเธ

๐“ เธงเธดเธเธฒ: {course.CourseName}
๐“… เธงเธฑเธ{dayLabel}
๐• เน€เธงเธฅเธฒ: {course.StartTime:hh\:mm} - {course.EndTime:hh\:mm} เธ.{locationText}

โฐ เธเธฒเธเน€เธฃเธตเธขเธเธเธฐเน€เธฃเธดเนเธกเนเธเธญเธตเธ {minutesBefore} เธเธฒเธ—เธต";

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
                _logger.LogError(ex, $"Error sending class reminder to LINE user {lineUserId}");
                return false;
            }
        }
    }
}

