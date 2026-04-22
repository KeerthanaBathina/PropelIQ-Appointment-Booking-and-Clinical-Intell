/**
 * ConflictComparisonView — side-by-side layout for conflict source citations (US_044,
 * US_045, UXR-104, Edge Case: 3+ document conflicts).
 *
 * Layout rules:
 *  - 2 sources → MUI Grid with two equal columns side-by-side.
 *  - 3+ sources → horizontally scrollable row (one Card per source document) so all
 *    sources are visible without truncation (Edge Case requirement).
 *
 * Each column/card is a ConflictSourceCard showing: document identity, extracted value
 * (highlighted with warning-surface #FFF3E0), confidence badge, and source attribution.
 *
 * When the conflict is open for resolution, a ConflictValueSelector radio group is
 * rendered below the cards so staff can pick the correct value (US_045 AC-2) or
 * choose "Both Valid — Different Dates" (US_045 EC-2).
 *
 * Responsive: collapses to single-column stack on xs (375px) breakpoint.
 */

import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Grid from '@mui/material/Grid';
import Typography from '@mui/material/Typography';
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined';

import ConflictSourceCard from './ConflictSourceCard';
import ConflictValueSelector from './ConflictValueSelector';
import type { ConflictSourceCitationDto } from '@/hooks/useConflictDetail';

// ─── Props ────────────────────────────────────────────────────────────────────

interface ConflictComparisonViewProps {
  citations: ConflictSourceCitationDto[];
  conflictDescription: string;
  aiExplanation: string;
  /** When provided, renders ConflictValueSelector for resolution (US_045 AC-2, EC-2). */
  selectedValue?: string | null;
  onValueChange?: (value: string) => void;
  /** Disables the selector while a mutation is in-flight. */
  selectorDisabled?: boolean;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function ConflictComparisonView({
  citations,
  conflictDescription,
  aiExplanation,
  selectedValue,
  onValueChange,
  selectorDisabled,
}: ConflictComparisonViewProps) {
  const isMultiSource = citations.length >= 3;
  const showSelector = onValueChange !== undefined;

  return (
    <Box>
      {/* AI explanation banner */}
      <Box
        sx={{
          display: 'flex',
          gap: 1,
          alignItems: 'flex-start',
          bgcolor: 'info.surface',
          borderRadius: 1,
          px: 2,
          py: 1.5,
          mb: 2,
        }}
        role="note"
        aria-label="AI conflict explanation"
      >
        <InfoOutlinedIcon fontSize="small" sx={{ color: 'info.main', mt: 0.25, flexShrink: 0 }} />
        <Box>
          <Typography variant="body2" fontWeight={600} gutterBottom>
            {conflictDescription}
          </Typography>
          <Typography variant="body2" color="text.secondary">
            {aiExplanation}
          </Typography>
        </Box>
      </Box>

      {/* Source count badge */}
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 1.5 }}>
        <Typography variant="subtitle2" color="text.secondary">
          Conflicting sources:
        </Typography>
        <Chip
          label={`${citations.length} document${citations.length !== 1 ? 's' : ''}`}
          size="small"
          color={isMultiSource ? 'warning' : 'default'}
          aria-label={`${citations.length} conflicting source documents`}
        />
        {isMultiSource && (
          <Typography variant="caption" color="warning.dark">
            Scroll right to view all sources
          </Typography>
        )}
      </Box>

      {/* 2-source layout: side-by-side MUI Grid columns */}
      {!isMultiSource && citations.length === 2 && (
        <Grid container spacing={2} aria-label="Side-by-side conflict comparison">
          {citations.map((citation, idx) => (
            <Grid item xs={12} sm={6} key={citation.extractedDataId}>
              <ConflictSourceCard
                citation={citation}
                isPrimary={idx === 0}
                sx={{ height: '100%' }}
              />
            </Grid>
          ))}
        </Grid>
      )}

      {/* Single-source or no-source fallback */}
      {citations.length <= 1 && (
        <Grid container spacing={2}>
          {citations.map((citation) => (
            <Grid item xs={12} sm={8} md={6} key={citation.extractedDataId}>
              <ConflictSourceCard citation={citation} isPrimary />
            </Grid>
          ))}
          {citations.length === 0 && (
            <Grid item xs={12}>
              <Typography variant="body2" color="text.secondary" sx={{ py: 2 }}>
                Source citations are not available for this conflict.
              </Typography>
            </Grid>
          )}
        </Grid>
      )}

      {/* 3+ source layout: horizontal scroll (Edge Case) */}
      {isMultiSource && (
        <Box
          sx={{
            display: 'flex',
            gap: 2,
            overflowX: 'auto',
            pb: 1,
            // CSS scroll snap for smooth UX (MDN CSS Scroll Snap)
            scrollSnapType: 'x mandatory',
            '& > *': {
              scrollSnapAlign: 'start',
              flexShrink: 0,
              width: { xs: '85vw', sm: 280, md: 300 },
            },
          }}
          role="list"
          aria-label={`${citations.length} conflicting source documents — scroll to view all`}
        >
          {citations.map((citation, idx) => (
            <Box key={citation.extractedDataId} role="listitem">
              <ConflictSourceCard
                citation={citation}
                isPrimary={idx === 0}
                sx={{ height: '100%' }}
              />
            </Box>
          ))}
        </Box>
      )}

      {/* Value selector (US_045 AC-2, EC-2) — shown when conflict is open for resolution */}
      {showSelector && citations.length >= 1 && (
        <Box sx={{ mt: 2 }}>
          <ConflictValueSelector
            citations={citations}
            selectedValue={selectedValue ?? null}
            onChange={onValueChange!}
            disabled={selectorDisabled}
          />
        </Box>
      )}
    </Box>
  );
}
