using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using back_mylife.Data;
using back_mylife.Models;

namespace back_mylife.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TodoController : ControllerBase
    {
        private readonly AppDbContext _context;

        public TodoController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet("{userId}")]
        public async Task<IActionResult> GetTodos(Guid userId, [FromQuery] string? filterType, [FromQuery] string? tag)
        {
            var query = _context.TodoItems.Where(t => t.UserId == userId);

            if (!string.IsNullOrEmpty(tag))
            {
                query = query.Where(t => t.Tag == tag);
            }

            var list = await query.OrderBy(t => t.TargetDate).ToListAsync();
            return Ok(list);
        }

        [HttpPost]
        public async Task<IActionResult> AddTodo([FromBody] TodoItem item)
        {
            item.Id = Guid.NewGuid();
            _context.TodoItems.Add(item);
            await _context.SaveChangesAsync();
            return Ok(item);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateTodo(Guid id, [FromBody] TodoItem item)
        {
            var existing = await _context.TodoItems.FindAsync(id);
            if (existing == null) return NotFound();

            existing.Title = item.Title;
            existing.TargetDate = item.TargetDate;
            existing.Tag = item.Tag;
            existing.IsCompleted = item.IsCompleted;

            await _context.SaveChangesAsync();
            return Ok(existing);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTodo(Guid id)
        {
            var existing = await _context.TodoItems.FindAsync(id);
            if (existing == null) return NotFound();

            _context.TodoItems.Remove(existing);
            await _context.SaveChangesAsync();
            return Ok(new { message = "ลบรายการ Todolist สำเร็จ" });
        }

        [HttpGet("daily-completion/{userId}")]
        public async Task<IActionResult> GetDailyCompletion(Guid userId, [FromQuery] DateTime? date)
        {
            var targetDate = (date ?? DateTime.UtcNow).Date;

            var todos = await _context.TodoItems
                .Where(t => t.UserId == userId && t.TargetDate.Date == targetDate)
                .ToListAsync();

            int total = todos.Count;
            int completed = todos.Count(t => t.IsCompleted);
            double percentage = total > 0 ? (double)completed / total * 100.0 : 0.0;

            return Ok(new
            {
                date = targetDate,
                total,
                completed,
                percentage = Math.Round(percentage, 1),
                todos
            });
        }
    }
}
