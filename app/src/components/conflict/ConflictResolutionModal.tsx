/**
 * ConflictResolutionModal — MUI Dialog orchestrating the conflict list and
 * detail/resolution flow for a patient (US_044 AC-2, AC-3, UXR-104, FR-053).
 *
 * Two internal views:
 *   1. List view  — paginated, filterable conflict list (urgent at top).
 *   2. Detail view — side-by-side source comparison + resolution form.
 *
 * State transitions:
 *   closed → list view → (click conflict row) → detail view
 *   detail view → (back button) → list view
 *   detail view → (resolve/dismiss) → success toast → list view (refreshed)
 *
 * Loading, empty, and error states are handled at both list and detail levels.
 *
 * Screen sizes:
 *   xs (375px):  fullScreen Dialog
 *   sm+ (768px+): maxWidth="lg" Dialog (no fullScreen)
 */

import { useCallback, useState } from 'react';
import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Dialog from '@mui/material/Dialog';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Divider from '@mui/material/Divider';
import IconButton from '@mui/material/IconButton';
import List from '@mui/material/List';
import ListItemButton from '@mui/material/ListItemButton';
import MenuItem from '@mui/material/MenuItem';
import Select from '@mui/material/Select';
import Skeleton from '@mui/material/Skeleton';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import Alert from '@mui/material/Alert';
import useMediaQuery from '@mui/material/useMediaQuery';
import { useTheme } from '@mui/material/styles';
import ArrowBackIcon from '@mui/icons-material/ArrowBack';
import CloseIcon from '@mui/icons-material/Close';
import ErrorOutlineIcon from '@mui/icons-material/ErrorOutline';
import WarningAmberIcon from '@mui/icons-material/WarningAmber';

import ConfidenceBadge from '@/components/profile/ConfidenceBadge';
import UrgentBadge from './UrgentBadge';
import ConflictComparisonView from './ConflictComparisonView';
import ConflictResolutionForm from './ConflictResolutionForm';
import BothValidDialog from './BothValidDialog';
import ResolutionProgressIndicator from './ResolutionProgressIndicator';
import { useConflicts, type ConflictFilters, type ConflictListDto } from '@/hooks/useConflicts';
import { useConflictDetail } from '@/hooks/useConflictDetail';
import { useResolveConflict } from '@/hooks/useResolveConflict';
import { useSelectValue } from '@/hooks/useSelectValue';
import { useBothValid } from '@/hooks/useBothValid';
import { useToast } from '@/components/common/ToastProvider';

// ─── Severity chip helper ─────────────────────────────────────────────────────

function SeverityChip({ severity }: { severity: ConflictListDto['severity'] }) {
  const colorMap: Record<ConflictListDto['severity'], 'error' | 'warning' | 'info' | 'default'> = {
    Critical: 'error',
    High:     'warning',
    Medium:   'info',
    Low:      'default',
  };
  return (
    <Chip
      label={severity}
      color={colorMap[severity]}
      size="small"
      aria-label={`Severity: ${severity}`}
    />
  );
}

// ─── Status chip helper ───────────────────────────────────────────────────────

function StatusChip({ status }: { status: ConflictListDto['status'] }) {
  const colorMap: Record<ConflictListDto['status'], 'warning' | 'info' | 'success' | 'default'> = {
    Detected:    'warning',
    UnderReview: 'info',
    Resolved:    'success',
    Dismissed:   'default',
  };
  return (
    <Chip
      label={status === 'UnderReview' ? 'Under Review' : status}
      color={colorMap[status]}
      size="small"
      variant="outlined"
      aria-label={`Status: ${status}`}
    />
  );
}

// ─── Props ────────────────────────────────────────────────────────────────────

export interface ConflictResolutionModalProps {
  open: boolean;
  patientId: string;
  onClose: () => void;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function ConflictResolutionModal({
  open,
  patientId,
  onClose,
}: ConflictResolutionModalProps) {
  const theme = useTheme();
  const isXs = useMediaQuery(theme.breakpoints.down('sm'));
  const { showToast } = useToast();

  // ── Navigation state ─────────────────────────────────────────────────────
  const [selectedConflictId, setSelectedConflictId] = useState<string | null>(null);
  const isDetailView = selectedConflictId !== null;

  // ── Filters ──────────────────────────────────────────────────────────────
  const [filters, setFilters] = useState<ConflictFilters>({ page: 1, pageSize: 20 });

  // ── Data ─────────────────────────────────────────────────────────────────
  const { data: conflictsPage, isLoading: listLoading, isError: listError } =
    useConflicts(patientId, filters);

  const { conflict: detail, isLoading: detailLoading, isError: detailError } =
    useConflictDetail(patientId, selectedConflictId);

  // ── Resolution state ──────────────────────────────────────────────────────
  const [selectedValue, setSelectedValue] = useState<string | null>(null);
  const [bothValidOpen, setBothValidOpen] = useState(false);
  const [mutationError, setMutationError] = useState<string | null>(null);

  // ── Mutations ─────────────────────────────────────────────────────────────
  const { mutate: resolveConflict, isPending: resolveLoading } = useResolveConflict();
  const { mutate: selectValue, isPending: selectLoading } = useSelectValue();
  const { mutate: bothValid, isPending: bothValidLoading } = useBothValid();

  const isAnyLoading = resolveLoading || selectLoading || bothValidLoading;

  // ── Select value handler (AC-2) ───────────────────────────────────────────
  const handleSubmitSelectValue = useCallback(
    (selectedExtractedDataId: string, notes: string) => {
      if (!selectedConflictId) return;
      setMutationError(null);
      selectValue(
        { patientId, conflictId: selectedConflictId, selectedExtractedDataId, resolutionNotes: notes },
        {
          onSuccess: () => {
            showToast({ message: 'Conflict resolved — selected value saved.', severity: 'success' });
            setSelectedConflictId(null);
            setSelectedValue(null);
          },
          onError: (err) => {
            setMutationError(err instanceof Error ? err.message : 'An error occurred. Please try again.');
          },
        },
      );
    },
    [patientId, selectedConflictId, selectValue, showToast],
  );

  // ── Both Valid handler (EC-2) ─────────────────────────────────────────────
  const handleConfirmBothValid = useCallback(
    (explanation: string) => {
      if (!selectedConflictId) return;
      setMutationError(null);
      bothValid(
        { patientId, conflictId: selectedConflictId, explanation },
        {
          onSuccess: () => {
            setBothValidOpen(false);
            showToast({ message: 'Conflict resolved — both values preserved.', severity: 'success' });
            setSelectedConflictId(null);
            setSelectedValue(null);
          },
          onError: (err) => {
            setMutationError(err instanceof Error ? err.message : 'An error occurred. Please try again.');
          },
        },
      );
    },
    [patientId, selectedConflictId, bothValid, showToast],
  );

  // ── Dismiss handler (US_044 preserve) ────────────────────────────────────
  const handleDismiss = useCallback(
    (notes: string) => {
      if (!selectedConflictId) return;
      setMutationError(null);
      resolveConflict(
        { patientId, conflictId: selectedConflictId, action: 'dismiss', resolutionNotes: notes },
        {
          onSuccess: () => {
            showToast({ message: 'Conflict dismissed as false positive.', severity: 'success' });
            setSelectedConflictId(null);
            setSelectedValue(null);
          },
          onError: (err) => {
            setMutationError(err instanceof Error ? err.message : 'An error occurred. Please try again.');
          },
        },
      );
    },
    [patientId, selectedConflictId, resolveConflict, showToast],
  );

  const handleClose = useCallback(() => {
    setSelectedConflictId(null);
    setFilters({ page: 1, pageSize: 20 });
    setSelectedValue(null);
    setMutationError(null);
    setBothValidOpen(false);
    onClose();
  }, [onClose]);

  // ── Render ────────────────────────────────────────────────────────────────
  return (
    <Dialog
      open={open}
      onClose={handleClose}
      fullWidth
      maxWidth="lg"
      fullScreen={isXs}
      aria-labelledby="conflict-modal-title"
      aria-modal
    >
      {/* ── Title bar ────────────────────────────────────────────────────── */}
      <DialogTitle
        id="conflict-modal-title"
        sx={{ display: 'flex', alignItems: 'center', gap: 1, pr: 6 }}
      >
        {isDetailView && (
          <IconButton
            size="small"
            onClick={() => { setSelectedConflictId(null); setMutationError(null); }}
            aria-label="Back to conflict list"
            sx={{ mr: 0.5 }}
          >
            <ArrowBackIcon />
          </IconButton>
        )}
        <WarningAmberIcon sx={{ color: 'warning.main' }} aria-hidden />
        <span>
          {isDetailView ? 'Conflict Detail' : 'Clinical Conflicts'}
        </span>
        {!isDetailView && conflictsPage && (
          <Chip
            label={conflictsPage.totalCount}
            size="small"
            color="warning"
            sx={{ ml: 0.5 }}
            aria-label={`${conflictsPage.totalCount} total conflicts`}
          />
        )}
        <IconButton
          size="small"
          onClick={handleClose}
          aria-label="Close conflict modal"
          sx={{ position: 'absolute', right: 8, top: 8 }}
        >
          <CloseIcon />
        </IconButton>
      </DialogTitle>

      <Divider />

      {/* Resolution progress indicator — shown in both list and detail views (EC-1) */}
      <Box sx={{ px: { xs: 2, sm: 3 }, pt: 2 }}>
        <ResolutionProgressIndicator patientId={patientId} />
      </Box>

      <DialogContent sx={{ p: { xs: 2, sm: 3 }, pt: 0 }}>

        {/* ── LIST VIEW ──────────────────────────────────────────────────── */}
        {!isDetailView && (
          <>
            {/* Filter row */}
            <Stack direction="row" spacing={1} sx={{ mb: 2, flexWrap: 'wrap' }}>
              <Select
                value={filters.status ?? ''}
                onChange={(e) => setFilters((f) => ({ ...f, status: e.target.value || undefined, page: 1 }))}
                displayEmpty
                size="small"
                inputProps={{ 'aria-label': 'Filter by status' }}
                sx={{ minWidth: 130 }}
              >
                <MenuItem value="">All statuses</MenuItem>
                <MenuItem value="Detected">Detected</MenuItem>
                <MenuItem value="UnderReview">Under Review</MenuItem>
                <MenuItem value="Resolved">Resolved</MenuItem>
                <MenuItem value="Dismissed">Dismissed</MenuItem>
              </Select>

              <Select
                value={filters.severity ?? ''}
                onChange={(e) => setFilters((f) => ({ ...f, severity: e.target.value || undefined, page: 1 }))}
                displayEmpty
                size="small"
                inputProps={{ 'aria-label': 'Filter by severity' }}
                sx={{ minWidth: 130 }}
              >
                <MenuItem value="">All severities</MenuItem>
                <MenuItem value="Critical">Critical</MenuItem>
                <MenuItem value="High">High</MenuItem>
                <MenuItem value="Medium">Medium</MenuItem>
                <MenuItem value="Low">Low</MenuItem>
              </Select>

              <Select
                value={filters.type ?? ''}
                onChange={(e) => setFilters((f) => ({ ...f, type: e.target.value || undefined, page: 1 }))}
                displayEmpty
                size="small"
                inputProps={{ 'aria-label': 'Filter by conflict type' }}
                sx={{ minWidth: 160 }}
              >
                <MenuItem value="">All types</MenuItem>
                <MenuItem value="MedicationContraindication">Medication Contraindication</MenuItem>
                <MenuItem value="MedicationDiscrepancy">Medication Discrepancy</MenuItem>
                <MenuItem value="DuplicateDiagnosis">Duplicate Diagnosis</MenuItem>
                <MenuItem value="ConflictingDiagnosis">Conflicting Diagnosis</MenuItem>
                <MenuItem value="DateInconsistency">Date Inconsistency</MenuItem>
              </Select>
            </Stack>

            {/* Loading state */}
            {listLoading && (
              <Stack spacing={1.5} aria-label="Loading conflicts" aria-busy>
                {[1, 2, 3].map((i) => (
                  <Skeleton key={i} variant="rounded" height={64} />
                ))}
              </Stack>
            )}

            {/* Error state */}
            {listError && !listLoading && (
              <Alert severity="error" role="alert">
                Failed to load conflicts. Please close and try again.
              </Alert>
            )}

            {/* Empty state */}
            {!listLoading && !listError && conflictsPage?.items.length === 0 && (
              <Box
                sx={{ textAlign: 'center', py: 6, color: 'text.secondary' }}
                role="status"
                aria-label="No conflicts found"
              >
                <ErrorOutlineIcon sx={{ fontSize: 48, opacity: 0.3, mb: 1 }} aria-hidden />
                <Typography variant="body1">No conflicts found</Typography>
                <Typography variant="caption">
                  Try adjusting the filters above.
                </Typography>
              </Box>
            )}

            {/* Conflict list — urgent first (server-sorted), then by date desc */}
            {!listLoading && !listError && (conflictsPage?.items.length ?? 0) > 0 && (
              <List disablePadding aria-label="Conflict list">
                {conflictsPage!.items.map((c) => (
                  <ListItemButton
                    key={c.conflictId}
                    onClick={() => setSelectedConflictId(c.conflictId)}
                    divider
                    sx={{
                      borderRadius: 1,
                      mb: 0.5,
                      bgcolor: c.isUrgent ? 'error.surface' : undefined,
                      '&:hover': {
                        bgcolor: c.isUrgent ? 'error.surface' : 'action.hover',
                      },
                    }}
                    aria-label={`View conflict: ${c.conflictDescription}`}
                  >
                    <Stack spacing={0.5} sx={{ width: '100%' }}>
                      <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap">
                        <UrgentBadge isUrgent={c.isUrgent} />
                        <SeverityChip severity={c.severity} />
                        <StatusChip status={c.status} />
                        <Typography variant="caption" color="text.secondary" sx={{ ml: 'auto' }}>
                          {new Date(c.createdAt).toLocaleDateString()}
                        </Typography>
                      </Stack>
                      <Typography variant="body2" sx={{ fontWeight: c.isUrgent ? 600 : 400 }}>
                        {c.conflictDescription}
                      </Typography>
                      <Stack direction="row" spacing={1} alignItems="center">
                        <Typography variant="caption" color="text.secondary">
                          {c.conflictType.replace(/([A-Z])/g, ' $1').trim()} ·{' '}
                          {c.sourceDocumentCount} source{c.sourceDocumentCount !== 1 ? 's' : ''}
                        </Typography>
                        <ConfidenceBadge score={c.aiConfidenceScore} size="small" />
                      </Stack>
                    </Stack>
                  </ListItemButton>
                ))}
              </List>
            )}
          </>
        )}

        {/* ── DETAIL VIEW ────────────────────────────────────────────────── */}
        {isDetailView && (
          <>
            {/* Detail loading state */}
            {detailLoading && (
              <Stack spacing={2} aria-label="Loading conflict detail" aria-busy>
                <Skeleton variant="rounded" height={80} />
                <Stack direction="row" spacing={2}>
                  <Skeleton variant="rounded" height={200} sx={{ flex: 1 }} />
                  <Skeleton variant="rounded" height={200} sx={{ flex: 1 }} />
                </Stack>
              </Stack>
            )}

            {/* Detail error state */}
            {detailError && !detailLoading && (
              <Alert severity="error" role="alert">
                Failed to load conflict detail. Please go back and try again.
              </Alert>
            )}

            {/* Detail content */}
            {!detailLoading && !detailError && detail && (
              <Stack spacing={3}>
                {/* Header chips */}
                <Stack direction="row" spacing={1} flexWrap="wrap" alignItems="center">
                  <UrgentBadge isUrgent={detail.isUrgent} />
                  <SeverityChip severity={detail.severity} />
                  <StatusChip status={detail.status} />
                  <ConfidenceBadge score={detail.aiConfidenceScore} />
                  <Typography variant="caption" color="text.secondary" sx={{ ml: 'auto' }}>
                    Detected {new Date(detail.createdAt).toLocaleString()}
                  </Typography>
                </Stack>

                {/* Side-by-side comparison view with value selector (UXR-104, AC-2, EC-2) */}
                <ConflictComparisonView
                  citations={detail.sourceCitations}
                  conflictDescription={detail.conflictDescription}
                  aiExplanation={detail.aiExplanation}
                  selectedValue={(detail.status === 'Detected' || detail.status === 'UnderReview') ? selectedValue : undefined}
                  onValueChange={(detail.status === 'Detected' || detail.status === 'UnderReview') ? setSelectedValue : undefined}
                  selectorDisabled={isAnyLoading}
                />

                {/* Resolution info if already closed */}
                {(detail.status === 'Resolved' || detail.status === 'Dismissed') && (
                  <Alert
                    severity={detail.status === 'Resolved' ? 'success' : 'info'}
                    role="status"
                  >
                    <strong>{detail.status === 'Resolved' ? 'Resolved' : 'Dismissed'}</strong>
                    {detail.resolvedByUserName && ` by ${detail.resolvedByUserName}`}
                    {detail.resolvedAt && ` on ${new Date(detail.resolvedAt).toLocaleDateString()}`}
                    {detail.resolutionNotes && (
                      <Typography variant="body2" sx={{ mt: 0.5 }}>
                        Notes: {detail.resolutionNotes}
                      </Typography>
                    )}
                  </Alert>
                )}

                {/* Resolution form — shown only when conflict is still open */}
                {(detail.status === 'Detected' || detail.status === 'UnderReview') && (
                  <>
                    <Divider />
                    <ConflictResolutionForm
                      selectedValue={selectedValue}
                      onSubmitSelectValue={handleSubmitSelectValue}
                      onRequestBothValid={() => setBothValidOpen(true)}
                      onDismiss={handleDismiss}
                      isLoading={isAnyLoading}
                      errorMessage={mutationError}
                    />
                  </>
                )}
              </Stack>
            )}
          </>
        )}

      </DialogContent>

      {/* BothValidDialog — opened when staff selects Both Valid sentinel (EC-2) */}
      <BothValidDialog
        open={bothValidOpen}
        onClose={() => setBothValidOpen(false)}
        onConfirm={handleConfirmBothValid}
        isLoading={bothValidLoading}
        errorMessage={bothValidOpen ? mutationError : null}
      />
    </Dialog>
  );
}
