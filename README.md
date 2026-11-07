# ReçeteOCR — Faz 1 (RapidOCR Test & Temel Sistem)

Doktor reçetelerini okuyup analiz eden web uygulamasının Faz 1 MVP'si.

- **OCR:** RapidOCR (`rapidocr_onnxruntime`) — Python FastAPI mikroservisi
- **Backend:** ASP.NET Core Web API (.NET 10) + EF Core + SQLite (PostgreSQL'e geçilebilir yapı)
- **Frontend:** React 18 + TypeScript + Vite (Türkçe arayüz)
- **Hedef donanım:** Ryzen 7 3700X / 16 GB RAM / RTX 3070 — hafif, düşük bellekli kurulum (Docker yok, ekstra DB sunucusu yok)

## Mimari

```
recete_ocr/
├── backend/ReceteOcr.Api/    # ASP.NET Core Web API (JWT, Audit, EF Core SQLite)
├── ocr-service/              # FastAPI + RapidOCR + OpenCV ön işleme (.venv içinde)
├── frontend/                 # React + TS + Vite (port 5173, /api → backend'e proxy)
└── start-dev.sh              # 3 servisi başlatan/durduran script
```

**Mimari kurallar**
- Backend, Python sürecini spawn etmez; OCR servisiyle yalnızca HTTP üzerinden konuşur.
- Strategy Pattern: `IOcrProvider` (Faz 1: `RapidOcrProvider`) ve `IPrescriptionAnalyzer`
  (Faz 1: `HeuristicPrescriptionAnalyzer`, deterministik/kural tabanlı). Faz 2'de
  PaddleOCR / TrOCR / VLM ve LLM analiz, bu arayüzlerle eklenir.
- SQLite yalnızca `Program.cs`'de seçilir; PostgreSQL geçişi bağlantı dizesi + `UseSqlite → UseNpgsql` değişikliği demektir.
- OCR servisi izole `.venv` içinde; sistem Python'ı kirletilmez.

## Kurulum

### 1. Gereksinimler
- .NET 10 SDK (kurulu değilse, sudo gerektirmez):
  ```bash
  curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel LTS
  export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"
  ```
- Node.js 22+, npm
- Python 3.12+ (venv desteği)

### 2. Bağımlılıklar
```bash
# OCR servisi
python3 -m venv ocr-service/.venv
ocr-service/.venv/bin/pip install -r ocr-service/requirements.txt

# Backend
(cd backend/ReceteOcr.Api && dotnet build)

# Frontend
(cd frontend && npm install)
```

### 3. Çalıştırma
```bash
chmod +x start-dev.sh
./start-dev.sh
```
Script üç servisi arka plan process'i olarak başlatır, loglar `.logs/` altına yazılır ve
**tek Ctrl+C ile tümü kapatılır.**

| Servis | Adres | Log |
|---|---|---|
| Frontend (Vite) | http://localhost:5173 | `.logs/frontend.log` |
| Backend API | http://127.0.0.1:5000 | `.logs/backend.log` |
| OCR servisi | http://127.0.0.1:8100 (swagger: `/docs`) | `.logs/ocr.log` |

## Seed Kullanıcıları

İlk açılışta otomatik oluşturulur (giriş ekranındaki çiplerle de doldurulabilir):

| Kullanıcı | Şifre | Rol |
|---|---|---|
| `dr_ahmet` | `Ahmet.2024` | Doktor |
| `ecz_ayse` | `Ayse.2024` | Eczacı |
| `admin` | `Admin.2024` | Yönetici |

Şifreler PBKDF2 (SHA-256, 100k iterasyon) + rastgele salt ile saklanır; JWT HS256, 8 saat ömürlü.

## Kullanım Akışı

1. **Giriş** → JWT alınır, tüm işlemler kullanıcıya atfedilir.
2. **Reçeteler** sayfasından JPG/PNG yüklenir (≤15 MB) → `UPLOAD_PRESCRIPTION` audit.
3. Reçete detayında:
   - **Ön İşleme toggle'ı**: `Ham Görsel` ↔ `Gri Tonlama + CLAHE + Gürültü Azaltma` — RapidOCR başarımını iki modda karşılaştırmak için.
   - **OCR Çalıştır** → satır bazlı metin + güven + bbox, ortalama güven, süre, execution provider → `RUN_OCR` audit.
   - Görsel üzerinde renkli bounding box'lar: yeşil ≥%80, sarı %65–80, kırmızı <%65 (manuel kontrol uyarısı).
   - **Reçete Analizi** → kural tabanlı deterministik analiz: tarih / protokol / TC / reçete no adayları, ilaç satırı + dozaj kalıpları (`1x1`, `mg`, `tb`, `şurup`…), düşük güven uyarıları → `ANALYZE_PRESCRIPTION` audit.
4. **İşlem Geçmişi** sayfası: kim, ne zaman, hangi işlemi, hangi reçete üzerinde yaptı (filtrelenebilir).

## API Özeti (hepsi `/api` önekiyle, JWT Bearer)

| Metot | Yol | Açıklama |
|---|---|---|
| POST | `/api/auth/login` | Giriş, JWT döner |
| GET | `/api/prescriptions` | Reçete listesi |
| POST | `/api/prescriptions` | Görsel yükleme (multipart `file`) |
| POST | `/api/prescriptions/{id}/ocr?preprocess=raw\|enhanced` | RapidOCR çalıştır |
| POST | `/api/prescriptions/{id}/analyze` | Heuristic analiz |
| GET | `/api/prescriptions/{id}/image` | Görseli döndürür |
| DELETE | `/api/prescriptions/{id}` | Reçete + dosyaları sil |
| GET | `/api/audit?action=&username=&skip=&take=` | Audit log |

## Veritabanı

SQLite dosyası: `backend/ReceteOcr.Api/receteocr.db` (ilk açılışta `EnsureCreated` ile oluşur).
Tablolar: `Users`, `AuditLogs` (`UserId, Username, Action, TargetId, Details, CreatedAt` UTC),
`Prescriptions` (`UploadedByUserId, OriginalFileName, StoragePath, RawOcrText, OcrConfidenceAvg, ProcessingTimeMs, AnalysisJson` + satır bazlı `OcrLinesJson`).

## Faz 2 Notları

- `IOcrProvider` arayüzüne PaddleOCR / TrOCR / VLM sağlayıcıları eklenebilir; `/ocr` HTTP sözleşmesi aynı kalır.
- `IPrescriptionAnalyzer` arayüzünün arkasına LLM tabanlı analiz bağlanabilir; mevcut heuristic çıktı şeması korunur.
- PostgreSQL'e geçiş: `UseSqlite` → `UseNpgsql` (+ `Microsoft.EntityFrameworkCore.Npgsql` paketi).
- GPU: ocr-service şu anda `onnxruntime` (CPU) kullanır ve provider'ı her yanıtta raporlar. GPU için venv'e `onnxruntime-gpu` kurulup CUDA ExecutionProvider devreye alınabilir (RTX 3070 uyumlu).

## Bilinen Sınırlar (Faz 1)

- NuGet çözümlemesinde geçici paketlerden gelen 6 bilgilendirme uyarısı (NU1902/NU1903) vardır; doğrudan bağımlılık değildirler.
- Görsel bütünlüğü magic-byte kontrolü ile yapılır; derin görsel doğrulama Faz 2'de.
- Migrations yerine `EnsureCreated` (şema değişirse db dosyasını silin).
