/**
 * DocumentUploadPanel — SCR-012 upload entry point (US_038 AC-1, AC-3, AC-5).
 *
 * Renders:
 *   - Category selector (Lab Result, Prescription, Clinical Note, Imaging Report — AC-3)
 *   - Optional notes textarea
 *   - Drag-and-drop zone with browse fallback (AC-1)
 *   - Supported-format guidance hint (AC-5)
 *
 * Layout matches wireframe SCR-012: left column, card with category + notes above the drop zone.
 * Story rules override wireframe hints where they differ (categories and format/size limits).
 *
 * Accessibility:
 *   - Drop zone is keyboard-focusable (tabIndex=0) and activates on Enter/Space.
 *   - Hidden file input is triggered programmatically on click/keyboard activation.
 *   - Drag-over state announced via aria-live region (UXR-606).
 */

import { useCallback, useRef, useState } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import FormControl from '@mui/material/FormControl';
import InputLabel from '@mui/material/InputLabel';
import MenuItem from '@mui/material/MenuItem';
import Paper from '@mui/material/Paper';
import Select from '@mui/material/Select';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import UploadFileIcon from '@mui/icons-material/UploadFile';

import {
  ALLOWED_FORMATS_LABEL,
  MAX_FILE_SIZE_LABEL,
} from '@/utils/documentUploadValidation';
import {
  DOCUMENT_CATEGORY_LABELS,
  type DocumentCategory,
} from '@/hooks/useClinicalDocumentUpload';

// ─── Props ────────────────────────────────────────────────────────────────────

interface DocumentUploadPanelProps {
  /** Called with validated + category-tagged files ready for upload. */
  onFilesSelected: (files: File[], category: DocumentCategory, notes: string) => void;
  /** Disables the drop zone while uploads are in progress. */
  disabled?: boolean;
  /**
   * When `true`, shows a warning that Redis is unavailable and parsing may be slower (US_039 EC-1).
   * The upload flow continues normally; this is purely informational.
   */
  showQueueFallbackWarning?: boolean;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function DocumentUploadPanel({
  onFilesSelected,
  disabled = false,
  showQueueFallbackWarning = false,
}: DocumentUploadPanelProps) {
  const [category, setCategory] = useState<DocumentCategory>('LabResult');
  const [notes, setNotes] = useState('');
  const [isDragOver, setIsDragOver] = useState(false);

  const fileInputRef = useRef<HTMLInputElement>(null);

  // ── File collection helpers ────────────────────────────────────────────────

  const handleFiles = useCallback(
    (rawFiles: FileList | null) => {
      if (!rawFiles || rawFiles.length === 0) return;
      onFilesSelected(Array.from(rawFiles), category, notes);
    },
    [category, notes, onFilesSelected],
  );

  // ── Drag-and-drop handlers ────────────────────────────────────────────────

  const onDragOver = (e: React.DragEvent) => {
    e.preventDefault();
    if (!disabled) setIsDragOver(true);
  };

  const onDragLeave = () => setIsDragOver(false);

  const onDrop = (e: React.DragEvent) => {
    e.preventDefault();
    setIsDragOver(false);
    if (disabled) return;
    handleFiles(e.dataTransfer.files);
  };

  // ── Click / keyboard activation ───────────────────────────────────────────

  const openFilePicker = () => {
    if (!disabled) fileInputRef.current?.click();
  };

  const onKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      openFilePicker();
    }
  };

  // ── Render ────────────────────────────────────────────────────────────────

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      {/* Queue fallback warning — shown when Redis is unavailable (US_039 EC-1) */}
      {showQueueFallbackWarning && (
        <Alert severity="warning" role="status">
          <strong>Processing may take longer than usual</strong> &mdash; queue system is unavailable.
          Documents will still be uploaded and processed, but it may take more time.
        </Alert>
      )}
      {/* Category + notes card */}
      <Paper variant="outlined" sx={{ p: 3, borderRadius: 2 }}>
        <Typography variant="h6" gutterBottom>
          Select Category
        </Typography>

        <FormControl fullWidth size="small" sx={{ mb: 2 }}>
          <InputLabel id="doc-category-label">Document Type</InputLabel>
          <Select
            labelId="doc-category-label"
            id="doc-category"
            value={category}
            label="Document Type"
            onChange={e => setCategory(e.target.value as DocumentCategory)}
            disabled={disabled}
          >
            {(Object.entries(DOCUMENT_CATEGORY_LABELS) as [DocumentCategory, string][]).map(
              ([value, label]) => (
                <MenuItem key={value} value={value}>
                  {label}
                </MenuItem>
              ),
            )}
          </Select>
        </FormControl>

        <TextField
          id="doc-notes"
          label="Notes (optional)"
          multiline
          rows={3}
          fullWidth
          size="small"
          placeholder="Add any relevant notes about these documents…"
          value={notes}
          onChange={e => setNotes(e.target.value)}
          disabled={disabled}
        />
      </Paper>

      {/* Hidden file input */}
      <input
        ref={fileInputRef}
        type="file"
        multiple
        accept=".pdf,.docx,.txt,.png,.jpg,.jpeg"
        aria-hidden="true"
        style={{ display: 'none' }}
        onChange={e => handleFiles(e.target.files)}
        // Reset value so the same file can be re-selected after removal.
        onClick={e => ((e.target as HTMLInputElement).value = '')}
      />

      {/* Drop zone */}
      <Box
        role="button"
        tabIndex={disabled ? -1 : 0}
        aria-label="Drop files here or press Enter to browse your computer"
        aria-disabled={disabled}
        onDragOver={onDragOver}
        onDragLeave={onDragLeave}
        onDrop={onDrop}
        onClick={openFilePicker}
        onKeyDown={onKeyDown}
        sx={{
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          justifyContent: 'center',
          gap: 1.5,
          p: 4,
          border: '2px dashed',
          borderColor: isDragOver ? 'primary.main' : 'grey.300',
          borderRadius: 2,
          bgcolor: isDragOver ? 'primary.50' : disabled ? 'grey.50' : 'background.paper',
          cursor: disabled ? 'not-allowed' : 'pointer',
          transition: 'border-color 0.15s, background-color 0.15s',
          outline: 'none',
          '&:focus-visible': {
            borderColor: 'primary.main',
            boxShadow: theme => `0 0 0 3px ${theme.palette.primary.light}40`,
          },
        }}
      >
        <UploadFileIcon
          sx={{ fontSize: 40, color: isDragOver ? 'primary.main' : 'text.secondary' }}
        />
        <Box sx={{ textAlign: 'center' }}>
          <Typography variant="body1" fontWeight={600}>
            Drag &amp; drop files here
          </Typography>
          <Typography variant="body2" color="text.secondary">
            or{' '}
            <Box
              component="span"
              sx={{ color: 'primary.main', textDecoration: 'underline', fontWeight: 500 }}
            >
              browse your computer
            </Box>
          </Typography>
        </Box>

        {/* Supported formats hint (AC-5) */}
        <Typography variant="caption" color="text.secondary" sx={{ mt: 0.5 }}>
          {ALLOWED_FORMATS_LABEL} &mdash; up to {MAX_FILE_SIZE_LABEL} each
        </Typography>
      </Box>

      {/* Drag-over live region for screen readers (UXR-606) */}
      <Box
        aria-live="polite"
        aria-atomic="true"
        sx={{ position: 'absolute', width: 1, height: 1, overflow: 'hidden', clip: 'rect(0 0 0 0)' }}
      >
        {isDragOver ? 'Release to upload files' : ''}
      </Box>

      {disabled && (
        <Alert severity="info" sx={{ mt: 1 }}>
          Uploads are in progress. Drop zone is disabled until all files finish.
        </Alert>
      )}
    </Box>
  );
}
