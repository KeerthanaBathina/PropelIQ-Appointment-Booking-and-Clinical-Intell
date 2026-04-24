/**
 * MultiCodeAssignmentPanel — Panel for assigning multiple billable diagnosis / procedure
 * codes to a single encounter (US_051, AC-3, AC-4, SCR-014).
 *
 * Features:
 *   • Add multiple ICD-10 / CPT codes individually via a code-search input
 *   • Each row shows code value, type, description, sequence_order, and verification status
 *   • Individual Approve / Remove actions per code
 *   • Billing priority ordering via up/down reorder buttons (billing sequence)
 *   • "Validate Bundling" button triggers POST /api/coding/validate-bundling
 *   • "Submit Assignment" button triggers POST /api/coding/multi-assign
 *
 * Loading states: skeleton while validation runs (UXR-502).
 * Validation: empty list or duplicate code shows inline field error (UXR-401).
 *
 * Design tokens (designsystem.md):
 *   Verified badge: success-500 #2E7D32
 *   Pending badge:  warning-500 #ED6C02
 *   Rejected badge: error-500   #D32F2F
 */

import { useState, useCallback } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import Divider from '@mui/material/Divider';
import IconButton from '@mui/material/IconButton';
import List from '@mui/material/List';
import ListItem from '@mui/material/ListItem';
import ListItemText from '@mui/material/ListItemText';
import MenuItem from '@mui/material/MenuItem';
import Select from '@mui/material/Select';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import AddIcon from '@mui/icons-material/Add';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline';
import KeyboardArrowDownIcon from '@mui/icons-material/KeyboardArrowDown';
import KeyboardArrowUpIcon from '@mui/icons-material/KeyboardArrowUp';
import PlaylistAddCheckIcon from '@mui/icons-material/PlaylistAddCheck';

import BundlingRuleWarning from '@/components/coding/BundlingRuleWarning';
import {
  useAssignMultipleCodes,
  useValidateBundling,
  type CodeAssignmentEntry,
} from '@/hooks/useMultiCodeAssignment';
import type { BundlingRuleResultDto } from '@/hooks/usePayerValidation';

// ─── Local state row ──────────────────────────────────────────────────────────

interface DraftCodeRow {
  id:             string;   // client-side key
  code_value:     string;
  code_type:      'ICD10' | 'CPT';
  description:    string;
  /** "Pending" | "Verified" | "Rejected" */
  status:         'Pending' | 'Verified' | 'Rejected';
  sequence_order: number;
}

// ─── Status badge helpers ─────────────────────────────────────────────────────

function statusChip(status: DraftCodeRow['status']) {
  const props: Record<string, { label: string; color: 'success' | 'warning' | 'error' }> = {
    Verified: { label: 'Verified',  color: 'success' },
    Pending:  { label: 'Pending',   color: 'warning' },
    Rejected: { label: 'Rejected',  color: 'error'   },
  };
  const p = props[status] ?? props.Pending;
  return (
    <Chip
      label={p.label}
      size="small"
      color={p.color}
      variant="outlined"
      sx={{ fontSize: '0.7rem', height: 20 }}
      aria-label={`Code status: ${p.label}`}
    />
  );
}

// ─── Props ────────────────────────────────────────────────────────────────────

interface MultiCodeAssignmentPanelProps {
  patientId: string;
  /** Called after successful multi-code assignment so parent can refresh its tables. */
  onAssigned?: () => void;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function MultiCodeAssignmentPanel({
  patientId,
  onAssigned,
}: MultiCodeAssignmentPanelProps) {
  const [rows,              setRows]              = useState<DraftCodeRow[]>([]);
  const [newCodeValue,      setNewCodeValue]      = useState('');
  const [newCodeType,       setNewCodeType]       = useState<'ICD10' | 'CPT'>('ICD10');
  const [newDescription,    setNewDescription]    = useState('');
  const [addError,          setAddError]          = useState<string | null>(null);
  const [submitError,       setSubmitError]       = useState<string | null>(null);
  const [submitSuccess,     setSubmitSuccess]     = useState(false);
  const [bundlingViolations, setBundlingViolations] = useState<BundlingRuleResultDto[] | undefined>(undefined);

  const { assignCodes,    isLoading: assignLoading }   = useAssignMultipleCodes(patientId);
  const { validateCodes,  isLoading: validateLoading, data:    validateData  } = useValidateBundling(patientId);

  // ── Add a draft row ────────────────────────────────────────────────────────
  const handleAddCode = useCallback(() => {
    const trimmed = newCodeValue.trim().toUpperCase();
    setAddError(null);

    if (!trimmed) {
      setAddError('Code value is required.');
      return;
    }
    if (rows.some(r => r.code_value === trimmed && r.code_type === newCodeType)) {
      setAddError(`${trimmed} (${newCodeType}) is already in the list.`);
      return;
    }

    const nextSeq = rows.length + 1;
    setRows(prev => [
      ...prev,
      {
        id:             `${trimmed}-${Date.now()}`,
        code_value:     trimmed,
        code_type:      newCodeType,
        description:    newDescription.trim() || trimmed,
        status:         'Pending',
        sequence_order: nextSeq,
      },
    ]);
    setNewCodeValue('');
    setNewDescription('');
    setBundlingViolations(undefined); // reset check on list change
  }, [newCodeValue, newCodeType, newDescription, rows]);

  // ── Remove a row ──────────────────────────────────────────────────────────
  const handleRemove = useCallback((id: string) => {
    setRows(prev => {
      const updated = prev.filter(r => r.id !== id);
      return updated.map((r, i) => ({ ...r, sequence_order: i + 1 }));
    });
    setBundlingViolations(undefined);
  }, []);

  // ── Mark as verified ──────────────────────────────────────────────────────
  const handleVerify = useCallback((id: string) => {
    setRows(prev => prev.map(r => r.id === id ? { ...r, status: 'Verified' } : r));
  }, []);

  // ── Reorder ───────────────────────────────────────────────────────────────
  const handleMove = useCallback((id: string, direction: 'up' | 'down') => {
    setRows(prev => {
      const idx = prev.findIndex(r => r.id === id);
      if (idx < 0) return prev;
      const target = direction === 'up' ? idx - 1 : idx + 1;
      if (target < 0 || target >= prev.length) return prev;
      const next = [...prev];
      [next[idx], next[target]] = [next[target], next[idx]];
      return next.map((r, i) => ({ ...r, sequence_order: i + 1 }));
    });
    setBundlingViolations(undefined);
  }, []);

  // ── Validate bundling ─────────────────────────────────────────────────────
  const handleValidateBundling = useCallback(async () => {
    if (rows.length < 2) return;
    const violations = await validateCodes(rows.map(r => r.code_value));
    setBundlingViolations(violations?.bundling_violations ?? []);
  }, [rows, validateCodes]);

  // ── Submit assignment ─────────────────────────────────────────────────────
  const handleSubmit = useCallback(async () => {
    if (rows.length === 0) return;
    setSubmitError(null);
    setSubmitSuccess(false);

    try {
      const entries: CodeAssignmentEntry[] = rows.map(r => ({
        code_value:     r.code_value,
        code_type:      r.code_type,
        description:    r.description,
        sequence_order: r.sequence_order,
      }));

      await assignCodes({ patient_id: patientId, codes: entries });
      setRows([]);
      setBundlingViolations(undefined);
      setSubmitSuccess(true);
      onAssigned?.();
    } catch {
      setSubmitError('Failed to assign codes. Please try again.');
    }
  }, [rows, patientId, assignCodes, onAssigned]);

  const allVerified   = rows.length > 0 && rows.every(r => r.status === 'Verified');
  const canValidate   = rows.length >= 2;

  return (
    <Box
      component="section"
      aria-labelledby="multi-code-panel-heading"
      sx={{
        border:       '1px solid',
        borderColor:  'divider',
        borderRadius: 2,
        p:            2,
        mb:           3,
        bgcolor:      'background.paper',
      }}
    >
      <Typography id="multi-code-panel-heading" variant="h6" component="h2" fontWeight={600} sx={{ mb: 2 }}>
        Multi-Code Assignment
        <Typography component="span" variant="caption" color="text.secondary" sx={{ ml: 1 }}>
          Add multiple billable diagnosis/procedure codes for this encounter
        </Typography>
      </Typography>

      {/* ── Add code row ── */}
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ mb: 2 }} alignItems="flex-start">
        <Select
          size="small"
          value={newCodeType}
          onChange={e => setNewCodeType(e.target.value as 'ICD10' | 'CPT')}
          sx={{ minWidth: 90 }}
          inputProps={{ 'aria-label': 'Code type' }}
        >
          <MenuItem value="ICD10">ICD-10</MenuItem>
          <MenuItem value="CPT">CPT</MenuItem>
        </Select>

        <TextField
          size="small"
          placeholder="Code (e.g. E11.9)"
          value={newCodeValue}
          onChange={e => { setNewCodeValue(e.target.value); setAddError(null); }}
          error={Boolean(addError)}
          inputProps={{
            'aria-label':    'New code value',
            style:           { fontFamily: 'monospace', textTransform: 'uppercase' },
          }}
          sx={{ width: 140 }}
          onKeyDown={e => { if (e.key === 'Enter') handleAddCode(); }}
        />

        <TextField
          size="small"
          placeholder="Description (optional)"
          value={newDescription}
          onChange={e => setNewDescription(e.target.value)}
          inputProps={{ 'aria-label': 'Code description' }}
          sx={{ flex: 1 }}
        />

        <Button
          variant="contained"
          size="small"
          startIcon={<AddIcon />}
          onClick={handleAddCode}
          aria-label="Add code to assignment list"
          sx={{ whiteSpace: 'nowrap' }}
        >
          Add Code
        </Button>
      </Stack>

      {addError && (
        <Alert severity="error" sx={{ mb: 1 }} role="alert">
          {addError}
        </Alert>
      )}

      {/* ── Code list ── */}
      {rows.length === 0 ? (
        <Box
          sx={{
            py: 3,
            textAlign: 'center',
            color: 'text.secondary',
            bgcolor: 'grey.50',
            borderRadius: 1,
            border: '1px dashed',
            borderColor: 'divider',
          }}
          role="status"
        >
          <Typography variant="body2">No codes added yet. Add at least one ICD-10 or CPT code above.</Typography>
        </Box>
      ) : (
        <List disablePadding aria-label="Multi-code assignment list">
          {rows.map((row, idx) => (
            <ListItem
              key={row.id}
              divider={idx < rows.length - 1}
              sx={{ px: 0, py: 0.75 }}
              secondaryAction={
                <Box sx={{ display: 'flex', gap: 0.5, alignItems: 'center' }}>
                  {/* Verify */}
                  {row.status === 'Pending' && (
                    <Tooltip title="Mark as verified">
                      <IconButton
                        size="small"
                        color="success"
                        onClick={() => handleVerify(row.id)}
                        aria-label={`Verify code ${row.code_value}`}
                      >
                        <CheckCircleOutlineIcon fontSize="small" />
                      </IconButton>
                    </Tooltip>
                  )}

                  {/* Move up */}
                  <Tooltip title="Move up (higher billing priority)">
                    <span>
                      <IconButton
                        size="small"
                        disabled={idx === 0}
                        onClick={() => handleMove(row.id, 'up')}
                        aria-label={`Move ${row.code_value} up`}
                      >
                        <KeyboardArrowUpIcon fontSize="small" />
                      </IconButton>
                    </span>
                  </Tooltip>

                  {/* Move down */}
                  <Tooltip title="Move down (lower billing priority)">
                    <span>
                      <IconButton
                        size="small"
                        disabled={idx === rows.length - 1}
                        onClick={() => handleMove(row.id, 'down')}
                        aria-label={`Move ${row.code_value} down`}
                      >
                        <KeyboardArrowDownIcon fontSize="small" />
                      </IconButton>
                    </span>
                  </Tooltip>

                  {/* Remove */}
                  <Tooltip title="Remove">
                    <IconButton
                      size="small"
                      color="error"
                      onClick={() => handleRemove(row.id)}
                      aria-label={`Remove code ${row.code_value}`}
                    >
                      <DeleteOutlineIcon fontSize="small" />
                    </IconButton>
                  </Tooltip>
                </Box>
              }
            >
              <ListItemText
                primary={
                  <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
                    <Typography variant="caption" color="text.secondary" sx={{ minWidth: 20 }}>
                      #{row.sequence_order}
                    </Typography>
                    <Chip
                      label={row.code_value}
                      size="small"
                      variant="outlined"
                      sx={{ fontFamily: 'monospace', fontWeight: 700, fontSize: '0.8125rem' }}
                    />
                    <Chip
                      label={row.code_type}
                      size="small"
                      color="default"
                      variant="outlined"
                      sx={{ fontSize: '0.65rem', height: 18 }}
                    />
                    {statusChip(row.status)}
                  </Box>
                }
                secondary={
                  <Typography variant="caption" color="text.secondary">
                    {row.description}
                  </Typography>
                }
              />
            </ListItem>
          ))}
        </List>
      )}

      {/* ── Bundling check result ── */}
      {(bundlingViolations !== undefined || validateData) && (
        <Box sx={{ mt: 2 }}>
          <BundlingRuleWarning
            violations={bundlingViolations ?? validateData?.bundling_violations}
          />
        </Box>
      )}

      {submitError && (
        <Alert severity="error" sx={{ mt: 1 }} role="alert">{submitError}</Alert>
      )}
      {submitSuccess && (
        <Alert severity="success" sx={{ mt: 1 }} role="status">
          Codes assigned successfully. Individual verification is now required for each code.
        </Alert>
      )}

      {rows.length > 0 && (
        <>
          <Divider sx={{ my: 2 }} />
          <Box sx={{ display: 'flex', gap: 1, justifyContent: 'flex-end', flexWrap: 'wrap' }}>
            <Button
              variant="outlined"
              size="small"
              startIcon={validateLoading ? <CircularProgress size={14} /> : <PlaylistAddCheckIcon />}
              onClick={handleValidateBundling}
              disabled={!canValidate || validateLoading}
              aria-label="Validate complete code set against bundling rules"
            >
              Check Bundling Rules
            </Button>
            <Button
              variant="contained"
              size="small"
              color={allVerified ? 'success' : 'primary'}
              onClick={handleSubmit}
              disabled={assignLoading || rows.length === 0}
              startIcon={assignLoading ? <CircularProgress size={14} color="inherit" /> : undefined}
              aria-label="Submit multi-code assignment"
            >
              {allVerified ? 'Submit All Verified Codes' : 'Submit Assignment'}
            </Button>
          </Box>
        </>
      )}
    </Box>
  );
}
