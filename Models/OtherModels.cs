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

        [JsonIgnore]
        public User? User { get; set; }
    }

    public class TodoItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public string Title { get; set; } = string.Empty;
        public DateTime TargetDate { get; set; } = DateTime.UtcNow.Date;
        public string Tag { get; set; } = "ทั่วไป";
        public bool IsCompleted { get; set; } = false;

        [JsonIgnore]
        public User? User { get; set; }
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
}
