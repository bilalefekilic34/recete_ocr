#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# ReçeteOCR — Faz 1 geliştirme ortamı başlatıcı
# 3 servisi arka planda başlatır, tek Ctrl+C ile hepsini kapatır.
#
#   ./start-dev.sh
#
# Portlar:
#   OCR servisi (FastAPI)  : http://127.0.0.1:8100
#   Backend  (ASP.NET Core): http://127.0.0.1:5000
#   Frontend (Vite dev)    : http://localhost:5173
# ---------------------------------------------------------------------------
set -u

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LOG_DIR="$ROOT_DIR/.logs"
mkdir -p "$LOG_DIR"

PIDS=()

cleanup() {
  echo ""
  echo "⏹  Servisler kapatılıyor..."
  for pid in "${PIDS[@]:-}"; do
    kill "$pid" 2>/dev/null
  done
  wait 2>/dev/null
  echo "Kapandı."
  exit 0
}
trap cleanup INT TERM

echo "🚀 ReçeteOCR geliştirme ortamı başlatılıyor..."

# 1) OCR servisi (Python .venv — sistem Python'ı kirletilmez)
echo "  [1/3] OCR servisi      → http://127.0.0.1:8100  (log: .logs/ocr.log)"
(
  cd "$ROOT_DIR/ocr-service"
  .venv/bin/python -m uvicorn app.main:app --host 127.0.0.1 --port 8100
) >"$LOG_DIR/ocr.log" 2>&1 &
PIDS+=($!)

# 2) Backend (ASP.NET Core + SQLite)
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
echo "  [2/3] Backend API      → http://127.0.0.1:5000  (log: .logs/backend.log)"
(
  cd "$ROOT_DIR/backend/ReceteOcr.Api"
  dotnet run --urls "http://127.0.0.1:5000"
) >"$LOG_DIR/backend.log" 2>&1 &
PIDS+=($!)

# 3) Frontend (Vite dev sunucusu)
echo "  [3/3] Frontend (Vite)  → http://localhost:5173  (log: .logs/frontend.log)"
(
  cd "$ROOT_DIR/frontend"
  npm run dev
) >"$LOG_DIR/frontend.log" 2>&1 &
PIDS+=($!)

echo ""
echo "✅ Tüm servisler başlatıldı. Durdurmak için Ctrl+C."
echo "   Uygulama:            http://localhost:5173"
echo "   Seed kullanıcılar:   dr_ahmet/Ahmet.2024, ecz_ayse/Ayse.2024, admin/Admin.2024"
echo ""
wait
