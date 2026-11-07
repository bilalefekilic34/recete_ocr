using Microsoft.EntityFrameworkCore;
using ReceteOcr.Api.Data;
using ReceteOcr.Api.Models;

namespace ReceteOcr.Api.Services;

/// <summary>Kullanıcı adı + şifre ile doğrulama yapan servis.</summary>
public interface IUserAuthenticator
{
    Task<User?> AuthenticateAsync(string username, string password, CancellationToken ct = default);
}

public class DbUserAuthenticator(IServiceProvider provider, IPasswordHasher hasher) : IUserAuthenticator
{
    public async Task<User?> AuthenticateAsync(string username, string password, CancellationToken ct = default)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);
        if (user is null) return null;
        return hasher.Verify(password, user.PasswordHash, user.PasswordSalt) ? user : null;
    }
}
