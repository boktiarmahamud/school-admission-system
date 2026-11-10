namespace SchoolAdmission.Api.Entities;

public class Hold
{
    public Guid Id { get; set; }
    public Guid ClassId { get; set; }
    public Guid StudentId { get; set; }
    public HoldStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }

    public SchoolClass? Class { get; set; }
    public Student? Student { get; set; }
}