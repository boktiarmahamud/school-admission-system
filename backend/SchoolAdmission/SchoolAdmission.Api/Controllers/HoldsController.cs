using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using SchoolAdmission.Api.DTOs;
using SchoolAdmission.Api.Services;

namespace SchoolAdmission.Api.Controllers;

[ApiController]
public class HoldsController : ControllerBase
{
    private const string IdPattern = @"^\d{17}$";
    private const string IdError = "X-Student-Id must be exactly 17 digits.";

    private readonly HoldService _holds;

    public HoldsController(HoldService holds) => _holds = holds;

    [HttpPost("classes/{classId:guid}/holds")]
    public async Task<ActionResult<HoldResponse>> Create(
        Guid classId,
        [FromHeader(Name = "X-Student-Id")]
        [RegularExpression(IdPattern, ErrorMessage = IdError)] string studentId,
        CancellationToken ct)
    {
        var hold = await _holds.CreateAsync(classId, studentId.Trim(), ct);
        return CreatedAtAction(nameof(GetById), new { holdId = hold.Id }, hold);
    }

    [HttpGet("holds/{holdId:guid}")]
    public async Task<ActionResult<HoldResponse>> GetById(
        Guid holdId,
        [FromHeader(Name = "X-Student-Id")]
        [RegularExpression(IdPattern, ErrorMessage = IdError)] string? studentId,
        CancellationToken ct)
        => Ok(await _holds.GetAsync(holdId, studentId?.Trim(), ct));

    [HttpPost("holds/{holdId:guid}/confirm")]
    public async Task<ActionResult<HoldResponse>> Confirm(
        Guid holdId,
        [FromHeader(Name = "X-Student-Id")]
        [RegularExpression(IdPattern, ErrorMessage = IdError)] string studentId,
        CancellationToken ct)
        => Ok(await _holds.ConfirmAsync(holdId, studentId.Trim(), ct));

    [HttpDelete("holds/{holdId:guid}")]
    public async Task<ActionResult<HoldResponse>> Cancel(
        Guid holdId,
        [FromHeader(Name = "X-Student-Id")]
        [RegularExpression(IdPattern, ErrorMessage = IdError)] string studentId,
        CancellationToken ct)
        => Ok(await _holds.CancelAsync(holdId, studentId.Trim(), ct));

    [HttpGet("me/holds")]
    public async Task<ActionResult<List<HoldResponse>>> MyHolds(
        [FromHeader(Name = "X-Student-Id")]
        [RegularExpression(IdPattern, ErrorMessage = IdError)] string studentId,
        CancellationToken ct)
        => Ok(await _holds.GetMyHoldsAsync(studentId.Trim(), ct));
}