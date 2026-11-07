import { useCallback, useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api } from '../api'
import type { PrescriptionListItem } from '../types'

function fmtDate(iso: string) {
  return new Date(iso).toLocaleString('tr-TR')
}

export default function DashboardPage() {
  const [items, setItems] = useState<PrescriptionListItem[]>([])
  const [loading, setLoading] = useState(true)
  const [uploading, setUploading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const fileRef = useRef<HTMLInputElement>(null)
  const navigate = useNavigate()

  const load = useCallback(async () => {
    try {
      setError(null)
      setItems(await api.listPrescriptions())
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Liste yüklenemedi.')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  const onUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    e.target.value = ''
    if (!file) return
    setUploading(true)
    setError(null)
    try {
      const created = await api.uploadPrescription(file)
      navigate(`/prescriptions/${created.id}`)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Yükleme başarısız.')
    } finally {
      setUploading(false)
    }
  }

  const onDelete = async (id: number) => {
    if (!window.confirm('Bu reçeteyi ve dosyalarını silmek istediğinize emin misiniz?')) return
    try {
      await api.deletePrescription(id)
      await load()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Silme başarısız.')
    }
  }

  return (
    <div className="page">
      <div className="page-head">
        <h2>Reçeteler</h2>
        <label className={`btn btn-primary ${uploading ? 'is-busy' : ''}`}>
          {uploading ? 'Yükleniyor…' : '＋ Reçete Yükle (JPG/PNG)'}
          <input
            ref={fileRef}
            type="file"
            accept=".jpg,.jpeg,.png"
            hidden
            onChange={onUpload}
            disabled={uploading}
          />
        </label>
      </div>

      {error && <div className="alert alert-error">{error}</div>}

      {loading ? (
        <p className="muted">Yükleniyor…</p>
      ) : items.length === 0 ? (
        <div className="empty-state">
          <p>Henüz reçete yüklenmedi.</p>
          <p className="muted">Sağ üstteki düğmeyle ilk reçete görselini yükleyin.</p>
        </div>
      ) : (
        <table className="table">
          <thead>
            <tr>
              <th>#</th>
              <th>Dosya</th>
              <th>İşlemi Yapan</th>
              <th>Yükleme Zamanı</th>
              <th>Ort. Güven</th>
              <th>Süre</th>
              <th>Analiz</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {items.map((p) => (
              <tr key={p.id} className="row-click" onClick={() => navigate(`/prescriptions/${p.id}`)}>
                <td>{p.id}</td>
                <td>{p.originalFileName}</td>
                <td>
                  <span className="who">👤 {p.uploadedByUsername}</span>
                </td>
                <td>{fmtDate(p.createdAt)}</td>
                <td>
                  {p.ocrConfidenceAvg == null ? (
                    <span className="muted">—</span>
                  ) : (
                    <span className={p.ocrConfidenceAvg < 0.65 ? 'conf-low' : 'conf-ok'}>
                      {(p.ocrConfidenceAvg * 100).toFixed(1)}%
                    </span>
                  )}
                </td>
                <td>{p.processingTimeMs == null ? '—' : `${p.processingTimeMs} ms`}</td>
                <td>{p.hasAnalysis ? '✅' : '—'}</td>
                <td onClick={(e) => e.stopPropagation()}>
                  <button className="btn btn-danger btn-sm" onClick={() => onDelete(p.id)}>
                    Sil
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
