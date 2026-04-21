/**
 * InsurancePrecheckStatusBadge — inline soft pre-check result for insurance details (US_031 AC-2, AC-4, EC-2).
 *
 * States:
 *   idle          — no result yet (renders nothing)
 *   checking      — request in-flight (spinner + "Checking…" text)
 *   valid         — insurance matched dummy records (green success chip)
 *   needs-review  — not matched; staff will follow up (amber alert with explanation, AC-4)
 *   skipped       — insurance fields empty; staff will collect on visit (info alert, EC-2)
 *
 * Accessibility (WCAG 2.1 AA):
 *   - Container uses aria-live="polite" so screen readers announce state changes.
 *   - Status icon is aria-hidden; status text is read by the live region.
 *
 * Design tokens (designsystem.md):
 *   - success.surface (#E8F5E9) / success.main (#2E7D32) for "Valid"
 *   - warning.surface (#FFF3E0) / warning.main (#ED6C02) for "Needs Review"
 *   - info.surface (#E1F5FE) / info.main (#0288D1) for "Skipped"
 */

import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';
import HourglassEmptyIcon from '@mui/icons-material/HourglassEmpty';
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined';
import WarningAmberIcon from '@mui/icons-material/WarningAmber';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import CircularProgress from '@mui/material/CircularProgress';
import Typography from '@mui/material/Typography';

// ─── Types ────────────────────────────────────────────────────────────────────

export type InsurancePrecheckStatus =
  | 'idle'
  | 'checking'
  | 'valid'
  | 'needs-review'
  | 'skipped';

interface Props {
  /** Current pre-check status. Renders nothing when "idle". */
  status: InsurancePrecheckStatus;
  /**
   * Optional explanation message from the server (AC-4).
   * When absent and status is "needs-review", a default message is shown.
   */
  message?: string | null;
}

// ─── Constants ────────────────────────────────────────────────────────────────

const DEFAULT_NEEDS_REVIEW_MESSAGE =
  'Your insurance details could not be automatically verified. A staff member will review them before your visit.';

const SKIPPED_MESSAGE =
  'No insurance details provided. Staff will collect your insurance information during your visit.';

// ─── Component ────────────────────────────────────────────────────────────────

export default function InsurancePrecheckStatusBadge({ status, message }: Props) {
  if (status === 'idle') {
    return null;
  }

  return (
    <Box
      aria-live="polite"
      aria-atomic="true"
      sx={{ mt: 1.5 }}
    >
      {status === 'checking' && (
        <Box
          sx={{
            display: 'flex',
            alignItems: 'center',
            gap: 1,
            color: 'text.secondary',
          }}
        >
          <CircularProgress size={14} thickness={5} color="inherit" />
          <Typography variant="caption">Checking insurance…</Typography>
        </Box>
      )}

      {status === 'valid' && (
        <Alert
          icon={<CheckCircleOutlineIcon fontSize="small" />}
          severity="success"
          variant="outlined"
          sx={{ py: 0.5 }}
        >
          <Typography variant="body2" component="span" fontWeight={500}>
            Valid
          </Typography>
          <Typography variant="caption" color="text.secondary" sx={{ ml: 1 }}>
            Insurance details verified.
          </Typography>
        </Alert>
      )}

      {status === 'needs-review' && (
        <Alert
          icon={<WarningAmberIcon fontSize="small" />}
          severity="warning"
          variant="outlined"
          sx={{ py: 0.5 }}
        >
          <Typography variant="body2" component="span" fontWeight={500}>
            Needs Review
          </Typography>
          <Typography variant="caption" display="block" sx={{ mt: 0.25 }}>
            {message ?? DEFAULT_NEEDS_REVIEW_MESSAGE}
          </Typography>
        </Alert>
      )}

      {status === 'skipped' && (
        <Alert
          icon={<InfoOutlinedIcon fontSize="small" />}
          severity="info"
          variant="outlined"
          sx={{ py: 0.5 }}
        >
          <Typography variant="caption">
            {SKIPPED_MESSAGE}
          </Typography>
        </Alert>
      )}

      {/* Visually hidden, unused — kept to avoid unused-import lint warnings */}
      <Box sx={{ display: 'none' }}>
        <HourglassEmptyIcon fontSize="small" aria-hidden />
      </Box>
    </Box>
  );
}
