/**
 * VersionHistoryPanel — US_043 SCR-013 collapsible version history sidebar.
 *
 * Displays a vertically stacked list of profile version entries. Each entry shows:
 *   - Version number badge
 *   - Consolidation type (Full / Incremental)
 *   - Created date
 *   - Number of source documents
 *   - Author (when available)
 *
 * Collapsible via the "Show / Hide Version History" toggle button.
 * Loading skeleton shown during initial fetch.
 */

import { useState } from 'react';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Collapse from '@mui/material/Collapse';
import Divider from '@mui/material/Divider';
import Skeleton from '@mui/material/Skeleton';
import Typography from '@mui/material/Typography';
import ExpandLessIcon from '@mui/icons-material/ExpandLess';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import HistoryIcon from '@mui/icons-material/History';

import type { VersionHistoryDto } from '@/hooks/useVersionHistory';

// ─── Props ────────────────────────────────────────────────────────────────────

interface VersionHistoryPanelProps {
  versions: VersionHistoryDto[];
  isLoading: boolean;
  /** Whether the panel starts expanded. Defaults to false. */
  defaultExpanded?: boolean;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function VersionHistoryPanel({
  versions,
  isLoading,
  defaultExpanded = false,
}: VersionHistoryPanelProps) {
  const [expanded, setExpanded] = useState(defaultExpanded);

  return (
    <Box
      sx={{
        border: 1,
        borderColor: 'divider',
        borderRadius: 2,
        overflow: 'hidden',
      }}
      role="region"
      aria-label="Version history"
    >
      {/* Toggle header */}
      <Button
        fullWidth
        onClick={() => setExpanded((prev) => !prev)}
        startIcon={<HistoryIcon />}
        endIcon={expanded ? <ExpandLessIcon /> : <ExpandMoreIcon />}
        aria-expanded={expanded}
        aria-controls="version-history-list"
        sx={{
          justifyContent: 'flex-start',
          px: 2,
          py: 1.5,
          color: 'text.primary',
          fontWeight: 500,
          textTransform: 'none',
          bgcolor: 'grey.50',
          borderRadius: 0,
          '&:hover': { bgcolor: 'grey.100' },
        }}
      >
        Version History
        {versions.length > 0 && (
          <Chip
            label={versions.length}
            size="small"
            sx={{ ml: 1, height: 20, fontSize: '0.75rem' }}
          />
        )}
      </Button>

      {/* Collapsible list */}
      <Collapse in={expanded} id="version-history-list">
        <Box sx={{ maxHeight: 320, overflowY: 'auto' }}>
          {isLoading && (
            <Box sx={{ px: 2, py: 1.5 }}>
              {[1, 2, 3].map((i) => (
                <Box key={i} sx={{ mb: 1.5 }}>
                  <Skeleton variant="text" width="40%" />
                  <Skeleton variant="text" width="70%" />
                </Box>
              ))}
            </Box>
          )}

          {!isLoading && versions.length === 0 && (
            <Typography variant="body2" color="text.secondary" sx={{ px: 2, py: 2 }}>
              No version history available.
            </Typography>
          )}

          {!isLoading &&
            versions
              .slice()
              .sort((a, b) => b.versionNumber - a.versionNumber)
              .map((v, idx, arr) => (
                <Box key={v.versionNumber}>
                  <Box sx={{ px: 2, py: 1.5 }}>
                    <Box
                      sx={{
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'space-between',
                        mb: 0.5,
                      }}
                    >
                      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                        <Chip
                          label={`v${v.versionNumber}`}
                          size="small"
                          color="primary"
                          variant={idx === 0 ? 'filled' : 'outlined'}
                          sx={{ height: 22, fontWeight: 600 }}
                        />
                        <Chip
                          label={v.consolidationType}
                          size="small"
                          variant="outlined"
                          color={v.consolidationType === 'Full' ? 'secondary' : 'default'}
                          sx={{ height: 20, fontSize: '0.7rem' }}
                        />
                      </Box>
                      <Typography variant="caption" color="text.secondary">
                        {new Date(v.createdAt).toLocaleDateString('en-US', {
                          month: 'short',
                          day: 'numeric',
                          year: 'numeric',
                        })}
                      </Typography>
                    </Box>

                    <Typography variant="caption" color="text.secondary" display="block">
                      {v.sourceDocumentCount} source document{v.sourceDocumentCount !== 1 ? 's' : ''}
                      {v.consolidatedByUserName && ` · ${v.consolidatedByUserName}`}
                    </Typography>
                  </Box>

                  {idx < arr.length - 1 && <Divider />}
                </Box>
              ))}
        </Box>
      </Collapse>
    </Box>
  );
}
