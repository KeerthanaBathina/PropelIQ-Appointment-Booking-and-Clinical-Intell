/**
 * VerificationCard — MUI Card for a single pending AI verification item (US_075 AC-1–AC-4).
 *
 * Per wireframe SCR-014 and task spec:
 *   - Code value + description as primary text
 *   - AI justification as secondary text
 *   - Confidence Chip (UXR-105): green ≥ 0.90, amber 0.80–0.89, red < 0.80
 *   - Verification status Chip: Pending (amber), Verified (green), Modified (blue), Rejected (red)
 *   - Three action buttons: Approve (contained), Modify (outlined), Reject (text/error)
 *   - Disabled state for already-verified / modified / rejected items (read-only)
 *
 * ARIA:
 *   - Card has role="article" and aria-label describing the record
 *   - Buttons have aria-label including the code value
 *   - Status and confidence chips have role="status"
 *
 * Accessibility (WCAG 2.1 AA): all colour + text combinations meet 4.5:1 contrast ratio.
 */

import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import Divider from '@mui/material/Divider';
import Typography from '@mui/material/Typography';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';
import EditOutlinedIcon from '@mui/icons-material/EditOutlined';
import BlockIcon from '@mui/icons-material/Block';

import type { VerificationItem } from '../types';
import VerificationAuditTrail from './VerificationAuditTrail';

// ─── Helpers ──────────────────────────────────────────────────────────────────

/**
 * UXR-105 confidence chip colour thresholds for the verification queue.
 * Separate from the global ConfidenceBadge (which uses >= 0.80 as green)
 * because the verification context requires finer granularity.
 */
function confidenceChipColor(score: number): {
  bgcolor: string;
  label: string;
  contrastText: string;
} {
  if (score >= 0.9)  return { bgcolor: '#2E7D32', label: `${Math.round(score * 100)}%`, contrastText: '#fff' }; // green
  if (score >= 0.8)  return { bgcolor: '#ED6C02', label: `${Math.round(score * 100)}%`, contrastText: '#fff' }; // amber
  return             { bgcolor: '#D32F2F', label: `${Math.round(score * 100)}%`, contrastText: '#fff' }; // red
}

function statusChipProps(status: VerificationItem['verificationStatus']): {
  label: string;
  bgcolor: string;
  textColor: string;
} {
  switch (status) {
    case 'verified':   return { label: 'Verified',   bgcolor: 'success.main',  textColor: '#fff' };
    case 'modified':   return { label: 'Modified',   bgcolor: 'info.main',     textColor: '#fff' };
    case 'rejected':   return { label: 'Rejected',   bgcolor: 'error.main',    textColor: '#fff' };
    case 'pending-verification':
    default:           return { label: 'Pending',    bgcolor: 'warning.main',  textColor: '#fff' };
  }
}

// ─── Props ────────────────────────────────────────────────────────────────────

export interface VerificationCardProps {
  item: VerificationItem;
  /** Disable approve/modify/reject actions (e.g. while another action is in flight). */
  isActionsDisabled?: boolean;
  onApprove: (item: VerificationItem) => void;
  onModify:  (item: VerificationItem) => void;
  onReject:  (item: VerificationItem) => void;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function VerificationCard({
  item,
  isActionsDisabled = false,
  onApprove,
  onModify,
  onReject,
}: VerificationCardProps) {
  const isPending    = item.verificationStatus === 'pending-verification';
  const actionsDisabled = isActionsDisabled || !isPending;

  const confidence    = confidenceChipColor(item.confidenceScore);
  const statusChip    = statusChipProps(item.verificationStatus);

  return (
    <Card
      variant="outlined"
      role="article"
      aria-label={`Verification item: ${item.codeValue} — ${item.description}`}
      sx={{
        mb: 2,
        borderRadius: 2,
        borderColor: isPending ? 'warning.light' : 'divider',
        opacity: actionsDisabled && !isPending ? 0.85 : 1,
      }}
    >
      <CardContent>
        {/* ── Header row: code, status badge, confidence chip ── */}
        <Box sx={{ display: 'flex', alignItems: 'flex-start', gap: 1, flexWrap: 'wrap', mb: 1 }}>
          <Typography variant="subtitle1" fontWeight={700} component="span" sx={{ mr: 0.5 }}>
            {item.codeValue}
          </Typography>

          {/* Verification status badge */}
          <Chip
            label={statusChip.label}
            size="small"
            role="status"
            aria-label={`Verification status: ${statusChip.label}`}
            sx={{
              bgcolor:    statusChip.bgcolor,
              color:      statusChip.textColor,
              fontWeight: 600,
              fontSize:   '0.7rem',
            }}
          />

          {/* AI confidence chip (UXR-105) */}
          <Chip
            label={`AI ${confidence.label}`}
            size="small"
            role="status"
            aria-label={`AI confidence: ${confidence.label}`}
            sx={{
              bgcolor:    confidence.bgcolor,
              color:      confidence.contrastText,
              fontWeight: 500,
              fontSize:   '0.7rem',
            }}
          />

          {/* Record type badge */}
          <Chip
            label={item.recordType === 'MedicalCode' ? 'Medical Code' : 'Extracted Data'}
            size="small"
            variant="outlined"
            sx={{ fontSize: '0.7rem', ml: 'auto' }}
          />
        </Box>

        {/* ── Description ── */}
        <Typography variant="body2" color="text.primary" sx={{ mb: 0.5 }}>
          {item.description}
        </Typography>

        {/* ── AI justification ── */}
        {item.aiJustification && (
          <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 1 }}>
            <Box component="span" fontWeight={600}>AI justification: </Box>
            {item.aiJustification}
          </Typography>
        )}

        {/* ── Source attribution ── */}
        {item.sourceAttribution && (
          <Typography variant="caption" color="text.disabled" sx={{ display: 'block', mb: 1 }}>
            Source: {item.sourceAttribution}
          </Typography>
        )}

        <Divider sx={{ my: 1 }} />

        {/* ── Action buttons ── */}
        <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap', alignItems: 'center' }}>
          <Button
            variant="contained"
            size="small"
            color="success"
            disabled={actionsDisabled}
            startIcon={<CheckCircleOutlineIcon />}
            aria-label={`Approve ${item.codeValue}`}
            onClick={() => onApprove(item)}
          >
            Approve
          </Button>

          <Button
            variant="outlined"
            size="small"
            disabled={actionsDisabled}
            startIcon={<EditOutlinedIcon />}
            aria-label={`Modify ${item.codeValue}`}
            onClick={() => onModify(item)}
          >
            Modify
          </Button>

          <Button
            variant="text"
            size="small"
            color="error"
            disabled={actionsDisabled}
            startIcon={<BlockIcon />}
            aria-label={`Reject ${item.codeValue}`}
            onClick={() => onReject(item)}
          >
            Reject
          </Button>
        </Box>
      </CardContent>

      {/* ── Audit trail accordion ── */}
      <VerificationAuditTrail recordId={item.id} />
    </Card>
  );
}
