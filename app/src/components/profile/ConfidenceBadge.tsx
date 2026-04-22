/**
 * ConfidenceBadge — US_043 SCR-013 profile view confidence indicator (UXR-105).
 *
 * Renders a color-coded MUI Chip for AI confidence scores per the profile 360
 * design token thresholds (designsystem.md#confidence):
 *   >= 0.80 → green  (success.main #2E7D32)
 *   0.60 – 0.79 → amber (warning.main #ED6C02)
 *   < 0.60 → red   (error.main  #D32F2F)
 *   null/undefined → red "N/A"
 *
 * When `showNeedsReview` is true (US_046 AC-1, UXR-105), items below the 0.80
 * threshold render with an additional "Needs Review" label suffix so staff can
 * immediately identify low-confidence AI suggestions in the manual review form.
 *
 * Accessible: aria-label includes tier name and percentage (UXR-201, UXR-203).
 */

import Chip from '@mui/material/Chip';
import Tooltip from '@mui/material/Tooltip';
import type { SxProps, Theme } from '@mui/material/styles';

// ─── Thresholds (UXR-105 / designsystem.md#confidence) ───────────────────────

const HIGH_THRESHOLD   = 0.80;
const MEDIUM_THRESHOLD = 0.60;

// ─── Types ────────────────────────────────────────────────────────────────────

export type ConfidenceTier = 'high' | 'medium' | 'low' | 'unavailable';

export interface ConfidenceBadgeProps {
  /** Numeric confidence in [0, 1]. Pass null/undefined for the unavailable state. */
  score: number | null | undefined;
  size?: 'small' | 'medium';
  /**
   * When true, appends "— Needs Review" to the label for scores below the 0.80
   * threshold (US_046 AC-1, UXR-105 low-confidence manual review variant).
   */
  showNeedsReview?: boolean;
  sx?: SxProps<Theme>;
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

// eslint-disable-next-line react-refresh/only-export-components -- intentional: utility function consumed by sibling files (pre-existing pattern)
export function getProfileConfidenceTier(score: number | null | undefined): ConfidenceTier {
  if (score == null)               return 'unavailable';
  if (score >= HIGH_THRESHOLD)     return 'high';
  if (score >= MEDIUM_THRESHOLD)   return 'medium';
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
  if (tier === 'unavailable') return 'Confidence unavailable — review required';
  const pct = Math.round((score ?? 0) * 100);
  switch (tier) {
    case 'high':   return `High confidence: ${pct}%`;
    case 'medium': return `Medium confidence: ${pct}% — review recommended`;
    case 'low':    return `Low confidence: ${pct}% — mandatory review required`;
  }
}

function tierTooltip(tier: ConfidenceTier): string {
  switch (tier) {
    case 'high':        return 'High confidence (>=80%)';
    case 'medium':      return 'Medium confidence (60–79%) — review recommended';
    case 'low':         return 'Low confidence (<60%) — mandatory review required';
    case 'unavailable': return 'Confidence unavailable — manual review required';
  }
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function ConfidenceBadge({ score, size = 'small', showNeedsReview = false, sx }: ConfidenceBadgeProps) {
  const tier    = getProfileConfidenceTier(score);
  const color   = tierColor(tier);

  // "Needs Review" suffix for low/medium confidence in manual review context (US_046 AC-1).
  const needsReviewSuffix =
    showNeedsReview && (tier === 'low' || tier === 'medium' || tier === 'unavailable')
      ? ' — Needs Review'
      : '';

  const label   = tierLabel(tier, score) + needsReviewSuffix;
  const ariaLbl = tierAriaLabel(tier, score) + (needsReviewSuffix ? '; manual review required' : '');

  return (
    <Tooltip title={tierTooltip(tier)} arrow>
      <Chip
        label={label}
        color={color}
        size={size}
        variant="filled"
        aria-label={ariaLbl}
        sx={{ fontWeight: 600, minWidth: 52, ...sx }}
      />
    </Tooltip>
  );
}
