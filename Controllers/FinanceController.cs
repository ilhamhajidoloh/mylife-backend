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
        private static DateTime EnsureUtc(DateTime dt)
        {
            if (dt == default) return DateTime.UtcNow;
            return dt.Kind switch
            {
                DateTimeKind.Utc => dt,
                DateTimeKind.Local => dt.ToUniversalTime(),
                _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc)
            };
        }

        private static DateTime? EnsureUtc(DateTime? dt)
        {
            if (!dt.HasValue) return null;
            return EnsureUtc(dt.Value);
        }

        public FinanceController(AppDbContext context)
        {
            _context = context;
        }

        // ==========================================
        // Books (สมุดบัญชี) Management APIs
        // ==========================================

        [HttpGet("books/{userId}")]
        public async Task<IActionResult> GetBooks(Guid userId)
        {
            if (!IsCurrentUser(userId)) return Forbid();

            var books = await _context.FinanceBooks
                .Where(b => b.UserId == userId)
                .OrderByDescending(b => b.IsDefault)
                .ThenBy(b => b.CreatedAt)
                .ToListAsync();

            // Auto-provision initial default book if user has none
            if (books.Count == 0)
            {
                var defaultBook = new FinanceBook
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Name = "สมุดหลัก",
                    Icon = "🏠",
                    Color = "#8b5cf6",
                    IsDefault = true,
                    CreatedAt = DateTime.UtcNow,
                };
                _context.FinanceBooks.Add(defaultBook);
                await _context.SaveChangesAsync();

                // Backfill existing transactions/recurrings with no BookId
                var nullTx = await _context.FinanceTransactions
                    .Where(t => t.UserId == userId && t.BookId == null)
                    .ToListAsync();
                foreach (var tx in nullTx) tx.BookId = defaultBook.Id;

                var nullRec = await _context.RecurringExpenses
                    .Where(r => r.UserId == userId && r.BookId == null)
                    .ToListAsync();
                foreach (var r in nullRec) r.BookId = defaultBook.Id;

                await _context.SaveChangesAsync();
                books.Add(defaultBook);
            }

            return Ok(books);
        }

        [HttpPost("books")]
        public async Task<IActionResult> CreateBook([FromBody] FinanceBook item)
        {
            item.Id = Guid.NewGuid();
            item.UserId = CurrentUserId;
            item.Name = string.IsNullOrWhiteSpace(item.Name) ? "สมุดใหม่" : item.Name.Trim();
            item.Icon = string.IsNullOrWhiteSpace(item.Icon) ? "💼" : item.Icon.Trim();
            item.Color = string.IsNullOrWhiteSpace(item.Color) ? "#8b5cf6" : item.Color.Trim();
            item.CreatedAt = DateTime.UtcNow;

            var existingBooks = await _context.FinanceBooks
                .Where(b => b.UserId == CurrentUserId)
                .ToListAsync();

            if (existingBooks.Count == 0)
            {
                item.IsDefault = true;
            }
            else if (item.IsDefault)
            {
                foreach (var b in existingBooks) b.IsDefault = false;
            }

            _context.FinanceBooks.Add(item);
            await _context.SaveChangesAsync();
            return Ok(item);
        }

        [HttpPut("books/{id}")]
        public async Task<IActionResult> UpdateBook(Guid id, [FromBody] FinanceBook item)
        {
            var existing = await _context.FinanceBooks.FindAsync(id);
            if (existing == null || existing.UserId != CurrentUserId) return NotFound();

            existing.Name = string.IsNullOrWhiteSpace(item.Name) ? existing.Name : item.Name.Trim();
            existing.Icon = string.IsNullOrWhiteSpace(item.Icon) ? existing.Icon : item.Icon.Trim();
            existing.Color = string.IsNullOrWhiteSpace(item.Color) ? existing.Color : item.Color.Trim();

            if (item.IsDefault && !existing.IsDefault)
            {
                var others = await _context.FinanceBooks
                    .Where(b => b.UserId == CurrentUserId && b.Id != id)
                    .ToListAsync();
                foreach (var b in others) b.IsDefault = false;
                existing.IsDefault = true;
            }

            await _context.SaveChangesAsync();
            return Ok(existing);
        }

        [HttpDelete("books/{id}")]
        public async Task<IActionResult> DeleteBook(Guid id)
        {
            var existing = await _context.FinanceBooks.FindAsync(id);
            if (existing == null || existing.UserId != CurrentUserId) return NotFound();

            var totalBooks = await _context.FinanceBooks
                .Where(b => b.UserId == CurrentUserId)
                .ToListAsync();

            if (totalBooks.Count <= 1)
            {
                return BadRequest(new { message = "ไม่สามารถลบสมุดบัญชีเล่มสุดท้ายได้" });
            }

            // Move transactions & recurring items to another fallback book
            var fallbackBook = totalBooks.FirstOrDefault(b => b.Id != id && b.IsDefault) 
                ?? totalBooks.First(b => b.Id != id);

            var txToMove = await _context.FinanceTransactions
                .Where(t => t.UserId == CurrentUserId && t.BookId == id)
                .ToListAsync();
            foreach (var tx in txToMove) tx.BookId = fallbackBook.Id;

            var recToMove = await _context.RecurringExpenses
                .Where(r => r.UserId == CurrentUserId && r.BookId == id)
                .ToListAsync();
            foreach (var r in recToMove) r.BookId = fallbackBook.Id;

            if (existing.IsDefault)
            {
                fallbackBook.IsDefault = true;
            }

            _context.FinanceBooks.Remove(existing);
            await _context.SaveChangesAsync();
            return Ok(new { message = "ลบสมุดบัญชีสำเร็จ", fallbackBookId = fallbackBook.Id });
        }

        // ==========================================
        // Transactions APIs
        // ==========================================

        [HttpGet("{userId}")]
        public async Task<IActionResult> GetTransactions(Guid userId, [FromQuery] Guid? bookId = null)
        {
            if (!IsCurrentUser(userId)) return Forbid();

            var query = _context.FinanceTransactions
                .Where(t => t.UserId == userId);

            if (bookId.HasValue)
            {
                query = query.Where(t => t.BookId == bookId.Value);
            }

            var list = await query
                .OrderByDescending(t => t.TransactionDate)
                .ToListAsync();
            return Ok(list);
        }

        [HttpPost]
        public async Task<IActionResult> AddTransaction([FromBody] FinanceTransaction item)
        {
            item.Id = Guid.NewGuid();
            item.UserId = CurrentUserId;
            item.TransactionDate = EnsureUtc(item.TransactionDate);

            // Auto-assign default book if bookId is null
            if (!item.BookId.HasValue)
            {
                var defaultBook = await _context.FinanceBooks
                    .FirstOrDefaultAsync(b => b.UserId == CurrentUserId && b.IsDefault);
                if (defaultBook != null) item.BookId = defaultBook.Id;
            }

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
            existing.TransactionDate = EnsureUtc(item.TransactionDate);
            existing.Note = item.Note;
            if (item.BookId.HasValue) existing.BookId = item.BookId;

            await _context.SaveChangesAsync();
            return Ok(existing);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTransaction(Guid id)
        {
            var existing = await _context.FinanceTransactions.FindAsync(id);
            if (existing == null || existing.UserId != CurrentUserId) return NotFound();

            // หากรายการที่ถูกลบ เป็นการชำระรายจ่ายประจำ ให้ย้อนรอบวันชำระ (StartDate) ของรายจ่ายประจำกลับ 1 เดือนอัตโนมัติ
            if (!string.IsNullOrEmpty(existing.Note) && existing.Note.Contains("ชำระรายจ่ายประจำ"))
            {
                var userRecurrings = await _context.RecurringExpenses
                    .Where(r => r.UserId == CurrentUserId)
                    .ToListAsync();

                var noteClean = existing.Note.ToLower();
                var extractedTitle = existing.Note.Replace("ชำระรายจ่ายประจำ:", "").Replace("ชำระรายจ่ายประจำ", "").Trim().ToLower();

                var recurring = userRecurrings.FirstOrDefault(r => 
                    !string.IsNullOrEmpty(r.Title) && (
                        r.Title.Trim().ToLower() == extractedTitle ||
                        noteClean.Contains(r.Title.Trim().ToLower()) ||
                        (!string.IsNullOrEmpty(extractedTitle) && r.Title.Trim().ToLower().Contains(extractedTitle))
                    )
                );

                if (recurring != null)
                {
                    recurring.StartDate = EnsureUtc(recurring.StartDate.AddMonths(-1));
                }
            }

            _context.FinanceTransactions.Remove(existing);
            await _context.SaveChangesAsync();
            return Ok(new { message = "ลบรายการสำเร็จ" });
        }

        [HttpGet("summary/{userId}")]
        public async Task<IActionResult> GetSummary(Guid userId, [FromQuery] Guid? bookId = null)
        {
            if (!IsCurrentUser(userId)) return Forbid();

            var query = _context.FinanceTransactions
                .Where(t => t.UserId == userId);

            if (bookId.HasValue)
            {
                query = query.Where(t => t.BookId == bookId.Value);
            }

            var transactions = await query.ToListAsync();

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

        // ==========================================
        // Recurring Expenses APIs
        // ==========================================

        [HttpGet("recurring/{userId}")]
        public async Task<IActionResult> GetRecurring(Guid userId, [FromQuery] Guid? bookId = null)
        {
            if (!IsCurrentUser(userId)) return Forbid();

            var query = _context.RecurringExpenses
                .Where(r => r.UserId == userId);

            if (bookId.HasValue)
            {
                query = query.Where(r => r.BookId == bookId.Value);
            }

            var list = await query.ToListAsync();
            return Ok(list);
        }

        [HttpPost("recurring")]
        public async Task<IActionResult> AddRecurring([FromBody] RecurringExpense item)
        {
            item.Id = Guid.NewGuid();
            item.UserId = CurrentUserId;
            item.StartDate = EnsureUtc(item.StartDate);
            item.EndDate = EnsureUtc(item.EndDate);

            // Auto-assign default book if bookId is null
            if (!item.BookId.HasValue)
            {
                var defaultBook = await _context.FinanceBooks
                    .FirstOrDefaultAsync(b => b.UserId == CurrentUserId && b.IsDefault);
                if (defaultBook != null) item.BookId = defaultBook.Id;
            }

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
            existing.StartDate = EnsureUtc(item.StartDate);
            existing.EndDate = EnsureUtc(item.EndDate);
            existing.IsIndefinite = item.IsIndefinite;
            existing.DayOfMonthDue = item.DayOfMonthDue;
            if (item.BookId.HasValue) existing.BookId = item.BookId;

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
        public async Task<IActionResult> GetBreakdown(Guid userId, [FromQuery] string period = "monthly", [FromQuery] int? year = null, [FromQuery] int? month = null, [FromQuery] Guid? bookId = null)
        {
            if (!IsCurrentUser(userId)) return Forbid();

            var query = _context.FinanceTransactions
                .Where(t => t.UserId == userId);

            if (bookId.HasValue)
            {
                query = query.Where(t => t.BookId == bookId.Value);
            }

            var transactions = await query.ToListAsync();

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
