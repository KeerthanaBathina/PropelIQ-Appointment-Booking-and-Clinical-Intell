/**
 * CodeAuditTrail — expandable MUI Accordion showing the immutable audit history
 * for a single medical code (US_049 AC-4, FR-049).
 *
 * Rendered as a collapsible panel inside each queue table row. Entries are
 * fetched lazily (on first expand) via useAuditTrail and ordered newest-first
 * (the API returns them timestamp DESC).
 *
 * Visual layout: a vertical-timeline style using a left border on Box items
 * (replaces @mui/lab/Timeline which is not installed in this project).
 *
 * Read-only — audit entries are immutable by design (HIPAA AC-4).
 */

import Accordion from '@mui/material/Accordion';
import AccordionDetails from '@mui/material/AccordionDetails';
import AccordionSummary from '@mui/material/AccordionSummary';
import Box from '@mui/material/Box';
import CircularProgress from '@mui/material/CircularProgress';
import Typography from '@mui/material/Typography';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';
import SwapHorizIcon from '@mui/icons-material/SwapHoriz';
import BlockIcon from '@mui/icons-material/Block';
import ReplayIcon from '@mui/icons-material/Replay';
import HistoryIcon from '@mui/icons-material/History';

import { useAuditTrail } from '@/hooks/useAuditTrail';

// ─── Helpers ──────────────────────────────────────────────────────────────────

function getActionIcon(action: string) {
  switch (action.toLowerCase()) {
    case 'approved':          return <CheckCircleOutlineIcon fontSize="small" sx={{ color: 'success.main' }} />;
    case 'overridden':        return <SwapHorizIcon fontSize="small" sx={{ color: 'warning.main' }} />;
    case 'deprecatedblocked': return <BlockIcon fontSize="small" sx={{ color: 'error.main' }} />;
    case 'revalidated':       return <ReplayIcon fontSize="small" sx={{ color: 'info.main' }} />;
    default:                  return <HistoryIcon fontSize="small" sx={{ color: 'text.secondary' }} />;
  }
}

function formatActionLabel(action: string): string {
  switch (action.toLowerCase()) {
    case 'approved':          return 'Approved';
    case 'overridden':        return 'Overridden';
    case 'deprecatedblocked': return 'Deprecated — Blocked';
    case 'revalidated':       return 'Revalidated';
    default:                  return action;
  }
}

// ─── Props ────────────────────────────────────────────────────────────────────

interface CodeAuditTrailProps {
  codeId: string;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function CodeAuditTrail({ codeId }: CodeAuditTrailProps) {
  const { entries, isLoading } = useAuditTrail(codeId);

  return (
    <Accordion
      disableGutters
      elevation={0}
      sx={{
        bgcolor:     'grey.50',
        border:      '1px solid',
        borderColor: 'divider',
        borderRadius: 1,
        '&:before': { display: 'none' },
      }}
    >
      <AccordionSummary
        expandIcon={<ExpandMoreIcon />}
        aria-controls={`audit-trail-content-${codeId}`}
        id={`audit-trail-header-${codeId}`}
      >
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <HistoryIcon fontSize="small" sx={{ color: 'text.secondary' }} />
          <Typography variant="body2" fontWeight={600}>
            Audit Trail
          </Typography>
        </Box>
      </AccordionSummary>

      <AccordionDetails id={`audit-trail-content-${codeId}`} sx={{ pt: 0, px: 2, pb: 2 }}>
        {isLoading ? (
          <Box sx={{ display: 'flex', justifyContent: 'center', py: 2 }}>
            <CircularProgress size={24} aria-label="Loading audit trail" />
          </Box>
        ) : entries.length === 0 ? (
          <Typography variant="caption" color="text.secondary">
            No audit entries yet for this code.
          </Typography>
        ) : (
          <Box
            component="ol"
            aria-label="Code audit history"
            sx={{ listStyle: 'none', m: 0, p: 0 }}
          >
            {entries.map((entry, idx) => (
              <Box
                key={entry.log_id}
                component="li"
                sx={{
                  display:    'flex',
                  gap:        1.5,
                  pl:         1,
                  borderLeft: idx < entries.length - 1
                    ? '2px solid'
                    : '2px solid transparent',
                  borderColor: 'divider',
                  pb:          idx < entries.length - 1 ? 2 : 0,
                }}
              >
                {/* Icon column */}
                <Box sx={{ mt: 0.25, flexShrink: 0 }}>
                  {getActionIcon(entry.action)}
                </Box>

                {/* Content column */}
                <Box sx={{ flex: 1 }}>
                  <Box sx={{ display: 'flex', alignItems: 'baseline', gap: 1, flexWrap: 'wrap' }}>
                    <Typography variant="body2" fontWeight={600}>
                      {formatActionLabel(entry.action)}
                    </Typography>
                    <Typography variant="caption" color="text.secondary">
                      {new Date(entry.timestamp).toLocaleString(undefined, {
                        dateStyle: 'short',
                        timeStyle: 'short',
                      })}
                    </Typography>
                  </Box>

                  {entry.old_code_value && entry.new_code_value && (
                    <Typography variant="caption" color="text.secondary" display="block">
                      {entry.old_code_value} → {entry.new_code_value}
                    </Typography>
                  )}

                  {entry.justification && (
                    <Typography
                      variant="caption"
                      color="text.secondary"
                      display="block"
                      sx={{ mt: 0.25, fontStyle: 'italic' }}
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
