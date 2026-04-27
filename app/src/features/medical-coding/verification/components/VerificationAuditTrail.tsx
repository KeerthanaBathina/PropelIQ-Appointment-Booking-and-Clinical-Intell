/**
 * VerificationAuditTrail — expandable MUI Accordion showing the verification
 * history for a single AI-generated record (US_075 AC-3).
 *
 * Rendered as a collapsible panel below each VerificationCard. Entries are
 * fetched lazily (only when the accordion is first expanded) via
 * useVerificationAuditTrail to avoid unnecessary API calls.
 *
 * Visual layout: vertical-timeline style using a left border on Box items
 * (matches existing CodeAuditTrail pattern — @mui/lab/Timeline is not installed).
 *
 * Read-only — audit entries are immutable by design (HIPAA, AC-3).
 *
 * Empty state: "No verification history yet."
 * Error state: "Could not load verification history." with no retry (non-critical).
 */

import { useState } from 'react';
import Accordion from '@mui/material/Accordion';
import AccordionDetails from '@mui/material/AccordionDetails';
import AccordionSummary from '@mui/material/AccordionSummary';
import Box from '@mui/material/Box';
import CircularProgress from '@mui/material/CircularProgress';
import Typography from '@mui/material/Typography';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';
import EditOutlinedIcon from '@mui/icons-material/EditOutlined';
import BlockIcon from '@mui/icons-material/Block';
import HistoryIcon from '@mui/icons-material/History';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';

import { useVerificationAuditTrail } from '../hooks/useVerification';

// ─── Helpers ──────────────────────────────────────────────────────────────────

function getActionIcon(action: string) {
  switch (action.toLowerCase()) {
    case 'approved': return <CheckCircleOutlineIcon fontSize="small" sx={{ color: 'success.main' }} />;
    case 'modified': return <EditOutlinedIcon       fontSize="small" sx={{ color: 'info.main' }}    />;
    case 'rejected': return <BlockIcon              fontSize="small" sx={{ color: 'error.main' }}   />;
    default:         return <HistoryIcon            fontSize="small" sx={{ color: 'text.secondary' }} />;
  }
}

function formatActionLabel(action: string): string {
  switch (action.toLowerCase()) {
    case 'approved': return 'Approved';
    case 'modified': return 'Modified';
    case 'rejected': return 'Rejected';
    default:         return action;
  }
}

function formatTimestamp(iso: string): string {
  try {
    return new Date(iso).toLocaleString(undefined, {
      dateStyle: 'medium',
      timeStyle: 'short',
    });
  } catch {
    return iso;
  }
}

// ─── Props ────────────────────────────────────────────────────────────────────

interface VerificationAuditTrailProps {
  recordId: string;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function VerificationAuditTrail({ recordId }: VerificationAuditTrailProps) {
  const [expanded, setExpanded] = useState(false);

  // Lazy fetch — only enabled after first expansion
  const { entries, isLoading, isError } = useVerificationAuditTrail(recordId, expanded);

  return (
    <Accordion
      disableGutters
      elevation={0}
      expanded={expanded}
      onChange={(_, isExpanded) => setExpanded(isExpanded)}
      sx={{
        bgcolor:      'grey.50',
        border:       '1px solid',
        borderColor:  'divider',
        borderTop:    'none',
        borderRadius: '0 0 8px 8px',
        '&:before':   { display: 'none' },
      }}
    >
      <AccordionSummary
        expandIcon={<ExpandMoreIcon />}
        aria-controls={`audit-trail-content-${recordId}`}
        id={`audit-trail-header-${recordId}`}
      >
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <HistoryIcon fontSize="small" sx={{ color: 'text.secondary' }} />
          <Typography variant="caption" fontWeight={600} color="text.secondary">
            Verification History
          </Typography>
        </Box>
      </AccordionSummary>

      <AccordionDetails sx={{ pt: 0, pb: 1.5 }}>
        {/* Loading */}
        {isLoading && (
          <Box sx={{ display: 'flex', justifyContent: 'center', py: 2 }}>
            <CircularProgress size={20} aria-label="Loading verification history" />
          </Box>
        )}

        {/* Error */}
        {!isLoading && isError && (
          <Typography variant="caption" color="error">
            Could not load verification history.
          </Typography>
        )}

        {/* Empty state */}
        {!isLoading && !isError && entries.length === 0 && (
          <Typography variant="caption" color="text.disabled">
            No verification history yet.
          </Typography>
        )}

        {/* Entries — newest first (API returns timestamp DESC) */}
        {!isLoading && !isError && entries.length > 0 && (
          <Box component="ol" sx={{ listStyle: 'none', m: 0, p: 0 }}>
            {entries.map((entry, idx) => (
              <Box
                key={`${entry.recordId}-${idx}`}
                component="li"
                sx={{
                  display:     'flex',
                  gap:         1.5,
                  pl:          1,
                  borderLeft:  '2px solid',
                  borderColor: 'divider',
                  mb:          idx < entries.length - 1 ? 1.5 : 0,
                }}
              >
                {/* Action icon */}
                <Box sx={{ mt: 0.25, flexShrink: 0 }}>
                  {getActionIcon(entry.action)}
                </Box>

                {/* Event details */}
                <Box>
                  <Typography variant="caption" fontWeight={600} component="span">
                    {formatActionLabel(entry.action)}
                  </Typography>
                  {' '}
                  <Typography variant="caption" color="text.secondary" component="span">
                    by {entry.staffName}
                  </Typography>

                  <Typography variant="caption" color="text.disabled" display="block">
                    {formatTimestamp(entry.verifiedAt)}
                  </Typography>

                  {/* Original → Final values (shown for modify) */}
                  {entry.originalAiValue !== entry.finalValue && (
                    <Typography variant="caption" color="text.secondary" display="block">
                      {entry.originalAiValue} → {entry.finalValue}
                    </Typography>
                  )}

                  {/* Justification (shown for modify / reject) */}
                  {entry.justification && (
                    <Typography
                      variant="caption"
                      color="text.secondary"
                      display="block"
                      sx={{ fontStyle: 'italic', maxWidth: 480 }}
                    >
                      "{entry.justification}"
                    </Typography>
                  )}
                </Box>
              </Box>
            ))}
          </Box>
        )}
      </AccordionDetails>
    </Accordion>
  );
}
