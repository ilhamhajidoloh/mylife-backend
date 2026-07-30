using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using back_mylife.Data;
using back_mylife.Models;

namespace back_mylife.Controllers
{
    [Route("api/[controller]")]
    public class NotificationController : AuthorizedApiController
    {
        private readonly AppDbContext _context;

        public NotificationController(AppDbContext context)
        {
            _context = context;
        }

        public record NotificationItem(string Type, string Title, string Message, DateTime? OccursAt, string Severity);

        private static DateTime GetThaiNow()
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "SE Asia Standard Time" : "Asia/Bangkok");
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            }
            catch
            {
                return DateTime.UtcNow.AddHours(7);
            }
        }

        // รวมรายการแจ้งเตือนที่ควรแสดงตอนนี้ในจุดเดียว: คาบเรียนถัดไป, กิจกรรมถัดไป,
        // งานใกล้ deadline/เร่งด่วน, และเงินคงเหลือใกล้หมด (< 0.5% ของรายรับรวม)
        [HttpGet("{userId}")]
        public async Task<IActionResult> GetNotifications(Guid userId)
        {
            if (!IsCurrentUser(userId)) return Forbid();

            var items = new List<NotificationItem>();

            // 1. เงินคงเหลือใกล้หมด
            var transactions = await _context.FinanceTransactions
                .Where(t => t.UserId == userId)
                .ToListAsync();
            if (transactions.Count > 0)
            {
                var totalIncome = transactions.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount);
                var totalExpense = transactions.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount);
                var netBalance = totalIncome - totalExpense;
                if (totalIncome > 0 && netBalance < totalIncome * 0.005m)
                {
                    items.Add(new NotificationItem(
                        "balance",
                        "เงินคงเหลือใกล้หมด",
                        $"เหลือ ฿{netBalance:N2} จากรายรับทั้งหมด ฿{totalIncome:N2}",
                        null,
                        "high"));
                }
            }

            // 2. คาบเรียนถัดไป (วันนี้) — ใช้เวลาไทยเช่นเดียวกับ ScheduleController.GetTodayClasses
            var thaiNow = GetThaiNow();
            var nowDate = thaiNow.Date;
            var coursesToday = await _context.Courses
                .Include(c => c.Term)
                .Where(c => c.Term!.UserId == userId
                         && c.DayOfWeek == thaiNow.DayOfWeek
                         && c.Term.StartDate.Date <= nowDate
                         && c.Term.EndDate.Date >= nowDate)
                .OrderBy(c => c.StartTime)
                .ToListAsync();
            var nextCourse = coursesToday.FirstOrDefault(c => c.StartTime > thaiNow.TimeOfDay);
            if (nextCourse != null)
            {
                items.Add(new NotificationItem(
                    "class",
                    "คาบเรียนถัดไป",
                    $"{nextCourse.CourseName} เวลา {nextCourse.StartTime:hh\\:mm}" + (string.IsNullOrEmpty(nextCourse.Room) ? "" : $" ห้อง {nextCourse.Room}"),
                    nowDate.Add(nextCourse.StartTime),
                    "info"));
            }

            // 3. กิจกรรมถัดไป — ใช้ DateTime.Now เช่นเดียวกับ ActivityController.GetTimeline
            var localNow = DateTime.Now;
            var nextActivity = await _context.Activities
                .Where(a => a.UserId == userId && a.StartTime != null && a.StartTime > localNow)
                .OrderBy(a => a.StartTime)
                .FirstOrDefaultAsync();
            if (nextActivity != null)
            {
                items.Add(new NotificationItem(
                    "activity",
                    "กิจกรรมถัดไป",
                    $"{nextActivity.Title} — {nextActivity.StartTime:dd/MM/yyyy HH:mm}",
                    nextActivity.StartTime,
                    "info"));
            }

            // 4. งานใกล้ deadline / เร่งด่วน — เงื่อนไขเดียวกับ TaskController.GetUrgentTasks
            var utcNow = DateTime.UtcNow;
            var upcoming3Days = utcNow.AddDays(3);
            var urgentTasks = await _context.Assignments
                .Where(a => a.UserId == userId && !a.IsCompleted && (a.IsUrgent || (a.Deadline >= utcNow && a.Deadline <= upcoming3Days)))
                .OrderBy(a => a.Deadline)
                .ToListAsync();
            foreach (var task in urgentTasks)
            {
                items.Add(new NotificationItem(
                    "task",
                    "งานใกล้กำหนดส่ง",
                    $"{task.Title} — กำหนดส่ง {task.Deadline:dd/MM/yyyy HH:mm}",
                    task.Deadline,
                    task.IsUrgent ? "high" : "medium"));
            }

            var ordered = items
                .OrderBy(i => i.OccursAt ?? DateTime.MaxValue)
                .ToList();

            return Ok(ordered);
        }
    }
}
