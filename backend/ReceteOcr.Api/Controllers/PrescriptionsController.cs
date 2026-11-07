using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReceteOcr.Api.Data;
using ReceteOcr.Api.Models;
using ReceteOcr.Api.Services;

namespace ReceteOcr.Api.Controllers;

public record OcrLineView(string Text, double Confidence, List<List<int>> Bbox);

public record PrescriptionListItemDto(
    long Id, string OriginalFileName, string UploadedByUsername, DateTime CreatedAt,
    double? OcrConfidenceAvg, int? ProcessingTimeMs, bool HasAnalysis);

public record PrescriptionDetailDto(
    long Id, string OriginalFileName, string StoragePath, string UploadedByUsername,
    string? RawOcrText, string? OcrLinesJson, double? OcrConfidenceAvg, int? ProcessingTimeMs,
    string? AnalysisJson, DateTime CreatedAt);

[ApiController]
[Route("api/prescriptions")]
[Authorize]
public class PrescriptionsController(
    AppDbContext db,
    IOcrProvider ocrProvider,
    IPrescriptionAnalyzer analyzer,
    IAuditService audit,
    IWebHostEnvironment env,
    ILogger<PrescriptionsController> logger) : ControllerBase
{
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png"];
    private const long MaxFileSize = 15 * 1024 * 1024; // 15 MB

    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Username => User.FindFirstValue(ClaimTypes.Name) ?? "bilinmiyor";

    [HttpGet]
    public async Task<ActionResult<List<PrescriptionListItemDto>>> List(CancellationToken ct)
    {
        var items = await db.Prescriptions
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new PrescriptionListItemDto(
                p.Id, p.OriginalFileName, p.UploadedBy!.Username, p.CreatedAt,
                p.OcrConfidenceAvg, p.ProcessingTimeMs, p.AnalysisJson != null))
            .ToListAsync(ct);
        return Ok(items);
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<PrescriptionDetailDto>> Get(long id, CancellationToken ct)
    {
        var p = await db.Prescriptions.Include(x => x.UploadedBy).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound(new { message = "Reçete bulunamadı." });

        return Ok(new PrescriptionDetailDto(
            p.Id, p.OriginalFileName, p.StoragePath, p.UploadedBy!.Username,
            p.RawOcrText, p.OcrLinesJson, p.OcrConfidenceAvg, p.ProcessingTimeMs,
            p.AnalysisJson, p.CreatedAt));
    }

    /// <summary>Reçete görselini sunar (frontend önizlemesi için).</summary>
    [HttpGet("{id:long}/image")]
    public async Task<IActionResult> GetImage(long id, CancellationToken ct)
    {
        var p = await db.Prescriptions.FindAsync([id], ct);
        if (p is null) return NotFound();
        var full = Path.Combine(env.ContentRootPath, p.StoragePath);
        if (!System.IO.File.Exists(full)) return NotFound(new { message = "Görsel dosyası diskte bulunamadı." });

        var contentType = p.StoragePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? "image/png" : "image/jpeg";
        return PhysicalFile(full, contentType);
    }

    /// <summary>Reçete görselini yükler (JPG/PNG), diske kaydeder ve audit yazar.</summary>
    [HttpPost]
    [RequestSizeLimit(MaxFileSize)]
    public async Task<ActionResult<PrescriptionDetailDto>> Upload(IFormFile file, CancellationToken ct)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            return BadRequest(new { message = "Sadece JPG/PNG dosyaları yüklenebilir." });
        if (file.Length is 0 or > MaxFileSize)
            return BadRequest(new { message = "Dosya boş veya 15 MB sınırını aşıyor." });

        var prescription = new Prescription
        {
            UploadedByUserId = UserId,
            OriginalFileName = Path.GetFileName(file.FileName),
            CreatedAt = DateTime.UtcNow,
        };

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();

        // Görsel bütünlüğü: PNG/JPG magic byte kontrolü (Faz 1 için yeterli)
        if (!ImageProbe.IsRecognized(bytes))
            return BadRequest(new { message = "Görsel çözümlenemedi; bozuk dosya olabilir." });

        db.Prescriptions.Add(prescription);
        await db.SaveChangesAsync(ct); // Id'yi almak için

        var dir = Path.Combine(env.ContentRootPath, "uploads", prescription.Id.ToString());
        Directory.CreateDirectory(dir);
        var storedName = $"original{ext}";
        var relative = Path.Combine("uploads", prescription.Id.ToString(), storedName);
        await System.IO.File.WriteAllBytesAsync(Path.Combine(dir, storedName), bytes, ct);
        prescription.StoragePath = relative;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(UserId, Username, AuditActions.UploadPrescription, prescription.Id,
            new { file.FileName, sizeBytes = bytes.Length }, ct);

        return CreatedAtAction(nameof(Get), new { id = prescription.Id }, new PrescriptionDetailDto(
            prescription.Id, prescription.OriginalFileName, prescription.StoragePath, Username,
            null, null, null, null, null, prescription.CreatedAt));
    }

    /// <summary>RapidOCR çalıştırır, sonucu kaydeder ve audit yazar.</summary>
    [HttpPost("{id:long}/ocr")]
    public async Task<IActionResult> RunOcr(long id, [FromQuery] string preprocess = "raw", CancellationToken ct = default)
    {
        if (preprocess is not ("raw" or "enhanced"))
            return BadRequest(new { message = "preprocess 'raw' veya 'enhanced' olmalı." });

        var p = await db.Prescriptions.FindAsync([id], ct);
        if (p is null) return NotFound(new { message = "Reçete bulunamadı." });

        var fullPath = Path.Combine(env.ContentRootPath, p.StoragePath);
        if (!System.IO.File.Exists(fullPath))
            return NotFound(new { message = "Görsel dosyası diskte bulunamadı." });

        OcrResultDto result;
        try
        {
            await using var stream = System.IO.File.OpenRead(fullPath);
            result = await ocrProvider.RecognizeAsync(stream, p.OriginalFileName, preprocess, ct);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "OCR servisine ulaşılamadı (reçete {Id})", id);
            return StatusCode(502, new { message = "OCR servisine ulaşılamadı. /ocr-service çalışıyor mu?" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OCR hatası (reçete {Id})", id);
            return StatusCode(500, new { message = "OCR işlemi başarısız oldu." });
        }

        p.RawOcrText = result.RawText;
        p.OcrConfidenceAvg = result.ConfidenceAvg;
        p.ProcessingTimeMs = result.ProcessingTimeMs;
        p.OcrLinesJson = JsonSerializer.Serialize(result.Lines);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(UserId, Username, AuditActions.RunOcr, id,
            new { provider = ocrProvider.Name, preprocess, lines = result.Lines.Count, avgConfidence = result.ConfidenceAvg, processingTimeMs = result.ProcessingTimeMs }, ct);

        return Ok(result);
    }

    /// <summary>Heuristic analizi çalıştırır, sonucu AnalysisJson'a kaydeder.</summary>
    [HttpPost("{id:long}/analyze")]
    public async Task<IActionResult> Analyze(long id, CancellationToken ct)
    {
        var p = await db.Prescriptions.FindAsync([id], ct);
        if (p is null) return NotFound(new { message = "Reçete bulunamadı." });
        if (string.IsNullOrEmpty(p.RawOcrText) || p.OcrLinesJson is null)
            return BadRequest(new { message = "Önce OCR çalıştırılmalı." });

        var lines = JsonSerializer.Deserialize<List<OcrLineDto>>(p.OcrLinesJson)!
            .Select(l => new AnalyzerLine(l.Text, l.Confidence));

        var analysis = analyzer.Analyze(lines, p.OcrConfidenceAvg ?? 0);

        p.AnalysisJson = JsonSerializer.Serialize(analysis, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false,
        });
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(UserId, Username, AuditActions.AnalyzePrescription, id,
            new { analyzer = analyzer.Name, drugLineCount = analysis.DrugLines.Count, warningCount = analysis.Warnings.Count }, ct);

        return Ok(analysis);
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var p = await db.Prescriptions.FindAsync([id], ct);
        if (p is null) return NotFound(new { message = "Reçete bulunamadı." });

        var dir = Path.Combine(env.ContentRootPath, "uploads", id.ToString());
        if (Directory.Exists(dir))
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException ex) { logger.LogWarning(ex, "Reçete {Id} dosyaları silinemedi", id); }
        }

        db.Prescriptions.Remove(p);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(UserId, Username, AuditActions.DeletePrescription, id,
            new { p.OriginalFileName }, ct);

        return NoContent();
    }
}

/// <summary>Yüklenen dosyanın gerçekten bir görsel olduğunu doğrulayan küçük yardımcı.</summary>
internal static class ImageProbe
{
    public static bool IsRecognized(byte[] bytes)
    {
        if (bytes.Length < 12) return false;
        // PNG: 89 50 4E 47
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return true;
        // JPEG: FF D8 FF
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return true;
        return false;
    }
}
