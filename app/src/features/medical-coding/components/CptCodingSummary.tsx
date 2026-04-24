/**
 * CptCodingSummary — Summary stat cards for CPT procedure codes section (US_048, SCR-014).
 *
 * Per wireframe SCR-014 summary grid:
 *   Total Codes | Approved | Pending Review | Overridden
 *
 * Matches the wireframe coding-summary grid layout:
 *   `display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr))`
 *
 * Loading state: skeleton cards matching the final card dimensions.
 * All stat values have the appropriate semantic color per designsystem.md:
 *   Approved   → success.main (#2E7D32)
 *   Pending    → warning.main (#ED6C02)
 *   Overridden → error.main   (#D32F2F)
 *   Total      → text.primary (neutral)
 */

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Skeleton from '@mui/material/Skeleton';
import Typography from '@mui/material/Typography';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';
import EditOffIcon from '@mui/icons-material/EditOff';
import PendingOutlinedIcon from '@mui/icons-material/PendingOutlined';
import ReviewsOutlinedIcon from '@mui/icons-material/ReviewsOutlined';

import type { CptCodeDto } from '../types/cpt.types';

// ─── Props ────────────────────────────────────────────────────────────────────

interface CptCodingSummaryProps {
  codes:     CptCodeDto[];
  isLoading: boolean;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function CptCodingSummary({ codes, isLoading }: CptCodingSummaryProps) {
  if (isLoading) {
    return (
      <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap', mb: 2 }}>
        {Array.from({ length: 4 }).map((_, i) => (
          <Skeleton key={i} variant="rounded" width={130} height={76} sx={{ borderRadius: 2 }} />
        ))}
      </Box>
    );
  }

  const total      = codes.length;
  const approved   = codes.filter(c => c.status === 'Approved').length;
  const pending    = codes.filter(c => c.status === 'Pending').length;
  const overridden = codes.filter(c => c.status === 'Overridden').length;

  const stats = [
    { label: 'Total Codes',     value: total,      color: 'text.primary',   icon: <ReviewsOutlinedIcon fontSize="small" /> },
    { label: 'Approved',        value: approved,   color: 'success.main',   icon: <CheckCircleOutlineIcon fontSize="small" /> },
    { label: 'Pending Review',  value: pending,    color: 'warning.main',   icon: <PendingOutlinedIcon fontSize="small" /> },
    { label: 'Overridden',      value: overridden, color: overridden > 0 ? 'error.main' : 'text.disabled', icon: <EditOffIcon fontSize="small" /> },
  ];

  return (
    <Box
      sx={{ display: 'flex', gap: 2, flexWrap: 'wrap', mb: 2 }}
      role="region"
      aria-label="CPT code summary statistics"
    >
      {stats.map(stat => (
        <Card
          key={stat.label}
          variant="outlined"
          sx={{ minWidth: 120, flex: '1 1 120px', borderRadius: 2 }}
        >
          <CardContent sx={{ p: '12px !important', display: 'flex', alignItems: 'center', gap: 1 }}>
            <Box sx={{ color: stat.color }}>{stat.icon}</Box>
            <Box>
              <Typography variant="h5" component="div" fontWeight={700} color={stat.color}>
                {stat.value}
              </Typography>
              <Typography variant="caption" color="text.secondary">
                {stat.label}
              </Typography>
            </Box>
          </CardContent>
        </Card>
      ))}
    </Box>
  );
}
