"""FastAPI tabanlı RapidOCR mikroservisi.

Backend (ASP.NET Core) bu servisle yalnızca HTTP üzerinden konuşur;
Python process spawn edilmaz. Faz 2+'da PaddleOCR / TrOCR / VLM
sağlayıcıları aynı /ocr sözleşmesini döndüren yeni provider'lar olarak
eklenebilir.

Çalıştırma:
    uvicorn app.main:app --host 127.0.0.1 --port 8100
"""

from __future__ import annotations

import io
import logging
import time
from typing import Any, Optional

import cv2
import numpy as np
from fastapi import FastAPI, File, Form, HTTPException, UploadFile
from pydantic import BaseModel

from .preprocess import BORDER_PX, add_white_border, apply_preprocessing, maybe_downscale

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s: %(message)s")
logger = logging.getLogger("ocr-service")

app = FastAPI(title="ReceteOCR OCR Service", version="1.0.0")

# ---------------------------------------------------------------------------
# RapidOCR motoru (lazy singleton — ilk istekte yüklenir, model ~10 MB RAM)
# ---------------------------------------------------------------------------
_engine: Optional[Any] = None


def get_engine() -> Any:
    global _engine
    if _engine is None:
        from rapidocr_onnxruntime import RapidOCR

        logger.info("RapidOCR motoru yükleniyor (onnxruntime)...")
        _engine = RapidOCR()
        logger.info("RapidOCR motoru hazır.")
    return _engine


def _execution_provider() -> str:
    """Yüklü onnxruntime paketine göre kullanılan execution provider'ı raporlar."""
    try:
        import onnxruntime as ort

        if "CUDAExecutionProvider" in ort.get_available_providers():
            return "CUDA (GPU)"
        return "CPU"
    except Exception:  # pragma: no cover
        return "CPU"


class OcrLine(BaseModel):
    text: str
    confidence: float
    bbox: list[list[int]]  # 4 köşe: [x, y] x4


class OcrResponse(BaseModel):
    lines: list[OcrLine]
    raw_text: str
    confidence_avg: float
    processing_time_ms: int
    preprocess_mode: str
    execution_provider: str
    image_width: int
    image_height: int


@app.get("/health")
def health() -> dict[str, Any]:
    return {"status": "ok", "execution_provider": _execution_provider()}


@app.post("/ocr", response_model=OcrResponse)
async def run_ocr(
    file: UploadFile = File(..., description="JPG/PNG reçete görseli"),
    preprocess: str = Form("raw", description="raw | enhanced"),
) -> OcrResponse:
    if preprocess not in ("raw", "enhanced"):
        raise HTTPException(status_code=400, detail="preprocess alanı 'raw' veya 'enhanced' olmalı")

    content = await file.read()
    if not content:
        raise HTTPException(status_code=400, detail="Boş dosya")

    image = cv2.imdecode(np.frombuffer(content, dtype=np.uint8), cv2.IMREAD_COLOR)
    if image is None:
        raise HTTPException(status_code=400, detail="Görsel çözümlenemedi (JPG/PNG bekleniyor)")

    height, width = image.shape[:2]

    processed = apply_preprocessing(image, preprocess)
    processed = maybe_downscale(processed)
    if preprocess == "enhanced":
        # Kenar boşluğu küçültmeden SONRA eklenir; böylece offset sabit 24px
        # kalır ve bbox koordinatlarından kayıpsız geri çıkarılabilir.
        processed = add_white_border(processed, BORDER_PX)

    started = time.perf_counter()
    try:
        # Silik el yazısı satırları kaçırmamak için dedeksiyon eşiği düşürüldü
        # ve kutu genişletme (unclip) artırıldı (sürümün __call__ kwargs'ı).
        result, _elapse = get_engine()(
            processed,
            box_thresh=0.35,
            unclip_ratio=1.8,
        )
    except Exception as exc:  # motor hatası
        logger.exception("OCR hatası")
        raise HTTPException(status_code=500, detail=f"OCR motoru hatası: {exc}") from exc
    elapsed_ms = int((time.perf_counter() - started) * 1000)

    lines: list[OcrLine] = []
    if result:
        for item in result:
            box, text, score = item[0], item[1], float(item[2])
            # Beyaz kenar boşluğu ofsetini geri çıkar; negatif değer olmasın.
            offset = BORDER_PX if preprocess == "enhanced" else 0
            pts = [
                [max(int(round(p[0])) - offset, 0), max(int(round(p[1])) - offset, 0)]
                for p in box
            ]
            lines.append(OcrLine(text=str(text), confidence=round(score, 4), bbox=pts))
        # Okuma sırasına göre satırları sırala (üstten alta, soldan sağa)
        lines.sort(key=lambda l: (min(p[1] for p in l.bbox), min(p[0] for p in l.bbox)))

    avg_conf = round(sum(l.confidence for l in lines) / len(lines), 4) if lines else 0.0
    raw_text = "\n".join(l.text for l in lines)

    provider = _execution_provider()
    logger.info(
        "OCR tamam: %d satır, ort. güven %.3f, %d ms, mod=%s, provider=%s",
        len(lines), avg_conf, elapsed_ms, preprocess, provider,
    )

    return OcrResponse(
        lines=lines,
        raw_text=raw_text,
        confidence_avg=avg_conf,
        processing_time_ms=elapsed_ms,
        preprocess_mode=preprocess,
        execution_provider=provider,
        image_width=width,
        image_height=height,
    )
