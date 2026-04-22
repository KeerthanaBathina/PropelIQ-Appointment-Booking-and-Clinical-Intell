/**
 * UploadedDocumentList — SCR-012 right-column file list (US_038 AC-1, AC-4, EC-1; US_039 AC-2, AC-3, AC-5; US_041 AC-2, AC-3).
 *
 * Renders one row per UploadEntry with:
 *   - File icon, name, and size
 *   - Progress bar during 'uploading' state
 *   - Upload status badge: Uploading (%), Success, Failed, Interrupted
 *   - AI parsing status badge: Queued, Parsing, Parsed, Failed (US_039)
 *   - Review-results link when parsing succeeds (AC-3)
 *   - Confidence-review affordance with flagged-item count when extraction data is ready (US_041 AC-2)
 *   - Manual-review action when parsing permanently fails (AC-5, EC-2)
 *   - Error alert with the specific message for 'error' and 'interrupted' states
 *   - Retry button for recoverable upload states (EC-1)
 *   - Remove button for non-uploading states
 *   - Backend-confirmed filename, timestamp, uploader, and status after success (AC-4)
 *
 * All status changes are announced via aria-live="polite" for accessibility (UXR-505, UXR-606).
 */

import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import IconButton from '@mui/material/IconButton';
import LinearProgress from '@mui/material/LinearProgress';
import Paper from '@mui/material/Paper';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import ArticleIcon from '@mui/icons-material/Article';
import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import CloseIcon from '@mui/icons-material/Close';
import ErrorIcon from '@mui/icons-material/Error';
import HourglassEmptyIcon from '@mui/icons-material/HourglassEmpty';
import OpenInNewIcon from '@mui/icons-material/OpenInNew';
import PendingIcon from '@mui/icons-material/Pending';
import PreviewIcon from '@mui/icons-material/Preview';
import RefreshIcon from '@mui/icons-material/Refresh';
import WarningIcon from '@mui/icons-material/Warning';

import {
  DOCUMENT_CATEGORY_LABELS,
  type UploadEntry,
  type UploadEntryStatus,
} from '@/hooks/useClinicalDocumentUpload';
import type {
  ParsingStatus,
  ParsingStatusMap,
} from '@/hooks/useClinicalDocumentParsingStatus';

// ─── Helpers ──────────────────────────────────────────────────────────────────

function formatBytes(bytes: number): string {
  if (bytes === 0) return '0 B';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(1))} ${sizes[i]}`;
}

function formatUploadedAt(iso: string): string {
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(iso));
}

// ─── Status badge ─────────────────────────────────────────────────────────────

interface StatusBadgeProps {
  status: UploadEntryStatus;
  progress: number;
}

function StatusBadge({ status, progress }: StatusBadgeProps) {
  switch (status) {
    case 'uploading':
      return (
        <Chip
          label={`Uploading ${progress}%`}
          size="small"
          color="primary"
          variant="outlined"
          aria-label={`Upload ${progress}% complete`}
        />
      );
    case 'pending':
      return <Chip label="Pending" size="small" variant="outlined" />;
    case 'success':
      return (
        <Chip
          label="Uploaded"
          size="small"
          color="success"
          icon={<CheckCircleIcon />}
          aria-label="Upload successful"
        />
      );
    case 'error':
      return (
        <Chip
          label="Failed"
          size="small"
          color="error"
          icon={<ErrorIcon />}
          aria-label="Upload failed"
        />
      );
    case 'interrupted':
      return (
        <Chip
          label="Interrupted"
          size="small"
          color="warning"
          icon={<WarningIcon />}
          aria-label="Upload interrupted"
        />
      );
  }
}

// ─── Parsing status badge (US_039) ────────────────────────────────────────────

interface ParsingStatusBadgeProps {
  status: ParsingStatus;
}

function ParsingStatusBadge({ status }: ParsingStatusBadgeProps) {
  switch (status) {
    case 'queued':
      return (
        <Chip
          label="Queued"
          size="small"
          color="default"
          icon={<HourglassEmptyIcon />}
          aria-label="Document queued for AI parsing"
        />
      );
    case 'parsing':
      return (
        <Chip
          label="Parsing"
          size="small"
          color="info"
          icon={<PendingIcon />}
          aria-label="AI parsing in progress"
        />
      );
    case 'parsed':
      return (
        <Chip
          label="Parsed"
          size="small"
          color="success"
          icon={<CheckCircleIcon />}
          aria-label="AI parsing complete"
        />
      );
    case 'failed':
      return (
        <Chip
          label="Parse Failed"
          size="small"
          color="error"
          icon={<ErrorIcon />}
          aria-label="AI parsing failed"
        />
      );
    default:
      return null;
  }
}

// ─── Props ────────────────────────────────────────────────────────────────────

interface UploadedDocumentListProps {
  entries: UploadEntry[];
  onRetry: (id: string) => void;
  onRemove: (id: string) => void;
  /** Parsing status map keyed by documentId — populated after upload success (US_039). */
  parsingStatusMap?: ParsingStatusMap;
  /** Called when staff clicks the manual-review action for a parse-failed document (US_039 AC-5, EC-2). */
  onManualReview?: (documentId: string) => void;
  /**
   * Number of flagged (low-confidence / unavailable) extracted items per documentId.
   * When > 0 for a parsed document, a confidence-review affordance is shown (US_041 AC-2).
   */
  flaggedCountMap?: Record<string, number>;
  /**
   * Called when staff clicks the Preview button for a parsed document (US_042 AC-1).
   * The parent mounts DocumentPreviewDrawer with the given documentId.
   */
  onPreviewDocument?: (documentId: string) => void;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function UploadedDocumentList({
  entries,
  onRetry,
  onRemove,
  parsingStatusMap = {},
  onManualReview,
  flaggedCountMap = {},
  onPreviewDocument,
}: UploadedDocumentListProps) {
  if (entries.length === 0) {
    return (
      <Paper variant="outlined" sx={{ p: 3, borderRadius: 2 }}>
        <Typography variant="h6" gutterBottom>
          Uploaded Files
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
          No files selected yet. Use the drop zone on the left to add documents.
        </Typography>
      </Paper>
    );
  }

  return (
    <Paper variant="outlined" sx={{ p: 3, borderRadius: 2 }}>
      <Typography variant="h6" gutterBottom>
        Uploaded Files
      </Typography>

      {/* Announce status changes to screen readers (UXR-505, US_039 AC-2/AC-3/AC-5) */}
      <Box
        aria-live="polite"
        aria-atomic="false"
        sx={{
          position: 'absolute',
          width: 1,
          height: 1,
          overflow: 'hidden',
          clip: 'rect(0 0 0 0)',
        }}
      >
        {entries
          .filter(e => e.status === 'success')
          .map(e => `${e.file.name} uploaded successfully.`)
          .join(' ')}
        {entries
          .filter(e => e.uploadedDocument)
          .map(e => {
            const ps = parsingStatusMap[e.uploadedDocument!.documentId];
            if (!ps) return null;
            if (ps.status === 'parsing') return `${e.file.name} is being parsed by AI.`;
            if (ps.status === 'parsed') return `${e.file.name} parsing complete. Results ready.`;
            if (ps.status === 'failed') return `${e.file.name} parsing failed. Manual review required.`;
            return null;
          })
          .filter(Boolean)
          .join(' ')}
      </Box>

      <Box sx={{ display: 'flex', flexDirection: 'column', gap: 0 }}>
        {entries.map((entry, index) => (
          <Box
            key={entry.id}
            sx={{
              py: 1.5,
              borderBottom:
                index < entries.length - 1 ? '1px solid' : 'none',
              borderColor: 'divider',
              opacity: entry.status === 'interrupted' ? 0.75 : 1,
            }}
          >
            {/* File row */}
            <Box sx={{ display: 'flex', alignItems: 'flex-start', gap: 1.5 }}>
              <ArticleIcon
                sx={{
                  mt: 0.25,
                  color:
                    entry.status === 'success'
                      ? 'success.main'
                      : entry.status === 'error' || entry.status === 'interrupted'
                      ? 'error.main'
                      : 'text.secondary',
                }}
              />

              <Box sx={{ flex: 1, minWidth: 0 }}>
                <Typography
                  variant="body2"
                  fontWeight={500}
                  noWrap
                  title={entry.file.name}
                >
                  {entry.file.name}
                </Typography>
                <Typography variant="caption" color="text.secondary">
                  {formatBytes(entry.file.size)} &bull;{' '}
                  {DOCUMENT_CATEGORY_LABELS[entry.category]}
                </Typography>

                {/* Progress bar — only shown while uploading */}
                {entry.status === 'uploading' && (
                  <LinearProgress
                    variant="determinate"
                    value={entry.progress}
                    aria-valuenow={entry.progress}
                    aria-valuemin={0}
                    aria-valuemax={100}
                    aria-label={`Uploading ${entry.file.name}`}
                    sx={{ mt: 0.75, height: 6, borderRadius: 1 }}
                  />
                )}

                {/* Success attribution (AC-4) */}
                {entry.status === 'success' && entry.uploadedDocument && ((
                  () => {
                    const docId = entry.uploadedDocument!.documentId;
                    const ps = parsingStatusMap[docId];
                    return (
                      <>
                        <Typography variant="caption" color="success.main" sx={{ display: 'block', mt: 0.5 }}>
                          Saved as "{entry.uploadedDocument!.fileName}" &bull;{' '}
                          {formatUploadedAt(entry.uploadedDocument!.uploadedAt)} &bull;{' '}
                          Uploaded by {entry.uploadedDocument!.uploadedByName}
                        </Typography>

                        {/* Parsing status row (US_039) */}
                        {ps && ps.status !== 'uploaded' && (
                          <Box sx={{ mt: 0.5, display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
                            <ParsingStatusBadge status={ps.status} />

                            {/* Review results link (AC-3) */}
                            {ps.status === 'parsed' && ps.reviewUrl && (
                              <Button
                                size="small"
                                variant="text"
                                color="success"
                                endIcon={<OpenInNewIcon />}
                                href={ps.reviewUrl}
                                aria-label={`Review AI parsing results for ${entry.file.name}`}
                              >
                                Review Results
                              </Button>
                            )}

                            {/* Preview button for parsed documents (US_042 AC-1) */}
                            {ps.status === 'parsed' && onPreviewDocument && (
                              <Tooltip title="Preview document with extraction highlights" arrow>
                                <Button
                                  size="small"
                                  variant="outlined"
                                  color="info"
                                  startIcon={<PreviewIcon />}
                                  onClick={() => onPreviewDocument(docId)}
                                  aria-label={`Preview ${entry.file.name} with extraction annotations`}
                                >
                                  Preview
                                </Button>
                              </Tooltip>
                            )}

                            {/* Confidence-review affordance: shown when flagged items exist (US_041 AC-2) */}
                            {ps.status === 'parsed' && docId && (flaggedCountMap[docId] ?? 0) > 0 && (
                              <Button
                                size="small"
                                variant="outlined"
                                color="warning"
                                href={ps.reviewUrl}
                                endIcon={<OpenInNewIcon />}
                                aria-label={`Review ${flaggedCountMap[docId]} low-confidence extraction item${flaggedCountMap[docId] !== 1 ? 's' : ''} for ${entry.file.name}`}
                              >
                                {flaggedCountMap[docId]} flagged item{flaggedCountMap[docId] !== 1 ? 's' : ''} — Review Confidence
                              </Button>
                            )}

                            {/* Manual review action (AC-5, EC-2) */}
                            {ps.status === 'failed' && onManualReview && (
                              <Button
                                size="small"
                                variant="outlined"
                                color="warning"
                                onClick={() => onManualReview(docId)}
                                aria-label={`Request manual review for ${entry.file.name}`}
                              >
                                Manual Review
                              </Button>
                            )}
                          </Box>
                        )}
                      </>
                    );
                  }
                )())}
              </Box>

              <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5, flexShrink: 0 }}>
                <StatusBadge status={entry.status} progress={entry.progress} />

                {/* Retry button for recoverable states (EC-1) */}
                {(entry.status === 'error' || entry.status === 'interrupted') && (
                  <Tooltip title="Retry upload">
                    <IconButton
                      size="small"
                      color="primary"
                      aria-label={`Retry upload for ${entry.file.name}`}
                      onClick={() => onRetry(entry.id)}
                    >
                      <RefreshIcon fontSize="small" />
                    </IconButton>
                  </Tooltip>
                )}

                {/* Remove button (not shown while actively uploading) */}
                {entry.status !== 'uploading' && (
                  <Tooltip
                    title={
                      entry.status === 'success'
                        ? 'Remove from list'
                        : 'Discard file'
                    }
                  >
                    <IconButton
                      size="small"
                      aria-label={`Remove ${entry.file.name} from list`}
                      onClick={() => onRemove(entry.id)}
                    >
                      <CloseIcon fontSize="small" />
                    </IconButton>
                  </Tooltip>
                )}
              </Box>
            </Box>

            {/* Error / interrupted message */}
            {entry.errorMessage && (
              <Alert
                severity={entry.status === 'interrupted' ? 'warning' : 'error'}
                sx={{ mt: 1, py: 0.5, fontSize: '0.8125rem' }}
                role="alert"
              >
                {entry.errorMessage}
                {(entry.status === 'error' || entry.status === 'interrupted') && (
                  <Button
                    size="small"
                    startIcon={<RefreshIcon />}
                    sx={{ ml: 1 }}
                    onClick={() => onRetry(entry.id)}
                    aria-label={`Retry upload for ${entry.file.name}`}
                  >
                    Retry
                  </Button>
                )}
              </Alert>
            )}
          </Box>
        ))}
      </Box>
    </Paper>
  );
}
