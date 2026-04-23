/**
 * VerificationQueueTable — MUI Table showing the full code verification queue
 * for a patient encounter (US_049 AC-1, AC-2, AC-3, AC-4).
 *
 * Columns: Code Type | Code Value | Description | AI Justification |
 *          Confidence | Status | Actions
 *
 * Features:
 *   - Sortable columns: Code Type, Code Value, Confidence (TableSortLabel)
 *   - Filter dropdown for code type (All / ICD-10 / CPT)
 *   - Approve button (green) — calls useApproveCode; blocks on deprecated (EC-1)
 *   - Override button (amber) — opens CodeOverrideModal (AC-3)
 *   - Row expansion → CodeAuditTrail accordion (AC-4)
 *   - DeprecatedCodeAlert shown inline below row on 409 Conflict
 *   - Toast notifications via useToast
 *
 * Screen states:
 *   Loading   — Skeleton rows (5 rows × all columns)
 *   Empty     — "No pending codes for review" with description
 *   Error     — Alert with retry button
 *   Default   — Populated table
 *
 * Responsive: Justification column hidden at xs/sm (display: { xs:'none', md:'table-cell' })
 */

import { Fragment, useState } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Collapse from '@mui/material/Collapse';
import FormControl from '@mui/material/FormControl';
import IconButton from '@mui/material/IconButton';
import InputLabel from '@mui/material/InputLabel';
import MenuItem from '@mui/material/MenuItem';
import Paper from '@mui/material/Paper';
import Select from '@mui/material/Select';
import Skeleton from '@mui/material/Skeleton';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import TableSortLabel from '@mui/material/TableSortLabel';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import ErrorOutlineIcon from '@mui/icons-material/ErrorOutline';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import KeyboardArrowUpIcon from '@mui/icons-material/KeyboardArrowUp';
import PendingActionsIcon from '@mui/icons-material/PendingActions';
import SwapHorizIcon from '@mui/icons-material/SwapHoriz';
import { visuallyHidden } from '@mui/utils';

import ConfidenceBadge from '@/components/shared/ConfidenceBadge';
import CodeAuditTrail from './CodeAuditTrail';
import CodeOverrideModal from './CodeOverrideModal';
import DeprecatedCodeAlert from './DeprecatedCodeAlert';
import { useApproveCode, DeprecatedCodeError } from '@/hooks/useApproveCode';
import { useOverrideCode } from '@/hooks/useOverrideCode';
import { useToast } from '@/components/common/ToastProvider';
import type { VerificationQueueItem } from '@/hooks/useVerificationQueue';
import type { OverrideSubmitParams } from './CodeOverrideModal';

// ─── Types ────────────────────────────────────────────────────────────────────

type SortColumn    = 'code_type' | 'code_value' | 'ai_confidence_score';
type SortDirection = 'asc' | 'desc';

interface DeprecatedAlertState {
  codeId:           string;
  deprecatedNotice: string;
  replacementCodes: string[];
}

interface OverrideModalState {
  open:              boolean;
  codeId:            string;
  codeType:          string;
  originalCodeValue: string;
  prefillCode?:      string;
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

function StatusChip({ status }: { status: string }) {
  switch (status.toLowerCase()) {
    case 'verified':
      return (
        <Chip
          icon={<CheckCircleIcon />}
          label="Verified"
          color="success"
          size="small"
          sx={{ fontWeight: 600 }}
        />
      );
    case 'overridden':
      return (
        <Chip
          icon={<SwapHorizIcon />}
          label="Overridden"
          color="warning"
          size="small"
          sx={{ fontWeight: 600 }}
        />
      );
    case 'deprecated':
      return (
        <Chip
          icon={<ErrorOutlineIcon />}
          label="Deprecated"
          color="error"
          size="small"
          sx={{ fontWeight: 600 }}
        />
      );
    default:
      return (
        <Chip
          icon={<PendingActionsIcon />}
          label="Pending"
          color="default"
          size="small"
          sx={{ fontWeight: 600 }}
        />
      );
  }
}

function CodeTypeChip({ codeType }: { codeType: string }) {
  const label = codeType.toUpperCase() === 'CPT' ? 'CPT' : 'ICD-10';
  return (
    <Chip
      label={label}
      size="small"
      variant="outlined"
      color={label === 'CPT' ? 'primary' : 'secondary'}
      sx={{ fontWeight: 600, fontSize: '0.7rem' }}
    />
  );
}

// ─── Skeleton ─────────────────────────────────────────────────────────────────

function LoadingRows() {
  return (
    <>
      {Array.from({ length: 5 }).map((_, i) => (
        <TableRow key={i} sx={{ '& td': { py: 1.5 } }}>
          <TableCell><Skeleton variant="rounded" width={20} height={20} /></TableCell>
          <TableCell><Skeleton variant="rounded" width={60} height={22} /></TableCell>
          <TableCell><Skeleton variant="text" width={72} /></TableCell>
          <TableCell><Skeleton variant="text" width={180} /></TableCell>
          <TableCell sx={{ display: { xs: 'none', md: 'table-cell' } }}>
            <Skeleton variant="text" width={160} />
          </TableCell>
          <TableCell><Skeleton variant="rounded" width={44} height={22} sx={{ borderRadius: '9999px' }} /></TableCell>
          <TableCell><Skeleton variant="rounded" width={70} height={22} /></TableCell>
          <TableCell><Skeleton variant="rounded" width={140} height={32} /></TableCell>
        </TableRow>
      ))}
    </>
  );
}

// ─── Props ────────────────────────────────────────────────────────────────────

interface VerificationQueueTableProps {
  patientId: string;
  items:     VerificationQueueItem[];
  isLoading: boolean;
  isError:   boolean;
  onRetry:   () => void;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function VerificationQueueTable({
  patientId,
  items,
  isLoading,
  isError,
  onRetry,
}: VerificationQueueTableProps) {
  const { showToast }    = useToast();
  const approveMutation  = useApproveCode(patientId);
  const overrideMutation = useOverrideCode(patientId);

  // ── Table state ────────────────────────────────────────────────────────────
  const [sortCol,    setSortCol]    = useState<SortColumn>('code_type');
  const [sortDir,    setSortDir]    = useState<SortDirection>('asc');
  const [codeFilter, setCodeFilter] = useState<string>('all');
  const [expandedId, setExpandedId] = useState<string | null>(null);

  // ── Deprecated alert state (per row) ──────────────────────────────────────
  const [deprecatedAlert, setDeprecatedAlert] = useState<DeprecatedAlertState | null>(null);

  // ── Override modal state ───────────────────────────────────────────────────
  const [overrideModal, setOverrideModal] = useState<OverrideModalState>({
    open:              false,
    codeId:            '',
    codeType:          '',
    originalCodeValue: '',
  });

  // ── Sort handler ──────────────────────────────────────────────────────────
  function handleSort(col: SortColumn) {
    if (sortCol === col) {
      setSortDir(d => d === 'asc' ? 'desc' : 'asc');
    } else {
      setSortCol(col);
      setSortDir('asc');
    }
  }

  // ── Open override modal ───────────────────────────────────────────────────
  function openOverride(item: VerificationQueueItem, prefillCode?: string) {
    setOverrideModal({
      open:              true,
      codeId:            item.code_id,
      codeType:          item.code_type,
      originalCodeValue: item.code_value,
      prefillCode,
    });
  }

  // ── Approve handler ───────────────────────────────────────────────────────
  async function handleApprove(item: VerificationQueueItem) {
    try {
      await approveMutation.mutateAsync(item.code_id);
      showToast({ message: 'Code approved successfully', severity: 'success' });
      setDeprecatedAlert(null);
    } catch (err) {
      if (err instanceof DeprecatedCodeError) {
        setDeprecatedAlert({
          codeId:           item.code_id,
          deprecatedNotice: err.conflict.deprecated_notice,
          replacementCodes: err.conflict.replacement_codes,
        });
        showToast({ message: 'Cannot approve deprecated code', severity: 'error' });
      } else {
        showToast({ message: 'Failed to approve code. Please try again.', severity: 'error' });
      }
    }
  }

  // ── Override submit handler ───────────────────────────────────────────────
  async function handleOverrideSubmit(params: OverrideSubmitParams) {
    try {
      await overrideMutation.mutateAsync({
        codeId:          overrideModal.codeId,
        new_code_value:  params.newCodeValue,
        new_description: params.newDescription,
        justification:   params.justification,
      });
      showToast({ message: 'Code overridden successfully', severity: 'success' });
      setOverrideModal(m => ({ ...m, open: false }));
      setDeprecatedAlert(null);
    } catch {
      showToast({ message: 'Failed to override code. Please try again.', severity: 'error' });
    }
  }

  // ── Filter + sort ─────────────────────────────────────────────────────────
  const filtered = items.filter(item => {
    if (codeFilter === 'all')    return true;
    if (codeFilter === 'icd10')  return item.code_type.toUpperCase() !== 'CPT';
    if (codeFilter === 'cpt')    return item.code_type.toUpperCase() === 'CPT';
    return true;
  });

  const sorted = [...filtered].sort((a, b) => {
    let cmp = 0;
    if (sortCol === 'code_type')           cmp = a.code_type.localeCompare(b.code_type);
    else if (sortCol === 'code_value')      cmp = a.code_value.localeCompare(b.code_value);
    else if (sortCol === 'ai_confidence_score') cmp = a.ai_confidence_score - b.ai_confidence_score;
    return sortDir === 'asc' ? cmp : -cmp;
  });

  // ── Render ────────────────────────────────────────────────────────────────
  return (
    <>
      {/* ── Toolbar: filter dropdown ── */}
      <Box sx={{ display: 'flex', justifyContent: 'flex-end', mb: 1.5 }}>
        <FormControl size="small" sx={{ minWidth: 160 }}>
          <InputLabel id="code-type-filter-label">Code type</InputLabel>
          <Select
            labelId="code-type-filter-label"
            value={codeFilter}
            label="Code type"
            onChange={e => setCodeFilter(e.target.value)}
            aria-label="Filter by code type"
          >
            <MenuItem value="all">All types</MenuItem>
            <MenuItem value="icd10">ICD-10 only</MenuItem>
            <MenuItem value="cpt">CPT only</MenuItem>
          </Select>
        </FormControl>
      </Box>

      {/* ── Error state ── */}
      {isError && !isLoading && (
        <Alert
          severity="error"
          action={
            <Button size="small" onClick={onRetry} color="inherit">
              Retry
            </Button>
          }
          sx={{ mb: 2 }}
        >
          Failed to load verification queue. Check your connection and try again.
        </Alert>
      )}

      <TableContainer component={Paper} variant="outlined">
        <Table aria-label="Code verification queue" size="small">
          <TableHead sx={{ bgcolor: 'grey.100' }}>
            <TableRow>
              {/* Expand toggle column */}
              <TableCell sx={{ width: 40 }} aria-label="Expand row" />

              {/* Code Type — sortable */}
              <TableCell sx={{ fontWeight: 600, whiteSpace: 'nowrap' }}>
                <TableSortLabel
                  active={sortCol === 'code_type'}
                  direction={sortCol === 'code_type' ? sortDir : 'asc'}
                  onClick={() => handleSort('code_type')}
                >
                  Type
                  {sortCol === 'code_type' && (
                    <Box component="span" sx={visuallyHidden}>
                      {sortDir === 'desc' ? 'sorted descending' : 'sorted ascending'}
                    </Box>
                  )}
                </TableSortLabel>
              </TableCell>

              {/* Code Value — sortable */}
              <TableCell sx={{ fontWeight: 600 }}>
                <TableSortLabel
                  active={sortCol === 'code_value'}
                  direction={sortCol === 'code_value' ? sortDir : 'asc'}
                  onClick={() => handleSort('code_value')}
                >
                  Code
                </TableSortLabel>
              </TableCell>

              <TableCell sx={{ fontWeight: 600 }}>Description</TableCell>

              {/* AI Justification — hidden on mobile */}
              <TableCell
                sx={{ fontWeight: 600, display: { xs: 'none', md: 'table-cell' } }}
              >
                AI Justification
              </TableCell>

              {/* Confidence — sortable */}
              <TableCell sx={{ fontWeight: 600, whiteSpace: 'nowrap' }}>
                <TableSortLabel
                  active={sortCol === 'ai_confidence_score'}
                  direction={sortCol === 'ai_confidence_score' ? sortDir : 'asc'}
                  onClick={() => handleSort('ai_confidence_score')}
                >
                  Confidence
                </TableSortLabel>
              </TableCell>

              <TableCell sx={{ fontWeight: 600 }}>Status</TableCell>
              <TableCell sx={{ fontWeight: 600 }}>Actions</TableCell>
            </TableRow>
          </TableHead>

          <TableBody>
            {isLoading ? (
              <LoadingRows />
            ) : sorted.length === 0 && !isError ? (
              <TableRow>
                <TableCell colSpan={8}>
                  <Box
                    sx={{
                      py:         4,
                      textAlign:  'center',
                      color:      'text.secondary',
                    }}
                    role="status"
                    aria-live="polite"
                  >
                    <PendingActionsIcon
                      sx={{ fontSize: 40, mb: 1, opacity: 0.4 }}
                      aria-hidden="true"
                    />
                    <Typography variant="body1" fontWeight={600}>
                      No pending codes for review
                    </Typography>
                    <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
                      All codes for this patient have been verified or overridden.
                    </Typography>
                  </Box>
                </TableCell>
              </TableRow>
            ) : (
              sorted.map(item => {
                const isPending    = item.status.toLowerCase() === 'pending';
                const isExpanded   = expandedId === item.code_id;
                const showDepAlert = deprecatedAlert?.codeId === item.code_id;

                return (
                  <Fragment key={item.code_id}>
                    {/* ── Main data row ── */}
                    <TableRow
                      hover
                      sx={{
                        bgcolor:     item.status.toLowerCase() === 'verified'   ? 'success.50'
                                   : item.status.toLowerCase() === 'overridden' ? 'warning.50'
                                   : undefined,
                        '& td': { py: 1.5 },
                        verticalAlign: 'top',
                      }}
                    >
                      {/* Expand / collapse toggle */}
                      <TableCell padding="checkbox">
                        <Tooltip title={isExpanded ? 'Collapse audit trail' : 'View audit trail'}>
                          <IconButton
                            size="small"
                            onClick={() => setExpandedId(isExpanded ? null : item.code_id)}
                            aria-expanded={isExpanded}
                            aria-label={`${isExpanded ? 'Collapse' : 'Expand'} audit trail for code ${item.code_value}`}
                          >
                            {isExpanded ? <KeyboardArrowUpIcon /> : <ExpandMoreIcon />}
                          </IconButton>
                        </Tooltip>
                      </TableCell>

                      {/* Code type chip */}
                      <TableCell>
                        <CodeTypeChip codeType={item.code_type} />
                      </TableCell>

                      {/* Code value */}
                      <TableCell>
                        <Typography variant="body2" fontWeight={700} sx={{ fontFamily: 'monospace' }}>
                          {item.code_value}
                        </Typography>
                      </TableCell>

                      {/* Description */}
                      <TableCell>
                        <Typography variant="body2">{item.description}</Typography>
                      </TableCell>

                      {/* AI Justification — hidden on mobile */}
                      <TableCell sx={{ display: { xs: 'none', md: 'table-cell' }, maxWidth: 220 }}>
                        <Typography
                          variant="caption"
                          color="text.secondary"
                          sx={{
                            display:           '-webkit-box',
                            WebkitLineClamp:   2,
                            WebkitBoxOrient:   'vertical',
                            overflow:          'hidden',
                            textOverflow:      'ellipsis',
                          }}
                        >
                          {item.justification || '—'}
                        </Typography>
                      </TableCell>

                      {/* Confidence badge */}
                      <TableCell>
                        <ConfidenceBadge score={item.ai_confidence_score} />
                      </TableCell>

                      {/* Status chip */}
                      <TableCell>
                        <StatusChip status={item.status} />
                      </TableCell>

                      {/* Action buttons */}
                      <TableCell>
                        {isPending ? (
                          <Box sx={{ display: 'flex', gap: 1 }}>
                            <Button
                              variant="contained"
                              color="success"
                              size="small"
                              onClick={() => handleApprove(item)}
                              disabled={approveMutation.isLoading}
                              aria-label={`Approve code ${item.code_value}`}
                              sx={{ whiteSpace: 'nowrap' }}
                            >
                              Approve
                            </Button>
                            <Button
                              variant="outlined"
                              color="warning"
                              size="small"
                              onClick={() => openOverride(item)}
                              disabled={overrideMutation.isLoading}
                              aria-label={`Override code ${item.code_value}`}
                              sx={{ whiteSpace: 'nowrap' }}
                            >
                              Override
                            </Button>
                          </Box>
                        ) : (
                          <Typography variant="caption" color="text.secondary">
                            —
                          </Typography>
                        )}
                      </TableCell>
                    </TableRow>

                    {/* ── Deprecated code alert row ── */}
                    {showDepAlert && deprecatedAlert && (
                      <TableRow sx={{ bgcolor: 'warning.50' }}>
                        <TableCell colSpan={8} sx={{ py: 0, px: 2, pb: 1 }}>
                          <DeprecatedCodeAlert
                            deprecatedNotice={deprecatedAlert.deprecatedNotice}
                            replacementCodes={deprecatedAlert.replacementCodes}
                            onSelectReplacement={code => openOverride(item, code)}
                            onDismiss={() => setDeprecatedAlert(null)}
                          />
                        </TableCell>
                      </TableRow>
                    )}

                    {/* ── Audit trail expansion row ── */}
                    {isExpanded && (
                      <TableRow sx={{ bgcolor: 'grey.50' }}>
                        <TableCell colSpan={8} sx={{ py: 0, px: 2, pb: 1 }}>
                          <Collapse in={isExpanded} timeout="auto" unmountOnExit>
                            <Box sx={{ py: 1 }}>
                              <CodeAuditTrail codeId={item.code_id} />
                            </Box>
                          </Collapse>
                        </TableCell>
                      </TableRow>
                    )}
                  </Fragment>
                );
              })
            )}
          </TableBody>
        </Table>
      </TableContainer>

      {/* ── Override modal (portal-rendered outside table) ── */}
      <CodeOverrideModal
        open={overrideModal.open}
        codeType={overrideModal.codeType}
        originalCodeValue={overrideModal.originalCodeValue}
        prefillCode={overrideModal.prefillCode}
        onClose={() => setOverrideModal(m => ({ ...m, open: false }))}
        onSubmit={handleOverrideSubmit}
        isSubmitting={overrideMutation.isLoading}
      />
    </>
  );
}
