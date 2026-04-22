/**
 * BulkVerificationDialog — US_041 AC-4, EC-2
 *
 * Confirmation dialog for verifying multiple flagged extraction rows in a single action.
 *
 * Requirements:
 *   - EC-2: allow bulk confirmation of multiple selected flagged items.
 *   - Shows count of selected items and a summary by data type.
 *   - Disabled Confirm button while nothing is selected or mutation is in flight.
 *   - Loading spinner on the Confirm button during mutation (prevents double-submit).
 *   - Accessible: traps focus, announces result via aria-live, WAI-ARIA dialog pattern.
 *   - Success feedback is surfaced by the caller via ToastProvider (UXR-505).
 */

import { useMemo } from 'react';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import List from '@mui/material/List';
import ListItem from '@mui/material/ListItem';
import ListItemText from '@mui/material/ListItemText';
import Typography from '@mui/material/Typography';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';

// ─── Types ────────────────────────────────────────────────────────────────────

export interface VerifiableItem {
  /** `ExtractedData` row identifier. */
  extractedDataId: string;
  /** Human-readable label for the row (e.g. "Lisinopril 10 mg"). */
  label: string;
  /** Data category for grouping in the summary list. */
  dataType: 'Medication' | 'Diagnosis' | 'Procedure' | 'Allergy';
  /** Numeric confidence [0,1] or null (unavailable). */
  confidenceScore: number | null;
}

export interface BulkVerificationDialogProps {
  open: boolean;
  /** Items selected by the staff user for bulk confirmation. */
  selectedItems: VerifiableItem[];
  /** True while the bulk verification mutation is in flight. */
  isLoading: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

const DATA_TYPE_LABELS: Record<VerifiableItem['dataType'], string> = {
  Medication: 'Medications',
  Diagnosis:  'Diagnoses',
  Procedure:  'Procedures',
  Allergy:    'Allergies',
};

// ─── Component ────────────────────────────────────────────────────────────────

export default function BulkVerificationDialog({
  open,
  selectedItems,
  isLoading,
  onConfirm,
  onCancel,
}: BulkVerificationDialogProps) {
  const grouped = useMemo(() => {
    const map = new Map<VerifiableItem['dataType'], VerifiableItem[]>();
    for (const item of selectedItems) {
      const arr = map.get(item.dataType) ?? [];
      arr.push(item);
      map.set(item.dataType, arr);
    }
    return map;
  }, [selectedItems]);

  const isEmpty = selectedItems.length === 0;

  return (
    <Dialog
      open={open}
      onClose={isLoading ? undefined : onCancel}
      aria-labelledby="bulk-verify-title"
      aria-describedby="bulk-verify-desc"
      maxWidth="xs"
      fullWidth
      disableEscapeKeyDown={isLoading}
    >
      <DialogTitle id="bulk-verify-title" sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
        <CheckCircleOutlineIcon color="success" />
        Confirm Bulk Verification
      </DialogTitle>

      <DialogContent>
        <Typography id="bulk-verify-desc" variant="body2" color="text.secondary" gutterBottom>
          You are about to mark{' '}
          <strong>{selectedItems.length} item{selectedItems.length !== 1 ? 's' : ''}</strong>{' '}
          as verified. This will record your user attribution and a timestamp for each item.
        </Typography>

        {/* Per-type summary list */}
        {!isEmpty && (
          <List dense disablePadding sx={{ mt: 1 }}>
            {(Array.from(grouped.entries()) as [VerifiableItem['dataType'], VerifiableItem[]][]).map(
              ([type, items]) => (
                <ListItem key={type} disableGutters sx={{ py: 0.25 }}>
                  <ListItemText
                    primary={
                      <Typography variant="body2">
                        <strong>{DATA_TYPE_LABELS[type]}:</strong>{' '}
                        {items.map(i => i.label).join(', ')}
                      </Typography>
                    }
                  />
                </ListItem>
              ),
            )}
          </List>
        )}

        {isEmpty && (
          <Typography variant="body2" color="warning.main" sx={{ mt: 1 }}>
            No items are selected. Select flagged items before confirming.
          </Typography>
        )}
      </DialogContent>

      <DialogActions sx={{ px: 3, pb: 2 }}>
        <Button
          onClick={onCancel}
          disabled={isLoading}
          color="inherit"
          aria-label="Cancel bulk verification"
        >
          Cancel
        </Button>
        <Button
          onClick={onConfirm}
          disabled={isEmpty || isLoading}
          variant="contained"
          color="success"
          startIcon={
            isLoading
              ? <CircularProgress size={16} color="inherit" aria-hidden />
              : <CheckCircleOutlineIcon />
          }
          aria-label={
            isLoading
              ? 'Verifying items…'
              : `Verify ${selectedItems.length} selected item${selectedItems.length !== 1 ? 's' : ''}`
          }
          aria-busy={isLoading}
        >
          {isLoading ? 'Verifying…' : `Verify ${selectedItems.length} Item${selectedItems.length !== 1 ? 's' : ''}`}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
