using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using back_mylife.Data;
using back_mylife.Models;

namespace back_mylife.Controllers
{
    [Route("api/[controller]")]
    public class TaskController : AuthorizedApiController
    {
        private readonly AppDbContext _context;

        public TaskController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet("{userId}")]
        public async Task<IActionResult> GetTasks(Guid userId)
        {
            if (!IsCurrentUser(userId)) return Forbid();

            var list = await _context.Assignments
                .Where(a => a.UserId == userId)
                .OrderBy(a => a.Deadline)
                .ToListAsync();
            return Ok(list);
        }

        [HttpPost]
        public async Task<IActionResult> AddTask([FromBody] Assignment item)
        {
            item.Id = Guid.NewGuid();
            item.UserId = CurrentUserId;
            _context.Assignments.Add(item);
            await _context.SaveChangesAsync();
            return Ok(item);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateTask(Guid id, [FromBody] Assignment item)
        {
            var existing = await _context.Assignments.FindAsync(id);
            if (existing == null || existing.UserId != CurrentUserId) return NotFound();

            existing.Title = item.Title;
            existing.Subject = item.Subject;
            existing.Deadline = item.Deadline;
            existing.IsUrgent = item.IsUrgent;
            existing.IsCompleted = item.IsCompleted;

            await _context.SaveChangesAsync();
            return Ok(existing);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTask(Guid id)
        {
            var existing = await _context.Assignments.FindAsync(id);
            if (existing == null || existing.UserId != CurrentUserId) return NotFound();

            _context.Assignments.Remove(existing);
            await _context.SaveChangesAsync();
            return Ok(new { message = "ลบงานสำเร็จ" });
        }

        [HttpGet("urgent/{userId}")]
        public async Task<IActionResult> GetUrgentTasks(Guid userId)
        {
            if (!IsCurrentUser(userId)) return Forbid();

            var now = DateTime.UtcNow;
            var upcoming3Days = now.AddDays(3);

            var urgentList = await _context.Assignments
                .Where(a => a.UserId == userId && !a.IsCompleted && (a.IsUrgent || (a.Deadline >= now && a.Deadline <= upcoming3Days)))
                .OrderBy(a => a.Deadline)
                .ToListAsync();

            return Ok(urgentList);
        }
    }
}
