/**
 * AddStaffDialog — Modal form for creating a new staff account (US_061 AC-1).
 *
 * Fields: Full Name (required), Email (required, email format), Role (Staff/Admin).
 * On submit: POST /api/admin/users — provisions account with temp password + email invite.
 * Inline validation on blur within 200ms (UXR-501).
 * Success: showToast + close dialog + invalidate staff list.
 * API duplicate-email error: field-level error on email field.
 */

import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import FormControl from '@mui/material/FormControl';
import FormHelperText from '@mui/material/FormHelperText';
import InputLabel from '@mui/material/InputLabel';
import MenuItem from '@mui/material/MenuItem';
import Select from '@mui/material/Select';
import TextField from '@mui/material/TextField';
import { useState } from 'react';
import { useToast } from '@/components/common/ToastProvider';
import { useCreateStaff } from '@/hooks/useStaffAccounts';
import type { AdminUserRole } from '@/types/staff';

// ─── Props ────────────────────────────────────────────────────────────────────

interface AddStaffDialogProps {
  open: boolean;
  onClose: () => void;
}

// ─── Form state ───────────────────────────────────────────────────────────────

interface FormFields {
  fullName: string;
  email: string;
  role: AdminUserRole;
}

interface FormErrors {
  fullName: string;
  email: string;
}

const EMPTY_FORM: FormFields = { fullName: '', email: '', role: 'Staff' };
const EMPTY_ERRORS: FormErrors = { fullName: '', email: '' };

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

// ─── Component ────────────────────────────────────────────────────────────────

export default function AddStaffDialog({ open, onClose }: AddStaffDialogProps) {
  const { mutate: create, isLoading } = useCreateStaff();
  const { showToast } = useToast();

  const [form, setForm]     = useState<FormFields>(EMPTY_FORM);
  const [errors, setErrors] = useState<FormErrors>(EMPTY_ERRORS);

  // ── Inline validation (UXR-501: on blur, <200 ms) ────────────────────────
  function validateField(field: keyof FormErrors, value: string): string {
    if (field === 'fullName') {
      return value.trim().length === 0 ? 'Full name is required' : '';
    }
    if (field === 'email') {
      if (value.trim().length === 0) return 'Email is required';
      if (!EMAIL_RE.test(value.trim()))   return 'Enter a valid email address';
    }
    return '';
  }

  function handleBlur(field: keyof FormErrors) {
    setErrors(prev => ({ ...prev, [field]: validateField(field, form[field]) }));
  }

  function validateAll(): boolean {
    const next: FormErrors = {
      fullName: validateField('fullName', form.fullName),
      email:    validateField('email',    form.email),
    };
    setErrors(next);
    return !next.fullName && !next.email;
  }

  function handleClose() {
    setForm(EMPTY_FORM);
    setErrors(EMPTY_ERRORS);
    onClose();
  }

  function handleSubmit() {
    if (!validateAll()) return;

    create(
      { fullName: form.fullName.trim(), email: form.email.trim(), role: form.role },
      {
        onSuccess: () => {
          showToast({ message: 'Staff account created and invitation sent.', severity: 'success' });
          handleClose();
        },
        onError: (err: unknown) => {
          // API may return 409 for duplicate email
          const message = (err as { message?: string })?.message ?? '';
          if (message.toLowerCase().includes('email') || message.toLowerCase().includes('duplicate')) {
            setErrors(prev => ({ ...prev, email: 'An account with this email already exists.' }));
          } else {
            showToast({ message: 'Failed to create account. Please try again.', severity: 'error' });
          }
        },
      },
    );
  }

  const isSubmitDisabled = isLoading || !form.fullName.trim() || !form.email.trim();

  return (
    <Dialog
      open={open}
      onClose={handleClose}
      maxWidth="xs"
      fullWidth
      aria-labelledby="add-staff-dialog-title"
    >
      <DialogTitle id="add-staff-dialog-title">Create Staff Account</DialogTitle>

      <DialogContent>
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2, mt: 1 }}>
          {/* Full Name */}
          <TextField
            label="Full Name"
            size="small"
            fullWidth
            required
            value={form.fullName}
            onChange={e => setForm(p => ({ ...p, fullName: e.target.value }))}
            onBlur={() => handleBlur('fullName')}
            error={!!errors.fullName}
            helperText={errors.fullName || ' '}
            inputProps={{ 'aria-label': 'Full name', maxLength: 200 }}
            autoFocus
          />

          {/* Email */}
          <TextField
            label="Email"
            type="email"
            size="small"
            fullWidth
            required
            value={form.email}
            onChange={e => {
              setForm(p => ({ ...p, email: e.target.value }));
              setErrors(prev => ({ ...prev, email: '' }));
            }}
            onBlur={() => handleBlur('email')}
            error={!!errors.email}
            helperText={errors.email || ' '}
            inputProps={{ 'aria-label': 'Email address', maxLength: 254 }}
          />

          {/* Role */}
          <FormControl size="small" fullWidth>
            <InputLabel id="add-staff-role-label">Role</InputLabel>
            <Select
              labelId="add-staff-role-label"
              value={form.role}
              label="Role"
              onChange={e => setForm(p => ({ ...p, role: e.target.value as AdminUserRole }))}
              inputProps={{ 'aria-label': 'Role' }}
            >
              <MenuItem value="Staff">Staff</MenuItem>
              <MenuItem value="Admin">Admin</MenuItem>
            </Select>
            <FormHelperText> </FormHelperText>
          </FormControl>
        </Box>
      </DialogContent>

      <DialogActions sx={{ px: 3, pb: 2 }}>
        <Button onClick={handleClose} disabled={isLoading}>
          Cancel
        </Button>
        <Button
          variant="contained"
          onClick={handleSubmit}
          disabled={isSubmitDisabled}
          aria-label="Create staff account and send email invitation"
        >
          {isLoading ? 'Creating…' : 'Create Account'}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
