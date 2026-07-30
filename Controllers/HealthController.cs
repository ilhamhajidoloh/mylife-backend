using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using back_mylife.Data;
using back_mylife.Models;

namespace back_mylife.Controllers
{
    [Route("api/[controller]")]
    public class HealthController : AuthorizedApiController
    {
        private readonly AppDbContext _context;

        public HealthController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet("{userId}")]
        public async Task<IActionResult> GetHealthLogs(Guid userId, [FromQuery] int days = 7)
        {
            if (!IsCurrentUser(userId)) return Forbid();

            var startDate = DateTime.UtcNow.AddDays(-days);

            var logs = await _context.HealthLogs
                .Where(h => h.UserId == userId && h.RecordedAt >= startDate)
                .OrderBy(h => h.RecordedAt)
                .ToListAsync();

            return Ok(logs);
        }

        [HttpPost]
        public async Task<IActionResult> LogHealthData([FromBody] HealthLog log)
        {
            log.Id = Guid.NewGuid();
            log.UserId = CurrentUserId;
            log.RecordedAt = log.RecordedAt == default ? DateTime.UtcNow : log.RecordedAt;

            _context.HealthLogs.Add(log);
            await _context.SaveChangesAsync();

            return Ok(log);
        }

        // Summary Graph Data: Daily steps & avg heart rate for past 7 days
        [HttpGet("chart-data/{userId}")]
        public async Task<IActionResult> GetChartData(Guid userId)
        {
            if (!IsCurrentUser(userId)) return Forbid();

            var startDate = DateTime.UtcNow.Date.AddDays(-6);

            var logs = await _context.HealthLogs
                .Where(h => h.UserId == userId && h.RecordedAt >= startDate)
                .ToListAsync();

            var dailyData = Enumerable.Range(0, 7).Select(offset =>
            {
                var date = startDate.AddDays(offset);
                var dayLogs = logs.Where(l => l.RecordedAt.Date == date).ToList();

                return new
                {
                    date = date.ToString("yyyy-MM-dd"),
                    dayOfWeek = date.ToString("ddd"),
                    steps = dayLogs.Sum(l => l.StepCount),
                    avgHeartRate = dayLogs.Any() ? (int)dayLogs.Average(l => l.HeartRate) : 0
                };
            });

            return Ok(dailyData);
        }
    }
}
