namespace SchoolAdmission.Api.DTOs;

public class ClassResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int TotalSeats { get; set; }
    public int HeldSeats { get; set; }
    public int ConfirmedSeats { get; set; }
    public int AvailableSeats { get; set; }
    public DateTime CreatedAt { get; set; }
}