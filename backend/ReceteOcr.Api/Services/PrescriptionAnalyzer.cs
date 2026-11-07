using System.Text.RegularExpressions;

namespace ReceteOcr.Api.Services;

// ---------------------------------------------------------------------------
// Heuristic / kural tabanlı reçete analizi (Faz 1)
// ---------------------------------------------------------------------------
// Dış LLM'e bağımlı değildir; tamamen deterministik regex + anahtar kelime
// kurallarıyla çalışır. Faz 2'de LlmPrescriptionAnalyzer aynı arayüzü
// uygulayarak devreye alınabilir.
//
// Gerçek reçete testlerinden gelen düzeltmeler:
// - Protokol/Reçete No adayları en az 4 ardışık rakam veya tireli sayı grubu
//   içermelidir; "Tarih/Protokol" gibi form etiketleri hariç tutulur.
// - İlaç adayı araması İLAÇLAR/Rp başlığı tespit edildiyse yalnızca o satırın
//   altında yapılır (kaşe/teşhis satırları dışarıda kalır).
// - Dozaj+birim+sıklık üçü birden boşsa satır tek başına ilaç adayı olamaz.
// - Bozuk OCR tarihleri (ör. "2912/2014" ← "27/12/2014") için ikincil desen.
// - El yazısı şekil karışmalarını (oo→00, fu→flk, ny/oy/ry→mg) yalnızca
//   analiz eşleştirmesinde normalize eden yardımcı (ham metin korunur).

public record AnalyzerLine(string Text, double Confidence);

public record DrugLineDto(
    string Text,
    double Confidence,
    List<string> Dosages,
    List<string> Units,
    List<string> FrequencyHints);

public record LowConfidenceLineDto(string Text, double Confidence);

public record PrescriptionAnalysis(
    List<string> DateCandidates,
    List<string> ProtocolNoCandidates,
    List<string> TcNoCandidates,
    List<string> PrescriptionNoCandidates,
    List<DrugLineDto> DrugLines,
    List<LowConfidenceLineDto> LowConfidenceLines,
    List<string> Warnings,
    string Summary);

public interface IPrescriptionAnalyzer
{
    string Name { get; }
    PrescriptionAnalysis Analyze(IEnumerable<AnalyzerLine> lines, double overallConfidence);
}

public class HeuristicPrescriptionAnalyzer : IPrescriptionAnalyzer
{
    public string Name => "Heuristic v2";

    private const double LowConfidenceThreshold = 0.65;

    // 1x1, 2x1, 2x2, 1x2x3 gibi kullanım kalıpları
    private static readonly Regex DosagePattern =
        new(@"\b\d{1,2}\s*[xX×]\s*\d{1,2}(?:\s*[xX×]\s*\d{1,2})?\b", RegexOptions.Compiled);

    // 500 mg, 10 ml, 1 g ...
    private static readonly Regex DoseAmountPattern =
        new(@"\b\d{1,4}(?:[.,]\d{1,2})?\s*(?:mg|ml|mcg|µg|gr?|ünite|iu)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Standart dd/MM/yyyy ve dd.MM.yyyy
    private static readonly Regex DatePattern =
        new(@"\b\d{1,2}[.\/-]\d{1,2}[.\/-]\d{2,4}\b", RegexOptions.Compiled);

    // Bozuk OCR tarihi: ilk ayırıcı rakama dönüşmüş — "2912/2014" ← "27/12/2014".
    // 4 hane + ayırıcı + (19|20)yy. Ayrı bir uyarıyla işaretlenir.
    private static readonly Regex FuzzyYearDatePattern =
        new(@"\b\d{4}[/.](19|20)\d{2}\b", RegexOptions.Compiled);

    private static readonly Regex TcPattern = new(@"\b\d{11}\b", RegexOptions.Compiled);

    private static readonly Regex ProtocolPattern =
        new(@"(?:protokol|proto)\s*(?:no|numaras[ıi])?\s*[:.\-]?\s*([A-Z0-9\/\-]{3,20})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PrescriptionNoPattern =
        new(@"(?:re[çc]ete|recete)\s*(?:no|numaras[ıi])?\s*[:.\-]?\s*([A-Z0-9\/\-]{3,20})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Geçerli numara adayı: en az 4 ardışık rakam VEYA tireyle ayrılmış sayı grupları
    private static readonly Regex StrongNumberPattern =
        new(@"\d{4,}|\d{2,}\s*-\s*\d{2,}", RegexOptions.Compiled);

    // Form etiketleri — numara adayı olarak filtrelenir
    private static readonly string[] FormLabelKeywords =
    [
        "tarih", "protokol", "proto", "kimlik", "teshis", "teşhis",
        "ilaclar", "ilaçlar", "hastanin", "hastanın", "hasta", "recete", "reçete",
    ];

    private static readonly string[] UnitKeywords =
    [
        "tb", "tab", "tablet", "kapsül", "kapsul", "kap", "şurup", "surup",
        "damla", "ampul", "flakon", "flk", "supozituvar", "krema", "krem", "merhem",
        "sprey", "pomad", "jel", "toz", "pastil",
    ];

    private static readonly string[] FrequencyHints =
    [
        "günde", "gunde", "günde bir", "günde 1", "günde iki", "günde 2", "günde üç", "günde 3",
        "sabah", "akşam", "aksam", "öğle", "ogle", "yatmadan", "aç karnına", "ac karnina",
        "tok karnına", "tok karnina", "yemekten sonra", "yemekten önce", "yemekten once",
        "8 saatte bir", "12 saatte bir", "24 saatte bir", "saatte bir",
        "günlük", "gunluk", "haftada", "haftalık", "haftalik", "idame", "gerekirse",
    ];

    // İlaç bölümü başlığı: bu satır bulunursa ilaç adayları yalnızca altında aranır
    private static readonly Regex DrugSectionHeaderPattern =
        new(@"^(?:rp\.?|İLAÇLAR|ILACLAR|ilaçlar|ilaclar)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Kaşe / üst bilgi satırları — ilaç bölümü altında bile ilaç adayı olamaz
    private static readonly string[] StampKeywords =
    [
        "hastanes", "hastanesi", "bakanligi", "bakanlığı", "uzm.dr", "uzm. dr", "dr.",
        "hastane", "üniversite", "universite", "aile hekimliği", "saglik", "sağlık",
    ];

    /// <summary>
    /// El yazısı OCR şekil karışmalarını yalnızca analiz eşleştirmesi için düzeltir;
    /// ham metin ve çıktıdaki satır metinleri korunur.
    /// </summary>
    private static string NormalizeForAnalysis(string text)
    {
        var t = text;
        // Yaygın şekil karışmaları
        t = t.Replace("oo", "00", StringComparison.OrdinalIgnoreCase);
        // Sondaki "fu" büyük olasılıkla "flk" (flakon) adayı
        if (t.EndsWith("fu", StringComparison.OrdinalIgnoreCase))
            t = t[..^2] + "flk";
        // Birim yakınlıkları: ny/oy/ry → mg (el yazısında benzer şekiller)
        t = Regex.Replace(t, @"\b(\d{1,4})\s*(?:ny|oy|ry)\b", "$1mg", RegexOptions.IgnoreCase);
        // "mq" → "mg"
        t = Regex.Replace(t, @"\b(\d{1,4})\s*mq\b", "$1mg", RegexOptions.IgnoreCase);
        return t;
    }

    public PrescriptionAnalysis Analyze(IEnumerable<AnalyzerLine> lines, double overallConfidence)
    {
        var lineList = lines.ToList();

        var allText = string.Join('\n', lineList.Select(l => l.Text));

        // ------------------------- Tarih adayları -------------------------
        var dateCandidates = DatePattern.Matches(allText)
            .Select(m => m.Value).Distinct().ToList();

        // Bozuk OCR tarihleri: yıl kalıbı tutan ama standart biçimde olmayanlar
        var fuzzyDates = FuzzyYearDatePattern.Matches(allText)
            .Select(m => m.Value)
            .Distinct()
            .Where(d => !dateCandidates.Contains(d))
            .ToList();
        foreach (var d in fuzzyDates)
        {
            dateCandidates.Add($"{d} (Olası Tarih — OCR doğrulaması önerilir)");
        }

        // ------------------------- TC adayları ----------------------------
        var tcCandidates = TcPattern.Matches(allText)
            .Select(m => m.Value).Distinct().ToList();

        // ---------------- Protokol / Reçete No adayları -------------------
        var protocolCandidates = FilterNumberCandidates(
            ProtocolPattern.Matches(allText).Select(m => m.Groups[1].Value.Trim()));
        var prescriptionNoCandidates = FilterNumberCandidates(
            PrescriptionNoPattern.Matches(allText).Select(m => m.Groups[1].Value.Trim()));

        // ------------------------- İlaç satırları -------------------------
        // İLAÇLAR / Rp başlığı varsa ilaç adayı araması o satırdan sonra başlar.
        var drugStartIndex = 0;
        for (var i = 0; i < lineList.Count; i++)
        {
            if (DrugSectionHeaderPattern.IsMatch(lineList[i].Text.Trim()))
            {
                drugStartIndex = i + 1;
                break;
            }
        }

        var drugLines = new List<DrugLineDto>();
        for (var i = drugStartIndex; i < lineList.Count; i++)
        {
            var line = lineList[i];
            var text = line.Text.Trim();
            if (text.Length < 3) continue;

            var lower = text.ToLowerInvariant();

            // Kaşe/üst bilgi satırları (alt kaşe dahil) ilaç adayı olamaz
            if (StampKeywords.Any(lower.Contains)) continue;

            // Şekil karışması düzeltmesi yalnızca eşleştirme için uygulanır;
            // çıktıdaki satır metni orijinal OCR metni olarak kalır.
            var normalized = NormalizeForAnalysis(text);
            var normalizedLower = normalized.ToLowerInvariant();

            var dosages = DosagePattern.Matches(normalized).Select(m => m.Value).ToList();
            var doseAmounts = DoseAmountPattern.Matches(normalized).Select(m => m.Value).ToList();
            dosages.AddRange(doseAmounts);

            var units = UnitKeywords.Where(k => normalizedLower.Contains(k)).ToList();
            var freq = FrequencyHints.Where(normalizedLower.Contains).ToList();

            // Kural: dozaj + birim + sıklık üçü birden boşsa bu satır tek
            // başına "Olası İlaç Satırı" olarak işaretlenmez.
            if (dosages.Count == 0 && units.Count == 0 && freq.Count == 0)
                continue;

            drugLines.Add(new DrugLineDto(text, Math.Round(line.Confidence, 4), dosages, units, freq));
        }

        // --------------------- Düşük güven satırları ----------------------
        var lowConfidence = lineList
            .Where(l => l.Text.Trim().Length >= 3 && l.Confidence < LowConfidenceThreshold)
            .Select(l => new LowConfidenceLineDto(l.Text.Trim(), Math.Round(l.Confidence, 4)))
            .ToList();

        // --------------------------- Uyarılar -----------------------------
        var warnings = new List<string>();
        if (lineList.Count == 0)
            warnings.Add("OCR hiç metin satırı üretmedi; görsel kalitesini kontrol edin.");

        foreach (var lc in lowConfidence.Take(10))
            warnings.Add($"Manuel kontrol önerilir (güven {lc.Confidence:F2}): \"{lc.Text}\"");

        foreach (var d in fuzzyDates)
            warnings.Add($"Tarih okuma şüpheli olabilir, manuel doğrulama önerilir: \"{d}\"");

        if (drugLines.Count == 0 && lineList.Count > 0)
            warnings.Add("İlaç satırı deseni bulunamadı; satırları elle inceleyin.");

        var summary =
            $"{lineList.Count} satır okundu; {drugLines.Count} ilaç satırı adayı, " +
            $"{dateCandidates.Count} tarih, {prescriptionNoCandidates.Count + protocolCandidates.Count} numara adayı bulundu. " +
            $"Ortalama güven: {overallConfidence:F2}.";

        return new PrescriptionAnalysis(
            dateCandidates, protocolCandidates, tcCandidates, prescriptionNoCandidates,
            drugLines, lowConfidence, warnings, summary);
    }

    /// <summary>
    /// Numara adayı filtresi: en az 4 ardışık rakam (veya tireli sayı grubu)
    /// içermeyenler ve form etiketleri ("Tarih", "Protokol", "kol" vb.) elenir.
    /// </summary>
    private static List<string> FilterNumberCandidates(IEnumerable<string> candidates)
    {
        var result = new List<string>();
        foreach (var c in candidates)
        {
            var value = c.Trim();
            if (value.Length < 2) continue;

            // Form etiketi içeren adayları at ("Tarih", "Kimlik No" etiketi vs.)
            var lower = value.ToLowerInvariant();
            if (FormLabelKeywords.Any(k => lower.Contains(k))) continue;

            // Rakam gücü: \d{4,} veya \d{2,}-\d{2,} bulunmalı
            if (!StrongNumberPattern.IsMatch(value)) continue;

            if (!result.Contains(value)) result.Add(value);
        }
        return result;
    }
}
