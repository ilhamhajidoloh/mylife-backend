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
            { DayOfWeek.Monday, "จันทร์" },
            { DayOfWeek.Tuesday, "อังคาร" },
            { DayOfWeek.Wednesday, "พุธ" },
            { DayOfWeek.Thursday, "พฤหัสบดี" },
            { DayOfWeek.Friday, "ศุกร์" },
            { DayOfWeek.Saturday, "เสาร์" },
            { DayOfWeek.Sunday, "อาทิตย์" }
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
                var thaiTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                var nowUtc = DateTime.UtcNow;
                var nowThai = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, thaiTimeZone);
                var todayThai = nowThai.Date;
                var currentDayOfWeek = nowThai.DayOfWeek;

                _logger.LogInformation($"Checking class reminders at {nowThai:yyyy-MM-dd HH:mm:ss} Thai time");

                // หาผู้ใช้ที่เปิดการแจ้งเตือนคาบเรียน
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

                    // หา Academic Terms ที่ active ในวันนี้
                    var activeTerms = lineConnection.User.AcademicTerms
                        .Where(t => t.StartDate.Date <= todayThai && t.EndDate.Date >= todayThai)
                        .ToList();

                    foreach (var term in activeTerms)
                    {
                        // หาคาบเรียนในวันนี้
                        var todayClasses = term.Courses
                            .Where(c => c.DayOfWeek == currentDayOfWeek)
                            .ToList();

                        foreach (var course in todayClasses)
                        {
                            // คำนวณเวลาที่ต้องแจ้งเตือน
                            var classStartTime = todayThai.Add(course.StartTime);
                            var reminderTime = classStartTime.AddMinutes(-lineConnection.ClassReminderMinutes);

                            // ตรวจสอบว่าถึงเวลาแจ้งเตือนหรือยัง (ภายใน 5 นาทีหลังจากเวลาที่กำหนด)
                            var timeDifference = (nowThai - reminderTime).TotalMinutes;

                            if (timeDifference >= 0 && timeDifference <= 5)
                            {
                                // ตรวจสอบว่าแจ้งเตือนไปแล้วหรือยัง
                                var alreadySent = await _context.ClassRemindersSent
                                    .AnyAsync(crs =>
                                        crs.UserId == lineConnection.UserId &&
                                        crs.CourseId == course.Id &&
                                        crs.ClassDate.Date == todayThai);

                                if (!alreadySent)
                                {
                                    // ส่งการแจ้งเตือน
                                    var sent = await SendClassReminder(
                                        lineConnection.LineUserId,
                                        course,
                                        lineConnection.ClassReminderMinutes
                                    );

                                    if (sent)
                                    {
                                        // บันทึกว่าส่งแล้ว
                                        var reminderSent = new ClassReminderSent
                                        {
                                            UserId = lineConnection.UserId,
                                            CourseId = course.Id,
                                            ClassDate = todayThai,
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

                // ลบ records ที่เก่าเกิน 7 วัน
                var sevenDaysAgo = todayThai.AddDays(-7);
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
                var locationText = !string.IsNullOrEmpty(course.Room) ? $"\n📍 สถานที่: {course.Room}" : "";

                var message = $@"🔔 แจ้งเตือนคาบเรียน

📚 วิชา: {course.CourseName}
📅 วัน{dayLabel}
🕐 เวลา: {course.StartTime:hh\:mm} - {course.EndTime:hh\:mm} น.{locationText}

⏰ คาบเรียนจะเริ่มในอีก {minutesBefore} นาที";

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
