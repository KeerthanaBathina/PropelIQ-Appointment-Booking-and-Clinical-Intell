/**
 * BothValidDialog — MUI Dialog for staff to provide a clinical explanation when
 * selecting "Both Valid — Different Dates" during conflict resolution (US_045 EC-2).
 *
 * Requires an explanation of at least 10 characters before the confirm button
 * is enabled, matching the server-side FluentValidation rule.
 *
 * Calls the onConfirm callback with the trimmed explanation text.
 * Loading state disables all actions while the mutation is in-flight.
 */

import { useState } from 'react';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import Alert from '@mui/material/Alert';
import EventAvailableIcon from '@mui/icons-material/EventAvailable';

// ─── Props ────────────────────────────────────────────────────────────────────

interface BothValidDialogProps {
  open: boolean;
  onClose: () => void;
  onConfirm: (explanation: string) => void;
  isLoading: boolean;
  errorMessage?: string | null;
}

// ─── Constants ────────────────────────────────────────────────────────────────

const MIN_EXPLANATION_LENGTH = 10;
const MAX_EXPLANATION_LENGTH = 2_000;

// ─── Component ────────────────────────────────────────────────────────────────

export default function BothValidDialog({
  open,
  onClose,
  onConfirm,
  isLoading,
  errorMessage,
}: BothValidDialogProps) {
  const [explanation, setExplanation] = useState('');
  const trimmed = explanation.trim();
  const canConfirm = trimmed.length >= MIN_EXPLANATION_LENGTH && !isLoading;

  const handleClose = () => {
    if (isLoading) return;
    setExplanation('');
    onClose();
  };

  const handleConfirm = () => {
    if (!canConfirm) return;
    onConfirm(trimmed);
  };

  return (
    <Dialog
      open={open}
      onClose={handleClose}
      fullWidth
      maxWidth="sm"
      aria-labelledby="both-valid-dialog-title"
      aria-modal
    >
      <DialogTitle
        id="both-valid-dialog-title"
        sx={{ display: 'flex', alignItems: 'center', gap: 1 }}
      >
        <EventAvailableIcon sx={{ color: 'info.main' }} aria-hidden />
        Both Valid — Different Dates
      </DialogTitle>

      <DialogContent>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          Both values will be preserved in the consolidated profile with distinct date
          attribution. Please provide a clinical explanation for why both entries are valid.
        </Typography>

        {errorMessage && (
          <Alert severity="error" sx={{ mb: 2 }} role="alert">
            {errorMessage}
          </Alert>
        )}

        <TextField
          multiline
          minRows={4}
          maxRows={8}
          fullWidth
          autoFocus
          value={explanation}
          onChange={(e) => setExplanation(e.target.value)}
          placeholder="e.g. Patient received treatment at two facilities on different dates. Both medication records are valid for their respective encounters."
          inputProps={{
            'aria-label': 'Clinical explanation for both valid resolution',
            'aria-required': 'true',
            maxLength: MAX_EXPLANATION_LENGTH,
          }}
          helperText={
            trimmed.length === 0
              ? 'Explanation is required (minimum 10 characters).'
              : trimmed.length < MIN_EXPLANATION_LENGTH
                ? `${MIN_EXPLANATION_LENGTH - trimmed.length} more character${MIN_EXPLANATION_LENGTH - trimmed.length !== 1 ? 's' : ''} required.`
                : `${trimmed.length} / ${MAX_EXPLANATION_LENGTH} characters`
          }
          error={trimmed.length > 0 && trimmed.length < MIN_EXPLANATION_LENGTH}
          disabled={isLoading}
        />
      </DialogContent>

      <DialogActions sx={{ px: 3, pb: 2, gap: 1 }}>
        <Button
          variant="text"
          onClick={handleClose}
          disabled={isLoading}
          aria-label="Cancel and return to conflict resolution"
        >
          Cancel
        </Button>
        <Button
          variant="contained"
          color="info"
          onClick={handleConfirm}
          disabled={!canConfirm}
          startIcon={isLoading ? <CircularProgress size={16} color="inherit" /> : <EventAvailableIcon />}
          aria-label="Confirm both values are valid and preserve both in consolidated profile"
        >
          Confirm Both Valid
        </Button>
      </DialogActions>
    </Dialog>
  );
}
