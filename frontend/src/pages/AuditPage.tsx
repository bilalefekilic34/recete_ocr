import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api'
import type { AuditLogItem } from '../types'

const ACTION_LABELS: Record<string, string> = {
  LOGIN: '🔓 Giriş',
  UPLOAD_PRESCRIPTION: '📤 Reçete Yükleme',
  RUN_OCR: '🔍 OCR Çalıştırma',
  ANALYZE_PRESCRIPTION: '🧠 Reçete Analizi',
  DELETE_PRESCRIPTION: '🗑️ Reçete Silme',
}

function fmtDate(iso: string) {
  return new Date(iso).toLocaleString('tr-TR')
}

export default function AuditPage() {
  const [items, setItems] = useState<AuditLogItem[]>([])
  const [action, setAction] = useState('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      setError(null)
      setLoading(true)
      setItems(await api.listAudit(0, 100, action || undefined))
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Kayıtlar yüklenemedi.')
    } finally {
      setLoading(false)
    }
  }, [action])

  useEffect(() => {
    void load()
  }, [load])

  return (
    <div className="page">
      <div className="page-head">
        <h2>İşlem Geçmişi (Audit Log)</h2>
        <label className="field field-inline">
          <span>İşlem türü:</span>
          <select value={action} onChange={(e) => setAction(e.target.value)}>
            <option value="">Tümü</option>
            {Object.entries(ACTION_LABELS).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </label>
      </div>

      {error && <div className="alert alert-error">{error}</div>}

      {loading ? (
        <p className="muted">Yükleniyor…</p>
      ) : items.length === 0 ? (
        <p className="muted">Kayıt bulunamadı.</p>
      ) : (
        <table className="table">
          <thead>
            <tr>
              <th>Zaman</th>
              <th>Kullanıcı</th>
              <th>İşlem</th>
              <th>Hedef (Reçete)</th>
              <th>Detaylar</th>
            </tr>
          </thead>
          <tbody>
            {items.map((a) => (
              <tr key={a.id}>
                <td>{fmtDate(a.createdAt)}</td>
                <td>👤 {a.username}</td>
                <td>
                  <span className="action-badge">{ACTION_LABELS[a.action] ?? a.action}</span>
                </td>
                <td>
                  {a.targetId != null ? (
                    a.action !== 'DELETE_PRESCRIPTION' ? (
                      <Link to={`/prescriptions/${a.targetId}`}>#{a.targetId}</Link>
                    ) : (
                      `#${a.targetId}`
                    )
                  ) : (
                    '—'
                  )}
                </td>
                <td className="details-cell">{a.details ?? '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
