using Microsoft.EntityFrameworkCore;
using back_mylife.Models;

namespace back_mylife.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<FinanceBook> FinanceBooks { get; set; }
        public DbSet<FinanceTransaction> FinanceTransactions { get; set; }
        public DbSet<RecurringExpense> RecurringExpenses { get; set; }
        public DbSet<AcademicTerm> AcademicTerms { get; set; }
        public DbSet<Course> Courses { get; set; }
        public DbSet<Activity> Activities { get; set; }
        public DbSet<TodoItem> TodoItems { get; set; }
        public DbSet<TodoCompletion> TodoCompletions { get; set; }
        public DbSet<Assignment> Assignments { get; set; }
        public DbSet<HealthLog> HealthLogs { get; set; }
        public DbSet<GoogleCalendarConnection> GoogleCalendarConnections { get; set; }
        public DbSet<LineConnection> LineConnections { get; set; }
        public DbSet<ClassReminderSent> ClassRemindersSent { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // User Relations
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            modelBuilder.Entity<FinanceBook>()
                .HasOne(b => b.User)
                .WithMany()
                .HasForeignKey(b => b.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<FinanceTransaction>()
                .HasOne(f => f.User)
                .WithMany(u => u.FinanceTransactions)
                .HasForeignKey(f => f.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<FinanceTransaction>()
                .HasOne(f => f.Book)
                .WithMany(b => b.Transactions)
                .HasForeignKey(f => f.BookId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<RecurringExpense>()
                .HasOne(r => r.User)
                .WithMany(u => u.RecurringExpenses)
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<RecurringExpense>()
                .HasOne(r => r.Book)
                .WithMany(b => b.RecurringExpenses)
                .HasForeignKey(r => r.BookId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<AcademicTerm>()
                .HasOne(a => a.User)
                .WithMany(u => u.AcademicTerms)
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Course>()
                .HasOne(c => c.Term)
                .WithMany(t => t.Courses)
                .HasForeignKey(c => c.TermId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Activity>()
                .HasOne(a => a.User)
                .WithMany(u => u.Activities)
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<TodoItem>()
                .HasOne(t => t.User)
                .WithMany(u => u.TodoItems)
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<TodoCompletion>()
                .HasIndex(c => new { c.TodoItemId, c.CompletedDate })
                .IsUnique();

            modelBuilder.Entity<TodoCompletion>()
                .HasOne(c => c.TodoItem)
                .WithMany()
                .HasForeignKey(c => c.TodoItemId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Assignment>()
                .HasOne(a => a.User)
                .WithMany(u => u.Assignments)
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<HealthLog>()
                .HasOne(h => h.User)
                .WithMany(u => u.HealthLogs)
                .HasForeignKey(h => h.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<GoogleCalendarConnection>()
                .HasIndex(g => g.UserId)
                .IsUnique();

            modelBuilder.Entity<GoogleCalendarConnection>()
                .HasOne(g => g.User)
                .WithOne(u => u.GoogleCalendarConnection)
                .HasForeignKey<GoogleCalendarConnection>(g => g.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<LineConnection>()
                .HasIndex(l => l.UserId)
                .IsUnique();

            modelBuilder.Entity<LineConnection>()
                .HasIndex(l => l.LineUserId)
                .IsUnique();

            modelBuilder.Entity<LineConnection>()
                .HasOne(l => l.User)
                .WithOne(u => u.LineConnection)
                .HasForeignKey<LineConnection>(l => l.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ClassReminderSent>()
                .HasIndex(c => new { c.UserId, c.CourseId, c.ClassDate })
                .IsUnique();

            modelBuilder.Entity<ClassReminderSent>()
                .HasOne(c => c.User)
                .WithMany()
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ClassReminderSent>()
                .HasOne(c => c.Course)
                .WithMany()
                .HasForeignKey(c => c.CourseId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
