using System.Text.Json.Serialization;

namespace back_mylife.Models
{
    public enum RecurrenceType
    {
        None,
        Daily,
        Weekly,
        Monthly,
        Yearly
    }

    public enum TodoStatus
    {
        Pending,
        InProgress,
        Completed
    }

    public enum TodoPriority
    {
        Low,
        Medium,
        High
    }

    public class Activity
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public bool IsAllDay { get; set; } = false;
        public bool IsMultiDay { get; set; } = false;
        public bool IsIndefinite { get; set; } = false; // ไม่ระบุวัน (ไปเรื่อยๆ)
        public RecurrenceType Recurrence { get; set; } = RecurrenceType.None;
        public string? Location { get; set; }
        public int? ReminderMinutes { get; set; }
        public DateTime? ReminderSentAt { get; set; }
        public string? GoogleEventId { get; set; }

        [JsonIgnore]
        public User? User { get; set; }
    }

    public class TodoItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime TargetDate { get; set; } = DateTime.UtcNow.Date;
        public string Tag { get; set; } = "ทั่วไป";
        public RecurrenceType Recurrence { get; set; } = RecurrenceType.None;
        public TodoStatus Status { get; set; } = TodoStatus.Pending;
        public TodoPriority Priority { get; set; } = TodoPriority.Medium;
        public bool IsCompleted { get; set; } = false;
        public DateTime? ReminderSentAt { get; set; }

        [JsonIgnore]
        public User? User { get; set; }
    }

    public class TodoCompletion
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TodoItemId { get; set; }
        public DateTime CompletedDate { get; set; }
        public bool IsCompleted { get; set; }

        [JsonIgnore]
        public TodoItem? TodoItem { get; set; }
    }

    public class TodoCompletionUpdate
    {
        public DateTime Date { get; set; }
        public bool IsCompleted { get; set; }
    }

    public class Assignment
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Subject { get; set; }
        public DateTime Deadline { get; set; }
        public bool IsUrgent { get; set; } = false;
        public bool IsCompleted { get; set; } = false;

        [JsonIgnore]
        public User? User { get; set; }
    }

    public class HealthLog
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public int StepCount { get; set; }
        public int HeartRate { get; set; }
        public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

        [JsonIgnore]
        public User? User { get; set; }
    }

    public class GoogleCalendarConnection
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime TokenExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [JsonIgnore]
        public User? User { get; set; }
    }

    public class LineConnection
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public string LineUserId { get; set; } = string.Empty;
        public bool NotificationsEnabled { get; set; } = true;
        public bool ClassRemindersEnabled { get; set; } = false;
        public int ClassReminderMinutes { get; set; } = 15;
        public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
        public string? SessionStateJson { get; set; }
        public DateTime? SessionExpiresAt { get; set; }

        [JsonIgnore]
        public User? User { get; set; }
    }

    public class EmailNotificationPreference
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public bool Enabled { get; set; } = true;
        public string? RecipientEmail { get; set; }
        public bool ClassRemindersEnabled { get; set; } = true;
        public int ClassReminderMinutes { get; set; } = 15;
        public bool EventRemindersEnabled { get; set; } = true;
        public bool TaskRemindersEnabled { get; set; } = true;
        public bool BillRemindersEnabled { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [JsonIgnore]
        public User? User { get; set; }
    }

    public class ClassReminderSent
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public Guid CourseId { get; set; }
        public DateTime ClassDate { get; set; }
        public string Channel { get; set; } = "line";
        public DateTime SentAt { get; set; } = DateTime.UtcNow;

        [JsonIgnore]
        public User? User { get; set; }
        [JsonIgnore]
        public Course? Course { get; set; }
    }
}
