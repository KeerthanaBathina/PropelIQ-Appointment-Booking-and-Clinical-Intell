/**
 * DateConflictAlert — MUI Alert (warning severity) for chronological plausibility
 * violations detected during clinical data consolidation (US_046 AC-2, UXR-105).
 *
 * Renders a collapsible explanation of the date inconsistency so staff understand
 * exactly which dates are in conflict (e.g., "Procedure date 2023-01-10 is before
 * diagnosis date 2023-06-15").
 *
 * Props:
 *   explanation  — server-generated human-readable conflict description
 *   documentName — source document contributing the conflicting date
 *
 * ARIA: role="alert" (assertive when first rendered), UXR-206.
 */

import Alert from '@mui/material/Alert';
import AlertTitle from '@mui/material/AlertTitle';
import Typography from '@mui/material/Typography';
import EventRepeatIcon from '@mui/icons-material/EventRepeat';

// ─── Props ────────────────────────────────────────────────────────────────────

interface DateConflictAlertProps {
  explanation: string;
  documentName?: string;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function DateConflictAlert({
  explanation,
  documentName,
}: DateConflictAlertProps) {
  return (
    <Alert
      severity="warning"
      icon={<EventRepeatIcon fontSize="inherit" />}
      role="alert"
      sx={{ mb: 1.5 }}
      aria-label="Chronological date conflict detected"
    >
      <AlertTitle sx={{ fontWeight: 600 }}>Date Inconsistency Detected</AlertTitle>
      <Typography variant="body2">
        {explanation}
      </Typography>
      {documentName && (
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5 }}>
          Source: {documentName}
        </Typography>
      )}
    </Alert>
  );
}
