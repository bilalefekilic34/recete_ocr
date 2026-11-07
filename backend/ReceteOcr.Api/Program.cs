using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ReceteOcr.Api;
using ReceteOcr.Api.Data;
using ReceteOcr.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ----------------------------- Veritabanı --------------------------------
// Faz 1: SQLite (sıfır ekstra RAM yükü). PostgreSQL'e geçiş için yalnızca
// bu blok ve bağlantı dizesi değişir; DbContext ve servisler aynı kalır.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Sqlite")));

// ----------------------------- Servisler ---------------------------------
builder.Services.AddScoped<IUserAuthenticator, DbUserAuthenticator>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();

// Strategy Pattern: OCR sağlayıcısı (Faz 1: RapidOCR).
builder.Services.AddScoped<IOcrProvider>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILogger<RapidOcrProvider>>();
    var http = new HttpClient { BaseAddress = new Uri(cfg["Ocr:BaseUrl"] ?? RapidOcrProvider.DefaultBaseUrl), Timeout = TimeSpan.FromMinutes(3) };
    return new RapidOcrProvider(http, logger);
});

// Strategy Pattern: Analiz sağlayıcısı (Faz 1: Heuristic; Faz 2: LLM).
builder.Services.AddScoped<IPrescriptionAnalyzer, HeuristicPrescriptionAnalyzer>();

// ----------------------------- Auth --------------------------------------
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Secret"]!)),
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });

builder.Services.AddAuthorization();

// ----------------------------- CORS --------------------------------------
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
          .AllowAnyHeader()
          .AllowAnyMethod()));

builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
    });

var app = builder.Build();

// ----------------------------- Seed --------------------------------------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    await DbSeeder.SeedAsync(db, hasher, scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder"));
}

// uploads klasörü
Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "uploads"));

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
