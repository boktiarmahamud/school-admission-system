using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.DTOs;

namespace SchoolAdmission.Tests;

public class AdmissionApiTests : IClassFixture<ApiFactory>
{
    private static long _studentCounter = 10_000_000_000;

    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public AdmissionApiTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ---------- helpers ----------

    // Unique 17-digit ID per call, so tests never collide on the one-active-hold rule
    private static string NewStudentId() =>
        Interlocked.Increment(ref _studentCounter).ToString("D17");

    private async Task<ClassResponse> CreateClassAsync(int seats)
    {
        var res = await _client.PostAsJsonAsync("/classes",
            new { name = $"Test class {Guid.NewGuid():N}", totalSeats = seats });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<ClassResponse>())!;
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? studentId)
    {
        var req = new HttpRequestMessage(method, url);
        if (studentId is not null) req.Headers.Add("X-Student-Id", studentId);
        return _client.SendAsync(req);
    }

    private Task<HttpResponseMessage> HoldAsync(Guid classId, string studentId) =>
        SendAsync(HttpMethod.Post, $"/classes/{classId}/holds", studentId);

    private async Task<HoldResponse> HoldOkAsync(Guid classId, string studentId)
    {
        var res = await HoldAsync(classId, studentId);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<HoldResponse>())!;
    }

    private async Task<ClassResponse> GetClassAsync(Guid classId) =>
        (await _client.GetFromJsonAsync<ClassResponse>($"/classes/{classId}"))!;

    // Simulates time passing by moving ExpiresAt into the past
    private async Task ExpireHoldAsync(Guid holdId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Holds.Where(h => h.Id == holdId)
            .ExecuteUpdateAsync(s => s.SetProperty(
                h => h.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
    }

    // ---------- required test 1 ----------

    [Fact]
    public async Task Create_and_list_classes()
    {
        var created = await CreateClassAsync(120);

        Assert.Equal(120, created.TotalSeats);
        Assert.Equal(120, created.AvailableSeats);

        var all = await _client.GetFromJsonAsync<List<ClassResponse>>("/classes");

        var found = Assert.Single(all!, c => c.Id == created.Id);
        Assert.Equal(120, found.TotalSeats);
        Assert.Equal(0, found.HeldSeats);
        Assert.Equal(0, found.ConfirmedSeats);
    }

    // ---------- required test 2 ----------

    [Fact]
    public async Task Hold_then_confirm_a_seat()
    {
        var cls = await CreateClassAsync(10);
        var student = NewStudentId();

        var hold = await HoldOkAsync(cls.Id, student);
        Assert.Equal("HELD", hold.Status);

        var afterHold = await GetClassAsync(cls.Id);
        Assert.Equal(1, afterHold.HeldSeats);
        Assert.Equal(9, afterHold.AvailableSeats);

        var confirmRes = await SendAsync(HttpMethod.Post, $"/holds/{hold.Id}/confirm", student);
        Assert.Equal(HttpStatusCode.OK, confirmRes.StatusCode);
        var confirmed = await confirmRes.Content.ReadFromJsonAsync<HoldResponse>();
        Assert.Equal("CONFIRMED", confirmed!.Status);
        Assert.NotNull(confirmed.ConfirmedAt);

        var afterConfirm = await GetClassAsync(cls.Id);
        Assert.Equal(0, afterConfirm.HeldSeats);
        Assert.Equal(1, afterConfirm.ConfirmedSeats);
        Assert.Equal(9, afterConfirm.AvailableSeats);
    }

    // ---------- required test 3: concurrency ----------

    [Fact]
    public async Task Concurrent_requests_never_overbook()
    {
        const int seats = 10;
        const int requests = 100;

        var cls = await CreateClassAsync(seats);

        // All requests wait at the gate, then start together
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable.Range(0, requests).Select(async _ =>
        {
            var student = NewStudentId();           // 100 different students
            await gate.Task;
            using var res = await HoldAsync(cls.Id, student);
            return res.StatusCode;
        }).ToList();

        gate.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(seats, results.Count(s => s == HttpStatusCode.Created));
        Assert.Equal(requests - seats, results.Count(s => s == HttpStatusCode.Conflict));
        Assert.DoesNotContain(results,
            s => s != HttpStatusCode.Created && s != HttpStatusCode.Conflict);

        var final = await GetClassAsync(cls.Id);
        Assert.Equal(seats, final.HeldSeats);
        Assert.Equal(0, final.ConfirmedSeats);
        Assert.Equal(0, final.AvailableSeats);      // never negative = no overbooking
    }

    // ---------- extra tests ----------

    [Fact]
    public async Task Expired_hold_does_not_consume_a_seat()
    {
        var cls = await CreateClassAsync(1);
        var hold = await HoldOkAsync(cls.Id, NewStudentId());

        Assert.Equal(0, (await GetClassAsync(cls.Id)).AvailableSeats);

        await ExpireHoldAsync(hold.Id);

        Assert.Equal(1, (await GetClassAsync(cls.Id)).AvailableSeats);

        // Another student can now take the seat
        await HoldOkAsync(cls.Id, NewStudentId());
    }

    [Fact]
    public async Task Another_student_cannot_confirm_my_hold()
    {
        var cls = await CreateClassAsync(5);
        var hold = await HoldOkAsync(cls.Id, NewStudentId());

        var res = await SendAsync(HttpMethod.Post, $"/holds/{hold.Id}/confirm", NewStudentId());

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Cancelled_hold_frees_the_seat()
    {
        var cls = await CreateClassAsync(1);
        var student = NewStudentId();
        var hold = await HoldOkAsync(cls.Id, student);

        var res = await SendAsync(HttpMethod.Delete, $"/holds/{hold.Id}", student);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("CANCELLED",
            (await res.Content.ReadFromJsonAsync<HoldResponse>())!.Status);

        Assert.Equal(1, (await GetClassAsync(cls.Id)).AvailableSeats);
    }

    [Fact]
    public async Task Confirm_after_expiry_is_rejected()
    {
        var cls = await CreateClassAsync(5);
        var student = NewStudentId();
        var hold = await HoldOkAsync(cls.Id, student);

        await ExpireHoldAsync(hold.Id);

        var res = await SendAsync(HttpMethod.Post, $"/holds/{hold.Id}/confirm", student);

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal(0, (await GetClassAsync(cls.Id)).ConfirmedSeats);
    }

    [Fact]
    public async Task Confirming_twice_does_not_double_count()
    {
        var cls = await CreateClassAsync(5);
        var student = NewStudentId();
        var hold = await HoldOkAsync(cls.Id, student);

        await SendAsync(HttpMethod.Post, $"/holds/{hold.Id}/confirm", student);
        var second = await SendAsync(HttpMethod.Post, $"/holds/{hold.Id}/confirm", student);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(1, (await GetClassAsync(cls.Id)).ConfirmedSeats);
    }
}