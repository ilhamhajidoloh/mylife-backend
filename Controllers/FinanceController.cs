using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using back_mylife.Data;
using back_mylife.Models;

namespace back_mylife.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FinanceController : ControllerBase
    {
        private readonly AppDbContext _context;

        public FinanceController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet("{userId}")]
        public async Task<IActionResult> GetTransactions(Guid userId)
        {
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
            item.TransactionDate = item.TransactionDate == default ? DateTime.UtcNow : item.TransactionDate;
            _context.FinanceTransactions.Add(item);
            await _context.SaveChangesAsync();
            return Ok(item);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateTransaction(Guid id, [FromBody] FinanceTransaction item)
        {
            var existing = await _context.FinanceTransactions.FindAsync(id);
            if (existing == null) return NotFound();

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
            if (existing == null) return NotFound();

            _context.FinanceTransactions.Remove(existing);
            await _context.SaveChangesAsync();
            return Ok(new { message = "ลบรายการสำเร็จ" });
        }

        [HttpGet("summary/{userId}")]
        public async Task<IActionResult> GetSummary(Guid userId)
        {
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
            var list = await _context.RecurringExpenses
                .Where(r => r.UserId == userId)
                .ToListAsync();
            return Ok(list);
        }

        [HttpPost("recurring")]
        public async Task<IActionResult> AddRecurring([FromBody] RecurringExpense item)
        {
            item.Id = Guid.NewGuid();
            _context.RecurringExpenses.Add(item);
            await _context.SaveChangesAsync();
            return Ok(item);
        }

        [HttpDelete("recurring/{id}")]
        public async Task<IActionResult> DeleteRecurring(Guid id)
        {
            var existing = await _context.RecurringExpenses.FindAsync(id);
            if (existing == null) return NotFound();

            _context.RecurringExpenses.Remove(existing);
            await _context.SaveChangesAsync();
            return Ok(new { message = "ลบรายการจ่ายประจำสำเร็จ" });
        }
    }
}
