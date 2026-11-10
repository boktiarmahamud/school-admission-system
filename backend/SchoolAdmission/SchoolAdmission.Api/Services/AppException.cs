using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace SchoolAdmission.Api.Services;

public class AppException : Exception
{
    public int StatusCode { get; }

    public AppException(int statusCode, string message) : base(message)
        => StatusCode = statusCode;
}

public class AppExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not AppException app) return false;

        context.Response.StatusCode = app.StatusCode;
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = app.StatusCode,
            Title = app.StatusCode switch
            {
                400 => "Bad Request",
                404 => "Not Found",
                409 => "Conflict",
                _ => "Error"
            },
            Detail = app.Message
        }, ct);
        return true;
    }
}