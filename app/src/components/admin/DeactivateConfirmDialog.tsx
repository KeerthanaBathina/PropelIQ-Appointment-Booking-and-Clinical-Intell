/**
 * DeactivateConfirmDialog — Confirmation dialog for staff account deactivation (US_061 AC-3).
 *
 * EC-1: Prevents self-deactivation — shows "Cannot deactivate your own account."
 * EC-2: API returns 409/422 for last-admin guard — shows "At least one active admin
 *        account required."
 * UXR-102: Destructive action requires explicit confirmation dialog.
 * UXR-501: Accessible focus management — Cancel focused by default for safe keyboard nav.
 */

import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Typography from '@mui/material/Typography';
import { useToast } from '@/components/common/ToastProvider';
import { useDeactivateStaff } from '@/hooks/useStaffAccounts';
import { useAuthStore } from '@/hooks/useAuth';
import type { StaffAccount } from '@/types/staff';

// ─── Props ────────────────────────────────────────────────────────────────────

interface DeactivateConfirmDialogProps {
  /** The account to be deactivated, or null when the dialog is closed. */
  target: StaffAccount | null;
  onClose: () => void;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function DeactivateConfirmDialog({ target, onClose }: DeactivateConfirmDialogProps) {
  const { mutate: deactivate, isLoading, error, reset } = useDeactivateStaff();
  const { showToast } = useToast();
  const currentEmail  = useAuthStore(s => s.email);

  // EC-1: current user is the target
  const isSelf = !!target && !!currentEmail && target.email.toLowerCase() === currentEmail.toLowerCase();

  // EC-2: server returned last-admin error
  const apiErrMsg = (error as { message?: string } | null)?.message ?? '';
  const isLastAdmin =
    apiErrMsg.toLowerCase().includes('last') ||
    apiErrMsg.toLowerCase().includes('at least one');

  function handleClose() {
    reset(); // clear mutation error state
    onClose();
  }

  function handleConfirm() {
    if (!target || isSelf) return;

    deactivate(target.id, {
      onSuccess: () => {
        showToast({ message: `${target.fullName} has been deactivated.`, severity: 'success' });
        handleClose();
      },
      onError: () => {
        // EC-2 handled via `error` reactive state — dialog stays open, shows error
      },
    });
  }

  return (
    <Dialog
      open={!!target}
      onClose={handleClose}
      maxWidth="xs"
      fullWidth
      aria-labelledby="deactivate-dialog-title"
    >
      <DialogTitle id="deactivate-dialog-title">Deactivate Account</DialogTitle>

      <DialogContent>
        {/* EC-1: self-deactivation guard */}
        {isSelf ? (
          <Alert severity="error" sx={{ mb: 2 }} role="alert">
            Cannot deactivate your own account.
          </Alert>
        ) : isLastAdmin ? (
          /* EC-2: last-admin guard (from API error) */
          <Alert severity="error" sx={{ mb: 2 }} role="alert">
            At least one active admin account required.
          </Alert>
        ) : (
          <Typography>
            Are you sure you want to deactivate{' '}
            <strong>{target?.fullName}</strong>? They will no longer be able to
            log in, but all historical data and audit records will be preserved.
          </Typography>
        )}
      </DialogContent>

      <DialogActions sx={{ px: 3, pb: 2 }}>
        <Button onClick={handleClose} autoFocus={!isSelf}>
          Cancel
        </Button>
        <Button
          variant="contained"
          color="error"
          onClick={handleConfirm}
          disabled={isLoading || isSelf || isLastAdmin}
          aria-label={target ? `Confirm deactivation of ${target.fullName}` : 'Confirm deactivation'}
        >
          {isLoading ? 'Deactivating…' : 'Deactivate'}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
