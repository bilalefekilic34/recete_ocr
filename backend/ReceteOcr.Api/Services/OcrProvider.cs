using System.Text.Json.Serialization;

namespace ReceteOcr.Api.Services;

/// <summary>OCR servisinden gelen satır bazlı sonuç (RapidOCR çıktı sözleşmesi).</summary>
public record OcrLineDto(
    string Text,
    double Confidence,
    List<List<int>> Bbox);

/// <summary>OCR sağlayıcısının üniform çıktısı.</summary>
public record OcrResultDto(
    List<OcrLineDto> Lines,
    string RawText,
    double ConfidenceAvg,
    int ProcessingTimeMs,
    string PreprocessMode,
    string ExecutionProvider,
    int ImageWidth,
    int ImageHeight);

/// <summary>
/// Strategy Pattern: Faz 1'de RapidOCR; Faz 2'de PaddleOCR / TrOCR / VLM
/// sağlayıcıları bu arayüzü uygulayarak eklenir.
/// </summary>
public interface IOcrProvider
{
    string Name { get; }
    Task<OcrResultDto> RecognizeAsync(Stream image, string fileName, string preprocessMode, CancellationToken ct = default);
}

/// <summary>
/// /ocr-service (FastAPI + RapidOCR) HTTP üzerinden çağıran sağlayıcı.
/// </summary>
public class RapidOcrProvider(HttpClient http, ILogger<RapidOcrProvider> logger) : IOcrProvider
{
    public const string DefaultBaseUrl = "http://127.0.0.1:8100";

    public string Name => "RapidOCR (onnxruntime)";

    public async Task<OcrResultDto> RecognizeAsync(Stream image, string fileName, string preprocessMode, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(image);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg");
        content.Add(streamContent, "file", fileName);
        content.Add(new StringContent(preprocessMode), "preprocess");

        var baseUrl = http.BaseAddress?.ToString().TrimEnd('/') ?? DefaultBaseUrl;
        using var response = await http.PostAsync($"{baseUrl}/ocr", content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("OCR servisi hata döndürdü: {Status} {Body}", (int)response.StatusCode, body);
            throw new HttpRequestException($"OCR servisi hata döndürdü ({(int)response.StatusCode}): {body}");
        }

        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            // Python (FastAPI) yanıtı snake_case döndürür: confidence_avg, raw_text, ...
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
        };
        options.Converters.Add(new JsonStringEnumConverter());

        var result = System.Text.Json.JsonSerializer.Deserialize<OcrResultDto>(body, options)
            ?? throw new InvalidOperationException("OCR yanıtı çözümlenemedi");
        return result;
    }
}
