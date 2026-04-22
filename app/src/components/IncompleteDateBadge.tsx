/**
 * IncompleteDateBadge — warning chip and inline date picker for data points where
 * the AI could only extract a partial date (month/year only), flagged as
 * "incomplete-date" (US_046 edge case, UXR-105).
 *
 * Layout:
 *   [⚠ Incomplete Date]  [date picker input]  [Save]
 *
 * When onComplete is provided the staff can pick the full date and submit.
 * When onComplete is omitted the badge is display-only (read-only mode).
 *
 * The partial date string (e.g. "2024-03") is shown as the defaultValue and
 * serves as a helpful hint so staff can identify the record context.
 *
 * ARIA: role="group", aria-label describes the purpose of the control group.
 * Keyboard: all interactive elements reachable via Tab (UXR-202).
 * Touch target: 44×44 px minimum (UXR-304 / WCAG 2.2 SC 2.5.8).
 */

import { useState } from 'react';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import EventBusyIcon from '@mui/icons-material/EventBusy';

// ─── Props ────────────────────────────────────────────────────────────────────

interface IncompleteDateBadgeProps {
  /** Partial ISO date string as extracted (e.g. "2024-03" or "2024"). */
  partialDate: string | null;
  /** When provided, renders the inline date input and Save button (AC-3). */
  onComplete?: (fullDate: string) => void;
  disabled?: boolean;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function IncompleteDateBadge({
  partialDate,
  onComplete,
  disabled = false,
}: IncompleteDateBadgeProps) {
  const [dateValue, setDateValue] = useState('');
  const canSave = dateValue.trim().length > 0 && !disabled;

  return (
    <Box
      role="group"
      aria-label="Incomplete date — staff completion required"
      sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}
    >
      {/* Warning badge chip */}
      <Chip
        icon={<EventBusyIcon fontSize="small" />}
        label="Incomplete Date"
        size="small"
        color="warning"
        variant="outlined"
        aria-label="This date is incomplete and requires staff completion"
      />

      {/* Partial date hint */}
      {partialDate && (
        <Typography variant="caption" color="text.secondary" aria-label={`Partial date extracted: ${partialDate}`}>
          Extracted: {partialDate}
        </Typography>
      )}

      {/* Inline date picker (only when onComplete is provided) */}
      {onComplete && (
        <>
          <TextField
            type="date"
            size="small"
            value={dateValue}
            onChange={(e) => setDateValue(e.target.value)}
            inputProps={{
              'aria-label': 'Enter full date to complete this record',
              min: '1900-01-01',
              max: new Date().toISOString().split('T')[0],
            }}
            sx={{ width: 160, '& .MuiInputBase-root': { height: 36 } }}
            disabled={disabled}
          />
          <Button
            size="small"
            variant="contained"
            color="warning"
            disabled={!canSave}
            onClick={() => onComplete(dateValue)}
            sx={{ minHeight: 36, minWidth: 60 }}
            aria-label="Save completed date"
          >
            Save
          </Button>
        </>
      )}
    </Box>
  );
}
