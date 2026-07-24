using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using back_mylife.Data;
using back_mylife.Models;

namespace back_mylife.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ActivityController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ActivityController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet("{userId}")]
        public async Task<IActionResult> GetActivities(Guid userId)
        {
            var list = await _context.Activities
                .Where(a => a.UserId == userId)
                .OrderBy(a => a.StartTime)
                .ToListAsync();
            return Ok(list);
        }

        [HttpPost]
        public async Task<IActionResult> AddActivity([FromBody] Activity item)
        {
            item.Id = Guid.NewGuid();
            _context.Activities.Add(item);
            await _context.SaveChangesAsync();
            return Ok(item);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateActivity(Guid id, [FromBody] Activity item)
        {
            var existing = await _context.Activities.FindAsync(id);
            if (existing == null) return NotFound();

            existing.Title = item.Title;
            existing.Description = item.Description;
            existing.StartTime = item.StartTime;
            existing.EndTime = item.EndTime;
            existing.IsAllDay = item.IsAllDay;
            existing.IsMultiDay = item.IsMultiDay;
            existing.IsIndefinite = item.IsIndefinite;
            existing.Recurrence = item.Recurrence;
            existing.Location = item.Location;

            await _context.SaveChangesAsync();
            return Ok(existing);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteActivity(Guid id)
        {
            var existing = await _context.Activities.FindAsync(id);
            if (existing == null) return NotFound();

            _context.Activities.Remove(existing);
            await _context.SaveChangesAsync();
            return Ok(new { message = "ลบกิจกรรมสำเร็จ" });
        }

        // Timeline: Previous, Current, Next activity with countdown
        [HttpGet("timeline/{userId}")]
        public async Task<IActionResult> GetTimeline(Guid userId)
        {
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
