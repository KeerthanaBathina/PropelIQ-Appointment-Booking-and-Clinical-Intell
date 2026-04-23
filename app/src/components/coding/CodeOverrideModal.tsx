/**
 * CodeOverrideModal — MUI Dialog for staff to override an AI-suggested code
 * (US_049 AC-3, FR-049).
 *
 * Features:
 *   - Code search TextField with 300 ms debounce via useCodeSearch
 *   - Search results list (code value + description)
 *   - Mandatory justification field (minimum 10 characters) with char counter
 *   - Accepts optional prefillCode prop to pre-populate search from a
 *     DeprecatedCodeAlert replacement chip click
 *   - "Save Override" disabled until a code is selected and justification ≥ 10 chars
 *
 * Overrides are logged immutably in the audit trail per HIPAA requirements (AC-4).
 */

import { useEffect, useState } from 'react';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Divider from '@mui/material/Divider';
import InputAdornment from '@mui/material/InputAdornment';
import List from '@mui/material/List';
import ListItemButton from '@mui/material/ListItemButton';
import ListItemText from '@mui/material/ListItemText';
import Paper from '@mui/material/Paper';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import SearchIcon from '@mui/icons-material/Search';

import { useCodeSearch } from '@/hooks/useCodeSearch';

// ─── Constants ────────────────────────────────────────────────────────────────

const MIN_JUSTIFICATION = 10;

// ─── Props ────────────────────────────────────────────────────────────────────

export interface OverrideSubmitParams {
  newCodeValue:   string;
  newDescription: string;
  justification:  string;
}

interface CodeOverrideModalProps {
  open:              boolean;
  /** Code type string used to scope code search ("ICD10" | "CPT"). */
  codeType:          string;
  /** Original AI-suggested code value displayed in the modal header. */
  originalCodeValue: string;
  /** Optional replacement code value to pre-fill search (from DeprecatedCodeAlert). */
  prefillCode?:      string;
  onClose:           () => void;
  onSubmit:          (params: OverrideSubmitParams) => void;
  isSubmitting:      boolean;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function CodeOverrideModal({
  open,
  codeType,
  originalCodeValue,
  prefillCode,
  onClose,
  onSubmit,
  isSubmitting,
}: CodeOverrideModalProps) {
  const [searchQuery,   setSearchQuery]   = useState('');
  const [selectedCode,  setSelectedCode]  = useState<{ value: string; description: string } | null>(null);
  const [justification, setJustification] = useState('');
  const [touched,       setTouched]       = useState(false);

  // Map code type string to API param expected by /codes/search
  const apiCodeType = codeType.toUpperCase() === 'CPT' ? 'cpt' : 'icd10';

  const { results, isLoading: isSearching } = useCodeSearch(searchQuery, apiCodeType);

  // Pre-fill from replacement chip click
  useEffect(() => {
    if (open && prefillCode) {
      setSearchQuery(prefillCode);
    }
  }, [open, prefillCode]);

  // Reset state each time the modal opens
  useEffect(() => {
    if (open) {
      setSearchQuery(prefillCode ?? '');
      setSelectedCode(null);
      setJustification('');
      setTouched(false);
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  const justError   = touched && justification.trim().length < MIN_JUSTIFICATION;
  const canSubmit   = selectedCode !== null && justification.trim().length >= MIN_JUSTIFICATION;
  const showResults = results.length > 0 && !selectedCode && searchQuery.trim().length >= 2;

  function handleSubmit() {
    setTouched(true);
    if (!canSubmit || !selectedCode) return;
    onSubmit({
      newCodeValue:   selectedCode.value,
      newDescription: selectedCode.description,
      justification:  justification.trim(),
    });
  }

  function handleSelectCode(value: string, description: string) {
    setSelectedCode({ value, description });
    setSearchQuery(value);
  }

  return (
    <Dialog
      open={open}
      onClose={isSubmitting ? undefined : onClose}
      maxWidth="sm"
      fullWidth
      aria-labelledby="override-modal-title"
      aria-describedby="override-modal-desc"
    >
      <DialogTitle id="override-modal-title">
        Override Code — Justification Required
        <Typography
          variant="body2"
          color="text.secondary"
          sx={{ mt: 0.5, fontWeight: 400 }}
          id="override-modal-desc"
        >
          Original AI-suggested code: <strong>{originalCodeValue}</strong>
        </Typography>
      </DialogTitle>

      <DialogContent dividers>
        {/* ── Code search ── */}
        <TextField
          label="Search replacement code"
          value={searchQuery}
          onChange={e => {
            setSearchQuery(e.target.value);
            setSelectedCode(null);
          }}
          fullWidth
          size="small"
          placeholder={`Enter ${codeType} code or description…`}
          InputProps={{
            startAdornment: (
              <InputAdornment position="start">
                <SearchIcon fontSize="small" />
              </InputAdornment>
            ),
            endAdornment: isSearching
              ? <InputAdornment position="end"><CircularProgress size={16} /></InputAdornment>
              : null,
          }}
          sx={{ mb: 1 }}
          aria-label="Search for a replacement code"
          autoComplete="off"
        />

        {/* ── Selected code indicator ── */}
        {selectedCode && (
          <Box
            sx={{
              mb:          1.5,
              p:           1.5,
              bgcolor:     'success.50',
              borderRadius: 1,
              border:      '1px solid',
              borderColor: 'success.200',
              display:     'flex',
              alignItems:  'center',
              gap:         1,
            }}
            role="status"
            aria-label={`Selected replacement code: ${selectedCode.value}`}
          >
            <CheckCircleIcon fontSize="small" sx={{ color: 'success.main' }} />
            <Box>
              <Typography variant="body2" fontWeight={700}>{selectedCode.value}</Typography>
              <Typography variant="caption" color="text.secondary">{selectedCode.description}</Typography>
            </Box>
          </Box>
        )}

        {/* ── Search results dropdown ── */}
        {showResults && (
          <Paper
            variant="outlined"
            sx={{ mb: 2, maxHeight: 200, overflow: 'auto' }}
            role="listbox"
            aria-label="Code search results"
          >
            <List dense disablePadding>
              {results.map((result, idx) => (
                <Box key={result.code_value}>
                  <ListItemButton
                    onClick={() => handleSelectCode(result.code_value, result.description)}
                    role="option"
                    aria-label={`Select ${result.code_value}: ${result.description}`}
                  >
                    <ListItemText
                      primary={
                        <Typography variant="body2" fontWeight={600}>
                          {result.code_value}
                        </Typography>
                      }
                      secondary={
                        <Typography variant="caption" color="text.secondary">
                          {result.description}
                          {result.category ? ` — ${result.category}` : ''}
                        </Typography>
                      }
                    />
                  </ListItemButton>
                  {idx < results.length - 1 && <Divider />}
                </Box>
              ))}
            </List>
          </Paper>
        )}

        {/* ── Justification field ── */}
        <TextField
          label={
            <Box component="span">
              Justification{' '}
              <Box component="span" sx={{ color: 'error.main' }} aria-hidden="true">*</Box>
            </Box>
          }
          value={justification}
          onChange={e => {
            setJustification(e.target.value);
            setTouched(true);
          }}
          fullWidth
          multiline
          minRows={4}
          required
          error={justError}
          helperText={
            justError
              ? `Minimum ${MIN_JUSTIFICATION} characters required (${justification.length} entered)`
              : `${justification.length} characters entered`
          }
          placeholder="Provide clinical justification for overriding the AI-suggested code…"
          inputProps={{ 'aria-required': true, 'aria-describedby': 'justification-hint' }}
          sx={{ mt: 0.5 }}
        />

        <Typography
          id="justification-hint"
          variant="caption"
          color="text.secondary"
          sx={{ mt: 1, display: 'block' }}
        >
          Overrides are logged in the audit trail per HIPAA compliance requirements.
        </Typography>
      </DialogContent>

      <DialogActions sx={{ px: 3, py: 2 }}>
        <Button onClick={onClose} disabled={isSubmitting} color="inherit">
          Cancel
        </Button>
        <Button
          variant="contained"
          color="warning"
          onClick={handleSubmit}
          disabled={isSubmitting || !canSubmit}
          aria-label="Save code override"
          startIcon={isSubmitting ? <CircularProgress size={16} color="inherit" /> : undefined}
        >
          {isSubmitting ? 'Saving…' : 'Save Override'}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
