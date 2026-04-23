/**
 * AgreementRateAlert — MUI Alert banners for the agreement rate section.
 * (US_050, AC-4, SCR-014)
 *
 * Two variants:
 *
 *   1. Below-threshold warning (severity="warning")
 *      Shown when: dailyAgreementRate < 98.0 AND meetsMinimumThreshold = true
 *      Content: rate %, target, top disagreement patterns from the alert record.
 *
 *   2. Not-enough-data info (severity="info")
 *      Shown when: meetsMinimumThreshold = false
 *      Content: "Not enough data" message with current count and minimum of 50.
 *
 *   3. AI service unavailable info (severity="info", UXR-605)
 *      Shown when: aiUnavailable = true
 *      Content: warning that agreement rate reflects historical data only.
 *
 * Dismissible via MUI Alert onClose.
 */

import { useState } from 'react';
import Alert from '@mui/material/Alert';
import AlertTitle from '@mui/material/AlertTitle';
import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Link from '@mui/material/Link';
import List from '@mui/material/List';
import ListItem from '@mui/material/ListItem';
import ListItemText from '@mui/material/ListItemText';
import type { AgreementAlertDto, AgreementRateDto } from '@/hooks/useAgreementRate';

// ─── Props ────────────────────────────────────────────────────────────────────

interface AgreementRateAlertProps {
  /** Most-recent metric; null if not yet available. */
  metrics:        AgreementRateDto | null;
  /** Active alert from the alerts endpoint (most-recent below-threshold record). */
  latestAlert:    AgreementAlertDto | null;
  /** When true, display AI service unavailability notice (UXR-605). */
  aiUnavailable?: boolean;
  /** Optional callback when user clicks "View all discrepancies" link. */
  onViewDiscrepancies?: () => void;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function AgreementRateAlert({
  metrics,
  latestAlert,
  aiUnavailable = false,
  onViewDiscrepancies,
}: AgreementRateAlertProps) {
  const [dismissed, setDismissed] = useState(false);

  const showBelowThreshold =
    !dismissed &&
    metrics !== null &&
    metrics.meets_minimum_threshold &&
    metrics.daily_agreement_rate < 98.0;

  const showInsufficientData =
    metrics !== null && !metrics.meets_minimum_threshold;

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1, mb: 2 }}>
      {/* ── AI service unavailable (UXR-605) ───────────────────────────── */}
      {aiUnavailable && (
        <Alert severity="info" role="status" aria-live="polite">
          AI code suggestions are temporarily unavailable. Agreement rate reflects historical data only.
        </Alert>
      )}

      {/* ── Not enough data (EC-1) ──────────────────────────────────────── */}
      {showInsufficientData && (
        <Alert severity="info" role="status" aria-live="polite">
          <AlertTitle>Not enough data for agreement rate calculation</AlertTitle>
          Minimum 50 verified codes required. Current:{' '}
          <strong>{metrics.total_codes_verified}</strong> code
          {metrics.total_codes_verified === 1 ? '' : 's'}.
          The daily background job will recalculate once the threshold is met.
        </Alert>
      )}

      {/* ── Below 98% threshold alert (AC-4) ───────────────────────────── */}
      {showBelowThreshold && (
        <Alert
          severity="warning"
          role="alert"
          aria-live="assertive"
          onClose={() => setDismissed(true)}
        >
          <AlertTitle>Agreement rate below 98% target</AlertTitle>
          Current rate:{' '}
          <strong>{metrics!.daily_agreement_rate.toFixed(1)}%</strong> — target:{' '}
          <strong>{metrics!.target_rate}%</strong>.
          {latestAlert && latestAlert.disagreement_patterns.length > 0 && (
            <Box sx={{ mt: 1 }}>
              <Box sx={{ fontWeight: 600, mb: 0.5, fontSize: '0.875rem' }}>
                Top disagreement patterns:
              </Box>
              <List dense disablePadding>
                {latestAlert.disagreement_patterns.map((pattern, idx) => (
                  <ListItem key={idx} disableGutters sx={{ py: 0 }}>
                    <ListItemText
                      primary={
                        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                          <Chip
                            label={idx + 1}
                            size="small"
                            sx={{ minWidth: 22, height: 20, fontSize: '0.7rem' }}
                          />
                          <span style={{ fontSize: '0.875rem' }}>{pattern}</span>
                        </Box>
                      }
                    />
                  </ListItem>
                ))}
              </List>
            </Box>
          )}
          {onViewDiscrepancies && (
            <Box sx={{ mt: 1 }}>
              <Link
                component="button"
                variant="body2"
                underline="hover"
                onClick={onViewDiscrepancies}
                aria-label="View all discrepancy details"
              >
                View all discrepancy details ↓
              </Link>
            </Box>
          )}
        </Alert>
      )}
    </Box>
  );
}
