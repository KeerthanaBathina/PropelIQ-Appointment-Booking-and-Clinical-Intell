/**
 * ConflictResolutionForm — resolution action bar for a single conflict (US_044 FR-053,
 * US_045 AC-2, EC-2).
 *
 * Action buttons:
 *   1. "Save Selected Value" (primary, success) — enabled when a source radio is
 *      selected and resolution notes are provided. Calls onSubmitSelectValue (AC-2).
 *   2. "Confirm Both Valid" (contained, info) — enabled when the BOTH_VALID sentinel
 *      is selected. Opens BothValidDialog via onRequestBothValid (EC-2).
 *   3. "Dismiss (False Positive)" (outlined, warning) — remains available for
 *      non-clinically-significant conflicts (original US_044 behaviour).
 *
 * The parent (ConflictResolutionModal) drives selectedValue and onValueChange so the
 * radio state lives at the modal level and can be reset on navigation.
 */

import { useState } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import Divider from '@mui/material/Divider';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import BlockIcon from '@mui/icons-material/Block';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';
import EventAvailableIcon from '@mui/icons-material/EventAvailable';

import { BOTH_VALID_VALUE } from './ConflictValueSelector';

// ─── Types ────────────────────────────────────────────────────────────────────

export interface ConflictResolutionFormProps {
  /** Currently selected extractedDataId from ConflictValueSelector, or null. */
  selectedValue: string | null;
  /** Called when staff confirms "Save Selected Value". */
  onSubmitSelectValue: (selectedExtractedDataId: string, notes: string) => void;
  /** Called when staff clicks "Confirm Both Valid" (parent opens BothValidDialog). */
  onRequestBothValid: () => void;
  /** Called when staff dismisses the conflict as a false positive. */
  onDismiss: (notes: string) => void;
  isLoading: boolean;
  errorMessage?: string | null;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function ConflictResolutionForm({
  selectedValue,
  onSubmitSelectValue,
  onRequestBothValid,
  onDismiss,
  isLoading,
  errorMessage,
}: ConflictResolutionFormProps) {
  const [notes, setNotes] = useState('');
  const trimmedNotes = notes.trim();

  const isBothValid = selectedValue === BOTH_VALID_VALUE;
  const hasSourceSelected = !!selectedValue && !isBothValid;
  const canSaveValue = hasSourceSelected && trimmedNotes.length > 0 && !isLoading;
  const canDismiss = trimmedNotes.length > 0 && !isLoading;

  return (
    <Box
      component="section"
      aria-label="Conflict resolution form"
      sx={{ pt: 1 }}
    >
      <Typography variant="subtitle2" gutterBottom>
        Resolution Notes{' '}
        <Typography component="span" color="error" aria-hidden>
          *
        </Typography>
      </Typography>

      <TextField
        multiline
        minRows={3}
        maxRows={6}
        fullWidth
        value={notes}
        onChange={(e) => setNotes(e.target.value)}
        placeholder="Enter your clinical reasoning for this resolution decision…"
        inputProps={{
          'aria-label': 'Resolution notes',
          'aria-required': 'true',
          maxLength: 2000,
        }}
        helperText={
          trimmedNotes.length === 0
            ? 'Notes are required to close this conflict.'
            : `${notes.length} / 2000 characters`
        }
        disabled={isLoading}
        sx={{ mb: 2 }}
      />

      {errorMessage && (
        <Alert severity="error" sx={{ mb: 2 }} role="alert">
          {errorMessage}
        </Alert>
      )}

      <Box sx={{ display: 'flex', gap: 1.5, flexWrap: 'wrap', alignItems: 'center' }}>
        {/* Save Selected Value — primary action (AC-2) */}
        <Button
          variant="contained"
          color="success"
          startIcon={
            isLoading && hasSourceSelected
              ? <CircularProgress size={16} color="inherit" />
              : <CheckCircleOutlineIcon />
          }
          disabled={!canSaveValue}
          onClick={() => onSubmitSelectValue(selectedValue!, trimmedNotes)}
          aria-label="Save selected value — marks the chosen source value as correct and resolves the conflict"
        >
          Save Selected Value
        </Button>

        {/* Confirm Both Valid — shown when sentinel option is selected (EC-2) */}
        {isBothValid && (
          <Button
            variant="contained"
            color="info"
            startIcon={
              isLoading
                ? <CircularProgress size={16} color="inherit" />
                : <EventAvailableIcon />
            }
            disabled={isLoading}
            onClick={onRequestBothValid}
            aria-label="Open dialog to confirm both values are valid with different dates"
          >
            Confirm Both Valid
          </Button>
        )}

        <Divider orientation="vertical" flexItem sx={{ mx: 0.5 }} />

        {/* Dismiss false positive (US_044 behaviour preserved) */}
        <Button
          variant="outlined"
          color="warning"
          startIcon={
            isLoading && !hasSourceSelected && !isBothValid
              ? <CircularProgress size={16} color="inherit" />
              : <BlockIcon />
          }
          disabled={!canDismiss}
          onClick={() => onDismiss(trimmedNotes)}
          aria-label="Dismiss as false positive — marks this conflict as not clinically significant"
        >
          Dismiss (False Positive)
        </Button>
      </Box>
    </Box>
  );
}
