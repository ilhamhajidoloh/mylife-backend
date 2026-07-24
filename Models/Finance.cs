using System.Text.Json.Serialization;

namespace back_mylife.Models
{
    public enum TransactionType
    {
        Income,
        Expense
    }

    public class FinanceTransaction
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public TransactionType Type { get; set; }
        public decimal Amount { get; set; }
        public string Category { get; set; } = "ทั่วไป";
        public DateTime TransactionDate { get; set; } = DateTime.UtcNow;
        public string? Note { get; set; }

        [JsonIgnore]
        public User? User { get; set; }
    }

    public class RecurringExpense
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public string Title { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Category { get; set; } = "ค่าใช้จ่ายประจำ";
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public bool IsIndefinite { get; set; } = true;
        public int DayOfMonthDue { get; set; } = 1; // วันกำหนดจ่ายของทุกเดือน

        [JsonIgnore]
        public User? User { get; set; }
    }
}
