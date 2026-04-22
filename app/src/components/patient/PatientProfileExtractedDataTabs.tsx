/**
 * PatientProfileExtractedDataTabs — SCR-013 clinical data extraction panel (US_041 AC-2–AC-4, EC-1, EC-2).
 *
 * Renders four MUI tabs (Medications, Diagnoses, Procedures, Allergies) each containing:
 *   - A data table with a Confidence badge column and a Status / Verify column.
 *   - Row-level select checkboxes for eligible (pending/flagged) items (EC-2).
 *   - Single-item Verify button for individual confirmation (AC-4).
 *   - Bulk Verify toolbar that activates when one or more rows are selected (EC-2).
 *   - BulkVerificationDialog for the bulk confirmation step.
 *
 * Color coding (AC-3):
 *   ≥0.90 → green badge | 0.80–0.89 → amber badge | <0.80 → red badge | null → red "N/A" (EC-1)
 *
 * Flagged rows (confidence < 0.80 or null) receive a highlighted background and a
 * "Pending Review" chip so they are unmistakably distinct from verified rows (AC-2).
 *
 * Keyboard accessible: all interactive elements reachable via Tab; checkboxes and
 * verify buttons labelled per-row (UXR-505).
 */

import { useCallback, useMemo, useState } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Checkbox from '@mui/material/Checkbox';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import Skeleton from '@mui/material/Skeleton';
import Tab from '@mui/material/Tab';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Tabs from '@mui/material/Tabs';
import Toolbar from '@mui/material/Toolbar';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import VerifiedUserIcon from '@mui/icons-material/VerifiedUser';

import ExtractedDataConfidenceBadge, {
  getConfidenceTier,
  CONFIDENCE_THRESHOLD_LOW,
} from '@/components/documents/ExtractedDataConfidenceBadge';
import BulkVerificationDialog, {
  type VerifiableItem,
} from '@/components/documents/BulkVerificationDialog';
import {
  useExtractedDataByPatient,
  useVerifyExtractedItem,
  useBulkVerifyExtractedData,
  type ExtractedDataRow,
} from '@/hooks/useExtractedDataVerification';
import { useToast } from '@/components/common/ToastProvider';

// ─── Types ────────────────────────────────────────────────────────────────────

interface PatientProfileExtractedDataTabsProps {
  patientId: string;
}

type DataTab = 'Medication' | 'Diagnosis' | 'Procedure' | 'Allergy';

const TABS: DataTab[] = ['Medication', 'Diagnosis', 'Procedure', 'Allergy'];

const TAB_LABELS: Record<DataTab, string> = {
  Medication: 'Medications',
  Diagnosis:  'Diagnoses',
  Procedure:  'Procedures',
  Allergy:    'Allergies',
};

// ─── Helpers ──────────────────────────────────────────────────────────────────

function isEligibleForVerification(row: ExtractedDataRow): boolean {
  return row.verificationStatus === 'pending';
}

function isFlaggedRow(row: ExtractedDataRow): boolean {
  return row.flaggedForReview || row.confidenceScore == null ||
    (row.confidenceScore ?? 1) < CONFIDENCE_THRESHOLD_LOW;
}

function getDisplayLabel(row: ExtractedDataRow): string {
  const c = row.dataContent;
  switch (row.dataType) {
    case 'Medication': return c['drug_name'] ?? 'Unknown Medication';
    case 'Diagnosis':  return c['condition_name'] ?? c['icd_code'] ?? 'Unknown Diagnosis';
    case 'Procedure':  return c['procedure_name'] ?? c['cpt_code'] ?? 'Unknown Procedure';
    case 'Allergy':    return c['allergen'] ?? 'Unknown Allergen';
  }
}

// ─── Row sub-components ───────────────────────────────────────────────────────

interface VerificationStatusChipProps {
  status: ExtractedDataRow['verificationStatus'];
  flagged: boolean;
}

function VerificationStatusChip({ status, flagged }: VerificationStatusChipProps) {
  if (status === 'verified' || status === 'corrected') {
    return (
      <Chip
        label={status === 'corrected' ? 'Corrected' : 'Verified'}
        size="small"
        color="success"
        icon={<CheckCircleIcon />}
        aria-label={status === 'corrected' ? 'Data corrected and verified' : 'Data verified'}
      />
    );
  }
  if (flagged) {
    return (
      <Chip
        label="Pending Review"
        size="small"
        color="error"
        aria-label="Pending mandatory review — confidence is low or unavailable"
      />
    );
  }
  return <Chip label="Pending" size="small" color="default" aria-label="Pending verification" />;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function PatientProfileExtractedDataTabs({
  patientId,
}: PatientProfileExtractedDataTabsProps) {
  const { showToast } = useToast();
  const [activeTab, setActiveTab] = useState<DataTab>('Medication');
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [bulkDialogOpen, setBulkDialogOpen] = useState(false);

  const { data: rows = [], isLoading, isError } = useExtractedDataByPatient(patientId);

  const verifyMutation     = useVerifyExtractedItem('', patientId);
  const bulkVerifyMutation = useBulkVerifyExtractedData('', patientId);

  // Filter rows by active tab.
  const tabRows = useMemo(
    () => rows.filter(r => r.dataType === activeTab),
    [rows, activeTab],
  );

  // Count unverified per tab for tab labels.
  const pendingCounts = useMemo(() => {
    const map = new Map<DataTab, number>();
    for (const tab of TABS) {
      map.set(tab, rows.filter(r => r.dataType === tab).length);
    }
    return map;
  }, [rows]);

  // Eligible (pending) rows in current tab.
  const eligibleRows = useMemo(
    () => tabRows.filter(isEligibleForVerification),
    [tabRows],
  );

  // Items for BulkVerificationDialog.
  const selectedItems = useMemo<VerifiableItem[]>(
    () =>
      eligibleRows
        .filter(r => selectedIds.has(r.extractedDataId))
        .map(r => ({
          extractedDataId: r.extractedDataId,
          label:           getDisplayLabel(r),
          dataType:        r.dataType,
          confidenceScore: r.confidenceScore,
        })),
    [eligibleRows, selectedIds],
  );

  // ── Handlers ────────────────────────────────────────────────────────────────

  const handleTabChange = useCallback((_: React.SyntheticEvent, tab: DataTab) => {
    setActiveTab(tab);
    setSelectedIds(new Set());
  }, []);

  const handleToggleRow = useCallback((id: string) => {
    setSelectedIds(prev => {
      const next = new Set(prev);
      if (next.has(id)) { next.delete(id); } else { next.add(id); }
      return next;
    });
  }, []);

  const handleSelectAll = useCallback(() => {
    const allIds = eligibleRows.map(r => r.extractedDataId);
    setSelectedIds(prev =>
      prev.size === allIds.length ? new Set() : new Set(allIds),
    );
  }, [eligibleRows]);

  const handleSingleVerify = useCallback(
    (row: ExtractedDataRow) => {
      verifyMutation.mutate(
        { extractedDataId: row.extractedDataId, action: 'verified' },
        {
          onSuccess: () => {
            showToast({
              message: `"${getDisplayLabel(row)}" verified successfully.`,
              severity: 'success',
            });
          },
          onError: () => {
            showToast({
              message: `Failed to verify "${getDisplayLabel(row)}". Please try again.`,
              severity: 'error',
            });
          },
        },
      );
    },
    [verifyMutation, showToast],
  );

  const handleBulkConfirm = useCallback(() => {
    bulkVerifyMutation.mutate(
      { extractedDataIds: Array.from(selectedIds) },
      {
        onSuccess: res => {
          setBulkDialogOpen(false);
          setSelectedIds(new Set());
          showToast({
            message: `${res.verifiedCount} item${res.verifiedCount !== 1 ? 's' : ''} verified successfully.`,
            severity: 'success',
          });
        },
        onError: () => {
          showToast({
            message: 'Bulk verification failed. Please try again.',
            severity: 'error',
          });
        },
      },
    );
  }, [bulkVerifyMutation, selectedIds, showToast]);

  // ── Render ──────────────────────────────────────────────────────────────────

  if (isLoading) {
    return (
      <Box sx={{ p: 2 }}>
        {[1, 2, 3].map(i => (
          <Skeleton key={i} variant="rectangular" height={40} sx={{ mb: 1, borderRadius: 1 }} />
        ))}
      </Box>
    );
  }

  if (isError) {
    return (
      <Alert severity="error" sx={{ m: 2 }}>
        Failed to load extracted clinical data. Please refresh and try again.
      </Alert>
    );
  }

  const allEligibleSelected =
    eligibleRows.length > 0 && selectedIds.size === eligibleRows.length;

  return (
    <Box>
      {/* Tab strip (SCR-013 wireframe) */}
      <Tabs
        value={activeTab}
        onChange={handleTabChange}
        aria-label="Clinical data categories"
        variant="scrollable"
        scrollButtons="auto"
        sx={{ borderBottom: 1, borderColor: 'divider' }}
      >
        {TABS.map(tab => (
          <Tab
            key={tab}
            value={tab}
            label={`${TAB_LABELS[tab]} (${pendingCounts.get(tab) ?? 0})`}
            id={`tab-${tab.toLowerCase()}`}
            aria-controls={`panel-${tab.toLowerCase()}`}
          />
        ))}
      </Tabs>

      {/* Bulk-action toolbar — visible when rows are selected */}
      {selectedIds.size > 0 && (
        <Toolbar
          variant="dense"
          sx={{
            bgcolor: 'primary.50',
            borderBottom: 1,
            borderColor: 'divider',
            gap: 1,
          }}
          aria-label="Bulk verification toolbar"
        >
          <Typography variant="body2" sx={{ flex: 1 }}>
            <strong>{selectedIds.size}</strong> item{selectedIds.size !== 1 ? 's' : ''} selected
          </Typography>
          <Button
            size="small"
            variant="contained"
            color="success"
            startIcon={<VerifiedUserIcon />}
            onClick={() => setBulkDialogOpen(true)}
            aria-label={`Bulk verify ${selectedIds.size} selected item${selectedIds.size !== 1 ? 's' : ''}`}
          >
            Verify Selected
          </Button>
          <Button
            size="small"
            color="inherit"
            onClick={() => setSelectedIds(new Set())}
            aria-label="Clear selection"
          >
            Clear
          </Button>
        </Toolbar>
      )}

      {/* Data table */}
      <Box
        role="tabpanel"
        id={`panel-${activeTab.toLowerCase()}`}
        aria-labelledby={`tab-${activeTab.toLowerCase()}`}
      >
        {tabRows.length === 0 ? (
          <Typography variant="body2" color="text.secondary" sx={{ p: 3 }}>
            No {TAB_LABELS[activeTab].toLowerCase()} recorded.
          </Typography>
        ) : (
          <TableContainer>
            <Table size="small" aria-label={`${TAB_LABELS[activeTab]} extraction results`}>
              <TableHead>
                <TableRow>
                  <TableCell padding="checkbox">
                    <Checkbox
                      size="small"
                      checked={allEligibleSelected}
                      indeterminate={selectedIds.size > 0 && !allEligibleSelected}
                      onChange={handleSelectAll}
                      disabled={eligibleRows.length === 0}
                      inputProps={{ 'aria-label': 'Select all pending items' }}
                    />
                  </TableCell>
                  <TableCell>Item</TableCell>
                  <TableCell>Source</TableCell>
                  <TableCell>Confidence</TableCell>
                  <TableCell>Status</TableCell>
                  <TableCell align="right">Action</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {tabRows.map(row => {
                  const flagged   = isFlaggedRow(row);
                  const eligible  = isEligibleForVerification(row);
                  const checked   = selectedIds.has(row.extractedDataId);
                  const label     = getDisplayLabel(row);
                  const tier      = getConfidenceTier(row.confidenceScore);

                  return (
                    <TableRow
                      key={row.extractedDataId}
                      sx={{
                        bgcolor: flagged && eligible
                          ? 'warning.50'
                          : 'inherit',
                        '&:hover': { bgcolor: flagged && eligible ? 'warning.100' : 'action.hover' },
                      }}
                      aria-label={`${label} — ${flagged ? 'flagged for review' : 'not flagged'}`}
                    >
                      <TableCell padding="checkbox">
                        <Checkbox
                          size="small"
                          checked={checked}
                          onChange={() => handleToggleRow(row.extractedDataId)}
                          disabled={!eligible}
                          inputProps={{ 'aria-label': `Select ${label} for bulk verification` }}
                        />
                      </TableCell>
                      <TableCell>
                        <Typography variant="body2" fontWeight={flagged ? 600 : 400}>
                          {label}
                        </Typography>
                        {row.pageNumber > 0 && (
                          <Typography variant="caption" color="text.secondary">
                            p.{row.pageNumber}
                            {row.extractionRegion ? ` · ${row.extractionRegion}` : ''}
                          </Typography>
                        )}
                      </TableCell>
                      <TableCell>
                        <Typography variant="body2" color="text.secondary">
                          {row.dataContent['source_document'] ?? '—'}
                        </Typography>
                      </TableCell>
                      <TableCell>
                        <ExtractedDataConfidenceBadge score={row.confidenceScore} />
                        {tier === 'unavailable' && (
                          <Typography
                            variant="caption"
                            color="error.main"
                            sx={{ display: 'block', mt: 0.25 }}
                          >
                            confidence-unavailable
                          </Typography>
                        )}
                      </TableCell>
                      <TableCell>
                        <VerificationStatusChip
                          status={row.verificationStatus}
                          flagged={flagged}
                        />
                        {row.verifiedByName && row.verifiedAt && (
                          <Tooltip
                            title={`Verified by ${row.verifiedByName} at ${new Date(row.verifiedAt).toLocaleString()}`}
                            arrow
                          >
                            <Typography
                              variant="caption"
                              color="text.secondary"
                              sx={{ display: 'block', cursor: 'help' }}
                            >
                              {row.verifiedByName}
                            </Typography>
                          </Tooltip>
                        )}
                      </TableCell>
                      <TableCell align="right">
                        {eligible && (
                          <Button
                            size="small"
                            variant="outlined"
                            color={flagged ? 'warning' : 'primary'}
                            startIcon={
                              verifyMutation.isPending &&
                              verifyMutation.variables?.extractedDataId === row.extractedDataId
                                ? <CircularProgress size={12} />
                                : <VerifiedUserIcon />
                            }
                            disabled={
                              verifyMutation.isPending &&
                              verifyMutation.variables?.extractedDataId === row.extractedDataId
                            }
                            onClick={() => handleSingleVerify(row)}
                            aria-label={`Verify ${label}`}
                            aria-busy={
                              verifyMutation.isPending &&
                              verifyMutation.variables?.extractedDataId === row.extractedDataId
                            }
                          >
                            Verify
                          </Button>
                        )}
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          </TableContainer>
        )}
      </Box>

      {/* Bulk verification confirmation dialog (EC-2) */}
      <BulkVerificationDialog
        open={bulkDialogOpen}
        selectedItems={selectedItems}
        isLoading={bulkVerifyMutation.isPending}
        onConfirm={handleBulkConfirm}
        onCancel={() => setBulkDialogOpen(false)}
      />

      {/* Screen-reader live region for verification outcomes (UXR-505) */}
      <Box
        aria-live="polite"
        aria-atomic="true"
        sx={{ position: 'absolute', width: 1, height: 1, overflow: 'hidden', clip: 'rect(0 0 0 0)' }}
      >
        {verifyMutation.isSuccess && 'Item verified successfully.'}
        {bulkVerifyMutation.isSuccess && `${bulkVerifyMutation.data?.verifiedCount ?? 0} items verified successfully.`}
      </Box>
    </Box>
  );
}
