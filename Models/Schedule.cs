using System.Text.Json.Serialization;

namespace back_mylife.Models
{
    public class AcademicTerm
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public string TermName { get; set; } = string.Empty; // e.g. "ภาคเรียนที่ 1/2569"
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        [JsonIgnore]
        public User? User { get; set; }
        public ICollection<Course> Courses { get; set; } = new List<Course>();
    }

    public class Course
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TermId { get; set; }
        public string CourseCode { get; set; } = string.Empty;
        public string CourseName { get; set; } = string.Empty;
        public string? Room { get; set; }
        public string? Instructor { get; set; }
        public DayOfWeek DayOfWeek { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public string? ColorHex { get; set; }

        [JsonIgnore]
        public AcademicTerm? Term { get; set; }
    }
}
