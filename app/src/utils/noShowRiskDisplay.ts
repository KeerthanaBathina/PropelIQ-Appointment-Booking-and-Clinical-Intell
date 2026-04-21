/**
 * noShowRiskDisplay — shared score-range mapping for staff no-show risk indicators
 * (US_026, FR-014, AC-2, AC-3).
 *
 * Color bands per acceptance criteria:
 *   Low    : score <  30  → green  (success palette)
 *   Medium : score 30–69  → amber  (warning palette)
 *   High   : score >= 70  → red    (error palette)
 *
 * Design tokens sourced from designsystem.md § AI Confidence Colors /
 * Secondary Colors.  The same semantic green/amber/red palette is reused
 * for confidence indicators (UXR-105) for visual consistency across staff
 * surfaces (UXR-403).
 *
 * All functions are pure and take only a numeric score so they are safe to
 * use in both React components and outside the render cycle.
 */

// ─── Types ────────────────────────────────────────────────────────────────────

/** Discrete risk band matching AC-2 thresholds. */
export type RiskBand = 'low' | 'medium' | 'high';

/** Visual style tokens for a single risk band. */
export interface RiskBandStyle {
  /** Main foreground/text colour (meets WCAG 4.5:1 on white). */
  color: string;
  /** 10 % opacity background surface used for the chip background. */
  surface: string;
  /** Human-readable label for the band. */
  bandLabel: string;
}

// ─── Constants ────────────────────────────────────────────────────────────────

/** Score threshold boundaries (AC-2). */
const MEDIUM_THRESHOLD = 30;
const HIGH_THRESHOLD   = 70;

/** Design-system colour tokens (designsystem.md). */
const BAND_STYLES: Record<RiskBand, RiskBandStyle> = {
  low: {
    color:      '#2E7D32',  // success.main
    surface:    '#E8F5E9',  // success.surface
    bandLabel:  'Low',
  },
  medium: {
    color:      '#ED6C02',  // warning.main
    surface:    '#FFF3E0',  // warning.surface
    bandLabel:  'Medium',
  },
  high: {
    color:      '#D32F2F',  // error.main
    surface:    '#FFEBEE',  // error.surface
    bandLabel:  'High',
  },
};

// ─── Functions ────────────────────────────────────────────────────────────────

/**
 * Maps a numeric score to its risk band.
 *
 * Clamps `score` to [0, 100] before classification so capped values (EC-2)
 * always map to 'high' without overflow.
 */
export function getRiskBand(score: number): RiskBand {
  const clamped = Math.max(0, Math.min(100, score));
  if (clamped >= HIGH_THRESHOLD)   return 'high';
  if (clamped >= MEDIUM_THRESHOLD) return 'medium';
  return 'low';
}

/** Returns the design-system style tokens for a given risk band. */
export function getRiskBandStyle(band: RiskBand): RiskBandStyle {
  return BAND_STYLES[band];
}

/**
 * Derives the style tokens directly from a numeric score.
 * Convenience wrapper over {@link getRiskBand} + {@link getRiskBandStyle}.
 */
export function getRiskStyleFromScore(score: number): RiskBandStyle {
  return getRiskBandStyle(getRiskBand(score));
}

/**
 * Returns a complete accessible aria-label for a risk badge.
 *
 * Examples:
 *   "No-show risk: 72 out of 100, High risk"
 *   "No-show risk: 45 out of 100, Medium risk (Estimated)"
 */
export function buildRiskAriaLabel(score: number, isEstimated: boolean): string {
  const band = getRiskBand(score);
  const clamped = Math.max(0, Math.min(100, score));
  const estimated = isEstimated ? ' (Estimated)' : '';
  return `No-show risk: ${clamped} out of 100, ${BAND_STYLES[band].bandLabel} risk${estimated}`;
}
