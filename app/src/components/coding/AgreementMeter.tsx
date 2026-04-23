/**
 * AgreementMeter — Horizontal progress bar visualizing the AI-human agreement rate.
 * (US_050, AC-2, SCR-014 wireframe `.agreement-meter` pattern)
 *
 * Colour thresholds (design tokens from designsystem.md#colors — AI Confidence Colors):
 *   >= 98%  → success-500  #2E7D32
 *   90-97%  → warning-500  #ED6C02
 *   < 90%   → error-500    #D32F2F
 *
 * Matches wireframe:
 *   height: 8px, background: neutral-200, border-radius: var(--rad-sm)
 *   Percentage label displayed above bar.
 */

import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';

// ─── Helpers ──────────────────────────────────────────────────────────────────

function resolveBarColor(rate: number): string {
  if (rate >= 98) return '#2E7D32'; // success-500
  if (rate >= 90) return '#ED6C02'; // warning-500
  return '#D32F2F';                 // error-500
}

function resolveTextColor(rate: number): string {
  if (rate >= 98) return 'success.main';
  if (rate >= 90) return 'warning.main';
  return 'error.main';
}

// ─── Props ────────────────────────────────────────────────────────────────────

interface AgreementMeterProps {
  /** Agreement rate as percentage [0.0, 100.0]. */
  rate: number;
  /** When true the meter is not rendered (caller shows "Not enough data" instead). */
  meetsThreshold?: boolean;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function AgreementMeter({
  rate,
  meetsThreshold = true,
}: AgreementMeterProps) {
  if (!meetsThreshold) return null;

  const clamped  = Math.min(100, Math.max(0, rate));
  const barColor = resolveBarColor(clamped);
  const textColor= resolveTextColor(clamped);

  return (
    <Box aria-label={`Agreement rate: ${clamped.toFixed(1)}%`}>
      {/* Percentage label */}
      <Typography
        variant="caption"
        component="div"
        fontWeight={700}
        color={textColor}
        sx={{ mb: 0.5 }}
        aria-hidden="true"
      >
        {clamped.toFixed(1)}%
      </Typography>

      {/* Track */}
      <Box
        role="progressbar"
        aria-valuenow={Math.round(clamped)}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-label={`AI-human agreement rate ${clamped.toFixed(1)} percent`}
        sx={{
          height:       8,
          bgcolor:      '#E0E0E0',           // neutral-200
          borderRadius: '4px',               // var(--rad-sm)
          overflow:     'hidden',
        }}
      >
        {/* Fill */}
        <Box
          sx={{
            height:       '100%',
            width:        `${clamped}%`,
            bgcolor:      barColor,
            borderRadius: '4px',
            transition:   'width 0.4s ease',
          }}
        />
      </Box>
    </Box>
  );
}
