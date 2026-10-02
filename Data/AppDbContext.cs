using back_mylife.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace back_mylife.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    private static readonly ValueConverter<System.Guid, string> GuidToString = new(
        value => value.ToString(), value => System.Guid.Parse(value));
    private static readonly ValueConverter<System.Guid?, string?> NullableGuidToString = new(
        value => value.HasValue ? value.Value.ToString() : null,
        value => string.IsNullOrWhiteSpace(value) ? null : System.Guid.Parse(value));

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
    public DbSet<EmailNotificationPreference> EmailNotificationPreferences { get; set; }
    public DbSet<ClassReminderSent> ClassRemindersSent { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("MYLIFE_APP");

        ConfigureUser(modelBuilder.Entity<User>());
        ConfigureFinanceBook(modelBuilder.Entity<FinanceBook>());
        ConfigureFinanceTransaction(modelBuilder.Entity<FinanceTransaction>());
        ConfigureRecurringExpense(modelBuilder.Entity<RecurringExpense>());
        ConfigureAcademicTerm(modelBuilder.Entity<AcademicTerm>());
        ConfigureCourse(modelBuilder.Entity<Course>());
        ConfigureActivity(modelBuilder.Entity<Activity>());
        ConfigureTodoItem(modelBuilder.Entity<TodoItem>());
        ConfigureTodoCompletion(modelBuilder.Entity<TodoCompletion>());
        ConfigureAssignment(modelBuilder.Entity<Assignment>());
        ConfigureHealthLog(modelBuilder.Entity<HealthLog>());
        ConfigureGoogleCalendarConnection(modelBuilder.Entity<GoogleCalendarConnection>());
        ConfigureLineConnection(modelBuilder.Entity<LineConnection>());
        ConfigureEmailNotificationPreference(modelBuilder.Entity<EmailNotificationPreference>());
        ConfigureClassReminderSent(modelBuilder.Entity<ClassReminderSent>());
    }

    private static void ConfigureUser(EntityTypeBuilder<User> b)
    {
        b.ToTable("USERS"); b.HasKey(x => x.Id).HasName("PK_Users");
        Guid(b.Property(x => x.Id), "ID"); Varchar(b.Property(x => x.Email), "EMAIL", 320, false);
        Varchar(b.Property(x => x.PasswordHash), "PASSWORDHASH", 1000, true); Varchar(b.Property(x => x.FullName), "FULLNAME", 500, false);
        Varchar(b.Property(x => x.GoogleId), "GOOGLEID", 500, true); Varchar(b.Property(x => x.LineId), "LINEID", 500, true);
        Timestamp(b.Property(x => x.CreatedAt), "CREATEDAT"); Varchar(b.Property(x => x.ProfileImageUrl), "PROFILEIMAGEURL", 2000, true);
        b.HasIndex(x => x.Email).IsUnique().HasDatabaseName("UQ_Users_Email");
    }
    private static void ConfigureFinanceBook(EntityTypeBuilder<FinanceBook> b)
    {
        b.ToTable("FINANCEBOOKS"); b.HasKey(x => x.Id).HasName("PK_FinanceBooks"); Guid(b.Property(x => x.Id), "ID"); Guid(b.Property(x => x.UserId), "USERID");
        Varchar(b.Property(x => x.Name), "NAME", 500, false); Varchar(b.Property(x => x.Icon), "ICON", 100, false); Varchar(b.Property(x => x.Color), "COLOR", 50, false);
        Bool(b.Property(x => x.IsDefault), "ISDEFAULT"); Timestamp(b.Property(x => x.CreatedAt), "CREATEDAT");
        b.HasIndex(x => x.UserId).HasDatabaseName("IX_FinanceBooks_UserId"); b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_FinanceBooks_Users");
    }
    private static void ConfigureFinanceTransaction(EntityTypeBuilder<FinanceTransaction> b)
    {
        b.ToTable("FINANCETRANSACTIONS"); b.HasKey(x => x.Id).HasName("PK_FinanceTransactions"); Guid(b.Property(x => x.Id), "ID"); Guid(b.Property(x => x.UserId), "USERID"); Guid(b.Property(x => x.BookId), "BOOKID");
        Number(b.Property(x => x.Type), "TYPE"); b.Property(x => x.Amount).HasColumnName("AMOUNT").HasColumnType("NUMBER(38,10)").HasPrecision(38, 10);
        Varchar(b.Property(x => x.Category), "CATEGORY", 1000, false); Timestamp(b.Property(x => x.TransactionDate), "TRANSACTIONDATE"); Varchar(b.Property(x => x.Note), "NOTE", 4000, true);
        b.HasIndex(x => x.UserId).HasDatabaseName("IX_FinanceTransactions_UserId"); b.HasIndex(x => x.BookId).HasDatabaseName("IX_FinanceTransactions_BookId");
        b.HasOne(x => x.User).WithMany(x => x.FinanceTransactions).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_FT_Users"); b.HasOne(x => x.Book).WithMany(x => x.Transactions).HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.SetNull).HasConstraintName("FK_FT_FinanceBooks");
    }
    private static void ConfigureRecurringExpense(EntityTypeBuilder<RecurringExpense> b)
    {
        b.ToTable("RECURRINGEXPENSES"); b.HasKey(x => x.Id).HasName("PK_RecurringExpenses"); Guid(b.Property(x => x.Id), "ID"); Guid(b.Property(x => x.UserId), "USERID"); Guid(b.Property(x => x.BookId), "BOOKID");
        Varchar(b.Property(x => x.Title), "TITLE", 1000, false); b.Property(x => x.Amount).HasColumnName("AMOUNT").HasColumnType("NUMBER(38,10)").HasPrecision(38, 10); Varchar(b.Property(x => x.Category), "CATEGORY", 1000, false);
        Timestamp(b.Property(x => x.StartDate), "STARTDATE"); Timestamp(b.Property(x => x.EndDate), "ENDDATE"); Bool(b.Property(x => x.IsIndefinite), "ISINDEFINITE"); Number(b.Property(x => x.DayOfMonthDue), "DAYOFMONTHDUE");
        b.HasIndex(x => x.UserId).HasDatabaseName("IX_RecurringExpenses_UserId"); b.HasIndex(x => x.BookId).HasDatabaseName("IX_RecurringExpenses_BookId"); b.HasOne(x => x.User).WithMany(x => x.RecurringExpenses).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_RE_Users"); b.HasOne(x => x.Book).WithMany(x => x.RecurringExpenses).HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.SetNull).HasConstraintName("FK_RE_FinanceBooks");
    }
    private static void ConfigureAcademicTerm(EntityTypeBuilder<AcademicTerm> b)
    {
        b.ToTable("ACADEMICTERMS"); b.HasKey(x => x.Id).HasName("PK_AcademicTerms"); Guid(b.Property(x => x.Id), "ID"); Guid(b.Property(x => x.UserId), "USERID"); Varchar(b.Property(x => x.TermName), "TERMNAME", 500, false); Timestamp(b.Property(x => x.StartDate), "STARTDATE"); Timestamp(b.Property(x => x.EndDate), "ENDDATE"); b.HasIndex(x => x.UserId).HasDatabaseName("IX_AcademicTerms_UserId"); b.HasOne(x => x.User).WithMany(x => x.AcademicTerms).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_AcademicTerms_Users");
    }
    private static void ConfigureCourse(EntityTypeBuilder<Course> b)
    {
        b.ToTable("COURSES"); b.HasKey(x => x.Id).HasName("PK_Courses"); Guid(b.Property(x => x.Id), "ID"); Guid(b.Property(x => x.TermId), "TERMID"); Varchar(b.Property(x => x.CourseCode), "COURSECODE", 500, true); Varchar(b.Property(x => x.CourseName), "COURSENAME", 1000, false); Varchar(b.Property(x => x.Room), "ROOM", 500, true); Varchar(b.Property(x => x.Instructor), "INSTRUCTOR", 1000, true); Number(b.Property(x => x.DayOfWeek), "DAYOFWEEK"); Interval(b.Property(x => x.StartTime), "STARTTIME"); Interval(b.Property(x => x.EndTime), "ENDTIME"); Varchar(b.Property(x => x.ColorHex), "COLORHEX", 50, true); b.HasIndex(x => x.TermId).HasDatabaseName("IX_Courses_TermId"); b.HasOne(x => x.Term).WithMany(x => x.Courses).HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_Courses_AcademicTerms");
    }
    private static void ConfigureActivity(EntityTypeBuilder<Activity> b)
    {
        b.ToTable("ACTIVITIES"); b.HasKey(x => x.Id).HasName("PK_Activities"); Guid(b.Property(x => x.Id), "ID"); Guid(b.Property(x => x.UserId), "USERID"); Varchar(b.Property(x => x.Title), "TITLE", 1000, false); Varchar(b.Property(x => x.Description), "DESCRIPTION", 4000, true); Timestamp(b.Property(x => x.StartTime), "STARTTIME"); Timestamp(b.Property(x => x.EndTime), "ENDTIME"); Bool(b.Property(x => x.IsAllDay), "ISALLDAY"); Bool(b.Property(x => x.IsMultiDay), "ISMULTIDAY"); Bool(b.Property(x => x.IsIndefinite), "ISINDEFINITE"); Number(b.Property(x => x.Recurrence), "RECURRENCE"); Varchar(b.Property(x => x.Location), "LOCATION", 1000, true); Number(b.Property(x => x.ReminderMinutes), "REMINDERMINUTES"); Timestamp(b.Property(x => x.ReminderSentAt), "REMINDERSENTAT"); Varchar(b.Property(x => x.GoogleEventId), "GOOGLEEVENTID", 1000, true); b.HasIndex(x => x.UserId).HasDatabaseName("IX_Activities_UserId"); b.HasOne(x => x.User).WithMany(x => x.Activities).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_Activities_Users");
    }
    private static void ConfigureTodoItem(EntityTypeBuilder<TodoItem> b)
    {
        b.ToTable("TODOITEMS"); b.HasKey(x => x.Id).HasName("PK_TodoItems"); Guid(b.Property(x => x.Id), "ID"); Guid(b.Property(x => x.UserId), "USERID"); Varchar(b.Property(x => x.Title), "TITLE", 1000, false); Timestamp(b.Property(x => x.TargetDate), "TARGETDATE"); Varchar(b.Property(x => x.Tag), "TAG", 500, false); Bool(b.Property(x => x.IsCompleted), "ISCOMPLETED"); Number(b.Property(x => x.Recurrence), "RECURRENCE"); Varchar(b.Property(x => x.Description), "DESCRIPTION", 4000, true); Number(b.Property(x => x.Status), "STATUS"); Number(b.Property(x => x.Priority), "PRIORITY"); Timestamp(b.Property(x => x.ReminderSentAt), "REMINDERSENTAT"); b.HasIndex(x => x.UserId).HasDatabaseName("IX_TodoItems_UserId"); b.HasOne(x => x.User).WithMany(x => x.TodoItems).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_TodoItems_Users");
    }
    private static void ConfigureTodoCompletion(EntityTypeBuilder<TodoCompletion> b)
    {
        b.ToTable("TODOCOMPLETIONS"); b.HasKey(x => x.Id).HasName("PK_TodoCompletions"); Guid(b.Property(x => x.Id), "ID"); Guid(b.Property(x => x.TodoItemId), "TODOITEMID"); Timestamp(b.Property(x => x.CompletedDate), "COMPLETEDDATE"); Bool(b.Property(x => x.IsCompleted), "ISCOMPLETED"); b.HasIndex(x => new { x.TodoItemId, x.CompletedDate }).IsUnique().HasDatabaseName("UQ_TC_Item_Date"); b.HasOne(x => x.TodoItem).WithMany().HasForeignKey(x => x.TodoItemId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_TC_TodoItems");
    }
    private static void ConfigureAssignment(EntityTypeBuilder<Assignment> b)
    {
        b.ToTable("ASSIGNMENTS"); b.HasKey(x => x.Id).HasName("PK_Assignments"); Guid(b.Property(x => x.Id), "ID"); Guid(b.Property(x => x.UserId), "USERID"); Varchar(b.Property(x => x.Title), "TITLE", 1000, false); Varchar(b.Property(x => x.Subject), "SUBJECT", 1000, true); Timestamp(b.Property(x => x.Deadline), "DEADLINE"); Bool(b.Property(x => x.IsUrgent), "ISURGENT"); Bool(b.Property(x => x.IsCompleted), "ISCOMPLETED"); b.HasIndex(x => x.UserId).HasDatabaseName("IX_Assignments_UserId"); b.HasOne(x => x.User).WithMany(x => x.Assignments).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_Assignments_Users");
    }
    private static void ConfigureHealthLog(EntityTypeBuilder<HealthLog> b)
    {
        b.ToTable("HEALTHLOGS"); b.HasKey(x => x.Id).HasName("PK_HealthLogs"); Guid(b.Property(x => x.Id), "ID"); Guid(b.Property(x => x.UserId), "USERID"); Number(b.Property(x => x.StepCount), "STEPCOUNT"); Number(b.Property(x => x.HeartRate), "HEARTRATE"); Timestamp(b.Property(x => x.RecordedAt), "RECORDEDAT"); b.HasIndex(x => x.UserId).HasDatabaseName("IX_HealthLogs_UserId"); b.HasOne(x => x.User).WithMany(x => x.HealthLogs).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_HealthLogs_Users");
    }
    private static void ConfigureGoogleCalendarConnection(EntityTypeBuilder<GoogleCalendarConnection> b)
    {
        b.ToTable("GOOGLECALENDARCONNECTIONS"); b.HasKey(x => x.Id).HasName("PK_GoogleCalendarConnections"); Guid(b.Property(x => x.Id), "ID"); Guid(b.Property(x => x.UserId), "USERID"); Varchar(b.Property(x => x.AccessToken), "ACCESSTOKEN", 4000, false); Varchar(b.Property(x => x.RefreshToken), "REFRESHTOKEN", 4000, false); Timestamp(b.Property(x => x.TokenExpiresAt), "TOKENEXPIRESAT"); Timestamp(b.Property(x => x.CreatedAt), "CREATEDAT"); Timestamp(b.Property(x => x.UpdatedAt), "UPDATEDAT"); b.HasIndex(x => x.UserId).IsUnique().HasDatabaseName("UQ_GCC_UserId"); b.HasOne(x => x.User).WithOne(x => x.GoogleCalendarConnection).HasForeignKey<GoogleCalendarConnection>(x => x.UserId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_GCC_Users");
    }
    private static void ConfigureLineConnection(EntityTypeBuilder<LineConnection> b)
    {
        b.ToTable("LINECONNECTIONS"); b.HasKey(x => x.Id).HasName("PK_LineConnections"); Guid(b.Property(x => x.Id), "ID"); Guid(b.Property(x => x.UserId), "USERID"); Varchar(b.Property(x => x.LineUserId), "LINEUSERID", 1000, false); Bool(b.Property(x => x.NotificationsEnabled), "NOTIFICATIONSENABLED"); Timestamp(b.Property(x => x.ConnectedAt), "CONNECTEDAT"); Varchar(b.Property(x => x.SessionStateJson), "SESSIONSTATEJSON", 4000, true); Timestamp(b.Property(x => x.SessionExpiresAt), "SESSIONEXPIRESAT"); Bool(b.Property(x => x.ClassRemindersEnabled), "CLASSREMINDERSENABLED"); Number(b.Property(x => x.ClassReminderMinutes), "CLASSREMINDERMINUTES"); b.HasIndex(x => x.UserId).IsUnique().HasDatabaseName("UQ_LineConnections_UserId"); b.HasIndex(x => x.LineUserId).IsUnique().HasDatabaseName("UQ_LineConnections_LineUserId"); b.HasOne(x => x.User).WithOne(x => x.LineConnection).HasForeignKey<LineConnection>(x => x.UserId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_LineConnections_Users");
    }
    private static void ConfigureEmailNotificationPreference(EntityTypeBuilder<EmailNotificationPreference> b)
    {
        b.ToTable("EMAILNOTIFICATIONPREFERENCES"); b.HasKey(x => x.Id).HasName("PK_EmailNotificationPreferences"); Guid(b.Property(x => x.Id), "ID"); Guid(b.Property(x => x.UserId), "USERID"); Bool(b.Property(x => x.Enabled), "ENABLED"); Varchar(b.Property(x => x.RecipientEmail), "RECIPIENTEMAIL", 320, true); Bool(b.Property(x => x.ClassRemindersEnabled), "CLASSREMINDERSENABLED"); Number(b.Property(x => x.ClassReminderMinutes), "CLASSREMINDERMINUTES"); Bool(b.Property(x => x.EventRemindersEnabled), "EVENTREMINDERSENABLED"); Bool(b.Property(x => x.TaskRemindersEnabled), "TASKREMINDERSENABLED"); Bool(b.Property(x => x.BillRemindersEnabled), "BILLREMINDERSENABLED"); Timestamp(b.Property(x => x.CreatedAt), "CREATEDAT"); Timestamp(b.Property(x => x.UpdatedAt), "UPDATEDAT"); b.HasIndex(x => x.UserId).IsUnique().HasDatabaseName("UQ_ENP_UserId"); b.HasOne(x => x.User).WithOne(x => x.EmailNotificationPreference).HasForeignKey<EmailNotificationPreference>(x => x.UserId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_ENP_Users");
    }
    private static void ConfigureClassReminderSent(EntityTypeBuilder<ClassReminderSent> b)
    {
        b.ToTable("CLASSREMINDERSSENT"); b.HasKey(x => x.Id).HasName("PK_ClassRemindersSent"); Guid(b.Property(x => x.Id), "ID"); Guid(b.Property(x => x.UserId), "USERID"); Guid(b.Property(x => x.CourseId), "COURSEID"); Timestamp(b.Property(x => x.ClassDate), "CLASSDATE"); Timestamp(b.Property(x => x.SentAt), "SENTAT"); Varchar(b.Property(x => x.Channel), "CHANNEL", 100, false); b.HasIndex(x => new { x.UserId, x.CourseId, x.ClassDate, x.Channel }).IsUnique().HasDatabaseName("UQ_CRS_User_Course_Date_Channel"); b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_CRS_Users"); b.HasOne(x => x.Course).WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_CRS_Courses");
    }

    private static PropertyBuilder<Guid> Guid(PropertyBuilder<Guid> p, string n) => p.HasColumnName(n).HasConversion(GuidToString).HasColumnType("VARCHAR2(36)").HasMaxLength(36).IsUnicode(false);
    private static PropertyBuilder<Guid?> Guid(PropertyBuilder<Guid?> p, string n) => p.HasColumnName(n).HasConversion(NullableGuidToString).HasColumnType("VARCHAR2(36)").HasMaxLength(36).IsUnicode(false);
    private static PropertyBuilder Varchar(PropertyBuilder p, string n, int l, bool nullable) => p.HasColumnName(n).HasColumnType($"VARCHAR2({l})").HasMaxLength(l).IsUnicode(false).IsRequired(!nullable);
    private static PropertyBuilder<bool> Bool(PropertyBuilder<bool> p, string n) => p.HasColumnName(n).HasColumnType("NUMBER(1,0)").HasPrecision(1, 0);
    private static PropertyBuilder<T> Number<T>(PropertyBuilder<T> p, string n) where T : struct => p.HasColumnName(n).HasColumnType("NUMBER(19,0)").HasPrecision(19, 0);
    private static PropertyBuilder<int?> Number(PropertyBuilder<int?> p, string n) => p.HasColumnName(n).HasColumnType("NUMBER(19,0)").HasPrecision(19, 0);
    private static PropertyBuilder<DateTime> Timestamp(PropertyBuilder<DateTime> p, string n) => p.HasColumnName(n).HasColumnType("TIMESTAMP(6)").HasPrecision(6);
    private static PropertyBuilder<DateTime?> Timestamp(PropertyBuilder<DateTime?> p, string n) => p.HasColumnName(n).HasColumnType("TIMESTAMP(6)").HasPrecision(6);
    private static PropertyBuilder<TimeSpan> Interval(PropertyBuilder<TimeSpan> p, string n) => p.HasColumnName(n).HasColumnType("INTERVAL DAY(2) TO SECOND(6)");
}
