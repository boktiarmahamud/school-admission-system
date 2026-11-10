using Microsoft.AspNetCore.Mvc;
using SchoolAdmission.Api.DTOs;
using SchoolAdmission.Api.Services;

namespace SchoolAdmission.Api.Controllers;

[ApiController]
[Route("classes")]
public class ClassesController : ControllerBase
{
    private readonly ClassService _classes;

    public ClassesController(ClassService classes) => _classes = classes;

    [HttpPost]
    public async Task<ActionResult<ClassResponse>> Create(
        CreateClassRequest request, CancellationToken ct)
    {
        var created = await _classes.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { classId = created.Id }, created);
    }

    [HttpGet]
    public async Task<ActionResult<List<ClassResponse>>> GetAll(CancellationToken ct)
        => Ok(await _classes.GetAllAsync(ct));

    [HttpGet("{classId:guid}")]
    public async Task<ActionResult<ClassResponse>> GetById(Guid classId, CancellationToken ct)
    {
        var result = await _classes.GetByIdAsync(classId, ct);
        return result is null ? NotFound() : Ok(result);
    }
}