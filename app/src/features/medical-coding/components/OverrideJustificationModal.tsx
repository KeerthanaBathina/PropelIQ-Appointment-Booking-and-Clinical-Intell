/**
 * OverrideJustificationModal — MUI Dialog for CPT code override workflow (US_048, SCR-014).
 *
 * Per wireframe SCR-014:
 *   - Replacement Code input (text field, optional — allows correcting to a different code)
 *   - Justification textarea (required, 4 rows, multiline)
 *   - HIPAA audit notice caption
 *   - Cancel / Submit Override action buttons
 *
 * Validation: justification must be non-empty before submission (AC-2, HIPAA).
 * ARIA: focus moves to dialog title on open (UXR-201); focus trap enforced by MUI (UXR-202).
 * Keyboard: Cancel bound to Escape key via MUI Dialog onClose (UXR-203).
 */

import { useCallback, useEffect, useRef, useState } from 'react';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import GavelIcon from '@mui/icons-material/Gavel';

// ─── Props ────────────────────────────────────────────────────────────────────

export interface OverrideSubmitPayload {
  replacementCode: string;
  justification: string;
}

interface OverrideJustificationModalProps {
  open: boolean;
  /** Display name of the code being overridden (e.g. "99213 — Office Visit Level 3"). */
  codeLabel: string;
  isSubmitting: boolean;
  submitError: string | null;
  onClose: () => void;
  onSubmit: (payload: OverrideSubmitPayload) => void;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function OverrideJustificationModal({
  open,
  codeLabel,
  isSubmitting,
  submitError,
  onClose,
  onSubmit,
}: OverrideJustificationModalProps) {
  const titleRef    = useRef<HTMLHeadingElement>(null);
  const [replacementCode, setReplacementCode] = useState('');
  const [justification,   setJustification]   = useState('');
  const [touched, setTouched]                 = useState(false);

  const justificationError = touched && justification.trim().length === 0;

  // Reset form on open/close (UXR-201 — clean state each invocation)
  useEffect(() => {
    if (open) {
      setReplacementCode('');
      setJustification('');
      setTouched(false);
      // Move focus to title for screen reader announcement (UXR-201)
      requestAnimationFrame(() => titleRef.current?.focus());
    }
  }, [open]);

  const handleSubmit = useCallback(() => {
    setTouched(true);
    if (justification.trim().length === 0) return;
    onSubmit({ replacementCode: replacementCode.trim(), justification: justification.trim() });
  }, [justification, replacementCode, onSubmit]);

  return (
    <Dialog
      open={open}
      onClose={onClose}
      aria-labelledby="override-modal-title"
      aria-describedby="override-modal-desc"
      maxWidth="sm"
      fullWidth
    >
      <DialogTitle id="override-modal-title">
        <Typography
          ref={titleRef}
          tabIndex={-1}
          variant="h6"
          component="span"
          sx={{ display: 'flex', alignItems: 'center', gap: 1, outline: 'none' }}
        >
          <GavelIcon fontSize="small" color="warning" />
          Override Code — Justification Required
        </Typography>
        <Typography
          id="override-modal-desc"
          variant="body2"
          color="text.secondary"
          sx={{ mt: 0.5 }}
        >
          Overriding: <strong>{codeLabel}</strong>
        </Typography>
      </DialogTitle>

      <DialogContent dividers>
        {/* ── Submission error ── */}
        {submitError && (
          <Alert severity="error" role="alert" sx={{ mb: 2 }}>
            {submitError}
          </Alert>
        )}

        {/* ── Replacement code ── */}
        <TextField
          id="override-replacement-code"
          label="Replacement Code"
          placeholder="Enter CPT code (optional)"
          value={replacementCode}
          onChange={e => setReplacementCode(e.target.value)}
          fullWidth
          size="small"
          sx={{ mb: 2 }}
          inputProps={{ 'aria-label': 'Replacement CPT code' }}
        />

        {/* ── Justification ── */}
        <TextField
          id="override-justification"
          label={
            <>
              Justification{' '}
              <Typography component="span" color="error.main" variant="inherit">
                *
              </Typography>
            </>
          }
          placeholder="Provide clinical justification for overriding the AI-suggested code…"
          value={justification}
          onChange={e => { setJustification(e.target.value); setTouched(true); }}
          multiline
          rows={4}
          fullWidth
          required
          error={justificationError}
          helperText={justificationError ? 'Justification is required' : ''}
          inputProps={{ 'aria-label': 'Clinical justification for code override', 'aria-required': 'true' }}
        />

        {/* ── HIPAA audit notice ── */}
        <Typography
          variant="caption"
          color="text.secondary"
          sx={{ display: 'block', mt: 1.5 }}
        >
          Overrides are logged in the audit trail per HIPAA compliance requirements.
        </Typography>
      </DialogContent>

      <DialogActions sx={{ px: 3, py: 2, gap: 1 }}>
        <Button
          onClick={onClose}
          disabled={isSubmitting}
          variant="outlined"
          color="inherit"
          aria-label="Cancel code override"
        >
          Cancel
        </Button>
        <Button
          onClick={handleSubmit}
          disabled={isSubmitting || (touched && justificationError)}
          variant="contained"
          color="warning"
          aria-label="Submit code override"
          startIcon={isSubmitting ? <CircularProgress size={16} color="inherit" /> : null}
        >
          {isSubmitting ? 'Submitting…' : 'Submit Override'}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
