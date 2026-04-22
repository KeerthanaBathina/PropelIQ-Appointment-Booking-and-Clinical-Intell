/**
 * DocumentPreviewDrawer — US_042 AC-1, AC-2, AC-4, EC-1, EC-2
 *
 * Right-anchored MUI Drawer (width 560 px) that renders:
 *   - Overlay mode  (supportsOverlay = true):  <img> or <object> + DocumentExtractionOverlay (AC-1).
 *   - Fallback mode (supportsOverlay = false): InlineExtractionAnnotations plain-text view (EC-1).
 *
 * The "Re-Upload" action stays within the SCR-012 screen (EC-2).
 * Uploads use useDocumentReplacementUpload with an inline progress bar.
 * Toast feedback is driven by the parent via the onReplacementSuccess callback.
 *
 * Accessibility:
 *   - Role="dialog" with aria-labelledby / aria-describedby (UXR-505, UXR-606).
 *   - Focus is trapped inside the Drawer (MUI Drawer default).
 *   - Close button always visible and keyboard accessible.
 */

import { useCallback, useEffect, useRef, useState } from 'react';
import type { ChangeEvent } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import Divider from '@mui/material/Divider';
import Drawer from '@mui/material/Drawer';
import IconButton from '@mui/material/IconButton';
import LinearProgress from '@mui/material/LinearProgress';
import Skeleton from '@mui/material/Skeleton';
import Tab from '@mui/material/Tab';
import Tabs from '@mui/material/Tabs';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import CloseIcon from '@mui/icons-material/Close';
import FileUploadIcon from '@mui/icons-material/FileUpload';
import VisibilityIcon from '@mui/icons-material/Visibility';

import DocumentExtractionOverlay from '@/components/documents/DocumentExtractionOverlay';
import InlineExtractionAnnotations from '@/components/documents/InlineExtractionAnnotations';
import { useDocumentPreview } from '@/hooks/useDocumentPreview';
import { useDocumentReplacementUpload } from '@/hooks/useDocumentReplacementUpload';
import type { DocumentCategory } from '@/hooks/useClinicalDocumentUpload';

// ─── Constants ────────────────────────────────────────────────────────────────

const DRAWER_WIDTH  = 560;
const PREVIEW_ID    = 'doc-preview-drawer-title';
const DESCRIPTION_ID = 'doc-preview-drawer-desc';

// ─── Tab panel helper ─────────────────────────────────────────────────────────

interface TabPanelProps {
  children: React.ReactNode;
  index: number;
  value: number;
}

function TabPanel({ children, index, value }: TabPanelProps) {
  return (
    <Box
      role="tabpanel"
      hidden={value !== index}
      id={`doc-preview-tab-panel-${index}`}
      aria-labelledby={`doc-preview-tab-${index}`}
    >
      {value === index && <Box sx={{ pt: 2 }}>{children}</Box>}
    </Box>
  );
}

// ─── Props ────────────────────────────────────────────────────────────────────

export interface DocumentPreviewDrawerProps {
  /** ID of the document to preview. Pass null/undefined to close the drawer. */
  documentId:   string | null | undefined;
  /** Patient ID — required for the replacement upload multipart payload. */
  patientId:    string;
  onClose:      () => void;
  /**
   * Called after a successful replacement upload with the new document ID.
   * Parent should refresh the document list and/or show a success toast.
   */
  onReplacementSuccess: (newDocumentId: string, fileName: string) => void;
  /** Called when the preview could not be loaded (to show error toast in parent). */
  onPreviewLoadFailed?: (fileName: string) => void;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function DocumentPreviewDrawer({
  documentId,
  patientId,
  onClose,
  onReplacementSuccess,
  onPreviewLoadFailed,
}: DocumentPreviewDrawerProps) {
  const open = !!documentId;

  // Preview data
  const {
    data:    previewData,
    isLoading: previewLoading,
    isError:   previewError,
  } = useDocumentPreview(documentId);

  // Replacement upload
  const { uploadState, startReplacement, resetUpload } = useDocumentReplacementUpload();

  // Tab state (Preview | Annotations)
  const [activeTab, setActiveTab] = useState(0);

  // Overlay container dimensions (measured after image renders)
  const containerRef = useRef<HTMLDivElement>(null);
  const [containerDims, setContainerDims] = useState({ width: 0, height: 0 });

  // Hidden file input for re-upload (AC-2)
  const fileInputRef = useRef<HTMLInputElement>(null);

  // ── Measure container for overlay coordinates ──
  useEffect(() => {
    if (!containerRef.current) return;
    const obs = new ResizeObserver((entries) => {
      const entry = entries[0];
      if (entry) {
        const { width, height } = entry.contentRect;
        setContainerDims({ width, height });
      }
    });
    obs.observe(containerRef.current);
    return () => obs.disconnect();
  }, []);

  // ── Reset upload state whenever drawer closes or a new document opens ──
  useEffect(() => {
    if (!open) {
      resetUpload();
      setActiveTab(0);
    }
  }, [open, documentId, resetUpload]);

  // ── Watch for replacement success ──
  useEffect(() => {
    if (uploadState.status === 'success' && uploadState.newDocumentId && previewData) {
      onReplacementSuccess(uploadState.newDocumentId, previewData.fileName);
      onClose();
    }
  }, [uploadState.status, uploadState.newDocumentId, previewData, onReplacementSuccess, onClose]);

  // ── Notify parent when preview fails (AC toast) ──
  useEffect(() => {
    if (previewError && previewData === undefined && onPreviewLoadFailed) {
      onPreviewLoadFailed(documentId ?? 'document');
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [previewError]);

  // ── File picker handler ──────────────────────────────────────────────────
  const handleFileInputChange = useCallback(
    (ev: ChangeEvent<HTMLInputElement>) => {
      const file = ev.target.files?.[0];
      if (!file || !documentId || !previewData) return;
      // Use the same category as the original document for the replacement.
      startReplacement(
        documentId,
        file,
        previewData.category as DocumentCategory,
        patientId,
      );
      // Reset input so the same file can be re-selected if needed.
      ev.target.value = '';
    },
    [documentId, previewData, patientId, startReplacement],
  );

  // ── Determine render mode ────────────────────────────────────────────────
  const supportsOverlay = previewData?.supportsOverlay ?? false;
  const isImage = previewData?.contentType.startsWith('image/') ?? false;

  // ─────────────────────────────────────────────────────────────────────────

  return (
    <Drawer
      anchor="right"
      open={open}
      onClose={onClose}
      PaperProps={{
        sx: { width: DRAWER_WIDTH, display: 'flex', flexDirection: 'column' },
        role: 'dialog',
        'aria-labelledby': PREVIEW_ID,
        'aria-describedby': DESCRIPTION_ID,
        'aria-modal': 'true',
      }}
    >
      {/* ── Header ──────────────────────────────────────────────────────── */}
      <Box
        sx={{
          px: 2.5,
          py: 1.5,
          display: 'flex',
          alignItems: 'center',
          gap: 1,
          borderBottom: '1px solid',
          borderColor: 'divider',
        }}
      >
        <VisibilityIcon color="action" fontSize="small" sx={{ mr: 0.5 }} />
        <Box sx={{ flex: 1, minWidth: 0 }}>
          <Typography
            id={PREVIEW_ID}
            variant="subtitle1"
            fontWeight={600}
            noWrap
            title={previewData?.fileName}
          >
            {previewLoading ? <Skeleton width={200} /> : (previewData?.fileName ?? 'Document Preview')}
          </Typography>
          <Typography id={DESCRIPTION_ID} variant="caption" color="text.secondary">
            {previewLoading
              ? <Skeleton width={120} />
              : previewData?.category ?? ''}
          </Typography>
        </Box>

        {/* Re-Upload button (AC-2) */}
        <Tooltip title="Replace this document with a new file" arrow>
          <span>
            <Button
              variant="outlined"
              size="small"
              startIcon={<FileUploadIcon />}
              disabled={previewLoading || !!previewError || uploadState.status === 'uploading'}
              onClick={() => fileInputRef.current?.click()}
              aria-label="Re-upload document"
            >
              Re-Upload
            </Button>
          </span>
        </Tooltip>

        <IconButton
          onClick={onClose}
          aria-label="Close document preview"
          size="small"
        >
          <CloseIcon />
        </IconButton>
      </Box>

      {/* Hidden file input */}
      <input
        ref={fileInputRef}
        type="file"
        accept=".pdf,.png,.jpg,.jpeg,.tif,.tiff,.txt"
        aria-hidden="true"
        tabIndex={-1}
        style={{ display: 'none' }}
        onChange={handleFileInputChange}
      />

      {/* ── Upload progress bar ──────────────────────────────────────────── */}
      {uploadState.status === 'uploading' && (
        <Box sx={{ px: 2.5, pt: 1 }}>
          <Typography variant="caption" color="text.secondary" display="block" gutterBottom>
            Uploading replacement… {uploadState.progress}%
          </Typography>
          <LinearProgress
            variant="determinate"
            value={uploadState.progress}
            aria-label="Replacement upload progress"
            aria-valuenow={uploadState.progress}
          />
        </Box>
      )}

      {/* ── Upload error ─────────────────────────────────────────────────── */}
      {uploadState.status === 'error' && uploadState.errorMessage && (
        <Alert
          severity="error"
          onClose={resetUpload}
          sx={{ mx: 2.5, mt: 1 }}
        >
          {uploadState.errorMessage}
        </Alert>
      )}

      {/* ── Body ─────────────────────────────────────────────────────────── */}
      <Box sx={{ flex: 1, overflowY: 'auto', px: 2.5, py: 2 }}>

        {/* Loading skeleton */}
        {previewLoading && (
          <Box>
            <Skeleton variant="rectangular" height={320} sx={{ mb: 2 }} />
            <Skeleton width="80%" />
            <Skeleton width="60%" />
            <Skeleton width="70%" />
          </Box>
        )}

        {/* Error state */}
        {previewError && (
          <Alert severity="error" sx={{ mt: 2 }}>
            Preview could not be loaded. Please try again or contact support.
          </Alert>
        )}

        {/* Tabs: Preview | Annotations */}
        {previewData && !previewLoading && (
          <>
            <Tabs
              value={activeTab}
              onChange={(_, v: number) => setActiveTab(v)}
              aria-label="Document preview tabs"
              sx={{ borderBottom: 1, borderColor: 'divider', mb: 0 }}
            >
              <Tab
                id="doc-preview-tab-0"
                aria-controls="doc-preview-tab-panel-0"
                label="Preview"
              />
              <Tab
                id="doc-preview-tab-1"
                aria-controls="doc-preview-tab-panel-1"
                label={`Annotations (${previewData.annotations.length})`}
              />
            </Tabs>

            {/* ── Preview Tab ─────────────────────────────────────────────── */}
            <TabPanel value={activeTab} index={0}>
              {supportsOverlay ? (
                /* Overlay mode (AC-1) — image or PDF with bounding-box highlights */
                <Box
                  ref={containerRef}
                  sx={{ position: 'relative', display: 'inline-block', width: '100%' }}
                >
                  {isImage ? (
                    <Box
                      component="img"
                      src={previewData.previewUrl}
                      alt={`Preview of ${previewData.fileName}`}
                      sx={{ width: '100%', display: 'block', borderRadius: 1 }}
                      onLoad={() => {
                        if (containerRef.current) {
                          setContainerDims({
                            width:  containerRef.current.offsetWidth,
                            height: containerRef.current.offsetHeight,
                          });
                        }
                      }}
                    />
                  ) : (
                    /* PDF via <object> */
                    <Box
                      component="object"
                      data={previewData.previewUrl}
                      type={previewData.contentType}
                      aria-label={`PDF preview of ${previewData.fileName}`}
                      sx={{
                        width: '100%',
                        height: 600,
                        borderRadius: 1,
                        border: '1px solid',
                        borderColor: 'divider',
                      }}
                    >
                      <Typography variant="body2" sx={{ p: 2 }}>
                        Your browser cannot display this PDF.{' '}
                        <a
                          href={previewData.previewUrl}
                          target="_blank"
                          rel="noopener noreferrer"
                        >
                          Open in new tab
                        </a>
                      </Typography>
                    </Box>
                  )}

                  {containerDims.width > 0 && containerDims.height > 0 && (
                    <DocumentExtractionOverlay
                      containerWidth={containerDims.width}
                      containerHeight={containerDims.height}
                      annotations={previewData.annotations}
                    />
                  )}
                </Box>
              ) : (
                /* Text fallback mode (EC-1) */
                <InlineExtractionAnnotations
                  annotations={previewData.annotations}
                />
              )}
            </TabPanel>

            {/* ── Annotations Tab ─────────────────────────────────────────── */}
            <TabPanel value={activeTab} index={1}>
              <InlineExtractionAnnotations
                annotations={previewData.annotations}
              />
            </TabPanel>
          </>
        )}
      </Box>

      {/* ── Footer ───────────────────────────────────────────────────────── */}
      <Divider />
      <Box
        sx={{
          px: 2.5,
          py: 1.5,
          display: 'flex',
          justifyContent: 'flex-end',
          gap: 1,
        }}
      >
        {uploadState.status === 'uploading' && (
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
            <CircularProgress size={18} />
            <Typography variant="caption" color="text.secondary">
              Uploading replacement…
            </Typography>
          </Box>
        )}
        <Button variant="outlined" onClick={onClose} disabled={uploadState.status === 'uploading'}>
          Close
        </Button>
      </Box>
    </Drawer>
  );
}
