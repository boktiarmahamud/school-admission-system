using SchoolAdmission.Api.Options;

var builder = WebApplication.CreateBuilder(args);

// Settings
builder.Services.Configure<AdmissionOptions>(
    builder.Configuration.GetSection(AdmissionOptions.SectionName));

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

app.MapOpenApi();   // always on, so the evaluator can import it from Docker too

app.UseCors();
app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program { }