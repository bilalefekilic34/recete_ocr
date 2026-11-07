using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReceteOcr.Api.Data;

namespace ReceteOcr.Api.Controllers;

public record AuditLogDto(
    long Id, int UserId, string Username, string Action,
    long? TargetId, string? Details, DateTime CreatedAt);

[ApiController]
[Route("api/audit")]
[Authorize]
public class AuditController(AppDbContext db) : ControllerBase
{
    /// <summary>En son işlemler; sayfalı. "Hangi işlemi kim yaptı" geçmişi.</summary>
    [HttpGet]
    public async Task<ActionResult<List<AuditLogDto>>> List(
        [FromQuery] int skip = 0, [FromQuery] int take = 50,
        [FromQuery] string? action = null, [FromQuery] string? username = null,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200);

        var query = db.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(a => a.Action == action.ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(username))
            query = query.Where(a => a.Username == username);

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip(skip).Take(take)
            .Select(a => new AuditLogDto(a.Id, a.UserId, a.Username, a.Action, a.TargetId, a.Details, a.CreatedAt))
            .ToListAsync(ct);

        return Ok(items);
    }
}
