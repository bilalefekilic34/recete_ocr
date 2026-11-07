namespace ReceteOcr.Api.Models;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    /// <summary>doctor | pharmacist | admin</summary>
    public string Role { get; set; } = "doctor";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AuditLog
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    /// <summary>LOGIN | UPLOAD_PRESCRIPTION | RUN_OCR | ANALYZE_PRESCRIPTION | DELETE_PRESCRIPTION</summary>
    public string Action { get; set; } = string.Empty;
    /// <summary>İşlemin hedefi (ör. reçete ID); login için null.</summary>
    public long? TargetId { get; set; }
    public string? Details { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Prescription
{
    public long Id { get; set; }
    public int UploadedByUserId { get; set; }
    public User? UploadedBy { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    /// <summary>wwwroot altındaki göreli yol (ör. uploads/12/reçete.jpg).</summary>
    public string StoragePath { get; set; } = string.Empty;
    public string? RawOcrText { get; set; }
    /// <summary>RapidOCR satır bazlı çıktısı (metin, güven, bbox) — JSON.</summary>
    public string? OcrLinesJson { get; set; }
    public double? OcrConfidenceAvg { get; set; }
    public int? ProcessingTimeMs { get; set; }
    /// <summary>Heuristic analiz çıktısı — JSON.</summary>
    public string? AnalysisJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
