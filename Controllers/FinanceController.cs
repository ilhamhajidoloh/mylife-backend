using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using back_mylife.Data;
using back_mylife.Models;

namespace back_mylife.Controllers
{
    [Route("api/[controller]")]
    public class FinanceController : AuthorizedApiController
    {
        private readonly AppDbContext _context;

        public FinanceController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet("{userId}")]
        public async Task<IActionResult> GetTransactions(Guid userId)
        {
            if (!IsCurrentUser(userId)) return Forbid();

            var list = await _context.FinanceTransactions
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.TransactionDate)
                .ToListAsync();
            return Ok(list);
        }

        [HttpPost]
        public async Task<IActionResult> AddTransaction([FromBody] FinanceTransaction item)
        {
            item.Id = Guid.NewGuid();
            item.UserId = CurrentUserId;
            item.TransactionDate = item.TransactionDate == default ? DateTime.UtcNow : item.TransactionDate;
            _context.FinanceTransactions.Add(item);
            await _context.SaveChangesAsync();
            return Ok(item);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateTransaction(Guid id, [FromBody] FinanceTransaction item)
        {
            var existing = await _context.FinanceTransactions.FindAsync(id);
            if (existing == null || existing.UserId != CurrentUserId) return NotFound();

            existing.Type = item.Type;
            existing.Amount = item.Amount;
            existing.Category = item.Category;
            existing.TransactionDate = item.TransactionDate;
            existing.Note = item.Note;

            await _context.SaveChangesAsync();
            return Ok(existing);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTransaction(Guid id)
        {
            var existing = await _context.FinanceTransactions.FindAsync(id);
            if (existing == null || existing.UserId != CurrentUserId) return NotFound();

            _context.FinanceTransactions.Remove(existing);
            await _context.SaveChangesAsync();
            return Ok(new { message = "ลบรายการสำเร็จ" });
        }

        [HttpGet("summary/{userId}")]
        public async Task<IActionResult> GetSummary(Guid userId)
        {
            if (!IsCurrentUser(userId)) return Forbid();

            var transactions = await _context.FinanceTransactions
                .Where(t => t.UserId == userId)
                .ToListAsync();

            decimal totalIncome = transactions.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount);
            decimal totalExpense = transactions.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount);
            decimal netBalance = totalIncome - totalExpense;

            // ตรวจสอบเงื่อนไขแจ้งเตือน เงินคงเหลือ < 0.5% ของรายรับทั้งหมด
            bool isLowBalance = totalIncome > 0 && (netBalance < (totalIncome * 0.005m));

            // Top 5 Expenses
            var expensesByCategory = transactions
                .Where(t => t.Type == TransactionType.Expense)
                .GroupBy(t => t.Category)
                .Select(g => new { Category = g.Key, Amount = g.Sum(x => x.Amount) })
                .OrderByDescending(x => x.Amount)
                .ToList();

            var top5 = expensesByCategory.Take(5).ToList();
            var remaining = expensesByCategory.Skip(5).ToList();

            return Ok(new
            {
                totalIncome,
                totalExpense,
                netBalance,
                isLowBalance,
                top5Expenses = top5,
                remainingExpenses = remaining
            });
        }

        // Recurring Expenses APIs
        [HttpGet("recurring/{userId}")]
        public async Task<IActionResult> GetRecurring(Guid userId)
        {
            if (!IsCurrentUser(userId)) return Forbid();

            var list = await _context.RecurringExpenses
                .Where(r => r.UserId == userId)
                .ToListAsync();
            return Ok(list);
        }

        [HttpPost("recurring")]
        public async Task<IActionResult> AddRecurring([FromBody] RecurringExpense item)
        {
            item.Id = Guid.NewGuid();
            item.UserId = CurrentUserId;
            _context.RecurringExpenses.Add(item);
            await _context.SaveChangesAsync();
            return Ok(item);
        }

        [HttpPut("recurring/{id}")]
        public async Task<IActionResult> UpdateRecurring(Guid id, [FromBody] RecurringExpense item)
        {
            var existing = await _context.RecurringExpenses.FindAsync(id);
            if (existing == null || existing.UserId != CurrentUserId) return NotFound();

            existing.Title = item.Title;
            existing.Amount = item.Amount;
            existing.Category = item.Category;
            existing.StartDate = item.StartDate;
            existing.EndDate = item.EndDate;
            existing.IsIndefinite = item.IsIndefinite;
            existing.DayOfMonthDue = item.DayOfMonthDue;

            await _context.SaveChangesAsync();
            return Ok(existing);
        }

        [HttpDelete("recurring/{id}")]
        public async Task<IActionResult> DeleteRecurring(Guid id)
        {
            var existing = await _context.RecurringExpenses.FindAsync(id);
            if (existing == null || existing.UserId != CurrentUserId) return NotFound();

            _context.RecurringExpenses.Remove(existing);
            await _context.SaveChangesAsync();
            return Ok(new { message = "ลบรายการจ่ายประจำสำเร็จ" });
        }

        // สรุปรายรับ-รายจ่ายแบบรายวัน/รายเดือน/รายปี สำหรับตารางและกราฟเปรียบเทียบ
        [HttpGet("breakdown/{userId}")]
        public async Task<IActionResult> GetBreakdown(Guid userId, [FromQuery] string period = "monthly", [FromQuery] int? year = null, [FromQuery] int? month = null)
        {
            if (!IsCurrentUser(userId)) return Forbid();

            var transactions = await _context.FinanceTransactions
                .Where(t => t.UserId == userId)
                .ToListAsync();

            var now = DateTime.UtcNow;
            var targetYear = year ?? now.Year;
            var targetMonth = month ?? now.Month;
            var result = new List<object>();

            switch (period.ToLower())
            {
                case "daily":
                    var daysInMonth = DateTime.DaysInMonth(targetYear, targetMonth);
                    for (int d = 1; d <= daysInMonth; d++)
                    {
                        var dayTx = transactions.Where(t => t.TransactionDate.Year == targetYear && t.TransactionDate.Month == targetMonth && t.TransactionDate.Day == d).ToList();
                        result.Add(new
                        {
                            label = d.ToString(),
                            income = dayTx.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount),
                            expense = dayTx.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount),
                        });
                    }
                    break;

                case "yearly":
                    var years = transactions.Select(t => t.TransactionDate.Year).Distinct().OrderBy(y => y).ToList();
                    if (years.Count == 0) years.Add(now.Year);
                    foreach (var y in years)
                    {
                        var yearTx = transactions.Where(t => t.TransactionDate.Year == y).ToList();
                        result.Add(new
                        {
                            label = y.ToString(),
                            income = yearTx.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount),
                            expense = yearTx.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount),
                        });
                    }
                    break;

                case "monthly":
                default:
                    for (int m = 1; m <= 12; m++)
                    {
                        var monthTx = transactions.Where(t => t.TransactionDate.Year == targetYear && t.TransactionDate.Month == m).ToList();
                        result.Add(new
                        {
                            label = m.ToString(),
                            income = monthTx.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount),
                            expense = monthTx.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount),
                        });
                    }
                    break;
            }

            return Ok(new { period = period.ToLower(), year = targetYear, month = targetMonth, data = result });
        }
    }
}
