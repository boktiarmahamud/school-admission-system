using System.Text.RegularExpressions;

namespace SchoolAdmission.Api.Services;

public static partial class StudentIdHeader
{
    public const string Name = "X-Student-Id";

    [GeneratedRegex(@"^\d{17}$")]
    private static partial Regex SeventeenDigits();

    // Returns null only when the header is absent and required == false
    public static string? Read(HttpRequest request, bool required)
    {
        var value = request.Headers[Name].FirstOrDefault()?.Trim();

        if (string.IsNullOrEmpty(value))
        {
            if (required) throw new AppException(400, $"The {Name} header is required.");
            return null;
        }

        if (!SeventeenDigits().IsMatch(value))
            throw new AppException(400, $"{Name} must be exactly 17 digits.");

        return value;
    }
}