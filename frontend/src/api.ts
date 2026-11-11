import type {
  AuditLogItem,
  LoginResponse,
  OcrResult,
  PrescriptionAnalysis,
  PrescriptionDetail,
  PrescriptionListItem,
} from './types'

const TOKEN_KEY = 'receteocr_token'
const USER_KEY = 'receteocr_user'

export function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY)
}

export function getStoredUser(): LoginResponse | null {
  const raw = localStorage.getItem(USER_KEY)
  return raw ? (JSON.parse(raw) as LoginResponse) : null
}

export function storeAuth(auth: LoginResponse) {
  localStorage.setItem(TOKEN_KEY, auth.token)
  localStorage.setItem(USER_KEY, JSON.stringify(auth))
}

export function clearAuth() {
  localStorage.removeItem(TOKEN_KEY)
  localStorage.removeItem(USER_KEY)
}

export class ApiError extends Error {
  constructor(
    message: string,
    public status: number,
  ) {
    super(message)
  }
}

async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  const headers = new Headers(options.headers)
  const token = getToken()
  if (token) headers.set('Authorization', `Bearer ${token}`)
  if (options.body && !(options.body instanceof FormData)) {
    headers.set('Content-Type', 'application/json')
  }

  const response = await fetch(path, { ...options, headers })

  if (response.status === 401) {
    clearAuth()
    // 401'de login'e yönlendir; sayfa yenilemesi state'i temizler.
    window.location.href = '/login'
    throw new ApiError('Oturum süresi doldu.', 401)
  }

  if (!response.ok) {
    let message = `İstek başarısız (${response.status})`
    try {
      const body = await response.json()
      if (body?.message) message = body.message
    } catch {
      /* gövde JSON değilse varsayılan mesaj */
    }
    throw new ApiError(message, response.status)
  }

  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

export const api = {
  login: (username: string, password: string) =>
    request<LoginResponse>('/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({ username, password }),
    }),

  me: () => request<{ userId: number; username: string; role: string }>('/api/auth/me'),

  listPrescriptions: () => request<PrescriptionListItem[]>('/api/prescriptions'),

  getPrescription: (id: number) => request<PrescriptionDetail>(`/api/prescriptions/${id}`),

  uploadPrescription: (file: File) => {
    const form = new FormData()
    form.append('file', file)
    return request<PrescriptionDetail>('/api/prescriptions', { method: 'POST', body: form })
  },

  runOcr: (id: number, preprocess: 'raw' | 'enhanced') =>
    request<OcrResult>(`/api/prescriptions/${id}/ocr?preprocess=${preprocess}`, { method: 'POST' }),

  analyze: (id: number) =>
    request<PrescriptionAnalysis>(`/api/prescriptions/${id}/analyze`, { method: 'POST' }),

  deletePrescription: (id: number) =>
    request<void>(`/api/prescriptions/${id}`, { method: 'DELETE' }),

  prescriptionImageUrl: (id: number) => `/api/prescriptions/${id}/image`,

  /// JWT korumalı görseli blob URL olarak getirir (<img> başlık gönderemez).
  ///
  /// Genel `request()` burada KULLANILMAZ: o yanıt gövdesini `response.json()`
  /// ile ayrıştırır; görsel baytlarında SyntaxError fırlatır ve Network'te
  /// 200 görünürken "görsel yüklenemedi" hatası üretir.
  fetchPrescriptionImage: async (id: number, signal?: AbortSignal): Promise<string> => {
    const headers = new Headers()
    const token = getToken()
    if (token) headers.set('Authorization', `Bearer ${token}`)

    const res = await fetch(`/api/prescriptions/${id}/image`, { headers, signal })

    if (res.status === 401) {
      clearAuth()
      window.location.href = '/login'
      throw new ApiError('Oturum süresi doldu.', 401)
    }
    if (!res.ok) {
      throw new ApiError(`Görsel isteği başarısız (${res.status})`, res.status)
    }

    const blob = await res.blob()
    // İçerik tipi doğrulaması: JSON hata sayfası vs. gerçek görsel ayrımı
    if (!blob.type.startsWith('image/')) {
      console.error(
        `[api] /image yanıtı görsel değil, Content-Type: "${blob.type}" (${blob.size} bayt)`,
      )
      throw new ApiError(`Görsel beklenirken beklenmedik içerik tipi: ${blob.type}`, res.status)
    }
    return URL.createObjectURL(blob)
  },

  listAudit: (skip = 0, take = 100, action?: string) => {
    let path = `/api/audit?skip=${skip}&take=${take}`
    if (action) path += `&action=${encodeURIComponent(action)}`
    return request<AuditLogItem[]>(path)
  },
}
