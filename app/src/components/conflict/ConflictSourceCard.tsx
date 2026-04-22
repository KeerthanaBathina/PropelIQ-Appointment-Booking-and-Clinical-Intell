/**
 * ConflictSourceCard — single source document card in the side-by-side comparison
 * view (US_044 AC-2, UXR-104, AIR-007).
 *
 * Shows: document name, category icon, upload date, extracted value, confidence
 * badge, source attribution (AIR-007), and an optional link to the full document.
 *
 * The conflicting value cell uses warning-surface (#FFF3E0) background as defined
 * in the wireframe to visually distinguish conflicting entries.
 */

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import Divider from '@mui/material/Divider';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import ArticleIcon from '@mui/icons-material/Article';
import BiotechIcon from '@mui/icons-material/Biotech';
import LocalHospitalIcon from '@mui/icons-material/LocalHospital';
import MedicalServicesIcon from '@mui/icons-material/MedicalServices';
import type { SxProps, Theme } from '@mui/material/styles';

import ConfidenceBadge from '@/components/profile/ConfidenceBadge';
import type { ConflictSourceCitationDto } from '@/hooks/useConflictDetail';

// ─── Category icon map (UXR-404 — distinct icons per document category) ───────

function CategoryIcon({ category }: { category: string }) {
  const lc = category.toLowerCase();
  if (lc.includes('lab') || lc.includes('result'))  return <BiotechIcon fontSize="small" aria-hidden />;
  if (lc.includes('discharge') || lc.includes('summary')) return <LocalHospitalIcon fontSize="small" aria-hidden />;
  if (lc.includes('prescription') || lc.includes('medication')) return <MedicalServicesIcon fontSize="small" aria-hidden />;
  return <ArticleIcon fontSize="small" aria-hidden />;
}

// ─── Props ────────────────────────────────────────────────────────────────────

interface ConflictSourceCardProps {
  citation: ConflictSourceCitationDto;
  /** Highlight this card as the "primary" conflicting source with a stronger border. */
  isPrimary?: boolean;
  sx?: SxProps<Theme>;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function ConflictSourceCard({
  citation,
  isPrimary = false,
  sx,
}: ConflictSourceCardProps) {
  const uploadDate = new Date(citation.uploadDate).toLocaleDateString(undefined, {
    year: 'numeric', month: 'short', day: 'numeric',
  });

  return (
    <Card
      variant="outlined"
      sx={{
        borderColor: isPrimary ? 'warning.main' : 'divider',
        borderWidth: isPrimary ? 2 : 1,
        height: '100%',
        display: 'flex',
        flexDirection: 'column',
        ...sx,
      }}
      // AC-3: ARIA label includes document name, upload date, and confidence score
      // so assistive technology users receive the same information as sighted staff.
      aria-label={`Source: ${citation.documentName}, uploaded ${uploadDate}, confidence ${Math.round(citation.confidenceScore * 100)}%`}
    >
      {/* Card header — document identity */}
      <Box
        sx={{
          px: 2,
          pt: 1.5,
          pb: 1,
          display: 'flex',
          alignItems: 'center',
          gap: 1,
          bgcolor: 'grey.50',
          borderBottom: '1px solid',
          borderColor: 'divider',
        }}
      >
        <CategoryIcon category={citation.documentCategory} />
        <Box sx={{ flexGrow: 1, minWidth: 0 }}>
          <Typography
            variant="subtitle2"
            noWrap
            title={citation.documentName}
            sx={{ lineHeight: 1.3 }}
          >
            {citation.documentName}
          </Typography>
          <Typography variant="caption" color="text.secondary">
            {citation.documentCategory} · {uploadDate}
          </Typography>
        </Box>
        <ConfidenceBadge score={citation.confidenceScore} size="small" />
      </Box>

      <CardContent sx={{ flexGrow: 1, py: 1.5 }}>
        {/* Extracted value — highlighted with warning-surface background (wireframe) */}
        <Box
          sx={{
            bgcolor: 'warning.surface',
            borderRadius: 1,
            px: 1.5,
            py: 1,
            mb: 1.5,
          }}
          aria-label={`Extracted value: ${citation.normalizedValue ?? citation.rawText ?? 'N/A'}`}
        >
          <Typography variant="body2" fontWeight={600} sx={{ wordBreak: 'break-word' }}>
            {citation.normalizedValue ?? citation.rawText ?? <em>No value extracted</em>}
          </Typography>
          {citation.unit && (
            <Typography variant="caption" color="text.secondary">
              Unit: {citation.unit}
            </Typography>
          )}
        </Box>

        {/* Source snippet (surrounding text from document) */}
        {citation.sourceSnippet && (
          <>
            <Typography variant="caption" color="text.secondary" sx={{ mb: 0.5, display: 'block' }}>
              Context:
            </Typography>
            <Typography
              variant="caption"
              color="text.secondary"
              sx={{
                display: 'block',
                fontStyle: 'italic',
                mb: 1.5,
                wordBreak: 'break-word',
              }}
            >
              "{citation.sourceSnippet}"
            </Typography>
          </>
        )}

        <Divider sx={{ my: 1 }} />

        {/* Attribution row (AIR-007) */}
        <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.5, alignItems: 'center' }}>
          {citation.pageNumber > 0 && (
            <Chip
              label={`Page ${citation.pageNumber}`}
              size="small"
              variant="outlined"
              sx={{ fontSize: '0.7rem', height: 20 }}
              aria-label={`Page ${citation.pageNumber}`}
            />
          )}
          {citation.extractionRegion && (
            <Chip
              label={citation.extractionRegion}
              size="small"
              variant="outlined"
              sx={{ fontSize: '0.7rem', height: 20 }}
              aria-label={`Region: ${citation.extractionRegion}`}
            />
          )}
        </Box>

        {/* Source attribution text (AIR-007) */}
        {citation.sourceAttributionText && (
          <Tooltip title={citation.sourceAttributionText} arrow>
            <Typography
              variant="caption"
              color="text.disabled"
              sx={{ mt: 0.75, display: 'block', cursor: 'help' }}
              noWrap
            >
              {citation.sourceAttributionText}
            </Typography>
          </Tooltip>
        )}
      </CardContent>
    </Card>
  );
}
