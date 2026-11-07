using System.Security.Cryptography;

namespace ReceteOcr.Api.Services;

/// <summary>
/// Harici bağımlılık istemeyen PBKDF2 (Rfc2898) tabanlı şifre saklama.
/// Faz 2'de ASP.NET Identity'ye geçilirse salt-arayüz değişmeden
/// replace edilebilir.
/// </summary>
public interface IPasswordHasher
{
    (string Hash, string Salt) Hash(string password);
    bool Verify(string password, string hash, string salt);
}

public class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    public (string Hash, string Salt) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    public bool Verify(string password, string hash, string salt)
    {
        var expected = Convert.FromBase64String(hash);
        var saltBytes = Convert.FromBase64String(salt);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, Iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
