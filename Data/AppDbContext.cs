using Microsoft.EntityFrameworkCore;
using back_mylife.Models;

namespace back_mylife.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<FinanceTransaction> FinanceTransactions { get; set; }
        public DbSet<RecurringExpense> RecurringExpenses { get; set; }
        public DbSet<AcademicTerm> AcademicTerms { get; set; }
        public DbSet<Course> Courses { get; set; }
        public DbSet<Activity> Activities { get; set; }
        public DbSet<TodoItem> TodoItems { get; set; }
        public DbSet<Assignment> Assignments { get; set; }
        public DbSet<HealthLog> HealthLogs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // User Relations
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            modelBuilder.Entity<FinanceTransaction>()
                .HasOne(f => f.User)
                .WithMany(u => u.FinanceTransactions)
                .HasForeignKey(f => f.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<RecurringExpense>()
                .HasOne(r => r.User)
                .WithMany(u => u.RecurringExpenses)
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);

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
        }
    }
}
