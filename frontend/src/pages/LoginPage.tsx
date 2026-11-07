import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../auth'

const DEMO_USERS = [
  { username: 'dr_ahmet', password: 'Ahmet.2024', label: 'Doktor' },
  { username: 'ecz_ayse', password: 'Ayse.2024', label: 'Eczacı' },
  { username: 'admin', password: 'Admin.2024', label: 'Yönetici' },
]

export default function LoginPage() {
  const { login } = useAuth()
  const navigate = useNavigate()
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)
    setBusy(true)
    try {
      await login(username.trim(), password)
      navigate('/')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Giriş başarısız.')
    } finally {
      setBusy(false)
    }
  }

  const quickFill = (u: string, p: string) => {
    setUsername(u)
    setPassword(p)
  }

  return (
    <div className="login-wrap">
      <form className="login-card" onSubmit={submit}>
        <div className="login-head">
          <span className="login-logo">🩺</span>
          <h1>ReçeteOCR</h1>
          <p>Reçete okuma ve analiz sistemi — Faz 1 (RapidOCR Test)</p>
        </div>

        {error && <div className="alert alert-error">{error}</div>}

        <label className="field">
          <span>Kullanıcı Adı</span>
          <input
            value={username}
            onChange={(e) => setUsername(e.target.value)}
            autoComplete="username"
            autoFocus
            required
          />
        </label>

        <label className="field">
          <span>Şifre</span>
          <input
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            autoComplete="current-password"
            required
          />
        </label>

        <button className="btn btn-primary btn-block" disabled={busy}>
          {busy ? 'Giriş yapılıyor…' : 'Giriş Yap'}
        </button>

        <div className="login-demo">
          <p>Örnek hesaplar (tıkla ve doldur):</p>
          <div className="demo-chips">
            {DEMO_USERS.map((d) => (
              <button
                type="button"
                key={d.username}
                className="chip"
                onClick={() => quickFill(d.username, d.password)}
              >
                {d.username} · {d.label}
              </button>
            ))}
          </div>
        </div>
      </form>
    </div>
  )
}
