import { Navigate, Route, Routes, useNavigate } from 'react-router-dom'
import { useAuth } from './auth'
import Layout from './components/Layout'
import AuditPage from './pages/AuditPage'
import DashboardPage from './pages/DashboardPage'
import LoginPage from './pages/LoginPage'
import PrescriptionPage from './pages/PrescriptionPage'

function RequireAuth({ children }: { children: React.ReactNode }) {
  const { user } = useAuth()
  if (!user) return <Navigate to="/login" replace />
  return <>{children}</>
}

export default function App() {
  const { user, logout } = useAuth()
  const navigate = useNavigate()

  const handleLogout = () => {
    logout()
    navigate('/login')
  }

  return (
    <Routes>
      <Route
        path="/login"
        element={user ? <Navigate to="/" replace /> : <LoginPage />}
      />
      <Route
        path="/"
        element={
          <RequireAuth>
            <Layout username={user?.fullName ?? user?.username ?? ''} onLogout={handleLogout}>
              <DashboardPage />
            </Layout>
          </RequireAuth>
        }
      />
      <Route
        path="/prescriptions/:id"
        element={
          <RequireAuth>
            <Layout username={user?.fullName ?? user?.username ?? ''} onLogout={handleLogout}>
              <PrescriptionPage />
            </Layout>
          </RequireAuth>
        }
      />
      <Route
        path="/audit"
        element={
          <RequireAuth>
            <Layout username={user?.fullName ?? user?.username ?? ''} onLogout={handleLogout}>
              <AuditPage />
            </Layout>
          </RequireAuth>
        }
      />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
