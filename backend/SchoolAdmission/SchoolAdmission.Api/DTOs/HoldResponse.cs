namespace SchoolAdmission.Api.DTOs;

public class HoldResponse
{
    public Guid Id { get; set; }
    public Guid ClassId { get; set; }
    public string ClassName { get; set; } = string.Empty;
    public string StudentId { get; set; } = string.Empty;   // the 17-digit number
    public string Status { get; set; } = string.Empty;      // HELD / CONFIRMED / EXPIRED / CANCELLED
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
}