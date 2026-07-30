namespace back_mylife.Models
{
    public class User
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Email { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? GoogleId { get; set; }
        public string? LineId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties
        public ICollection<FinanceTransaction> FinanceTransactions { get; set; } = new List<FinanceTransaction>();
        public ICollection<RecurringExpense> RecurringExpenses { get; set; } = new List<RecurringExpense>();
        public ICollection<AcademicTerm> AcademicTerms { get; set; } = new List<AcademicTerm>();
        public ICollection<Activity> Activities { get; set; } = new List<Activity>();
        public ICollection<TodoItem> TodoItems { get; set; } = new List<TodoItem>();
        public ICollection<Assignment> Assignments { get; set; } = new List<Assignment>();
        public ICollection<HealthLog> HealthLogs { get; set; } = new List<HealthLog>();
        public GoogleCalendarConnection? GoogleCalendarConnection { get; set; }
        public LineConnection? LineConnection { get; set; }
    }
}
