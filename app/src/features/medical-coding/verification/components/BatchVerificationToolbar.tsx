/**
 * BatchVerificationToolbar — Toolbar for bulk verification actions (US_075, edge case: 50+ items).
 *
 * Per task spec:
 *   - Checkbox "Select All" toggle
 *   - Selected count display (e.g. "3 of 12 selected")
 *   - "Approve Selected" Button (disabled when 0 selected)
 *   - LinearProgress showing verification progress (verified / total)
 *   - For 50+ items: shows a confirmation dialog before batch approve to prevent
 *     accidental bulk approval (edge case per AC spec)
 *
 * Rendered above the verification card list when items count > 1.
 * Hidden when queue is empty.
 *
 * ARIA:
 *   - Toolbar has role="toolbar" and aria-label
 *   - Progress bar has aria-label and aria-valuenow
 */

import { useState } from 'react';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Checkbox from '@mui/material/Checkbox';
import CircularProgress from '@mui/material/CircularProgress';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import FormControlLabel from '@mui/material/FormControlLabel';
import LinearProgress from '@mui/material/LinearProgress';
import Paper from '@mui/material/Paper';
import Typography from '@mui/material/Typography';
import DoneAllIcon from '@mui/icons-material/DoneAll';

// ─── Constants ────────────────────────────────────────────────────────────────

/** Items count threshold that triggers a confirmation dialog before batch approve. */
const BULK_CONFIRM_THRESHOLD = 50;

// ─── Props ────────────────────────────────────────────────────────────────────

interface BatchVerificationToolbarProps {
  /** Total number of items in the queue. */
  totalCount: number;
  /** Number of items that are already verified / modified / rejected. */
  verifiedCount: number;
  /** IDs of currently selected (checked) pending items. */
  selectedIds: string[];
  /** True when a batch approve mutation is in flight. */
  isApproving: boolean;
  onSelectAll:    (checked: boolean) => void;
  onApproveSelected: () => void;
}

// ─── Confirmation dialog ──────────────────────────────────────────────────────

interface BulkConfirmDialogProps {
  open:        boolean;
  count:       number;
  isApproving: boolean;
  onConfirm:   () => void;
  onCancel:    () => void;
}

function BulkConfirmDialog({
  open,
  count,
  isApproving,
  onConfirm,
  onCancel,
}: BulkConfirmDialogProps) {
  return (
    <Dialog
      open={open}
      onClose={onCancel}
      maxWidth="xs"
      fullWidth
      aria-labelledby="bulk-confirm-title"
    >
      <DialogTitle id="bulk-confirm-title">Confirm Batch Approval</DialogTitle>
      <DialogContent>
        <Typography variant="body2">
          You are about to approve <strong>{count}</strong>{' '}
          {count === 1 ? 'item' : 'items'}. Each item will receive an individual
          audit entry. This action cannot be undone. Continue?
        </Typography>
      </DialogContent>
      <DialogActions>
        <Button variant="outlined" onClick={onCancel} disabled={isApproving}>
          Cancel
        </Button>
        <Button
          variant="contained"
          color="success"
          onClick={onConfirm}
          disabled={isApproving}
          startIcon={
            isApproving ? (
              <CircularProgress size={16} color="inherit" />
            ) : (
              <DoneAllIcon />
            )
          }
          aria-label={`Confirm approving ${count} items`}
        >
          {isApproving ? 'Approving…' : 'Approve All'}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function BatchVerificationToolbar({
  totalCount,
  verifiedCount,
  selectedIds,
  isApproving,
  onSelectAll,
  onApproveSelected,
}: BatchVerificationToolbarProps) {
  const [confirmOpen, setConfirmOpen] = useState(false);

  if (totalCount <= 1) return null;

  const pendingCount   = totalCount - verifiedCount;
  const selectedCount  = selectedIds.length;
  const progressValue  = totalCount > 0 ? Math.round((verifiedCount / totalCount) * 100) : 0;
  const allSelected    = pendingCount > 0 && selectedCount === pendingCount;

  function handleApproveClick() {
    if (selectedCount >= BULK_CONFIRM_THRESHOLD) {
      setConfirmOpen(true);
    } else {
      onApproveSelected();
    }
  }

  function handleConfirm() {
    setConfirmOpen(false);
    onApproveSelected();
  }

  return (
    <>
      <Paper
        variant="outlined"
        role="toolbar"
        aria-label="Batch verification actions"
        sx={{
          display:        'flex',
          alignItems:     'center',
          gap:            2,
          flexWrap:       'wrap',
          px:             2,
          py:             1.5,
          mb:             2,
          borderRadius:   2,
          bgcolor:        'grey.50',
        }}
      >
        {/* ── Select all checkbox ── */}
        <FormControlLabel
          control={
            <Checkbox
              checked={allSelected}
              indeterminate={selectedCount > 0 && !allSelected}
              onChange={(e) => onSelectAll(e.target.checked)}
              disabled={isApproving || pendingCount === 0}
              aria-label="Select all pending items"
              size="small"
            />
          }
          label={
            <Typography variant="body2" fontWeight={500}>
              Select All
            </Typography>
          }
          sx={{ mr: 0 }}
        />

        {/* ── Selected count ── */}
        <Typography variant="body2" color="text.secondary" sx={{ flex: 1, minWidth: 120 }}>
          {selectedCount > 0
            ? `${selectedCount} of ${pendingCount} selected`
            : `${pendingCount} pending`}
        </Typography>

        {/* ── Approve selected button ── */}
        <Button
          variant="contained"
          color="success"
          size="small"
          disabled={selectedCount === 0 || isApproving}
          startIcon={
            isApproving ? (
              <CircularProgress size={16} color="inherit" />
            ) : (
              <DoneAllIcon />
            )
          }
          onClick={handleApproveClick}
          aria-label={`Approve ${selectedCount} selected items`}
        >
          {isApproving ? 'Approving…' : `Approve Selected (${selectedCount})`}
        </Button>

        {/* ── Verification progress ── */}
        <Box sx={{ width: '100%', mt: 1 }}>
          <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 0.5 }}>
            <Typography variant="caption" color="text.secondary">
              Verification progress
            </Typography>
            <Typography variant="caption" fontWeight={600}>
              {verifiedCount}/{totalCount} verified
            </Typography>
          </Box>
          <LinearProgress
            variant="determinate"
            value={progressValue}
            aria-label="Verification progress"
            aria-valuenow={progressValue}
            aria-valuemin={0}
            aria-valuemax={100}
            sx={{ borderRadius: 4, height: 6 }}
            color={progressValue === 100 ? 'success' : 'primary'}
          />
        </Box>
      </Paper>

      {/* ── Bulk confirmation dialog (50+ items) ── */}
      <BulkConfirmDialog
        open={confirmOpen}
        count={selectedCount}
        isApproving={isApproving}
        onConfirm={handleConfirm}
        onCancel={() => setConfirmOpen(false)}
      />
    </>
  );
}
