import { useCallback, useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { api } from '../api'
import type { OcrLine, OcrResult, PrescriptionAnalysis, PrescriptionDetail } from '../types'

function fmtDate(iso: string) {
  return new Date(iso).toLocaleString('tr-TR')
}

/// Kayıtlı OcrLinesJson'ı tolere eden ayrıştırıcı:
/// yeni kayıtlar camelCase ("text"), eski kayıtlar PascalCase ("Text")
/// olabilir; ayrıca kök dizinin gerçekten dizi olduğundan emin olunur.
function parseLines(json: string | null): OcrLine[] {
  if (!json) return []
  try {
    const parsed: unknown = JSON.parse(json)
    if (!Array.isArray(parsed)) return []
    return parsed
      .map((item): OcrLine | null => {
        const rec = item as Record<string, unknown>
        const text = (rec.text ?? rec.Text) as string | undefined
        const confidence = (rec.confidence ?? rec.Confidence) as number | undefined
        const bbox = (rec.bbox ?? rec.Bbox) as OcrLine['bbox'] | undefined
        if (typeof text !== 'string' || !Array.isArray(bbox)) return null
        return {
          text,
          confidence: typeof confidence === 'number' ? confidence : 0,
          bbox,
        }
      })
      .filter((l): l is OcrLine => l !== null)
  } catch {
    return []
  }
}

const CONF_COLORS = {
  high: 'rgba(34, 197, 94, 0.85)', // yeşil
  mid: 'rgba(234, 179, 8, 0.85)', // sarı
  low: 'rgba(239, 68, 68, 0.85)', // kırmızı
}

function confColor(c: number) {
  return c >= 0.8 ? CONF_COLORS.high : c >= 0.65 ? CONF_COLORS.mid : CONF_COLORS.low
}

export default function PrescriptionPage() {
  const { id } = useParams()
  const prescriptionId = Number(id)
  const navigate = useNavigate()

  const [p, setP] = useState<PrescriptionDetail | null>(null)
  const [ocr, setOcr] = useState<OcrResult | null>(null)
  const [analysis, setAnalysis] = useState<PrescriptionAnalysis | null>(null)
  const [preprocess, setPreprocess] = useState<'raw' | 'enhanced'>('raw')
  const [busyOcr, setBusyOcr] = useState(false)
  const [busyAnalyze, setBusyAnalyze] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [hoveredLine, setHoveredLine] = useState<number | null>(null)

  const load = useCallback(async () => {
    try {
      const detail = await api.getPrescription(prescriptionId)
      setP(detail)
      if (detail.ocrLinesJson) {
        const lines = parseLines(detail.ocrLinesJson)
        setOcr({
          lines,
          rawText: detail.rawOcrText ?? '',
          confidenceAvg: detail.ocrConfidenceAvg ?? 0,
          processingTimeMs: detail.processingTimeMs ?? 0,
          preprocessMode: '—',
          executionProvider: '—',
          imageWidth: 0,
          imageHeight: 0,
        })
      }
      if (detail.analysisJson) setAnalysis(JSON.parse(detail.analysisJson) as PrescriptionAnalysis)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Reçete yüklenemedi.')
    }
  }, [prescriptionId])

  useEffect(() => {
    void load()
  }, [load])

  const runOcr = async () => {
    setBusyOcr(true)
    setError(null)
    try {
      const result = await api.runOcr(prescriptionId, preprocess)
      setOcr(result)
      setAnalysis(null)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'OCR başarısız.')
    } finally {
      setBusyOcr(false)
    }
  }

  const runAnalyze = async () => {
    setBusyAnalyze(true)
    setError(null)
    try {
      setAnalysis(await api.analyze(prescriptionId))
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Analiz başarısız.')
    } finally {
      setBusyAnalyze(false)
    }
  }

  if (error && !p) {
    return (
      <div className="page">
        <div className="alert alert-error">{error}</div>
        <button className="btn" onClick={() => navigate('/')}>
          ← Reçetelere dön
        </button>
      </div>
    )
  }
  if (!p) return <div className="page muted">Yükleniyor…</div>

  const lines = ocr?.lines ?? []

  return (
    <div className="page">
      <div className="page-head">
        <div>
          <button className="btn btn-ghost" onClick={() => navigate('/')}>
            ← Liste
          </button>
          <h2 style={{ display: 'inline', marginLeft: 12 }}>
            Reçete #{p.id} — {p.originalFileName}
          </h2>
        </div>
        <div className="ocr-controls">
          <label className="switch-label">
            <input
              type="checkbox"
              checked={preprocess === 'enhanced'}
              onChange={(e) => setPreprocess(e.target.checked ? 'enhanced' : 'raw')}
            />
            <span>
              Ön İşleme (Gri + CLAHE + Gürültü Azaltma) —{' '}
              <strong>{preprocess === 'enhanced' ? 'Açık' : 'Ham Görsel'}</strong>
            </span>
          </label>
          <button className="btn btn-primary" onClick={runOcr} disabled={busyOcr}>
            {busyOcr ? 'OCR çalışıyor…' : '🔍 OCR Çalıştır'}
          </button>
          <button
            className="btn"
            onClick={runAnalyze}
            disabled={busyAnalyze || !ocr}
            title={!ocr ? 'Önce OCR çalıştırın' : undefined}
          >
            {busyAnalyze ? 'Analiz ediliyor…' : '🧠 Reçete Analizi'}
          </button>
        </div>
      </div>

      {error && <div className="alert alert-error">{error}</div>}

      <div className="meta-strip">
        <span>
          İşlemi Yapan: <strong>👤 {p.uploadedByUsername}</strong>
        </span>
        <span>Yükleme: {fmtDate(p.createdAt)}</span>
        {ocr && (
          <>
            <span>
              Ort. Güven:{' '}
              <strong className={ocr.confidenceAvg < 0.65 ? 'conf-low' : 'conf-ok'}>
                {(ocr.confidenceAvg * 100).toFixed(1)}%
              </strong>
            </span>
            <span>Süre: {ocr.processingTimeMs} ms</span>
            {ocr.executionProvider !== '—' && <span>Provider: {ocr.executionProvider}</span>}
          </>
        )}
      </div>

      {analysis && (
        <section className="card analysis-card">
          <h3>🧠 Reçete Analizi</h3>
          <p className="muted">{analysis.summary}</p>

          {analysis.warnings.length > 0 && (
            <div className="alert alert-warn">
              <strong>⚠️ Uyarılar:</strong>
              <ul>
                {analysis.warnings.map((w, i) => (
                  <li key={i}>{w}</li>
                ))}
              </ul>
            </div>
          )}

          <div className="analysis-grid">
            <div>
              <h4>Tarih Adayları</h4>
              {analysis.dateCandidates.length ? (
                <div className="chips">
                  {analysis.dateCandidates.map((d, i) => (
                    <span key={i} className="chip chip-static">
                      📅 {d}
                    </span>
                  ))}
                </div>
              ) : (
                <p className="muted">Bulunamadı</p>
              )}
              <h4>Protokol / Reçete No Adayları</h4>
              {[...analysis.protocolNoCandidates, ...analysis.prescriptionNoCandidates].length ? (
                <div className="chips">
                  {[...analysis.protocolNoCandidates, ...analysis.prescriptionNoCandidates].map((d, i) => (
                    <span key={i} className="chip chip-static">
                      🔢 {d}
                    </span>
                  ))}
                </div>
              ) : (
                <p className="muted">Bulunamadı</p>
              )}
              <h4>TC No Adayları</h4>
              {analysis.tcNoCandidates.length ? (
                <div className="chips">
                  {analysis.tcNoCandidates.map((d, i) => (
                    <span key={i} className="chip chip-static">
                      🪪 {d}
                    </span>
                  ))}
                </div>
              ) : (
                <p className="muted">Bulunamadı</p>
              )}
            </div>
            <div>
              <h4>Olası İlaç Satırları ({analysis.drugLines.length})</h4>
              {analysis.drugLines.length ? (
                <table className="table table-sm">
                  <thead>
                    <tr>
                      <th>Satır</th>
                      <th>Dozaj</th>
                      <th>Birim</th>
                      <th>Sıklık</th>
                    </tr>
                  </thead>
                  <tbody>
                    {analysis.drugLines.map((d, i) => (
                      <tr key={i}>
                        <td>{d.text}</td>
                        <td>{d.dosages.join(', ') || '—'}</td>
                        <td>{d.units.join(', ') || '—'}</td>
                        <td>{d.frequencyHints.join(', ') || '—'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              ) : (
                <p className="muted">Bulunamadı</p>
              )}
            </div>
          </div>
        </section>
      )}

      <div className="detail-grid">
        <section className="card">
          <h3>🖼️ Görsel + Bounding Box Önizleme</h3>
          {ocr && ocr.imageWidth === 0 && (
            <p className="muted">
              Not: Bu OCR sonucu daha önce kaydedilmiş; kutular referans amaçlı çizilmiştir.
            </p>
          )}
          <BBoxPreview prescriptionId={prescriptionId} lines={lines} hovered={hoveredLine} onHover={setHoveredLine} />
        </section>

        <section className="card">
          <h3>📝 Okunan Satırlar ({lines.length})</h3>
          {lines.length === 0 ? (
            <p className="muted">Henüz OCR çalıştırılmadı. "OCR Çalıştır" düğmesini kullanın.</p>
          ) : (
            <div className="line-table-wrap">
              <table className="table table-sm">
                <thead>
                  <tr>
                    <th>#</th>
                    <th>Metin</th>
                    <th>Güven</th>
                  </tr>
                </thead>
                <tbody>
                  {lines.map((l, i) => (
                    <tr
                      key={i}
                      className={hoveredLine === i ? 'row-hover' : ''}
                      onMouseEnter={() => setHoveredLine(i)}
                      onMouseLeave={() => setHoveredLine(null)}
                    >
                      <td>{i + 1}</td>
                      <td>{l.text}</td>
                      <td>
                        <span className={l.confidence < 0.65 ? 'conf-low' : 'conf-ok'}>
                          {(l.confidence * 100).toFixed(1)}%
                          {l.confidence < 0.65 && ' ⚠️'}
                        </span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      </div>
    </div>
  )
}

/// Yüklenen görsel üzerinde RapidOCR tespit kutularını çizer.
function BBoxPreview({
  prescriptionId,
  lines,
  hovered,
  onHover,
}: {
  prescriptionId: number
  lines: OcrLine[]
  hovered: number | null
  onHover: (i: number | null) => void
}) {
  // natural boyutlar yalnızca <img onLoad> sonrası doldurulur; görsel
  // yüklenmeden boyut hesabı yapılmaz (bbox oranı bozulmasın).
  const [natural, setNatural] = useState<{ w: number; h: number } | null>(null)
  const [imageUrl, setImageUrl] = useState<string | null>(null)
  const [imageError, setImageError] = useState<string | null>(null)

  useEffect(() => {
    // StrictMode'da effect iki kez çalışır: ilk çağrı AbortController ile
    // iptal edilir, ikinci çağrının URL'i etkilenmez. Temizlik yalnızca
    // kendi ürettiği objectUrl'i geri alır.
    const controller = new AbortController()
    let myUrl: string | null = null
    let cancelled = false

    setImageError(null)
    setImageUrl(null)
    setNatural(null)

    api
      .fetchPrescriptionImage(prescriptionId, controller.signal)
      .then((url) => {
        if (cancelled) {
          // Temizlik race'i: bu URL bu etkiye aitti ve bileşen/eski tur
          // ölmüş durumda — hemen geri al.
          URL.revokeObjectURL(url)
          return
        }
        myUrl = url
        setImageUrl(url)
      })
      .catch((err) => {
        if (cancelled || (err instanceof DOMException && err.name === 'AbortError')) return
        setImageError(err instanceof Error ? err.message : 'Görsel yüklenemedi.')
      })

    return () => {
      cancelled = true
      controller.abort()
      if (myUrl) URL.revokeObjectURL(myUrl)
    }
  }, [prescriptionId])

  return (
    <div className="bbox-wrap">
      {/* imageUrl hazır olana dek <img> DOM'a konmaz (boş src render yok) */}
      {imageUrl && (
        <img
          src={imageUrl}
          alt="Reçete görseli"
          className="bbox-img"
          onLoad={(e) => {
            const img = e.currentTarget
            setNatural({ w: img.naturalWidth, h: img.naturalHeight })
          }}
        />
      )}
      {imageError && <p className="muted">Görsel yüklenemedi: {imageError}</p>}
      {!imageUrl && !imageError && <p className="muted">Görsel yükleniyor…</p>}
      {natural &&
        lines.map((l, i) => {
          const xs = l.bbox.map((pt) => pt[0])
          const ys = l.bbox.map((pt) => pt[1])
          const left = (Math.min(...xs) / natural.w) * 100
          const top = (Math.min(...ys) / natural.h) * 100
          const width = ((Math.max(...xs) - Math.min(...xs)) / natural.w) * 100
          const height = ((Math.max(...ys) - Math.min(...ys)) / natural.h) * 100
          return (
            <div
              key={i}
              className={`bbox-box ${hovered === i ? 'bbox-active' : ''}`}
              style={{
                left: `${left}%`,
                top: `${top}%`,
                width: `${width}%`,
                height: `${height}%`,
                borderColor: confColor(l.confidence),
              }}
              title={`"${l.text}" — güven ${(l.confidence * 100).toFixed(1)}%`}
              onMouseEnter={() => onHover(i)}
              onMouseLeave={() => onHover(null)}
            />
          )
        })}
      <div className="bbox-legend">
        <span style={{ color: CONF_COLORS.high }}>■ ≥80%</span>
        <span style={{ color: CONF_COLORS.mid }}>■ 65–80%</span>
        <span style={{ color: CONF_COLORS.low }}>■ &lt;65% (manuel kontrol)</span>
      </div>
    </div>
  )
}
