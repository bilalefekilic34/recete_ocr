using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReceteOcr.Api.Services;

namespace ReceteOcr.Api.Controllers;

public record LoginRequest(string Username, string Password);
public record LoginResponse(string Token, DateTime ExpiresAt, int UserId, string Username, string FullName, string Role);

[ApiController]
[Route("api/auth")]
public class AuthController(IUserAuthenticator authenticator, IJwtTokenService tokenService, IAuditService audit, ILogger<AuthController> logger) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var user = await authenticator.AuthenticateAsync(request.Username?.Trim() ?? "", request.Password ?? "", ct);
        if (user is null)
        {
            logger.LogInformation("Başarısız giriş denemesi: {Username}", request.Username);
            return Unauthorized(new { message = "Kullanıcı adı veya şifre hatalı." });
        }

        var (token, expiresAt) = tokenService.CreateToken(user);
        await audit.LogAsync(user.Id, user.Username, AuditActions.Login, details: "Giriş başarılı", ct: ct);

        return Ok(new LoginResponse(token, expiresAt, user.Id, user.Username, user.FullName, user.Role));
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        return Ok(new
        {
            UserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!),
            Username = User.FindFirstValue(ClaimTypes.Name),
            Role = User.FindFirstValue(ClaimTypes.Role),
        });
    }
}

/// <summary>Audit Action sabitleri.</summary>
public static class AuditActions
{
    public const string Login = "LOGIN";
    public const string UploadPrescription = "UPLOAD_PRESCRIPTION";
    public const string RunOcr = "RUN_OCR";
    public const string AnalyzePrescription = "ANALYZE_PRESCRIPTION";
    public const string DeletePrescription = "DELETE_PRESCRIPTION";
}
