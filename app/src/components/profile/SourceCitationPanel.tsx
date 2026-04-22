/**
 * SourceCitationPanel — US_043 AC-3 SCR-013 source document citation drawer.
 *
 * MUI Drawer (anchor="right", 400px wide) that slides in when a data point
 * row is clicked, showing:
 *   - Document name + category chip (UXR-404 icon/color per category)
 *   - Upload date
 *   - Page number and extraction region (when available)
 *   - Source snippet text (raw extracted context)
 *   - Source attribution string
 *
 * Loading skeleton shown while the citation query is in-flight.
 * Accessible: focus is trapped inside the drawer when open (MUI default).
 */

import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import Divider from '@mui/material/Divider';
import Drawer from '@mui/material/Drawer';
import IconButton from '@mui/material/IconButton';
import Skeleton from '@mui/material/Skeleton';
import Typography from '@mui/material/Typography';
import CloseIcon from '@mui/icons-material/Close';
import ArticleIcon from '@mui/icons-material/Article';
import BiotechIcon from '@mui/icons-material/Biotech';
import MedicalServicesIcon from '@mui/icons-material/MedicalServices';
import ImageIcon from '@mui/icons-material/Image';


import { useSourceCitation } from '@/hooks/useSourceCitation';

// ─── Category icon/color map (UXR-404) ────────────────────────────────────────
// lab=blue, prescription=green, note=purple, imaging=orange

interface CategoryStyle {
  icon: React.ReactElement;
  color: string;
  chipColor: 'primary' | 'success' | 'secondary' | 'warning';
}

function getCategoryStyle(category: string): CategoryStyle {
  const lower = category.toLowerCase();
  if (lower.includes('lab') || lower.includes('result')) {
    return { icon: <BiotechIcon />, color: 'primary.main', chipColor: 'primary' };
  }
  if (lower.includes('prescri') || lower.includes('discharge') || lower.includes('medication')) {
    return { icon: <MedicalServicesIcon />, color: 'success.main', chipColor: 'success' };
  }
  if (lower.includes('imag') || lower.includes('radiol') || lower.includes('x-ray')) {
    return { icon: <ImageIcon />, color: 'warning.main', chipColor: 'warning' };
  }
  // default: note/referral/other → purple
  return { icon: <ArticleIcon />, color: 'secondary.main', chipColor: 'secondary' };
}

// ─── Props ────────────────────────────────────────────────────────────────────

interface SourceCitationPanelProps {
  open: boolean;
  onClose: () => void;
  patientId: string;
  /** extractedDataId of the selected data point, or null when nothing is selected. */
  extractedDataId: string | null;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function SourceCitationPanel({
  open,
  onClose,
  patientId,
  extractedDataId,
}: SourceCitationPanelProps) {
  const { citation, isLoading, isError } = useSourceCitation(patientId, extractedDataId);

  const catStyle = citation ? getCategoryStyle(citation.documentCategory) : null;

  return (
    <Drawer
      anchor="right"
      open={open}
      onClose={onClose}
      PaperProps={{ sx: { width: { xs: '100%', sm: 400 }, p: 0 } }}
      aria-label="Source document citation"
    >
      {/* Header */}
      <Box
        sx={{
          display: 'flex',
          alignItems: 'center',
          px: 2,
          py: 1.5,
          borderBottom: 1,
          borderColor: 'divider',
        }}
      >
        <Typography variant="h6" component="h2" sx={{ flexGrow: 1, fontWeight: 500 }}>
          Source Citation
        </Typography>
        <IconButton onClick={onClose} aria-label="Close source citation panel" size="small">
          <CloseIcon />
        </IconButton>
      </Box>

      {/* Body */}
      <Box sx={{ px: 2, py: 2, overflowY: 'auto', flexGrow: 1 }}>
        {isLoading && (
          <Box>
            <Skeleton variant="text" width="70%" height={28} />
            <Skeleton variant="text" width="40%" height={22} sx={{ mt: 1 }} />
            <Skeleton variant="rectangular" height={80} sx={{ mt: 2, borderRadius: 1 }} />
            <Skeleton variant="text" width="60%" height={22} sx={{ mt: 2 }} />
          </Box>
        )}

        {!isLoading && isError && (
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, color: 'error.main' }}>
            <Typography variant="body2">Failed to load citation details.</Typography>
          </Box>
        )}

        {!isLoading && !isError && !citation && extractedDataId && (
          <Typography variant="body2" color="text.secondary">
            Citation not available.
          </Typography>
        )}

        {!isLoading && citation && catStyle && (
          <Box>
            {/* Document name + category */}
            <Box sx={{ display: 'flex', alignItems: 'flex-start', gap: 1, mb: 1.5 }}>
              <Box sx={{ color: catStyle.color, mt: 0.25 }} aria-hidden="true">
                {catStyle.icon}
              </Box>
              <Box>
                <Typography variant="subtitle1" sx={{ fontWeight: 600, lineHeight: 1.3 }}>
                  {citation.documentName}
                </Typography>
                <Chip
                  label={citation.documentCategory}
                  color={catStyle.chipColor}
                  size="small"
                  variant="outlined"
                  sx={{ mt: 0.5 }}
                />
              </Box>
            </Box>

            <Divider sx={{ mb: 2 }} />

            {/* Upload date */}
            <Box sx={{ mb: 1.5 }}>
              <Typography variant="caption" color="text.secondary" display="block">
                Uploaded
              </Typography>
              <Typography variant="body2">
                {new Date(citation.uploadDate).toLocaleDateString('en-US', {
                  year: 'numeric',
                  month: 'long',
                  day: 'numeric',
                })}
              </Typography>
            </Box>

            {/* Location in document */}
            {(citation.pageNumber || citation.extractionRegion) && (
              <Box sx={{ mb: 1.5 }}>
                <Typography variant="caption" color="text.secondary" display="block">
                  Location
                </Typography>
                <Typography variant="body2">
                  {citation.pageNumber && `Page ${citation.pageNumber}`}
                  {citation.pageNumber && citation.extractionRegion && ' — '}
                  {citation.extractionRegion}
                </Typography>
              </Box>
            )}

            {/* Source snippet */}
            {citation.sourceSnippet && (
              <Box sx={{ mb: 1.5 }}>
                <Typography variant="caption" color="text.secondary" display="block" sx={{ mb: 0.5 }}>
                  Extracted text
                </Typography>
                <Box
                  sx={{
                    p: 1.5,
                    bgcolor: 'neutral.50',
                    borderRadius: 1,
                    border: 1,
                    borderColor: 'divider',
                    fontFamily: 'monospace',
                    fontSize: '0.8125rem',
                    lineHeight: 1.6,
                    whiteSpace: 'pre-wrap',
                    wordBreak: 'break-word',
                  }}
                >
                  {citation.sourceSnippet}
                </Box>
              </Box>
            )}

            {/* Attribution */}
            {citation.sourceAttribution && (
              <Box>
                <Typography variant="caption" color="text.secondary" display="block">
                  Attribution
                </Typography>
                <Typography variant="body2" color="text.secondary">
                  {citation.sourceAttribution}
                </Typography>
              </Box>
            )}
          </Box>
        )}

        {/* Initial empty state — nothing selected yet */}
        {!extractedDataId && (
          <Box
            sx={{
              display: 'flex',
              flexDirection: 'column',
              alignItems: 'center',
              justifyContent: 'center',
              height: 200,
              color: 'text.disabled',
              gap: 1,
            }}
          >
            <ArticleIcon sx={{ fontSize: 48 }} aria-hidden="true" />
            <Typography variant="body2">Select a data point to view its source citation.</Typography>
          </Box>
        )}
      </Box>

      {/* Inline spinner for refetch */}
      {isLoading && extractedDataId && (
        <Box sx={{ display: 'flex', justifyContent: 'center', py: 1 }}>
          <CircularProgress size={20} />
        </Box>
      )}
    </Drawer>
  );
}
