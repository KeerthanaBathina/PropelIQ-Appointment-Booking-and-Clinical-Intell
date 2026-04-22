/**
 * useDocumentPreview — US_042 AC-1, AC-4, EC-1
 *
 * Fetches preview metadata, annotation coordinates, and secure preview-stream URLs
 * for a selected document. Used by DocumentPreviewDrawer to render either:
 *   - An overlay-based image/PDF preview with region highlights (AC-1), or
 *   - A plain-text preview with inline extracted-value annotations (EC-1).
 *
 * Endpoint contract (task_002_be):
 *   GET /api/documents/:id/preview
 *   → { previewUrl, contentType, annotations, supportsOverlay }
 */

import { useQuery } from '@tanstack/react-query';
import { apiGet } from '@/lib/apiClient';

// ─── Types ────────────────────────────────────────────────────────────────────

/**
 * A single extraction annotation returned from the preview API.
 * `bounds` is present for overlay-capable formats; absent for text-only fallback (EC-1).
 */
export interface ExtractionAnnotation {
  /** Extracted data row identifier — matches ExtractedDataRow.extractedDataId. */
  extractedDataId: string;
  dataType:        'Medication' | 'Diagnosis' | 'Procedure' | 'Allergy';
  /** Primary display label for the extracted value (e.g. drug name or condition name). */
  label:           string;
  /** Confidence score in [0, 1] or null when unavailable. */
  confidenceScore: number | null;
  /** Review reason string (matches backend enum). */
  reviewReason:    string;
  /** Verification status of this row. */
  verificationStatus: string;
  /** Page number within the document (1-based). */
  pageNumber:      number;
  /** Coarse region label (e.g. "medication table row 3"). */
  extractionRegion: string;
  /**
   * Relative bounding box in [0, 1] coordinates (fraction of page width/height).
   * Present only when `supportsOverlay = true` (AC-1). Absent for text formats (EC-1).
   */
  bounds?: {
    x: number;
    y: number;
    width:  number;
    height: number;
  };
  /** Raw text snippet from the source document, if available. */
  sourceSnippet?: string;
}

/**
 * Full preview payload returned by GET /api/documents/:id/preview.
 */
export interface DocumentPreviewPayload {
  documentId:      string;
  /** Secure pre-signed URL or server-rendered proxy URL for rendering the document content. */
  previewUrl:      string;
  /**
   * MIME type of the preview asset.
   * Determines whether an image/PDF overlay or text fallback is rendered.
   */
  contentType:     string;
  /**
   * True when the format supports bounding-box region overlays (PDF, PNG, JPG).
   * False → InlineExtractionAnnotations fallback (EC-1).
   */
  supportsOverlay: boolean;
  /** All extraction annotations for this document version. */
  annotations:     ExtractionAnnotation[];
  /** File name for display in the drawer header. */
  fileName:        string;
  /** Document category string. */
  category:        string;
}

// ─── Query key factory ────────────────────────────────────────────────────────

export const documentPreviewKeys = {
  preview: (documentId: string) =>
    ['document-preview', documentId] as const,
};

// ─── Hook ─────────────────────────────────────────────────────────────────────

/**
 * Loads preview metadata and annotation data for the specified document.
 * Returns `undefined` when documentId is falsy (drawer is closed).
 */
export function useDocumentPreview(documentId: string | null | undefined) {
  return useQuery({
    queryKey: documentPreviewKeys.preview(documentId ?? ''),
    queryFn: () =>
      apiGet<DocumentPreviewPayload>(`/api/documents/${documentId}/preview`),
    enabled:   !!documentId,
    staleTime: 60_000,
    retry:     1,
  });
}
