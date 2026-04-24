/**
 * AgreementRateSummary — 5-card stat grid for the AI-human agreement rate dashboard.
 * (US_050, AC-2, SCR-014 wireframe `.coding-summary` pattern)
 *
 * Cards (5, responsive grid matching wireframe):
 *   1. AI-Human Agreement  — daily rate %, rolling 30-day as secondary
 *   2. Total Codes Verified
 *   3. Approved (green)
 *   4. Pending Review (amber) — derived as (total - approved_wo_override - overridden - partial)
 *   5. Overridden (red)        — codes_overridden + codes_partially_overridden
 *
 * Edge case: when meetsMinimumThreshold = false, Agreement card shows
 * "Not enough data" with minimum threshold indicator (EC-1).
 *
 * Loading: MUI Skeleton placeholders (UXR-502).
 *
 * Responsive grid:
 *   375px  — 1 column (minmax auto-fit)
 *   768px  — 2 columns
 *   1440px — 5 columns (matching wireframe)
 */

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Skeleton from '@mui/material/Skeleton';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';
import EditNoteIcon from '@mui/icons-material/EditNote';
import HandshakeOutlinedIcon from '@mui/icons-material/HandshakeOutlined';
import PendingOutlinedIcon from '@mui/icons-material/PendingOutlined';
import SummarizeOutlinedIcon from '@mui/icons-material/SummarizeOutlined';

import AgreementMeter from '@/components/coding/AgreementMeter';
import type { AgreementRateDto } from '@/hooks/useAgreementRate';

// ─── Helpers ──────────────────────────────────────────────────────────────────

function rateColor(rate: number): string {
  if (rate >= 98) return '#2E7D32';
  if (rate >= 90) return '#ED6C02';
  return '#D32F2F';
}

// ─── Stat card sub-component ─────────────────────────────────────────────────

interface StatCardProps {
  icon:       React.ReactNode;
  value:      React.ReactNode;
  label:      string;
  color:      string;
  secondary?: React.ReactNode;
  children?:  React.ReactNode;
}

function StatCard({ icon, value, label, color, secondary, children }: StatCardProps) {
  return (
    <Card
      variant="outlined"
      sx={{ flex: '1 1 160px', minWidth: 150, borderRadius: 2 }}
    >
      <CardContent
        sx={{
          p:          '16px !important',
          textAlign:  'center',
          display:    'flex',
          flexDirection: 'column',
          alignItems:    'center',
          gap:           0.5,
        }}
      >
        <Box sx={{ color, mb: 0.25 }}>{icon}</Box>
        <Typography
          variant="h4"
          component="div"
          fontWeight={700}
          sx={{ fontSize: '2rem', color, lineHeight: 1.1 }}
        >
          {value}
        </Typography>
        {secondary && (
          <Typography variant="caption" color="text.secondary" sx={{ lineHeight: 1.3 }}>
            {secondary}
          </Typography>
        )}
        <Typography
          variant="caption"
          color="text.secondary"
          sx={{ fontSize: '0.75rem', mt: 0.5 }}
        >
          {label}
        </Typography>
        {children}
      </CardContent>
    </Card>
  );
}

// ─── Props ────────────────────────────────────────────────────────────────────

interface AgreementRateSummaryProps {
  data:      AgreementRateDto | null;
  isLoading: boolean;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function AgreementRateSummary({ data, isLoading }: AgreementRateSummaryProps) {
  // ── Loading state (UXR-502) ──────────────────────────────────────────────
  if (isLoading) {
    return (
      <Box
        sx={{
          display:             'grid',
          gridTemplateColumns: 'repeat(auto-fit, minmax(160px, 1fr))',
          gap:                 2,
          mb:                  2,
        }}
        role="region"
        aria-label="Agreement rate summary loading"
        aria-busy="true"
      >
        {Array.from({ length: 5 }).map((_, i) => (
          <Skeleton key={i} variant="rounded" height={120} sx={{ borderRadius: 2 }} />
        ))}
      </Box>
    );
  }

  // ── Empty state ───────────────────────────────────────────────────────────
  if (!data) {
    return (
      <Box sx={{ mb: 2 }}>
        <Typography variant="body2" color="text.secondary" fontStyle="italic">
          No agreement rate data available. Daily metrics populate after the background job runs.
        </Typography>
      </Box>
    );
  }

  // ── Derived values ────────────────────────────────────────────────────────
  const daily      = data.daily_agreement_rate;
  const rolling    = data.rolling_30day_rate;
  const total      = data.total_codes_verified;
  const approved   = data.codes_approved_without_override;
  const overridden = data.codes_overridden + data.codes_partially_overridden;
  const pending    = Math.max(0, total - approved - overridden);

  const agreementValueDisplay = data.meets_minimum_threshold
    ? `${daily.toFixed(1)}%`
    : '—';

  const rollingDisplay = rolling != null
    ? `30-day avg: ${rolling.toFixed(1)}%`
    : null;

  return (
    <Box
      component="section"
      aria-label="Agreement rate summary"
      sx={{
        display:             'grid',
        gridTemplateColumns: 'repeat(auto-fit, minmax(160px, 1fr))',
        gap:                 2,
        mb:                  2,
      }}
    >
      {/* Card 1 — AI-Human Agreement */}
      <Card
        variant="outlined"
        sx={{ flex: '1 1 160px', minWidth: 160, borderRadius: 2 }}
      >
        <CardContent
          sx={{
            p:             '16px !important',
            textAlign:     'center',
            display:       'flex',
            flexDirection: 'column',
            alignItems:    'center',
            gap:           0.5,
          }}
        >
          <Box sx={{ color: data.meets_minimum_threshold ? rateColor(daily) : 'text.disabled', mb: 0.25 }}>
            <HandshakeOutlinedIcon />
          </Box>

          {data.meets_minimum_threshold ? (
            <Typography
              variant="h4"
              component="div"
              fontWeight={700}
              sx={{ fontSize: '2rem', color: rateColor(daily), lineHeight: 1.1 }}
              aria-label={`Daily agreement rate: ${agreementValueDisplay}`}
            >
              {agreementValueDisplay}
            </Typography>
          ) : (
            <Tooltip title={`Minimum 50 verified codes required. Current: ${total}`}>
              <Typography
                variant="body2"
                fontWeight={600}
                color="text.secondary"
                sx={{ cursor: 'help' }}
              >
                Not enough data
              </Typography>
            </Tooltip>
          )}

          {rollingDisplay && (
            <Typography variant="caption" color="text.secondary" sx={{ lineHeight: 1.3 }}>
              {rollingDisplay}
            </Typography>
          )}

          <Typography variant="caption" color="text.secondary" sx={{ fontSize: '0.75rem', mt: 0.5 }}>
            AI-Human Agreement
          </Typography>

          {/* Meter rendered only when threshold met */}
          {data.meets_minimum_threshold && (
            <Box sx={{ width: '100%', mt: 1 }}>
              <AgreementMeter rate={daily} meetsThreshold={data.meets_minimum_threshold} />
            </Box>
          )}

          {!data.meets_minimum_threshold && (
            <Typography variant="caption" color="info.main" sx={{ fontSize: '0.7rem', mt: 0.5 }}>
              Min. 50 codes · {total} so far
            </Typography>
          )}
        </CardContent>
      </Card>

      {/* Card 2 — Total Codes */}
      <StatCard
        icon={<SummarizeOutlinedIcon />}
        value={total}
        label="Total Codes"
        color="text.primary"
      />

      {/* Card 3 — Approved */}
      <StatCard
        icon={<CheckCircleOutlineIcon />}
        value={approved}
        label="Approved"
        color="#2E7D32"
      />

      {/* Card 4 — Pending Review */}
      <StatCard
        icon={<PendingOutlinedIcon />}
        value={pending}
        label="Pending Review"
        color="#ED6C02"
      />

      {/* Card 5 — Overridden */}
      <StatCard
        icon={<EditNoteIcon />}
        value={overridden}
        label="Overridden"
        color="#D32F2F"
        secondary={
          data.codes_partially_overridden > 0
            ? `(${data.codes_partially_overridden} partial)`
            : undefined
        }
      />
    </Box>
  );
}
