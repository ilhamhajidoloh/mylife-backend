using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using back_mylife.Data;
using back_mylife.Models;
using back_mylife.Services;

namespace back_mylife.Controllers
{
    [Route("api/[controller]")]
    public class TodoController : AuthorizedApiController
    {
        private readonly AppDbContext _context;
        private readonly UserEmailNotificationService _emailNotifications;

        public TodoController(AppDbContext context, UserEmailNotificationService emailNotifications)
        {
            _context = context;
            _emailNotifications = emailNotifications;
        }

        [HttpGet("{userId}")]
        public async Task<IActionResult> GetTodos(Guid userId, [FromQuery] string? tag, [FromQuery] int? year, [FromQuery] int? month, [FromQuery] int? day)
        {
            if (!IsCurrentUser(userId)) return Forbid();

            var query = _context.TodoItems.Where(t => t.UserId == userId);

            if (!string.IsNullOrEmpty(tag))
            {
                query = query.Where(t => t.Tag == tag);
            }

            DateTime? selectedDate = null;

            if (year != null && month != null && day != null)
            {
                selectedDate = new DateTime(year.Value, month.Value, day.Value);
            }
            else if (year != null && month != null)
            {
                var startOfMonth = new DateTime(year.Value, month.Value, 1);
                var startOfNextMonth = startOfMonth.AddMonths(1);
                query = query.Where(t =>
                    (t.Recurrence == RecurrenceType.None &&
                        t.TargetDate >= startOfMonth && t.TargetDate < startOfNextMonth) ||
                    (t.Recurrence != RecurrenceType.None && t.TargetDate < startOfNextMonth)
                );
            }
            else if (year != null)
            {
                var startOfYear = new DateTime(year.Value, 1, 1);
                var startOfNextYear = startOfYear.AddYears(1);
                query = query.Where(t =>
                    (t.Recurrence == RecurrenceType.None &&
                        t.TargetDate >= startOfYear && t.TargetDate < startOfNextYear) ||
                    (t.Recurrence != RecurrenceType.None && t.TargetDate < startOfNextYear)
                );
            }

            var list = selectedDate.HasValue
                ? await GetTodosForDateAsync(query, selectedDate.Value)
                : await query.AsNoTracking().OrderBy(t => t.TargetDate).ToListAsync();
            if (selectedDate.HasValue)
            {
                await ApplyCompletionForDate(list, selectedDate.Value);
            }
            return Ok(list);
        }

        [HttpPost]
        public async Task<IActionResult> AddTodo([FromBody] TodoItem item)
        {
            item.Id = Guid.NewGuid();
            item.UserId = CurrentUserId;
            _context.TodoItems.Add(item);
            await _context.SaveChangesAsync();
            await _emailNotifications.NotifyAsync(item.UserId, "todo", "MyLife: เพิ่ม To-do ใหม่", item.Title);
            return Ok(item);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateTodo(Guid id, [FromBody] TodoItem item)
        {
            var existing = await _context.TodoItems.FindAsync(id);
            if (existing == null || existing.UserId != CurrentUserId) return NotFound();

            existing.Title = item.Title;
            existing.Description = item.Description;
            existing.TargetDate = item.TargetDate;
            existing.Tag = item.Tag;
            existing.Recurrence = item.Recurrence;
            existing.Status = item.Status;
            existing.Priority = item.Priority;
            // A recurring todo has a separate completion state for each date.
            if (existing.Recurrence == RecurrenceType.None)
            {
                existing.IsCompleted = item.IsCompleted;
            }

            await _context.SaveChangesAsync();
            await _emailNotifications.NotifyAsync(existing.UserId, "todo", "MyLife: อัปเดต To-do", existing.Title);
            return Ok(existing);
        }

        [HttpPut("{id}/reminder-sent")]
        public async Task<IActionResult> MarkReminderSent(Guid id)
        {
            var existing = await _context.TodoItems.FindAsync(id);
            if (existing == null || existing.UserId != CurrentUserId) return NotFound();

            existing.ReminderSentAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return Ok(existing);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTodo(Guid id)
        {
            var existing = await _context.TodoItems.FindAsync(id);
            if (existing == null || existing.UserId != CurrentUserId) return NotFound();

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
            if (todo == null || todo.UserId != CurrentUserId) return NotFound();

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
            if (!IsCurrentUser(userId)) return Forbid();

            var targetDate = (date ?? DateTime.UtcNow).Date;

            var todos = await GetTodosForDateAsync(
                _context.TodoItems.Where(t => t.UserId == userId), targetDate);
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

        private static async Task<List<TodoItem>> GetTodosForDateAsync(
            IQueryable<TodoItem> query,
            DateTime date)
        {
            var targetDate = date.Date;
            var startOfNextDay = targetDate.AddDays(1);

            // Oracle's EF Core provider cannot translate DateTime.Date or DayOfWeek.
            // Apply only SQL-translatable filters first, then evaluate recurrence rules in memory.
            var candidates = await query
                .Where(t =>
                    (t.Recurrence == RecurrenceType.None &&
                        t.TargetDate >= targetDate && t.TargetDate < startOfNextDay) ||
                    (t.Recurrence != RecurrenceType.None &&
                        t.TargetDate < startOfNextDay))
                .AsNoTracking()
                .OrderBy(t => t.TargetDate)
                .ToListAsync();

            return candidates.Where(t => OccursOnDate(t, targetDate)).ToList();
        }

        private static bool OccursOnDate(TodoItem todo, DateTime targetDate)
        {
            var todoDate = todo.TargetDate.Date;

            return todo.Recurrence switch
            {
                RecurrenceType.None => todoDate == targetDate,
                RecurrenceType.Daily => todoDate <= targetDate,
                RecurrenceType.Weekly => todoDate <= targetDate &&
                    todoDate.DayOfWeek == targetDate.DayOfWeek,
                RecurrenceType.Monthly => todoDate <= targetDate &&
                    todoDate.Day == targetDate.Day,
                RecurrenceType.Yearly => todoDate <= targetDate &&
                    todoDate.Month == targetDate.Month && todoDate.Day == targetDate.Day,
                _ => false
            };
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
