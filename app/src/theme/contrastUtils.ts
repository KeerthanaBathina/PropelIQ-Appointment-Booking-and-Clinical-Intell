/**
 * WCAG 2.1 contrast ratio utilities (US_100, AC-4).
 *
 * Implements the relative luminance formula from WCAG 2.1 Section 1.4.3:
 *   https://www.w3.org/WAI/WCAG21/Understanding/contrast-minimum.html
 *
 * Use these functions to validate design-token color pairings during development
 * or in unit tests.  They are not bundled into production renders of components.
 */

/**
 * Converts a single sRGB channel value (0–255) to its linearised equivalent
 * per the IEC 61966-2-1 standard (the sRGB specification).
 */
function sRgbToLinear(value: number): number {
  const normalized = value / 255;
  return normalized <= 0.04045
    ? normalized / 12.92
    : Math.pow((normalized + 0.055) / 1.055, 2.4);
}

/**
 * Computes the relative luminance of a hex colour string (e.g. `"#1976D2"`).
 * Result is in the range [0, 1] where 0 is absolute black and 1 is absolute white.
 *
 * Formula: L = 0.2126·R_lin + 0.7152·G_lin + 0.0722·B_lin
 */
export function relativeLuminance(hex: string): number {
  const r = parseInt(hex.slice(1, 3), 16);
  const g = parseInt(hex.slice(3, 5), 16);
  const b = parseInt(hex.slice(5, 7), 16);
  return 0.2126 * sRgbToLinear(r) + 0.7152 * sRgbToLinear(g) + 0.0722 * sRgbToLinear(b);
}

/**
 * Computes the WCAG contrast ratio between two hex colours.
 * Returns a value ≥ 1 (identical colours → 1:1, black on white → 21:1).
 */
export function contrastRatio(hex1: string, hex2: string): number {
  const l1 = relativeLuminance(hex1);
  const l2 = relativeLuminance(hex2);
  const lighter = Math.max(l1, l2);
  const darker  = Math.min(l1, l2);
  return (lighter + 0.05) / (darker + 0.05);
}

/**
 * Returns `true` when the foreground/background pairing meets the WCAG 2.1
 * Level AA contrast threshold.
 *
 * @param fg          Foreground hex colour (e.g. `"#FFFFFF"`).
 * @param bg          Background hex colour (e.g. `"#1976D2"`).
 * @param isLargeText `true` for text ≥ 18pt regular or ≥ 14pt bold (threshold: 3:1).
 *                    `false` for normal-sized text (threshold: 4.5:1).
 */
export function meetsWcagAA(fg: string, bg: string, isLargeText: boolean): boolean {
  const ratio = contrastRatio(fg, bg);
  return isLargeText ? ratio >= 3.0 : ratio >= 4.5;
}
