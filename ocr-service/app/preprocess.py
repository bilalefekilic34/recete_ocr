"""OpenCV tabanlı reçete görseli ön işleme.

Faz 1 kapsamında iki mod sunulur:
- ``raw``     : Ham görsel, hiçbir dönüşüm uygulanmaz.
- ``enhanced``: Arka plan aydınlatma normalizasyonu + hafif CLAHE.

Gerçek reçete testlerinde tespit edilen sorunlar nedeniyle pipeline güncellendi:
eski agresif bulanıklaştırma (fastNlMeans + unsharp) ince tükenmez kalem
yazılarını siliyor ve rakamları bozuyordu (`4` -> `9`). Yeni yaklaşım:

1. Arka plan tahmini: geniş çekirdekli ``medianBlur`` ile kağıt dokusunu
   (arka yüz gölgeleri, kaşe lekeleri) tahmin et, ``cv2.divide`` ile orijinal
   görüntüyü arka plana böl — böylece kalem izi korunur, zemin düzleşir.
2. Hafif CLAHE (``clipLimit=1.5``) — kontrastı güçlendirir ama ince
   çizgileri aşırı keskinleştirmez.
3. 24px beyaz kenar boşluğu (``copyMakeBorder``) — kenara taşan satırların
   detektör tarafından kırpılmasını önler. Bu ofset, API yanıtı üretilirken
   bbox koordinatlarından geri çıkarılır (frontend kayması olmaz).
"""

from __future__ import annotations

import cv2
import numpy as np

# Kenar boşluğu kalınlığı (px). main.py bbox koordinatlarını bu kadar kaydırır.
BORDER_PX = 24


def apply_preprocessing(image: np.ndarray, mode: str) -> np.ndarray:
    """Görseli seçilen moda göre işler.

    Args:
        image: BGR formatında OpenCV görseli.
        mode: ``raw`` ya da ``enhanced``.

    Returns:
        İşlenmiş BGR görsel (``raw`` modunda orijinalin kopyası).
    """
    if mode == "raw":
        return image.copy()

    if mode != "enhanced":
        raise ValueError(f"Bilinmeyen ön işleme modu: {mode!r}")

    # 1) Arka plan aydınlatma normalizasyonu: geniş çekirdekli medyan ile
    #    zemin tahmini, sonra bölme ile gölge/kağıt dokusunu temizle.
    #    (Agresif blur YOK — ince tükenmez kalem izleri korunur.)
    #    Çekirdek 21 iken silik ince rakam grupları (100mg flk gibi)
    #    yutulabiliyordu; 17 gölge temizliğini korurken detayı koruyor.
    gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
    background = cv2.medianBlur(gray, 17)
    normalized = cv2.divide(gray, background, scale=255)

    # 2) Hafif CLAHE: düşük clipLimit ince çizgileri boğmaz.
    clahe = cv2.createCLAHE(clipLimit=1.5, tileGridSize=(8, 8))
    normalized = clahe.apply(normalized)

    return cv2.cvtColor(normalized, cv2.COLOR_GRAY2BGR)


def add_white_border(image: np.ndarray, border_px: int = BORDER_PX) -> np.ndarray:
    """Dört kenara beyaz boşluk ekler — kenara yakın satırlar detektörce görünsün."""
    value = (255, 255, 255)
    return cv2.copyMakeBorder(
        image,
        top=border_px, bottom=border_px, left=border_px, right=border_px,
        borderType=cv2.BORDER_CONSTANT, value=value,
    )


def maybe_downscale(image: np.ndarray, max_side: int = 2000) -> np.ndarray:
    """Çok büyük görselleri en uzun kenarı ``max_side``'a göre küçültür."""
    h, w = image.shape[:2]
    if max(h, w) <= max_side:
        return image
    scale = max_side / float(max(h, w))
    new_size = (int(round(w * scale)), int(round(h * scale)))
    interp = cv2.INTER_AREA if scale < 1.0 else cv2.INTER_CUBIC
    return cv2.resize(image, new_size, interpolation=interp)
