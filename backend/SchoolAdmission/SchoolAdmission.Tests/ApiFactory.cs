using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SchoolAdmission.Api.Data;

namespace SchoolAdmission.Tests;

public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"school_admission_test_{Guid.NewGuid():N}";

    private static string Host =>
        Environment.GetEnvironmentVariable("TEST_DB_HOST") ?? "localhost";

    private static string Port =>
        Environment.GetEnvironmentVariable("TEST_DB_PORT") ?? "5432";

    private static string User =>
        Environment.GetEnvironmentVariable("TEST_DB_USER") ?? "postgres";

    private static string Password =>
        Environment.GetEnvironmentVariable("TEST_DB_PASSWORD") ?? "postgres";

    // Pool size 50 keeps 100 concurrent requests queueing in the pool,
    // not in PostgreSQL (default max_connections is 100)
    private string ConnectionString =>
        $"Host={Host};Port={Port};Database={_dbName};Username={User};Password={Password};Maximum Pool Size=50";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Admission:HoldDurationMinutes", "10");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                using var scope = Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                NpgsqlConnection.ClearAllPools();
                db.Database.EnsureDeleted();
            }
            catch
            {
                // Cleanup must never fail the test run
            }
        }
        base.Dispose(disposing);
    }
}