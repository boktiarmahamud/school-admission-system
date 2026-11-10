namespace SchoolAdmission.Api.Entities;

public class Student
{
    public Guid Id { get; set; }
    public string BirthRegistrationNumber { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public List<Hold> Holds { get; set; } = new();
}