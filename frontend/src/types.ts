// Backend API sözleşmeleri (camelCase JSON)

export interface LoginResponse {
  token: string
  expiresAt: string
  userId: number
  username: string
  fullName: string
  role: string
}

export interface OcrLine {
  text: string
  confidence: number
  bbox: [number, number][]
}

export interface OcrResult {
  lines: OcrLine[]
  rawText: string
  confidenceAvg: number
  processingTimeMs: number
  preprocessMode: string
  executionProvider: string
  imageWidth: number
  imageHeight: number
}

export interface DrugLine {
  text: string
  confidence: number
  dosages: string[]
  units: string[]
  frequencyHints: string[]
}

export interface PrescriptionAnalysis {
  dateCandidates: string[]
  protocolNoCandidates: string[]
  tcNoCandidates: string[]
  prescriptionNoCandidates: string[]
  drugLines: DrugLine[]
  lowConfidenceLines: { text: string; confidence: number }[]
  warnings: string[]
  summary: string
}

export interface PrescriptionListItem {
  id: number
  originalFileName: string
  uploadedByUsername: string
  createdAt: string
  ocrConfidenceAvg: number | null
  processingTimeMs: number | null
  hasAnalysis: boolean
}

export interface PrescriptionDetail {
  id: number
  originalFileName: string
  storagePath: string
  uploadedByUsername: string
  rawOcrText: string | null
  ocrLinesJson: string | null
  ocrConfidenceAvg: number | null
  processingTimeMs: number | null
  analysisJson: string | null
  createdAt: string
}

export interface AuditLogItem {
  id: number
  userId: number
  username: string
  action: string
  targetId: number | null
  details: string | null
  createdAt: string
}
