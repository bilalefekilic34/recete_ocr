using Microsoft.EntityFrameworkCore;
using ReceteOcr.Api.Data;
using ReceteOcr.Api.Services;

namespace ReceteOcr.Api;

public static class DbSeeder
{
    /// <summary>Veritabanını oluşturur ve ilk açılışta örnek kullanıcıları seed eder.</summary>
    public static async Task SeedAsync(AppDbContext db, IPasswordHasher hasher, ILogger logger)
    {
        await db.Database.EnsureCreatedAsync();

        if (await db.Users.AnyAsync()) return;

        var users = new List<Models.User>();
        foreach (var (username, password, fullName, role) in new[]
        {
            ("dr_ahmet", "Ahmet.2024", "Dr. Ahmet Yılmaz", "doctor"),
            ("ecz_ayse", "Ayse.2024", "Ecz. Ayşe Demir", "pharmacist"),
            ("admin", "Admin.2024", "Sistem Yöneticisi", "admin"),
        })
        {
            var (hash, salt) = hasher.Hash(password);
            users.Add(new Models.User { Username = username, PasswordHash = hash, PasswordSalt = salt, FullName = fullName, Role = role });
        }

        db.Users.AddRange(users);
        await db.SaveChangesAsync();
        logger.LogInformation("Seed tamamlandı: 3 örnek kullanıcı oluşturuldu (dr_ahmet, ecz_ayse, admin).");
    }
}
