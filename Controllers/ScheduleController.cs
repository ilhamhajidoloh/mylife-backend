using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using back_mylife.Data;
using back_mylife.Models;

namespace back_mylife.Controllers
{
    [Route("api/[controller]")]
    public class ScheduleController : AuthorizedApiController
    {
        private readonly AppDbContext _context;

        public ScheduleController(AppDbContext context)
        {
            _context = context;
        }

        public record ClassReminderSentRequest(DateTime? ClassDate, string? Channel);

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

        private static string NormalizeReminderChannel(string? channel)
        {
            var normalized = channel?.Trim().ToLowerInvariant();
            return normalized == "email" ? "email" : "line";
        }

        // Academic Terms
        [HttpGet("terms/{userId}")]
        public async Task<IActionResult> GetTerms(Guid userId)
        {
            if (!IsCurrentUser(userId)) return Forbid();

            var terms = await _context.AcademicTerms
                .Include(t => t.Courses)
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.StartDate)
                .ToListAsync();
            return Ok(terms);
        }

        [HttpPost("terms")]
        public async Task<IActionResult> AddTerm([FromBody] AcademicTerm term)
        {
            term.Id = Guid.NewGuid();
            term.UserId = CurrentUserId;
            _context.AcademicTerms.Add(term);
            await _context.SaveChangesAsync();
            return Ok(term);
        }

        [HttpPut("terms/{id}")]
        public async Task<IActionResult> UpdateTerm(Guid id, [FromBody] AcademicTerm term)
        {
            var existing = await _context.AcademicTerms.FindAsync(id);
            if (existing == null || existing.UserId != CurrentUserId) return NotFound();

            existing.TermName = term.TermName;
            existing.StartDate = term.StartDate;
            existing.EndDate = term.EndDate;

            await _context.SaveChangesAsync();
            return Ok(existing);
        }

        [HttpDelete("terms/{id}")]
        public async Task<IActionResult> DeleteTerm(Guid id)
        {
            var existing = await _context.AcademicTerms
                .Include(t => t.Courses)
                .FirstOrDefaultAsync(t => t.Id == id);
            if (existing == null || existing.UserId != CurrentUserId) return NotFound();

            _context.Courses.RemoveRange(existing.Courses);
            _context.AcademicTerms.Remove(existing);
            await _context.SaveChangesAsync();
            return Ok(new { message = "ลบภาคเรียนสำเร็จ" });
        }

        // Courses
        [HttpPost("courses")]
        public async Task<IActionResult> AddCourse([FromBody] Course course)
        {
            var term = await _context.AcademicTerms.FindAsync(course.TermId);
            if (term == null || term.UserId != CurrentUserId) return NotFound();

            course.Id = Guid.NewGuid();
            _context.Courses.Add(course);
            await _context.SaveChangesAsync();
            return Ok(course);
        }

        [HttpPut("courses/{id}")]
        public async Task<IActionResult> UpdateCourse(Guid id, [FromBody] Course course)
        {
            var existing = await _context.Courses
                .Include(c => c.Term)
                .FirstOrDefaultAsync(c => c.Id == id);
            if (existing == null || existing.Term!.UserId != CurrentUserId) return NotFound();

            existing.CourseCode = course.CourseCode;
            existing.CourseName = course.CourseName;
            existing.Room = course.Room;
            existing.Instructor = course.Instructor;
            existing.DayOfWeek = course.DayOfWeek;
            existing.StartTime = course.StartTime;
            existing.EndTime = course.EndTime;
            existing.ColorHex = course.ColorHex;

            await _context.SaveChangesAsync();
            return Ok(existing);
        }

        [HttpDelete("courses/{id}")]
        public async Task<IActionResult> DeleteCourse(Guid id)
        {
            var existing = await _context.Courses
                .Include(c => c.Term)
                .FirstOrDefaultAsync(c => c.Id == id);
            if (existing == null || existing.Term!.UserId != CurrentUserId) return NotFound();

            _context.Courses.Remove(existing);
            await _context.SaveChangesAsync();
            return Ok(new { message = "ลบวิชาเรียนสำเร็จ" });
        }

        // Overview endpoint: Previous, Current, Next class
        [HttpGet("today-classes/{userId}")]
        public async Task<IActionResult> GetTodayClasses(Guid userId)
        {
            if (!IsCurrentUser(userId)) return Forbid();

            var now = GetThaiNow();

            var todayOfWeek = now.DayOfWeek;
            var currentTime = now.TimeOfDay;

            var nowDate = now.Date;
            var courses = await _context.Courses
                .Include(c => c.Term)
                .Where(c => c.Term!.UserId == userId
                         && c.DayOfWeek == todayOfWeek
                         && c.Term.StartDate.Date <= nowDate
                         && c.Term.EndDate.Date >= nowDate)
                .OrderBy(c => c.StartTime)
                .ToListAsync();

            if (!courses.Any())
            {
                courses = await _context.Courses
                    .Include(c => c.Term)
                    .Where(c => c.Term!.UserId == userId && c.DayOfWeek == todayOfWeek)
                    .OrderBy(c => c.StartTime)
                    .ToListAsync();
            }

            var activeTerm = await _context.AcademicTerms
                .Where(t => t.UserId == userId && t.StartDate.Date <= nowDate && t.EndDate.Date >= nowDate)
                .FirstOrDefaultAsync()
                ?? await _context.AcademicTerms
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.StartDate)
                .FirstOrDefaultAsync();

            var termName = activeTerm?.TermName ?? courses.FirstOrDefault()?.Term?.TermName;

            var previousCourse = courses.LastOrDefault(c => c.EndTime <= currentTime);
            var currentCourse = courses.FirstOrDefault(c => c.StartTime <= currentTime && c.EndTime >= currentTime);
            var nextCourse = courses.FirstOrDefault(c => c.StartTime > currentTime);

            return Ok(new
            {
                allToday = courses,
                previous = previousCourse,
                current = currentCourse,
                next = nextCourse,
                termName = termName
            });
        }

        [HttpGet("courses/{id}/reminder-sent")]
        public async Task<IActionResult> GetClassReminderSent(Guid id, [FromQuery] DateTime? classDate, [FromQuery] string? channel)
        {
            var course = await _context.Courses
                .Include(c => c.Term)
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id);
            if (course == null || course.Term!.UserId != CurrentUserId) return NotFound();

            var targetDate = (classDate ?? GetThaiNow()).Date;
            var classDateKey = DateTime.SpecifyKind(targetDate, DateTimeKind.Utc);
            var channelKey = NormalizeReminderChannel(channel);
            var sent = await _context.ClassRemindersSent.AnyAsync(r =>
                r.UserId == CurrentUserId &&
                r.CourseId == id &&
                r.ClassDate == classDateKey &&
                r.Channel == channelKey);

            return Ok(new { sent });
        }

        [HttpPut("courses/{id}/reminder-sent")]
        public async Task<IActionResult> MarkClassReminderSent(Guid id, [FromBody] ClassReminderSentRequest? request)
        {
            var course = await _context.Courses
                .Include(c => c.Term)
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id);
            if (course == null || course.Term!.UserId != CurrentUserId) return NotFound();

            var targetDate = (request?.ClassDate ?? GetThaiNow()).Date;
            var classDateKey = DateTime.SpecifyKind(targetDate, DateTimeKind.Utc);
            var channelKey = NormalizeReminderChannel(request?.Channel);
            var alreadySent = await _context.ClassRemindersSent.AnyAsync(r =>
                r.UserId == CurrentUserId &&
                r.CourseId == id &&
                r.ClassDate == classDateKey &&
                r.Channel == channelKey);

            if (!alreadySent)
            {
                _context.ClassRemindersSent.Add(new ClassReminderSent
                {
                    UserId = CurrentUserId,
                    CourseId = id,
                    ClassDate = classDateKey,
                    Channel = channelKey,
                    SentAt = DateTime.UtcNow,
                });
                await _context.SaveChangesAsync();
            }

            return Ok(new { sent = true });
        }
    }
}
