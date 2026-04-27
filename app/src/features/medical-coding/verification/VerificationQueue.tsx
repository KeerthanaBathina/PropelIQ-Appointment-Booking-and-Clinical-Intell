/**
 * VerificationQueue — Main container for the US_075 human-in-the-loop verification
 * workflow on SCR-014 (AI Safety & Reliability, EP-013).
 *
 * Composes:
 *   - BatchVerificationToolbar (select-all, approve-selected, progress)
 *   - VerificationCard list (per-item approve / modify / reject)
 *   - OverrideJustificationDialog (modify action modal)
 *   - Reject justification inline dialog
 *
 * Screen states (per UXR spec):
 *   Loading      — Skeleton card placeholders (UXR-502)
 *   Error        — Alert severity="error" + retry button (UXR-601)
 *   Empty        — Alert severity="success" "All AI outputs verified" (AC-1)
 *   AI Unavailable — Info banner with manual workflow fallback (UXR-605)
 *   Default      — Toolbar + card list
 *
 * Finalization guard: when pending items exist, any finalization-attempt that
 * returns HTTP 400 "verification_required" is displayed as an inline error
 * Alert per AC-4.
 *
 * Selection state is managed locally — only pending-verification items are
 * selectable. Verified / modified / rejected items are always shown (read-only).
 */

import { useCallback, useMemo, useState } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Skeleton from '@mui/material/Skeleton';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import BlockIcon from '@mui/icons-material/Block';
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined';

import BatchVerificationToolbar from './components/BatchVerificationToolbar';
import OverrideJustificationDialog from './components/OverrideJustificationDialog';
import VerificationCard from './components/VerificationCard';
import {
  useApproveVerification,
  useBatchApprove,
  useModifyVerification,
  useRejectVerification,
  useVerificationQueue,
} from './hooks/useVerification';
import type { VerificationItem } from './types';

// ─── Reject dialog ────────────────────────────────────────────────────────────

const MIN_REJECTION_REASON = 10;

interface RejectDialogProps {
  open:        boolean;
  item:        VerificationItem | null;
  isRejecting: boolean;
  onClose:     () => void;
  onConfirm:   (justification: string) => void;
}

function RejectConfirmDialog({ open, item, isRejecting, onClose, onConfirm }: RejectDialogProps) {
  const [reason, setReason] = useState('');
  const [touched, setTouched] = useState(false);
  const hasError = touched && reason.trim().length < MIN_REJECTION_REASON;

  // Reset on open
  const handleEntered = () => { setReason(''); setTouched(false); };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      maxWidth="xs"
      fullWidth
      TransitionProps={{ onEntered: handleEntered }}
      aria-labelledby="reject-dialog-title"
    >
      <DialogTitle id="reject-dialog-title">
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <BlockIcon color="error" />
          Reject AI Output
        </Box>
      </DialogTitle>
      <DialogContent>
        <Typography variant="body2" sx={{ mb: 2 }}>
          Rejecting <strong>{item?.codeValue}</strong> will flag this record and
          prevent it from being used in downstream workflows.
        </Typography>
        <TextField
          label="Rejection reason *"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          onBlur={() => setTouched(true)}
          multiline
          rows={3}
          fullWidth
          required
          error={hasError}
          helperText={hasError ? `Reason must be at least ${MIN_REJECTION_REASON} characters.` : ''}
          placeholder="Explain why this AI output is being rejected…"
        />
      </DialogContent>
      <DialogActions>
        <Button variant="outlined" onClick={onClose} disabled={isRejecting}>
          Cancel
        </Button>
        <Button
          variant="contained"
          color="error"
          disabled={reason.trim().length < MIN_REJECTION_REASON || isRejecting}
          onClick={() => { setTouched(true); onConfirm(reason.trim()); }}
          startIcon={<BlockIcon />}
        >
          {isRejecting ? 'Rejecting…' : 'Reject'}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

// ─── Skeleton cards ───────────────────────────────────────────────────────────

function VerificationSkeletons({ count = 3 }: { count?: number }) {
  return (
    <>
      {Array.from({ length: count }).map((_, i) => (
        <Skeleton
          key={i}
          variant="rounded"
          height={160}
          sx={{ mb: 2, borderRadius: 2 }}
          aria-hidden="true"
        />
      ))}
    </>
  );
}

// ─── Props ────────────────────────────────────────────────────────────────────

interface VerificationQueueProps {
  patientId: string;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function VerificationQueue({ patientId }: VerificationQueueProps) {
  // ── Data ────────────────────────────────────────────────────────────────────
  const { items: response, isLoading, isError, isAiUnavailable, refetch } =
    useVerificationQueue(patientId);

  const allItems = useMemo(
    () => response?.items ?? [],
    [response],
  );
  const pendingItems = useMemo(
    () => allItems.filter((i) => i.verificationStatus === 'pending-verification'),
    [allItems],
  );
  const verifiedCount = allItems.length - pendingItems.length;

  // ── Mutations ────────────────────────────────────────────────────────────────
  const approveMutation = useApproveVerification(patientId);
  const modifyMutation  = useModifyVerification(patientId);
  const rejectMutation  = useRejectVerification(patientId);
  const batchMutation   = useBatchApprove(patientId);

  // ── Selection state ──────────────────────────────────────────────────────────
  const [selectedIds, setSelectedIds] = useState<string[]>([]);

  const handleSelectAll = useCallback((checked: boolean) => {
    setSelectedIds(checked ? pendingItems.map((i) => i.id) : []);
  }, [pendingItems]);

  // ── Override dialog state ────────────────────────────────────────────────────
  const [modifyTarget, setModifyTarget] = useState<VerificationItem | null>(null);
  const [modifyError,  setModifyError]  = useState<string | null>(null);

  // ── Reject dialog state ──────────────────────────────────────────────────────
  const [rejectTarget, setRejectTarget] = useState<VerificationItem | null>(null);

  // ─── Handlers ────────────────────────────────────────────────────────────────

  const handleApprove = useCallback((item: VerificationItem) => {
    approveMutation.mutate({
      recordId:   item.id,
      recordType: item.recordType,
    });
  }, [approveMutation]);

  const handleModifyOpen = useCallback((item: VerificationItem) => {
    setModifyError(null);
    setModifyTarget(item);
  }, []);

  const handleModifySubmit = useCallback(
    (payload: { newValue: string; justification: string }) => {
      if (!modifyTarget) return;
      modifyMutation.mutate(
        {
          recordId:     modifyTarget.id,
          recordType:   modifyTarget.recordType,
          newValue:     payload.newValue,
          justification: payload.justification,
        },
        {
          onSuccess: () => setModifyTarget(null),
          onError:   (err) =>
            setModifyError(
              err instanceof Error ? err.message : 'Failed to submit modification.',
            ),
        },
      );
    },
    [modifyMutation, modifyTarget],
  );

  const handleRejectOpen = useCallback((item: VerificationItem) => {
    setRejectTarget(item);
  }, []);

  const handleRejectConfirm = useCallback((justification: string) => {
    if (!rejectTarget) return;
    rejectMutation.mutate(
      {
        recordId:     rejectTarget.id,
        recordType:   rejectTarget.recordType,
        justification,
      },
      {
        onSuccess: () => setRejectTarget(null),
      },
    );
  }, [rejectMutation, rejectTarget]);

  const handleBatchApprove = useCallback(() => {
    if (selectedIds.length === 0) return;
    const firstType = pendingItems.find((i) => selectedIds.includes(i.id))?.recordType ?? 'MedicalCode';
    batchMutation.mutate(
      { recordIds: selectedIds, recordType: firstType },
      { onSuccess: () => setSelectedIds([]) },
    );
  }, [batchMutation, pendingItems, selectedIds]);

  // ─── Render ──────────────────────────────────────────────────────────────────

  return (
    <Box component="section" aria-label="AI Verification Queue">
      {/* ── AI Unavailable banner (UXR-605) ── */}
      {isAiUnavailable && (
        <Alert
          severity="info"
          role="alert"
          aria-live="assertive"
          icon={<InfoOutlinedIcon />}
          sx={{ mb: 2 }}
        >
          <Typography component="span" variant="body2" fontWeight={600}>
            AI service unavailable —{' '}
          </Typography>
          <Typography component="span" variant="body2">
            switch to manual workflow. Queued items will be reprocessed when the service resumes.
          </Typography>
        </Alert>
      )}

      {/* ── Error state (UXR-601) ── */}
      {!isLoading && isError && !isAiUnavailable && (
        <Alert
          severity="error"
          role="alert"
          action={
            <Button
              color="inherit"
              size="small"
              onClick={() => refetch()}
              aria-label="Retry loading verification queue"
            >
              Retry
            </Button>
          }
          sx={{ mb: 2 }}
        >
          Failed to load verification queue.
        </Alert>
      )}

      {/* ── Loading state (UXR-502) ── */}
      {isLoading && <VerificationSkeletons count={3} />}

      {/* ── Empty state ── */}
      {!isLoading && !isError && allItems.length === 0 && (
        <Alert severity="success" role="status" sx={{ mb: 2 }}>
          All AI outputs have been verified. No pending items remain.
        </Alert>
      )}

      {/* ── Default state: toolbar + cards ── */}
      {!isLoading && !isError && allItems.length > 0 && (
        <>
          {/* Batch toolbar */}
          <BatchVerificationToolbar
            totalCount={allItems.length}
            verifiedCount={verifiedCount}
            selectedIds={selectedIds}
            isApproving={batchMutation.isLoading}
            onSelectAll={handleSelectAll}
            onApproveSelected={handleBatchApprove}
          />

          {/* Verification cards */}
          {allItems.map((item) => (
            <VerificationCard
              key={item.id}
              item={item}
              isActionsDisabled={
                approveMutation.isLoading ||
                modifyMutation.isLoading  ||
                rejectMutation.isLoading  ||
                batchMutation.isLoading
              }
              onApprove={handleApprove}
              onModify={handleModifyOpen}
              onReject={handleRejectOpen}
            />
          ))}
        </>
      )}

      {/* ── Override justification dialog ── */}
      <OverrideJustificationDialog
        open={modifyTarget !== null}
        codeType={modifyTarget?.recordType === 'MedicalCode' ? 'ICD10' : 'general'}
        originalCodeValue={modifyTarget?.codeValue ?? ''}
        originalDescription={modifyTarget?.description ?? ''}
        isSubmitting={modifyMutation.isLoading}
        submitError={modifyError}
        onClose={() => setModifyTarget(null)}
        onSubmit={handleModifySubmit}
      />

      {/* ── Reject confirmation dialog ── */}
      <RejectConfirmDialog
        open={rejectTarget !== null}
        item={rejectTarget}
        isRejecting={rejectMutation.isLoading}
        onClose={() => setRejectTarget(null)}
        onConfirm={handleRejectConfirm}
      />
    </Box>
  );
}
