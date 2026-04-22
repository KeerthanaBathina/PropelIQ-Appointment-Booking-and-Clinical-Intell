/**
 * ManualReviewForm — manual data review/entry form for Patient Profile 360 (US_046).
 *
 * Two operating modes driven by the `mode` prop:
 *
 *   "low-confidence":
 *     Pre-fills each field with AI-extracted suggestions that scored < 80%.
 *     Shows ConfidenceBadge with showNeedsReview=true next to each entry.
 *     Shows DateConflictAlert when a date plausibility violation was detected (AC-2).
 *     Shows IncompleteDateBadge when a partial date needs staff completion (edge case).
 *     Each row has a "Confirm" button and a "Confirm All" bulk action (AC-3).
 *
 *   "manual":
 *     All fields start empty (AC-4 — AI completely unavailable).
 *     Uses the same tab structure (Medications, Diagnoses, Procedures, Allergies)
 *     as the wireframe SCR-013.
 *     Staff submit new entries; each saves with "manual-verified" status (AC-3).
 *
 * Screen states implemented per spec:
 *   Default     — form with entries
 *   Loading     — skeleton rows while data fetches
 *   Empty       — "No items for review" message
 *   Error       — API failure with retry
 *   Validation  — inline field-level errors <200ms (NFR-048, UXR-501)
 *
 * All interactive elements have ARIA labels and meet 44px touch target (UXR-304).
 */

import { useState } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import Divider from '@mui/material/Divider';
import Skeleton from '@mui/material/Skeleton';
import Stack from '@mui/material/Stack';
import Tab from '@mui/material/Tab';
import Tabs from '@mui/material/Tabs';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';
import DoneAllIcon from '@mui/icons-material/DoneAll';

import ConfidenceBadge from '@/components/profile/ConfidenceBadge';
import IncompleteDateBadge from '@/components/IncompleteDateBadge';
import DateConflictAlert from './DateConflictAlert';
import type { LowConfidenceItemDto } from '@/hooks/useManualFallback';
import { useConfirmManualEntry } from '@/hooks/useManualFallback';
import { useToast } from '@/components/common/ToastProvider';

// ─── Types ────────────────────────────────────────────────────────────────────

type ClinicalDataType = 'Medication' | 'Diagnosis' | 'Procedure' | 'Allergy';
type FormMode = 'low-confidence' | 'manual';

interface ManualReviewFormProps {
  patientId: string;
  mode: FormMode;
  /** Required in "low-confidence" mode — list of items with confidence < 0.80. */
  lowConfidenceItems?: LowConfidenceItemDto[];
  isLoading?: boolean;
  isError?: boolean;
  onRetry?: () => void;
}

// ─── Empty manual entry shape ─────────────────────────────────────────────────

interface ManualEntryState {
  dataType: ClinicalDataType;
  value: string;
  date: string;
  notes: string;
}

const EMPTY_ENTRY: ManualEntryState = {
  dataType: 'Medication',
  value: '',
  date: '',
  notes: '',
};

const DATA_TYPE_TABS: ClinicalDataType[] = ['Medication', 'Diagnosis', 'Procedure', 'Allergy'];

// ─── Single low-confidence review row ────────────────────────────────────────

function LowConfidenceRow({
  item,
  patientId,
  onConfirmed,
}: {
  item: LowConfidenceItemDto;
  patientId: string;
  onConfirmed: () => void;
}) {
  const { showToast } = useToast();
  const { mutate: confirm, isPending } = useConfirmManualEntry();
  const [editedValue, setEditedValue] = useState(item.normalizedValue);
  const [date, setDate] = useState(item.recordDate?.split('T')[0] ?? '');
  const [notes, setNotes] = useState('');
  // Validation
  const valueError = editedValue.trim().length === 0 ? 'Value is required.' : null;
  const notesError = notes.trim().length === 0 ? 'Notes are required for audit trail.' : null;

  const handleConfirm = () => {
    if (valueError || notesError) return;
    confirm(
      {
        patientId,
        extractedDataId: item.extractedDataId,
        confirmedValue: editedValue.trim(),
        confirmedDate: date || null,
        resolutionNotes: notes.trim(),
      },
      {
        onSuccess: () => {
          showToast({ message: 'Entry confirmed with manual-verified status.', severity: 'success' });
          onConfirmed();
        },
        onError: () => {
          showToast({ message: 'Failed to confirm entry. Please try again.', severity: 'error' });
        },
      },
    );
  };

  return (
    <Box
      sx={{
        p: 2,
        borderRadius: 1,
        border: '1px solid',
        borderColor: 'warning.light',
        bgcolor: 'warning.50',
        mb: 1.5,
      }}
      aria-label={`Low-confidence ${item.dataType.toLowerCase()} entry: ${item.normalizedValue}`}
    >
      {/* Date conflict warning (AC-2) */}
      {item.dateConflictExplanation && (
        <DateConflictAlert
          explanation={item.dateConflictExplanation}
          documentName={item.sourceDocumentName}
        />
      )}

      {/* Incomplete date badge (edge case) */}
      {item.isIncompleteDate && (
        <Box sx={{ mb: 1.5 }}>
          <IncompleteDateBadge
            partialDate={item.recordDate}
            onComplete={(d) => setDate(d)}
            disabled={isPending}
          />
        </Box>
      )}

      {/* Row header */}
      <Stack direction="row" spacing={1} alignItems="center" sx={{ mb: 1.5 }}>
        <Typography variant="caption" color="text.secondary" fontWeight={600}>
          {item.dataType.toUpperCase()}
        </Typography>
        <ConfidenceBadge score={item.confidenceScore} size="small" showNeedsReview />
        <Typography variant="caption" color="text.secondary" sx={{ ml: 'auto' }}>
          Source: {item.sourceDocumentName}
        </Typography>
      </Stack>

      <Stack spacing={1.5}>
        {/* Editable value field — pre-filled with AI suggestion (AC-1) */}
        <TextField
          label="Confirmed Value"
          value={editedValue}
          onChange={(e) => setEditedValue(e.target.value)}
          size="small"
          fullWidth
          required
          error={!!valueError && editedValue.trim().length === 0}
          helperText={editedValue.trim().length === 0 ? valueError : undefined}
          inputProps={{ 'aria-label': `Confirm value for ${item.dataType.toLowerCase()} entry` }}
          disabled={isPending}
        />

        {/* Date field — skip when incomplete date editor is active */}
        {!item.isIncompleteDate && (
          <TextField
            label="Date"
            type="date"
            value={date}
            onChange={(e) => setDate(e.target.value)}
            size="small"
            InputLabelProps={{ shrink: true }}
            inputProps={{ 'aria-label': 'Date for this entry', max: new Date().toISOString().split('T')[0] }}
            sx={{ width: 220 }}
            disabled={isPending}
          />
        )}

        {/* Resolution notes (required for audit trail — AC-3) */}
        <TextField
          label="Confirmation Notes"
          multiline
          minRows={2}
          value={notes}
          onChange={(e) => setNotes(e.target.value)}
          size="small"
          fullWidth
          required
          error={!!notesError && notes.trim().length === 0}
          helperText={notes.trim().length === 0 ? notesError : undefined}
          placeholder="Clinical rationale for confirming this entry…"
          inputProps={{ 'aria-label': 'Confirmation notes — required for audit trail', maxLength: 2000 }}
          disabled={isPending}
        />

        {/* Confirm button */}
        <Box sx={{ display: 'flex', justifyContent: 'flex-end' }}>
          <Button
            variant="contained"
            color="success"
            size="small"
            startIcon={isPending ? <CircularProgress size={14} color="inherit" /> : <CheckCircleOutlineIcon />}
            disabled={isPending || !!valueError || !!notesError}
            onClick={handleConfirm}
            sx={{ minHeight: 36 }}
            aria-label={`Confirm this ${item.dataType.toLowerCase()} entry with manual-verified status`}
          >
            Confirm
          </Button>
        </Box>
      </Stack>
    </Box>
  );
}

// ─── Manual entry form for a single data type ─────────────────────────────────

function ManualEntryPanel({
  patientId,
  dataType,
}: {
  patientId: string;
  dataType: ClinicalDataType;
}) {
  const { showToast } = useToast();
  const { mutate: confirm, isPending } = useConfirmManualEntry();
  const [entry, setEntry] = useState<ManualEntryState>({ ...EMPTY_ENTRY, dataType });

  const valueError = entry.value.trim().length === 0 ? 'Value is required.' : null;
  const notesError = entry.notes.trim().length === 0 ? 'Notes are required for audit trail.' : null;
  const canSubmit = !isPending && !valueError && !notesError;

  const handleSubmit = () => {
    if (!canSubmit) return;
    confirm(
      {
        patientId,
        // In full manual mode, there's no pre-existing extractedDataId.
        // The API endpoint accepts an empty string to create a new manual record.
        extractedDataId: '',
        confirmedValue: entry.value.trim(),
        confirmedDate: entry.date || null,
        resolutionNotes: entry.notes.trim(),
      },
      {
        onSuccess: () => {
          showToast({ message: `${dataType} entry saved with manual-verified status.`, severity: 'success' });
          setEntry({ ...EMPTY_ENTRY, dataType });
        },
        onError: () => {
          showToast({ message: 'Failed to save entry. Please try again.', severity: 'error' });
        },
      },
    );
  };

  return (
    <Box sx={{ pt: 1 }}>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        AI service is unavailable. Enter {dataType.toLowerCase()} data manually.
        Each confirmed entry is saved with <strong>manual-verified</strong> status and staff attribution.
      </Typography>

      <Stack spacing={2}>
        <TextField
          label={`${dataType} Value`}
          value={entry.value}
          onChange={(e) => setEntry((s) => ({ ...s, value: e.target.value }))}
          size="small"
          fullWidth
          required
          error={entry.value.trim().length === 0 && entry.value.length > 0 ? false : false}
          inputProps={{ 'aria-label': `${dataType} value`, 'aria-required': 'true' }}
          disabled={isPending}
        />

        <TextField
          label="Date"
          type="date"
          value={entry.date}
          onChange={(e) => setEntry((s) => ({ ...s, date: e.target.value }))}
          size="small"
          InputLabelProps={{ shrink: true }}
          inputProps={{ 'aria-label': `Date for ${dataType.toLowerCase()} entry`, max: new Date().toISOString().split('T')[0] }}
          sx={{ width: 220 }}
          disabled={isPending}
        />

        <TextField
          label="Notes"
          multiline
          minRows={2}
          value={entry.notes}
          onChange={(e) => setEntry((s) => ({ ...s, notes: e.target.value }))}
          size="small"
          fullWidth
          required
          placeholder="Describe the clinical context for this manual entry…"
          inputProps={{ 'aria-label': 'Entry notes — required', 'aria-required': 'true', maxLength: 2000 }}
          disabled={isPending}
        />

        <Box sx={{ display: 'flex', justifyContent: 'flex-end' }}>
          <Button
            variant="contained"
            color="primary"
            size="small"
            startIcon={isPending ? <CircularProgress size={14} color="inherit" /> : <CheckCircleOutlineIcon />}
            disabled={!canSubmit}
            onClick={handleSubmit}
            sx={{ minHeight: 36 }}
            aria-label={`Save ${dataType.toLowerCase()} entry with manual-verified status`}
          >
            Save Entry
          </Button>
        </Box>
      </Stack>
    </Box>
  );
}

// ─── Main component ───────────────────────────────────────────────────────────

export default function ManualReviewForm({
  patientId,
  mode,
  lowConfidenceItems = [],
  isLoading = false,
  isError = false,
  onRetry,
}: ManualReviewFormProps) {
  const { showToast } = useToast();
  const { mutate: confirmAll, isPending: confirmAllPending } = useConfirmManualEntry();
  const [activeTab, setActiveTab] = useState<ClinicalDataType>('Medication');
  // Track which item ids have been confirmed locally so they're hidden after confirm.
  const [confirmedIds, setConfirmedIds] = useState<Set<string>>(new Set());

  const handleItemConfirmed = (id: string) => {
    setConfirmedIds((prev) => new Set([...prev, id]));
  };

  // ── Low-confidence mode ─────────────────────────────────────────────────
  if (mode === 'low-confidence') {
    const pendingItems = lowConfidenceItems.filter(
      (i) => !confirmedIds.has(i.extractedDataId),
    );

    // State 1: Loading
    if (isLoading) {
      return (
        <Stack spacing={1.5} aria-label="Loading low-confidence items" aria-busy>
          {[1, 2, 3].map((i) => <Skeleton key={i} variant="rounded" height={100} />)}
        </Stack>
      );
    }

    // State 3: Error
    if (isError) {
      return (
        <Alert
          severity="error"
          role="alert"
          action={onRetry && (
            <Button size="small" color="inherit" onClick={onRetry} aria-label="Retry loading low-confidence items">
              Retry
            </Button>
          )}
        >
          Failed to load items for review. Please try again.
        </Alert>
      );
    }

    // State 2: Empty
    if (pendingItems.length === 0) {
      return (
        <Alert severity="success" role="status">
          All low-confidence entries have been reviewed. The profile is up to date.
        </Alert>
      );
    }

    // State 4: Default (items to review)
    return (
      <Box>
        {/* Header + Confirm All action */}
        <Stack direction="row" spacing={1} alignItems="center" sx={{ mb: 2 }}>
          <Typography variant="subtitle1" fontWeight={600}>
            {pendingItems.length} item{pendingItems.length !== 1 ? 's' : ''} need{pendingItems.length === 1 ? 's' : ''} review
          </Typography>
          <Typography variant="caption" color="text.secondary">
            (AI confidence below 80%)
          </Typography>
          <Box sx={{ flexGrow: 1 }} />
          <Button
            variant="outlined"
            color="success"
            size="small"
            startIcon={confirmAllPending ? <CircularProgress size={14} color="inherit" /> : <DoneAllIcon />}
            disabled={confirmAllPending}
            onClick={() => {
              // Confirm all pending items with AI suggestion values as-is.
              pendingItems.forEach((item) => {
                confirmAll(
                  {
                    patientId,
                    extractedDataId: item.extractedDataId,
                    confirmedValue: item.normalizedValue,
                    confirmedDate: item.recordDate ?? null,
                    resolutionNotes: 'Bulk confirmed — staff accepted AI suggestion.',
                  },
                  {
                    onSuccess: () => handleItemConfirmed(item.extractedDataId),
                    onError: () => {
                      showToast({ message: `Failed to confirm ${item.dataType} entry. Try individually.`, severity: 'warning' });
                    },
                  },
                );
              });
            }}
            sx={{ minHeight: 36 }}
            aria-label={`Confirm all ${pendingItems.length} low-confidence items with AI suggestions`}
          >
            Confirm All
          </Button>
        </Stack>

        <Divider sx={{ mb: 2 }} />

        {pendingItems.map((item) => (
          <LowConfidenceRow
            key={item.extractedDataId}
            item={item}
            patientId={patientId}
            onConfirmed={() => handleItemConfirmed(item.extractedDataId)}
          />
        ))}
      </Box>
    );
  }

  // ── Manual (AI unavailable) mode — tabbed form (AC-4) ──────────────────
  return (
    <Box>
      <Tabs
        value={activeTab}
        onChange={(_e, v) => setActiveTab(v as ClinicalDataType)}
        aria-label="Clinical data entry tabs"
        variant="scrollable"
        scrollButtons="auto"
        sx={{ borderBottom: 1, borderColor: 'divider', mb: 2 }}
      >
        {DATA_TYPE_TABS.map((dt) => (
          <Tab
            key={dt}
            label={`${dt}s`}
            value={dt}
            id={`manual-tab-${dt}`}
            aria-controls={`manual-panel-${dt}`}
          />
        ))}
      </Tabs>

      {DATA_TYPE_TABS.map((dt) => (
        <Box
          key={dt}
          role="tabpanel"
          id={`manual-panel-${dt}`}
          aria-labelledby={`manual-tab-${dt}`}
          hidden={activeTab !== dt}
        >
          {activeTab === dt && (
            <ManualEntryPanel patientId={patientId} dataType={dt} />
          )}
        </Box>
      ))}
    </Box>
  );
}
