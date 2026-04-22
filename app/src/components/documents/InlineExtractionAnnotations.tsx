/**
 * InlineExtractionAnnotations — US_042 EC-1
 *
 * Text-preview fallback for document formats (e.g. TXT) that do not support
 * bounding-box region overlays. Renders extracted values and confidence scores
 * as inline annotation chips beneath each source snippet (EC-1).
 *
 * Accessibility: each annotation region is a list item with aria-label carrying
 * the data type, label, and confidence implication (UXR-505, UXR-606).
 */

import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Divider from '@mui/material/Divider';
import Paper from '@mui/material/Paper';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import MedicationIcon from '@mui/icons-material/Medication';
import LocalHospitalIcon from '@mui/icons-material/LocalHospital';
import MedicalServicesIcon from '@mui/icons-material/MedicalServices';
import WarningAmberIcon from '@mui/icons-material/WarningAmber';

import type { ExtractionAnnotation } from '@/hooks/useDocumentPreview';
import { getConfidenceTier } from '@/components/documents/ExtractedDataConfidenceBadge';

// ─── Helpers ──────────────────────────────────────────────────────────────────

const DATA_TYPE_ICON: Record<string, React.ReactNode> = {
  Medication: <MedicationIcon fontSize="small" />,
  Diagnosis:  <LocalHospitalIcon fontSize="small" />,
  Procedure:  <MedicalServicesIcon fontSize="small" />,
  Allergy:    <WarningAmberIcon fontSize="small" />,
};

const DATA_TYPE_COLOR: Record<string, 'primary' | 'secondary' | 'info' | 'warning'> = {
  Medication: 'primary',
  Diagnosis:  'secondary',
  Procedure:  'info',
  Allergy:    'warning',
};

function confidenceLabel(score: number | null): string {
  if (score === null) return 'Confidence unavailable — review required';
  const tier = getConfidenceTier(score);
  if (tier === 'high')   return `${Math.round(score * 100)}% confidence`;
  if (tier === 'medium') return `${Math.round(score * 100)}% confidence — review recommended`;
  return `${Math.round(score * 100)}% confidence — review required`;
}

function confidenceChipColor(score: number | null): 'success' | 'warning' | 'error' {
  const tier = getConfidenceTier(score);
  if (tier === 'high')   return 'success';
  if (tier === 'medium') return 'warning';
  return 'error';
}

// ─── Props ────────────────────────────────────────────────────────────────────

interface InlineExtractionAnnotationsProps {
  /** Raw text content of the document (may be empty for unsupported formats). */
  rawText?: string;
  /** All extraction annotations for this document version. */
  annotations: ExtractionAnnotation[];
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function InlineExtractionAnnotations({
  rawText,
  annotations,
}: InlineExtractionAnnotationsProps) {
  // Group annotations by page for structured display.
  const byPage = annotations.reduce<Record<number, ExtractionAnnotation[]>>((acc, a) => {
    const page = a.pageNumber ?? 1;
    if (!acc[page]) acc[page] = [];
    acc[page].push(a);
    return acc;
  }, {});

  const pages = Object.keys(byPage).map(Number).sort((a, b) => a - b);

  return (
    <Box>
      {/* Raw text block if provided */}
      {rawText && (
        <Paper
          variant="outlined"
          sx={{
            p: 2,
            mb: 3,
            fontFamily: 'monospace',
            fontSize: '0.8125rem',
            whiteSpace: 'pre-wrap',
            wordBreak: 'break-word',
            maxHeight: 300,
            overflowY: 'auto',
            bgcolor: 'grey.50',
          }}
          role="region"
          aria-label="Document text content"
        >
          {rawText}
        </Paper>
      )}

      {annotations.length === 0 && (
        <Typography variant="body2" color="text.secondary" sx={{ mt: 2 }}>
          No extracted data annotations available for this document.
        </Typography>
      )}

      {/* Per-page annotation list */}
      {pages.map((page, pi) => (
        <Box key={page} sx={{ mb: 3 }}>
          {pages.length > 1 && (
            <Typography variant="subtitle2" color="text.secondary" sx={{ mb: 1 }}>
              Page {page}
            </Typography>
          )}

          <Box
            component="ul"
            aria-label={`Extracted data annotations${pages.length > 1 ? ` page ${page}` : ''}`}
            sx={{ listStyle: 'none', p: 0, m: 0, display: 'flex', flexDirection: 'column', gap: 1 }}
          >
            {byPage[page].map((ann) => {
              const tipText = `${ann.dataType}: ${ann.label} — ${confidenceLabel(ann.confidenceScore)}`;
              return (
                <Box
                  key={ann.extractedDataId}
                  component="li"
                  aria-label={tipText}
                  sx={{
                    p: 1.5,
                    borderRadius: 1,
                    border: '1px solid',
                    borderColor:
                      getConfidenceTier(ann.confidenceScore) === 'high'
                        ? 'success.light'
                        : getConfidenceTier(ann.confidenceScore) === 'medium'
                        ? 'warning.light'
                        : 'error.light',
                    bgcolor:
                      getConfidenceTier(ann.confidenceScore) === 'high'
                        ? 'success.50'
                        : getConfidenceTier(ann.confidenceScore) === 'medium'
                        ? 'warning.50'
                        : 'error.50',
                  }}
                >
                  <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
                    {/* Data type chip */}
                    <Chip
                      icon={DATA_TYPE_ICON[ann.dataType] as React.ReactElement}
                      label={ann.dataType}
                      size="small"
                      color={DATA_TYPE_COLOR[ann.dataType] ?? 'default'}
                      variant="outlined"
                    />

                    {/* Extracted value label */}
                    <Typography variant="body2" fontWeight={500} sx={{ flex: 1, minWidth: 0 }}>
                      {ann.label}
                    </Typography>

                    {/* Confidence chip (AC-4) */}
                    <Tooltip title={confidenceLabel(ann.confidenceScore)} arrow>
                      <Chip
                        label={
                          ann.confidenceScore === null
                            ? 'No score'
                            : `${Math.round(ann.confidenceScore * 100)}%`
                        }
                        size="small"
                        color={confidenceChipColor(ann.confidenceScore)}
                        aria-label={confidenceLabel(ann.confidenceScore)}
                      />
                    </Tooltip>
                  </Box>

                  {/* Source snippet (AC-4 tooltip context) */}
                  {ann.sourceSnippet && (
                    <Typography
                      variant="caption"
                      color="text.secondary"
                      sx={{ display: 'block', mt: 0.75, fontStyle: 'italic' }}
                    >
                      "{ann.sourceSnippet}"
                    </Typography>
                  )}

                  {/* Region attribution */}
                  <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5 }}>
                    Region: {ann.extractionRegion}
                  </Typography>
                </Box>
              );
            })}
          </Box>

          {pi < pages.length - 1 && <Divider sx={{ mt: 2 }} />}
        </Box>
      ))}
    </Box>
  );
}
