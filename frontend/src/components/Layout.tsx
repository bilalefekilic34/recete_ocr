import type { ReactNode } from 'react'
import { NavLink } from 'react-router-dom'

interface Props {
  children: ReactNode
  username: string
  onLogout: () => void
}

export default function Layout({ children, username, onLogout }: Props) {
  return (
    <div className="layout">
      <header className="topbar">
        <div className="topbar-brand">
          <span className="brand-icon">🩺</span>
          <span className="brand-text">ReçeteOCR</span>
          <span className="brand-badge">Faz 1</span>
        </div>
        <nav className="topbar-nav">
          <NavLink to="/" end>
            Reçeteler
          </NavLink>
          <NavLink to="/audit">İşlem Geçmişi</NavLink>
        </nav>
        <div className="topbar-user">
          <span className="user-name">{username}</span>
          <button className="btn btn-ghost" onClick={onLogout}>
            Çıkış
          </button>
        </div>
      </header>
      <main className="content">{children}</main>
    </div>
  )
}
