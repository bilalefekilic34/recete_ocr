using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ReceteOcr.Api.Data;
using ReceteOcr.Api.Models;

namespace ReceteOcr.Api.Services;

/// <summary>
/// Her anlamlı işlemi AuditLogs tablosuna yazan servis.
/// "Hangi işlemi kimin yaptığı" kaydı bu sınıftan geçer.
/// </summary>
public interface IAuditService
{
    Task LogAsync(int userId, string username, string action, long? targetId = null, object? details = null, CancellationToken ct = default);
}

public class AuditService(IServiceProvider provider) : IAuditService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task LogAsync(int userId, string username, string action, long? targetId = null, object? details = null, CancellationToken ct = default)
    {
        // Audit kaydı asla ana akışı patlatmasın; hata loglanır.
        try
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Username = username,
                Action = action,
                TargetId = targetId,
                Details = details is null ? null : JsonSerializer.Serialize(details, JsonOpts),
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            provider.GetRequiredService<ILogger<AuditService>>()
                .LogError(ex, "Audit log yazılamadı (user={UserId}, action={Action})", userId, action);
        }
    }
}
