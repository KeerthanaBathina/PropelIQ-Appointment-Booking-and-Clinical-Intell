/**
 * OverrideJustificationDialog — MUI Dialog for verification modify workflow (US_075 AC-3).
 *
 * Per wireframe SCR-014 justification modal and task spec:
 *   - Current AI-suggested code displayed as read-only
 *   - Autocomplete code search field for replacement code (searches ICD-10 / CPT library)
 *     via useCodeSearch with 300 ms debounce (reuses existing hook from US_049)
 *   - Multiline TextField for mandatory justification (required, min 10 chars, char counter)
 *   - "Confirm Override" action button — disabled until valid selection + justification
 *   - "Cancel" button (also bound to Escape key via MUI Dialog onClose)
 *   - HIPAA audit notice caption per wireframe
 *
 * Overrides are logged immutably in the audit trail per AC-3.
 *
 * ARIA:
 *   - Focus moves to dialog title on open (UXR-201)
 *   - Focus trap enforced by MUI Dialog (UXR-202)
 *   - Keyboard: Cancel on Escape key (UXR-203)
 */

import { useEffect, useRef, useState } from 'react';
import Alert from '@mui/material/Alert';
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
import GavelIcon from '@mui/icons-material/Gavel';
import SearchIcon from '@mui/icons-material/Search';

import { useCodeSearch } from '@/hooks/useCodeSearch';

// ─── Constants ────────────────────────────────────────────────────────────────

const MIN_JUSTIFICATION = 10;
const MAX_JUSTIFICATION = 1000;

// ─── Props ────────────────────────────────────────────────────────────────────

export interface OverrideJustificationPayload {
  newValue: string;
  justification: string;
}

interface OverrideJustificationDialogProps {
  open: boolean;
  /** Code type used to scope the search ("ICD10" | "CPT"). */
  codeType: string;
  /** AI-suggested code displayed as read-only in the header. */
  originalCodeValue: string;
  /** Human-readable description of the original code. */
  originalDescription: string;
  isSubmitting: boolean;
  submitError: string | null;
  onClose: () => void;
  onSubmit: (payload: OverrideJustificationPayload) => void;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function OverrideJustificationDialog({
  open,
  codeType,
  originalCodeValue,
  originalDescription,
  isSubmitting,
  submitError,
  onClose,
  onSubmit,
}: OverrideJustificationDialogProps) {
  const titleRef = useRef<HTMLHeadingElement>(null);

  const [searchQuery,    setSearchQuery]    = useState('');
  const [selectedCode,   setSelectedCode]   = useState<{ value: string; description: string } | null>(null);
  const [justification,  setJustification]  = useState('');
  const [touched,        setTouched]        = useState(false);

  const apiCodeType = codeType.toUpperCase() === 'CPT' ? 'cpt' : 'icd10';
  const { results, isLoading: isSearching } = useCodeSearch(searchQuery, apiCodeType);

  // Reset state each time the dialog opens
  useEffect(() => {
    if (open) {
      setSearchQuery('');
      setSelectedCode(null);
      setJustification('');
      setTouched(false);
      // Announce dialog title to screen readers after render cycle
      const id = setTimeout(() => titleRef.current?.focus(), 50);
      return () => clearTimeout(id);
    }
  }, [open]);

  const justificationError =
    touched && justification.trim().length < MIN_JUSTIFICATION;

  const canSubmit =
    selectedCode !== null &&
    justification.trim().length >= MIN_JUSTIFICATION &&
    !isSubmitting;

  function handleSubmit() {
    setTouched(true);
    if (!canSubmit || !selectedCode) return;
    onSubmit({ newValue: selectedCode.value, justification: justification.trim() });
  }

  function handleJustificationBlur() {
    setTouched(true);
  }

  return (
    <Dialog
      open={open}
      onClose={onClose}
      maxWidth="sm"
      fullWidth
      aria-labelledby="override-dialog-title"
      aria-describedby="override-dialog-desc"
    >
      <DialogTitle id="override-dialog-title" ref={titleRef} tabIndex={-1}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <GavelIcon color="warning" />
          Override Code — Justification Required
        </Box>
      </DialogTitle>

      <DialogContent dividers>
        {/* ── Original AI-suggested code (read-only) ── */}
        <Box
          sx={{
            bgcolor:      'grey.100',
            border:       '1px solid',
            borderColor:  'divider',
            borderRadius: 1,
            px:           2,
            py:           1,
            mb:           2,
          }}
        >
          <Typography variant="caption" color="text.secondary">
            AI-suggested code (read-only)
          </Typography>
          <Typography variant="body2" fontWeight={600}>
            {originalCodeValue}
          </Typography>
          {originalDescription && (
            <Typography variant="caption" color="text.secondary">
              {originalDescription}
            </Typography>
          )}
        </Box>

        {/* ── Replacement code search ── */}
        <TextField
          label="Search replacement code"
          value={searchQuery}
          onChange={(e) => {
            setSearchQuery(e.target.value);
            setSelectedCode(null);
          }}
          fullWidth
          size="small"
          InputProps={{
            startAdornment: (
              <InputAdornment position="start">
                {isSearching ? (
                  <CircularProgress size={16} />
                ) : (
                  <SearchIcon fontSize="small" />
                )}
              </InputAdornment>
            ),
          }}
          placeholder="e.g. E11 or Type 2 Diabetes"
          aria-label="Search replacement ICD-10 or CPT code"
          sx={{ mb: 1 }}
        />

        {/* ── Search results dropdown ── */}
        {results.length > 0 && !selectedCode && (
          <Paper
            variant="outlined"
            sx={{ maxHeight: 180, overflowY: 'auto', mb: 2, borderRadius: 1 }}
            role="listbox"
            aria-label="Code search results"
          >
            <List dense disablePadding>
              {results.map((r) => (
                <ListItemButton
                  key={r.code_value}
                  role="option"
                  aria-selected={selectedCode !== null && (selectedCode as { value: string }).value === r.code_value}
                  onClick={() => {
                    setSelectedCode({ value: r.code_value, description: r.description });
                    setSearchQuery(r.code_value);
                  }}
                >
                  <ListItemText
                    primary={r.code_value}
                    secondary={r.description}
                    primaryTypographyProps={{ fontWeight: 600, variant: 'body2' }}
                    secondaryTypographyProps={{ variant: 'caption' }}
                  />
                </ListItemButton>
              ))}
            </List>
          </Paper>
        )}

        {/* ── Selected code confirmation ── */}
        {selectedCode && (
          <Box
            sx={{
              bgcolor:      'success.50',
              border:       '1px solid',
              borderColor:  'success.light',
              borderRadius: 1,
              px:           2,
              py:           1,
              mb: 2,
            }}
          >
            <Typography variant="caption" color="success.dark" fontWeight={600}>
              Selected replacement
            </Typography>
            <Typography variant="body2" fontWeight={700} color="success.dark">
              {selectedCode.value}
            </Typography>
            <Typography variant="caption" color="text.secondary">
              {selectedCode.description}
            </Typography>
          </Box>
        )}

        <Divider sx={{ my: 1.5 }} />

        {/* ── Justification textarea ── */}
        <TextField
          id="override-justification"
          label={
            <Box component="span">
              Justification{' '}
              <Box component="span" sx={{ color: 'error.main' }}>*</Box>
            </Box>
          }
          value={justification}
          onChange={(e) => setJustification(e.target.value)}
          onBlur={handleJustificationBlur}
          multiline
          rows={4}
          fullWidth
          required
          inputProps={{ maxLength: MAX_JUSTIFICATION, 'aria-required': true }}
          error={justificationError}
          helperText={
            justificationError
              ? `Justification must be at least ${MIN_JUSTIFICATION} characters.`
              : `${justification.length}/${MAX_JUSTIFICATION} characters`
          }
          placeholder="Provide clinical justification for overriding the AI-suggested code…"
          sx={{ mb: 1.5 }}
        />

        <Typography variant="caption" color="text.secondary" id="override-dialog-desc">
          Overrides are logged in the audit trail per HIPAA compliance requirements.
        </Typography>

        {/* ── Submit error ── */}
        {submitError && (
          <Alert severity="error" sx={{ mt: 1.5 }} role="alert">
            {submitError}
          </Alert>
        )}
      </DialogContent>

      <DialogActions sx={{ px: 3, py: 2, gap: 1 }}>
        <Button
          variant="outlined"
          onClick={onClose}
          disabled={isSubmitting}
          aria-label="Cancel override"
        >
          Cancel
        </Button>
        <Button
          variant="contained"
          color="warning"
          onClick={handleSubmit}
          disabled={!canSubmit}
          startIcon={isSubmitting ? <CircularProgress size={16} color="inherit" /> : <GavelIcon />}
          aria-label="Confirm code override"
        >
          {isSubmitting ? 'Submitting…' : 'Confirm Override'}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
