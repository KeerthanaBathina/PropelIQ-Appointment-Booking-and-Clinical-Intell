/**
 * NoShowRiskBadge — shared staff risk score chip (US_026, FR-014, AC-2, AC-3).
 *
 * Renders a MUI Chip with:
 *   - Numeric score (clamped to [0, 100] for EC-2 capped values)
 *   - Color band derived from score: green <30, amber 30–69, red >=70 (AC-2)
 *   - "Est." suffix when the score was computed from rule-based defaults
 *     (isEstimated = true, AC-3), with a tooltip explaining the reason
 *   - null score renders a neutral "N/A" chip so the column never breaks layout
 *
 * Design tokens (designsystem.md):
 *   Low    : success.main  #2E7D32 / surface #E8F5E9
 *   Medium : warning.main  #ED6C02 / surface #FFF3E0
 *   High   : error.main    #D32F2F / surface #FFEBEE
 *
 * Accessibility (UXR-201, UXR-203, UXR-204):
 *   - aria-label expresses score, band, and estimated state for screen readers
 *   - Chip background/text contrast passes WCAG 4.5:1 on white backgrounds
 *   - Focus indicator from MUI Chip default focus ring (UXR-205)
 *
 * Visual treatment (UXR-403):
 *   Consistent with staff portal secondary-accent styling and reuses the same
 *   green/amber/red semantic palette as AI confidence indicators (UXR-105).
 *
 * Usage:
 *   <NoShowRiskBadge score={72}  isEstimated={false} />
 *   <NoShowRiskBadge score={45}  isEstimated={true}  />
 *   <NoShowRiskBadge score={null} />   // renders N/A
 */

import Chip from '@mui/material/Chip';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import {
  getRiskBand,
  getRiskBandStyle,
  buildRiskAriaLabel,
} from '@/utils/noShowRiskDisplay';

// ─── Props ────────────────────────────────────────────────────────────────────

interface Props {
  /** Numeric risk score 0–100, or null when not yet computed. */
  score: number | null;
  /**
   * True when the score was estimated from rule-based defaults due to
   * insufficient appointment history (<3 prior appointments, AC-3).
   */
  isEstimated?: boolean;
}

// ─── Constants ────────────────────────────────────────────────────────────────

const NA_COLOR   = '#9E9E9E';  // neutral.500
const NA_SURFACE = '#F5F5F5';  // neutral.100

const ESTIMATED_TOOLTIP =
  'Score estimated — fewer than 3 prior appointments on record.';

// ─── Component ────────────────────────────────────────────────────────────────

export default function NoShowRiskBadge({ score, isEstimated = false }: Props) {
  // ── null / not-computed state ──────────────────────────────────────────────
  if (score === null || score === undefined) {
    return (
      <Chip
        label="N/A"
        size="small"
        aria-label="No-show risk: not available"
        sx={{
          bgcolor:    NA_SURFACE,
          color:      NA_COLOR,
          fontWeight: 600,
          fontSize:   '0.7rem',
          height:     22,
        }}
      />
    );
  }

  // ── Computed score chip ────────────────────────────────────────────────────
  const clamped   = Math.max(0, Math.min(100, score));
  const band      = getRiskBand(clamped);
  const { color, surface } = getRiskBandStyle(band);
  const ariaLabel = buildRiskAriaLabel(clamped, isEstimated);

  // Label: "72" or "45 Est." when estimated (AC-3)
  const label = (
    <Typography
      component="span"
      sx={{
        fontWeight:  600,
        fontSize:    '0.7rem',
        lineHeight:  1,
        display:     'flex',
        alignItems:  'center',
        gap:         '2px',
      }}
    >
      {clamped}
      {isEstimated && (
        <Typography
          component="span"
          sx={{
            fontSize:      '0.6rem',
            fontWeight:    500,
            opacity:       0.85,
            textTransform: 'uppercase',
            letterSpacing: '0.04em',
          }}
          aria-hidden="true"
        >
          {' Est.'}
        </Typography>
      )}
    </Typography>
  );

  const chip = (
    <Chip
      label={label}
      size="small"
      aria-label={ariaLabel}
      sx={{
        bgcolor:     surface,
        color,
        fontWeight:  600,
        fontSize:    '0.7rem',
        height:      22,
        // Prevent layout overflow for capped 100 score (EC-2)
        minWidth:    40,
        maxWidth:    72,
        '& .MuiChip-label': { overflow: 'visible', px: '6px' },
      }}
    />
  );

  // Wrap with tooltip when estimated to explain the label (AC-3, UXR-502)
  if (isEstimated) {
    return (
      <Tooltip title={ESTIMATED_TOOLTIP} arrow placement="top">
        {chip}
      </Tooltip>
    );
  }

  return chip;
}
