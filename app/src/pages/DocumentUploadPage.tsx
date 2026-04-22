/**
 * DocumentUploadPage — SCR-012 (US_038, US_039)
 *
 * Staff-facing document upload workflow. Mounts DocumentUploadPanel (left) and
 * UploadedDocumentList (right) in the two-column layout from the wireframe.
 *
 * The patientId is read from the URL parameter (:patientId) so the same page is
 * reusable for any patient without hard-coding an ID.
 *
 * Success toast is shown via ToastProvider after each confirmed upload (UXR-505).
 * Parsing-lifecycle toasts (started, complete, failed) shown as AI pipeline progresses (US_039 AC-2, AC-3, AC-5).
 * Validation and interruption feedback surfaces inline in UploadedDocumentList (AC-5, EC-1).
 * Degraded-mode warning shown in DocumentUploadPanel when Redis fallback is active (US_039 EC-1).
 */

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useParams } from 'react-router-dom';
import AppBar from '@mui/material/AppBar';
import Box from '@mui/material/Box';
import Breadcrumbs from '@mui/material/Breadcrumbs';
import Container from '@mui/material/Container';
import Link from '@mui/material/Link';
import Toolbar from '@mui/material/Toolbar';
import Typography from '@mui/material/Typography';

import DocumentPreviewDrawer from '@/components/documents/DocumentPreviewDrawer';
import DocumentUploadPanel from '@/components/documents/DocumentUploadPanel';
import UploadedDocumentList from '@/components/documents/UploadedDocumentList';
import { useToast } from '@/components/common/ToastProvider';
import {
  useClinicalDocumentUpload,
  type DocumentCategory,
} from '@/hooks/useClinicalDocumentUpload';
import {
  useClinicalDocumentParsingStatus,
  type ParsingStatus,
} from '@/hooks/useClinicalDocumentParsingStatus';
import { useDocumentsFlaggedCounts } from '@/hooks/useExtractedDataVerification';

// ─── Component ────────────────────────────────────────────────────────────────

export default function DocumentUploadPage() {
  const { patientId = '' } = useParams<{ patientId: string }>();
  const { showToast, showParsingStarted, showParsingComplete, showParsingFailed, showReplacementSuccess, showReprocessingStarted, showPreviewLoadFailed } = useToast();

  // ── Document preview drawer state (US_042 AC-1, EC-2) ──────────────────
  const [previewDocumentId, setPreviewDocumentId] = useState<string | null>(null);

  const handlePreviewDocument = useCallback((documentId: string) => {
    setPreviewDocumentId(documentId);
  }, []);

  const handlePreviewClose = useCallback(() => {
    setPreviewDocumentId(null);
  }, []);

  const handleReplacementSuccess = useCallback(
    (_newDocumentId: string, fileName: string) => {
      showReplacementSuccess(fileName);
      showReprocessingStarted(fileName);
      // Reset preview drawer
      setPreviewDocumentId(null);
      // The replacement document enters the normal backend upload→parse pipeline.
      // Existing polling (useClinicalDocumentParsingStatus) will surface its status
      // once the document list next refreshes.
    },
    [showReplacementSuccess, showReprocessingStarted],
  );
  const { entries, addFiles, retryEntry, removeEntry, isUploading } =
    useClinicalDocumentUpload();

  // Collect successfully uploaded document IDs to pass to the polling hook.
  const uploadedDocIds = entries
    .filter(e => e.status === 'success' && e.uploadedDocument)
    .map(e => e.uploadedDocument!.documentId);

  const { parsingStatusMap, degradedMode } = useClinicalDocumentParsingStatus(
    uploadedDocIds,
    patientId,
  );

  // Parsed document IDs — used to fetch flagged extraction item counts (US_041 AC-2).
  const parsedDocIds = useMemo(
    () => uploadedDocIds.filter(id => parsingStatusMap[id]?.status === 'parsed'),
    [uploadedDocIds, parsingStatusMap],
  );
  const { data: flaggedCountMap = {} } = useDocumentsFlaggedCounts(parsedDocIds);

  // Track previously-seen success entries to show upload toast exactly once per success.
  const notifiedUploadIds = useRef<Set<string>>(new Set());

  // Track previously-seen parsing state transitions to show parsing toasts exactly once.
  const notifiedParsingStates = useRef<Map<string, ParsingStatus>>(new Map());

  // Upload success toast.
  useEffect(() => {
    for (const entry of entries) {
      if (entry.status === 'success' && !notifiedUploadIds.current.has(entry.id)) {
        notifiedUploadIds.current.add(entry.id);
        showToast({
          message: `"${entry.uploadedDocument?.fileName ?? entry.file.name}" uploaded successfully.`,
          severity: 'success',
        });
      }
    }
  }, [entries, showToast]);

  // Parsing lifecycle toasts (US_039 AC-2, AC-3, AC-5).
  useEffect(() => {
    for (const [docId, row] of Object.entries(parsingStatusMap)) {
      const prev = notifiedParsingStates.current.get(docId);
      if (prev === row.status) continue;

      // Find the file name from the entries list for a friendlier message.
      const entry = entries.find(e => e.uploadedDocument?.documentId === docId);
      const fileName = entry?.uploadedDocument?.fileName ?? entry?.file.name ?? docId;

      if (row.status === 'parsing' && prev !== 'parsing') {
        showParsingStarted(fileName);
      } else if (row.status === 'parsed' && prev !== 'parsed') {
        showParsingComplete(fileName, row.reviewUrl ?? `/staff/documents/${docId}/results`);
      } else if (row.status === 'failed' && prev !== 'failed') {
        showParsingFailed(fileName, () => handleManualReview(docId));
      }

      notifiedParsingStates.current.set(docId, row.status);
    }
  }, [parsingStatusMap, entries, showParsingStarted, showParsingComplete, showParsingFailed]);

  const handleFilesSelected = useCallback(
    (files: File[], category: DocumentCategory, notes: string) => {
      addFiles(files, category, patientId, notes);
    },
    [addFiles, patientId],
  );

  const handleRetry = useCallback(
    (id: string) => {
      retryEntry(id);
    },
    [retryEntry],
  );

  const handleManualReview = useCallback((documentId: string) => {
    // Navigate to the manual review route when it is available (future EP-006 screen).
    // For now, open the patient profile where staff can escalate manually.
    window.open(`/staff/patients/${patientId}?reviewDocument=${documentId}`, '_blank');
  }, [patientId]);

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', minHeight: '100vh' }}>
      {/* App bar */}
      <AppBar position="static" color="default" elevation={1}>
        <Toolbar>
          <Typography variant="h6" component="h2" sx={{ flexGrow: 1 }}>
            Document Upload
          </Typography>
        </Toolbar>
      </AppBar>

      <Container maxWidth="xl" sx={{ py: 3, flexGrow: 1 }}>
        {/* Breadcrumb (SCR-012 wireframe) */}
        <Breadcrumbs aria-label="Breadcrumb" sx={{ mb: 3 }}>
          <Link href="/staff/dashboard" underline="hover" color="inherit">
            Staff Dashboard
          </Link>
          {patientId && (
            <Link
              href={`/staff/patients/${patientId}`}
              underline="hover"
              color="inherit"
            >
              Patient Profile
            </Link>
          )}
          <Typography color="text.primary">Upload Documents</Typography>
        </Breadcrumbs>

        <Typography variant="h4" component="h1" gutterBottom>
          Upload Clinical Documents
        </Typography>

        {/* Two-column layout (wireframe: left = panel, right = file list) */}
        <Box
          sx={{
            display: 'flex',
            gap: 3,
            flexWrap: 'wrap',
            alignItems: 'flex-start',
          }}
        >
          {/* Left column: category selector + drop zone */}
          <Box sx={{ flex: 1, minWidth: 320 }}>
            <DocumentUploadPanel
              onFilesSelected={handleFilesSelected}
              disabled={isUploading}
              showQueueFallbackWarning={degradedMode}
            />
          </Box>

          {/* Right column: file list */}
          <Box sx={{ flex: 1, minWidth: 320 }}>
          <UploadedDocumentList
              entries={entries}
              onRetry={handleRetry}
              onRemove={removeEntry}
              parsingStatusMap={parsingStatusMap}
              onManualReview={handleManualReview}
              flaggedCountMap={flaggedCountMap}
              onPreviewDocument={handlePreviewDocument}
            />
          </Box>
        </Box>
      </Container>

      {/* Document preview drawer (US_042 AC-1, AC-2, EC-2) */}
      <DocumentPreviewDrawer
        documentId={previewDocumentId}
        patientId={patientId}
        onClose={handlePreviewClose}
        onReplacementSuccess={handleReplacementSuccess}
        onPreviewLoadFailed={showPreviewLoadFailed}
      />
    </Box>
  );
}
