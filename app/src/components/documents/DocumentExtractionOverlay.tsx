/**
 * DocumentExtractionOverlay — US_042 AC-1, AC-4
 *
 * Renders semi-transparent bounding-box highlights over extracted regions within a
 * document image or PDF page. Used when `supportsOverlay = true` (AC-1).
 *
 * Design constraints:
 *   - Coordinates in `annotation.bounds` are fractional (0–1) relative to the
 *     container dimensions — the component scales them to pixels at render time.
 *   - Each box is keyboard-focusable (tabIndex=0, role="button") so staff can
 *     tab through all annotations without a pointer device (UXR-505).
 *   - Tooltip (MUI) shows: data type, extracted value, confidence score (AC-4).
 *   - Color-coded border matches `getConfidenceTier` thresholds (high=green, medium=amber, low|unavailable=red).
 */

import Box from '@mui/material/Box';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';

import { getConfidenceTier } from '@/components/documents/ExtractedDataConfidenceBadge';
import type { ExtractionAnnotation } from '@/hooks/useDocumentPreview';

// ─── Helpers ──────────────────────────────────────────────────────────────────

/** Returns MUI theme-compatible color tokens for each confidence tier. */
function tierColors(score: number | null): {
  border: string;
  bg: string;
} {
  const tier = getConfidenceTier(score);
  if (tier === 'high')   return { border: '#2e7d32', bg: 'rgba(46,125,50,0.15)' };
  if (tier === 'medium') return { border: '#ed6c02', bg: 'rgba(237,108,2,0.15)' };
  return { border: '#d32f2f', bg: 'rgba(211,47,47,0.15)' };
}

function confidenceText(score: number | null): string {
  if (score === null) return 'No score — review required';
  const tier = getConfidenceTier(score);
  const pct  = `${Math.round(score * 100)}%`;
  if (tier === 'high')   return `${pct} confidence`;
  if (tier === 'medium') return `${pct} confidence — review recommended`;
  return `${pct} confidence — review required`;
}

// ─── Props ────────────────────────────────────────────────────────────────────

interface DocumentExtractionOverlayProps {
  /** Width of the rendered document container in pixels. */
  containerWidth:  number;
  /** Height of the rendered document container in pixels. */
  containerHeight: number;
  /** Annotations whose `bounds` property is present (overlay-capable). */
  annotations: ExtractionAnnotation[];
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function DocumentExtractionOverlay({
  containerWidth,
  containerHeight,
  annotations,
}: DocumentExtractionOverlayProps) {
  const overlayAnnotations = annotations.filter((a) => a.bounds != null);

  if (overlayAnnotations.length === 0) return null;

  return (
    <Box
      aria-label="Extraction annotation overlay"
      sx={{
        position: 'absolute',
        top:      0,
        left:     0,
        width:    containerWidth,
        height:   containerHeight,
        pointerEvents: 'none', // container is transparent to clicks; children opt-in
      }}
    >
      {overlayAnnotations.map((ann) => {
        const { x, y, width, height } = ann.bounds!;
        const pxX = x * containerWidth;
        const pxY = y * containerHeight;
        const pxW = width  * containerWidth;
        const pxH = height * containerHeight;
        const colors = tierColors(ann.confidenceScore);

        const tooltipContent = (
          <Box>
            <Typography variant="caption" component="div" fontWeight={600}>
              {ann.dataType}: {ann.label}
            </Typography>
            <Typography variant="caption" component="div">
              {confidenceText(ann.confidenceScore)}
            </Typography>
            {ann.extractionRegion && (
              <Typography variant="caption" component="div" color="inherit" sx={{ opacity: 0.8 }}>
                Region: {ann.extractionRegion}
              </Typography>
            )}
          </Box>
        );

        return (
          <Tooltip
            key={ann.extractedDataId}
            title={tooltipContent}
            arrow
            placement="top"
            enterDelay={150}
          >
            <Box
              role="button"
              tabIndex={0}
              aria-label={`${ann.dataType}: ${ann.label} — ${confidenceText(ann.confidenceScore)}`}
              onKeyDown={(e) => {
                // Spacebar/Enter announce focus — tooltip is shown on focus by MUI.
                if (e.key !== 'Tab') e.stopPropagation();
              }}
              sx={{
                position: 'absolute',
                left:     pxX,
                top:      pxY,
                width:    pxW,
                height:   pxH,
                border:   `2px solid ${colors.border}`,
                bgcolor:  colors.bg,
                borderRadius: '2px',
                cursor: 'default',
                pointerEvents: 'auto',
                '&:focus-visible': {
                  outline:       `3px solid ${colors.border}`,
                  outlineOffset: '2px',
                },
              }}
            />
          </Tooltip>
        );
      })}
    </Box>
  );
}
