/**
 * ExtractedDataConfidenceBadge — US_041 AC-2, AC-3, EC-1
 *
 * Renders a color-coded confidence score chip following the SCR-013 wireframe thresholds:
 *   - Green  (≥ 0.90) — high confidence, no review required
 *   - Amber  (0.80–0.89) — medium confidence, review recommended
 *   - Red    (< 0.80)    — low confidence, mandatory review required
 *   - Red    (null / undefined) — "confidence-unavailable"; treated as 0.00 (EC-1)
 *
 * Maps directly to the wireframe `confidence-badge` CSS classes:
 *   confidence-high → green
 *   confidence-medium → amber
 *   confidence-low / confidence-unavailable → red
 *
 * Accessible: the chip's aria-label includes the human-readable score and tier name
 * so screen readers announce both the number and the review implication (UXR-505).
 */

import Chip from '@mui/material/Chip';
import Tooltip from '@mui/material/Tooltip';
import type { SxProps, Theme } from '@mui/material/styles';

// ─── Threshold constants (must align with backend guardrails.json 0.70 floor + AC-3 tiers) ──

/** Below this → red badge + mandatory review (AC-2). */
export const CONFIDENCE_THRESHOLD_LOW = 0.80;

/** At or above this → green badge (AC-3). */
export const CONFIDENCE_THRESHOLD_HIGH = 0.90;

// ─── Types ────────────────────────────────────────────────────────────────────

export type ConfidenceTier = 'high' | 'medium' | 'low' | 'unavailable';

export interface ExtractedDataConfidenceBadgeProps {
  /** Numeric confidence in [0, 1]. Pass null/undefined to trigger the EC-1 unavailable state. */
  score: number | null | undefined;
  /** Optional override for the chip size. Defaults to 'small'. */
  size?: 'small' | 'medium';
  sx?: SxProps<Theme>;
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

export function getConfidenceTier(score: number | null | undefined): ConfidenceTier {
  if (score == null) return 'unavailable';
  if (score >= CONFIDENCE_THRESHOLD_HIGH) return 'high';
  if (score >= CONFIDENCE_THRESHOLD_LOW) return 'medium';
  return 'low';
}

function tierColor(tier: ConfidenceTier): 'success' | 'warning' | 'error' {
  switch (tier) {
    case 'high':        return 'success';
    case 'medium':      return 'warning';
    case 'low':
    case 'unavailable': return 'error';
  }
}

function tierLabel(tier: ConfidenceTier, score: number | null | undefined): string {
  if (tier === 'unavailable') return 'N/A';
  return `${Math.round((score ?? 0) * 100)}%`;
}

function tierAriaLabel(tier: ConfidenceTier, score: number | null | undefined): string {
  if (tier === 'unavailable') return 'Confidence unavailable — manual review required';
  const pct = Math.round((score ?? 0) * 100);
  switch (tier) {
    case 'high':   return `High confidence: ${pct}%`;
    case 'medium': return `Medium confidence: ${pct}% — review recommended`;
    case 'low':    return `Low confidence: ${pct}% — mandatory review required`;
  }
}

function tierTooltip(tier: ConfidenceTier): string {
  switch (tier) {
    case 'high':        return 'High confidence (≥90%) — no review required';
    case 'medium':      return 'Medium confidence (80–89%) — review recommended';
    case 'low':         return 'Low confidence (<80%) — mandatory review required';
    case 'unavailable': return 'Confidence unavailable — treat as 0%; manual review required';
  }
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function ExtractedDataConfidenceBadge({
  score,
  size = 'small',
  sx,
}: ExtractedDataConfidenceBadgeProps) {
  const tier   = getConfidenceTier(score);
  const color  = tierColor(tier);
  const label  = tierLabel(tier, score);
  const aLabel = tierAriaLabel(tier, score);
  const tip    = tierTooltip(tier);

  return (
    <Tooltip title={tip} arrow>
      <Chip
        label={label}
        size={size}
        color={color}
        variant="outlined"
        aria-label={aLabel}
        sx={{
          fontWeight: 600,
          minWidth: 52,
          ...sx,
        }}
      />
    </Tooltip>
  );
}
