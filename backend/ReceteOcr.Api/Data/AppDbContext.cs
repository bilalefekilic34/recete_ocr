using Microsoft.EntityFrameworkCore;
using ReceteOcr.Api.Models;

namespace ReceteOcr.Api.Data;

/// <summary>
/// SQLite üzerinde çalışır; provider yalnızca Program.cs'de seçilir,
/// böylece ileride PostgreSQL'e geçmek için sadece bağlantı dizesi ve
/// UseDatabase çağrısının değişmesi yeterlidir.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Prescription> Prescriptions => Set<Prescription>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
            e.Property(u => u.Username).IsRequired().HasMaxLength(64);
            e.Property(u => u.FullName).HasMaxLength(128);
            e.Property(u => u.Role).HasMaxLength(32);
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.Property(a => a.Action).IsRequired().HasMaxLength(64);
            e.Property(a => a.Username).HasMaxLength(64);
            e.Property(a => a.Details).HasMaxLength(4000);
            e.HasIndex(a => a.CreatedAt);
            e.HasOne<User>()
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Prescription>(e =>
        {
            e.Property(p => p.OriginalFileName).HasMaxLength(256);
            e.Property(p => p.StoragePath).HasMaxLength(512);
            e.HasOne(p => p.UploadedBy)
                .WithMany()
                .HasForeignKey(p => p.UploadedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(p => p.CreatedAt);
        });
    }
}
