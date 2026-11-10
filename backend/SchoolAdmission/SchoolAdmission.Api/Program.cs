using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Options;
using SchoolAdmission.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Settings
builder.Services.Configure<AdmissionOptions>(
    builder.Configuration.GetSection(AdmissionOptions.SectionName));

// Database

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddScoped<SeatCountService>();
builder.Services.AddScoped<ClassService>();

// API + OpenAPI
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// CORS (frontend origins come from configuration)
var origins = (builder.Configuration["Cors:AllowedOrigins"] ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(origins)
              .AllowAnyHeader()   // needed for X-Student-Id
              .AllowAnyMethod());
});

var app = builder.Build();

// Apply migrations on startup (creates the schema automatically, also in Docker)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

app.MapOpenApi();   // always on, so the evaluator can import it from Docker too

app.UseCors();
app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program { }