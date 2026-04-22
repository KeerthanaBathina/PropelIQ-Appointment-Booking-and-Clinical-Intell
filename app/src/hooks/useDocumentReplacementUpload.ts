/**
 * useDocumentReplacementUpload — US_042 AC-2, EC-2
 *
 * Submits a replacement file for an existing document and tracks reprocessing state.
 * Keeps the user within the SCR-012 workflow (EC-2) — no navigation side-effect.
 *
 * Endpoint contract (task_003_be):
 *   POST /api/documents/:id/replace  (multipart/form-data)
 *   fields: file, category, notes?
 *   → { documentId, fileName, category, uploadedAt, uploadedByName, status }
 *
 * Progress tracking is XHR-based so the upload progress bar works accurately.
 */

import { useCallback, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useAuthStore } from '@/hooks/useAuth';
import { documentPreviewKeys } from '@/hooks/useDocumentPreview';
import type { DocumentCategory } from '@/hooks/useClinicalDocumentUpload';

// ─── Constants ────────────────────────────────────────────────────────────────

const BASE_URL = import.meta.env.VITE_API_BASE_URL as string;

// ─── Types ────────────────────────────────────────────────────────────────────

export type ReplacementUploadStatus =
  | 'idle'
  | 'uploading'
  | 'success'
  | 'error';

export interface ReplacementUploadState {
  status:        ReplacementUploadStatus;
  /** Upload progress percentage 0–100 (only meaningful during 'uploading'). */
  progress:      number;
  errorMessage:  string | null;
  /** New document ID returned by the server after a successful replacement. */
  newDocumentId: string | null;
}

export interface UseDocumentReplacementUploadReturn {
  uploadState: ReplacementUploadState;
  /** Start a replacement upload for the given existing document. */
  startReplacement: (
    existingDocumentId: string,
    file: File,
    category: DocumentCategory,
    patientId: string,
    notes?: string,
  ) => void;
  /** Reset state back to idle (e.g. when drawer closes). */
  resetUpload: () => void;
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function useDocumentReplacementUpload(): UseDocumentReplacementUploadReturn {
  const queryClient = useQueryClient();
  const [uploadState, setUploadState] = useState<ReplacementUploadState>({
    status:        'idle',
    progress:      0,
    errorMessage:  null,
    newDocumentId: null,
  });

  const startReplacement = useCallback(
    (
      existingDocumentId: string,
      file: File,
      category: DocumentCategory,
      patientId: string,
      notes?: string,
    ) => {
      const token = useAuthStore.getState().accessToken;

      const formData = new FormData();
      formData.append('file',      file);
      formData.append('patientId', patientId);
      formData.append('category',  category);
      if (notes) formData.append('notes', notes);

      const xhr = new XMLHttpRequest();

      xhr.upload.addEventListener('progress', (ev) => {
        if (ev.lengthComputable) {
          setUploadState(prev => ({
            ...prev,
            status:   'uploading',
            progress: Math.round((ev.loaded / ev.total) * 100),
          }));
        }
      });

      xhr.addEventListener('load', () => {
        if (xhr.status === 200 || xhr.status === 201) {
          try {
            const response = JSON.parse(xhr.responseText) as { documentId: string };
            // Invalidate the preview cache for the OLD document so it reflects the superseded state.
            queryClient.invalidateQueries({
              queryKey: documentPreviewKeys.preview(existingDocumentId),
            });
            setUploadState({
              status:        'success',
              progress:      100,
              errorMessage:  null,
              newDocumentId: response.documentId,
            });
          } catch {
            setUploadState({
              status:        'error',
              progress:      0,
              errorMessage:  'Replacement succeeded but response could not be parsed.',
              newDocumentId: null,
            });
          }
        } else {
          let message = `Upload failed (${xhr.status}).`;
          try {
            const body = JSON.parse(xhr.responseText) as { message?: string };
            if (body.message) message = body.message;
          } catch { /* ignore */ }
          setUploadState({
            status:        'error',
            progress:      0,
            errorMessage:  message,
            newDocumentId: null,
          });
        }
      });

      xhr.addEventListener('error', () => {
        setUploadState({
          status:        'error',
          progress:      0,
          errorMessage:  'Network error during replacement upload. Please try again.',
          newDocumentId: null,
        });
      });

      xhr.addEventListener('abort', () => {
        setUploadState({
          status:        'idle',
          progress:      0,
          errorMessage:  null,
          newDocumentId: null,
        });
      });

      setUploadState({ status: 'uploading', progress: 0, errorMessage: null, newDocumentId: null });

      xhr.open('POST', `${BASE_URL}/api/documents/${existingDocumentId}/replace`);
      if (token) xhr.setRequestHeader('Authorization', `Bearer ${token}`);
      xhr.send(formData);
    },
    [queryClient],
  );

  const resetUpload = useCallback(() => {
    setUploadState({ status: 'idle', progress: 0, errorMessage: null, newDocumentId: null });
  }, []);

  return { uploadState, startReplacement, resetUpload };
}
