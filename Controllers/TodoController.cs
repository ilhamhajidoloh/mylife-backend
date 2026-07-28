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
        public async Task<IActionResult> GetTodos(Guid userId, [FromQuery] string? tag, [FromQuery] int? year, [FromQuery] int? month, [FromQuery] int? day)
        {
            var query = _context.TodoItems.Where(t => t.UserId == userId);

            if (!string.IsNullOrEmpty(tag))
            {
                query = query.Where(t => t.Tag == tag);
            }

            if (year != null && month != null && day != null)
            {
                query = FilterForDate(query, new DateTime(year.Value, month.Value, day.Value));
            }
            else if (year != null && month != null)
            {
                query = query.Where(t => t.TargetDate.Year == year && t.TargetDate.Month == month);
            }
            else if (year != null)
            {
                query = query.Where(t => t.TargetDate.Year == year);
            }

            var list = await query.AsNoTracking().OrderBy(t => t.TargetDate).ToListAsync();
            if (year != null && month != null && day != null)
            {
                await ApplyCompletionForDate(list, new DateTime(year.Value, month.Value, day.Value));
            }
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
            existing.Recurrence = item.Recurrence;
            // A recurring todo has a separate completion state for each date.
            if (existing.Recurrence == RecurrenceType.None)
            {
                existing.IsCompleted = item.IsCompleted;
            }

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

        [HttpPut("{id}/completion")]
        public async Task<IActionResult> UpdateCompletion(
            Guid id,
            [FromBody] TodoCompletionUpdate update)
        {
            var todo = await _context.TodoItems.FindAsync(id);
            if (todo == null) return NotFound();

            if (todo.Recurrence == RecurrenceType.None)
            {
                todo.IsCompleted = update.IsCompleted;
            }
            else
            {
                var completionDate = update.Date.Date;
                var completion = await _context.TodoCompletions.SingleOrDefaultAsync(c =>
                    c.TodoItemId == id && c.CompletedDate == completionDate);

                if (completion == null)
                {
                    _context.TodoCompletions.Add(new TodoCompletion
                    {
                        TodoItemId = id,
                        CompletedDate = completionDate,
                        IsCompleted = update.IsCompleted,
                    });
                }
                else
                {
                    completion.IsCompleted = update.IsCompleted;
                }
            }

            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpGet("daily-completion/{userId}")]
        public async Task<IActionResult> GetDailyCompletion(Guid userId, [FromQuery] DateTime? date)
        {
            var targetDate = (date ?? DateTime.UtcNow).Date;

            var todos = await FilterForDate(
                    _context.TodoItems.Where(t => t.UserId == userId),
                    targetDate)
                .AsNoTracking()
                .ToListAsync();
            await ApplyCompletionForDate(todos, targetDate);

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

        private static IQueryable<TodoItem> FilterForDate(
            IQueryable<TodoItem> query,
            DateTime date)
        {
            var targetDate = date.Date;
            var dayOfWeek = targetDate.DayOfWeek;

            return query.Where(t =>
                (t.Recurrence == RecurrenceType.None &&
                    t.TargetDate.Date == targetDate) ||
                (t.Recurrence == RecurrenceType.Daily &&
                    t.TargetDate.Date <= targetDate) ||
                (t.Recurrence == RecurrenceType.Weekly &&
                    t.TargetDate.Date <= targetDate &&
                    t.TargetDate.DayOfWeek == dayOfWeek) ||
                (t.Recurrence == RecurrenceType.Monthly &&
                    t.TargetDate.Date <= targetDate &&
                    t.TargetDate.Day == targetDate.Day) ||
                (t.Recurrence == RecurrenceType.Yearly &&
                    t.TargetDate.Date <= targetDate &&
                    t.TargetDate.Month == targetDate.Month &&
                    t.TargetDate.Day == targetDate.Day));
        }

        private async Task ApplyCompletionForDate(
            List<TodoItem> todos,
            DateTime date)
        {
            var recurringIds = todos
                .Where(t => t.Recurrence != RecurrenceType.None)
                .Select(t => t.Id)
                .ToList();
            if (recurringIds.Count == 0) return;

            var completions = await _context.TodoCompletions
                .Where(c => recurringIds.Contains(c.TodoItemId) &&
                    c.CompletedDate == date.Date)
                .ToDictionaryAsync(c => c.TodoItemId, c => c.IsCompleted);

            foreach (var todo in todos.Where(t => t.Recurrence != RecurrenceType.None))
            {
                todo.IsCompleted = completions.GetValueOrDefault(todo.Id, false);
            }
        }
    }
}
