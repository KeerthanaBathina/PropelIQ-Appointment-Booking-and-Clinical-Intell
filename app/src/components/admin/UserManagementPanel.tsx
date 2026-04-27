/**
 * UserManagementPanel — Admin user list for SCR-015 (US_058 AC-3).
 *
 * Each row: Avatar (initials) + fullName + roleSubtitle + status badge + Switch toggle.
 * Inactive rows rendered at 60% opacity (wireframe spec).
 * Deactivation confirmation Dialog (UXR-102).
 * "+ Invite User" button opens basic invite dialog.
 * UXR-502: Skeleton loading with dummy rows.
 */

import Avatar from '@mui/material/Avatar';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Divider from '@mui/material/Divider';
import FormControl from '@mui/material/FormControl';
import InputLabel from '@mui/material/InputLabel';
import MenuItem from '@mui/material/MenuItem';
import Select from '@mui/material/Select';
import Skeleton from '@mui/material/Skeleton';
import Switch from '@mui/material/Switch';
import TextField from '@mui/material/TextField';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import { useState } from 'react';
import { useAdminUsers, useInviteUser, useSetUserStatus } from '@/hooks/useAdminUsers';
import type { AdminUser, AdminUserStatus, InviteUserRequest } from '@/types/adminConfig';

// ─── Helpers ─────────────────────────────────────────────────────────────────

function initials(name: string): string {
  return name
    .split(' ')
    .map(p => p[0] ?? '')
    .slice(0, 2)
    .join('')
    .toUpperCase();
}

// ─── Invite Dialog ────────────────────────────────────────────────────────────

interface InviteDialogProps {
  open: boolean;
  onClose: () => void;
}

function InviteDialog({ open, onClose }: InviteDialogProps) {
  const { mutate: invite, isLoading } = useInviteUser();
  const [form, setForm] = useState<InviteUserRequest>({ email: '', role: 'Staff', fullName: '' });
  const [emailError, setEmailError] = useState('');

  function handleSubmit() {
    if (!form.email.includes('@')) {
      setEmailError('Enter a valid email address');
      return;
    }
    invite(form, { onSuccess: onClose });
  }

  return (
    <Dialog open={open} onClose={onClose} maxWidth="xs" fullWidth aria-labelledby="invite-dialog-title">
      <DialogTitle id="invite-dialog-title">Invite New User</DialogTitle>
      <DialogContent>
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2, mt: 1 }}>
          <TextField
            label="Full Name"
            size="small"
            fullWidth
            value={form.fullName}
            onChange={e => setForm(p => ({ ...p, fullName: e.target.value }))}
            required
            inputProps={{ 'aria-label': 'Full name' }}
          />
          <TextField
            label="Email"
            type="email"
            size="small"
            fullWidth
            value={form.email}
            onChange={e => { setForm(p => ({ ...p, email: e.target.value })); setEmailError(''); }}
            error={!!emailError}
            helperText={emailError}
            required
            inputProps={{ 'aria-label': 'Email address' }}
          />
          <FormControl size="small" fullWidth>
            <InputLabel id="role-label">Role</InputLabel>
            <Select
              labelId="role-label"
              value={form.role}
              label="Role"
              onChange={e => setForm(p => ({ ...p, role: e.target.value as InviteUserRequest['role'] }))}
            >
              <MenuItem value="Admin">Admin</MenuItem>
              <MenuItem value="Staff">Staff</MenuItem>
            </Select>
          </FormControl>
        </Box>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={isLoading}>Cancel</Button>
        <Button variant="contained" onClick={handleSubmit} disabled={isLoading || !form.email || !form.fullName}>
          {isLoading ? 'Sending…' : 'Send Invite'}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

// ─── Deactivation Confirmation Dialog ────────────────────────────────────────

interface ConfirmDialogProps {
  user: AdminUser | null;
  onConfirm: () => void;
  onClose: () => void;
}

function ConfirmDeactivateDialog({ user, onConfirm, onClose }: ConfirmDialogProps) {
  return (
    <Dialog open={!!user} onClose={onClose} maxWidth="xs" fullWidth aria-labelledby="confirm-dialog-title">
      <DialogTitle id="confirm-dialog-title">Deactivate User</DialogTitle>
      <DialogContent>
        <Typography>
          Are you sure you want to deactivate <strong>{user?.fullName}</strong>? They will no longer be
          able to log in.
        </Typography>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" color="error" onClick={onConfirm}>
          Deactivate
        </Button>
      </DialogActions>
    </Dialog>
  );
}

// ─── User Row ─────────────────────────────────────────────────────────────────

interface UserRowProps {
  user: AdminUser;
  onToggle: (user: AdminUser) => void;
}

function UserRow({ user, onToggle }: UserRowProps) {
  const isActive = user.status === 'Active';
  return (
    <Box
      sx={{
        display:        'flex',
        alignItems:     'center',
        gap:            2,
        py:             1.5,
        opacity:        isActive ? 1 : 0.6,
        '&:not(:last-child)': { borderBottom: '1px solid', borderColor: 'divider' },
      }}
      aria-label={`${user.fullName}, ${user.role}, ${user.status}`}
    >
      {/* Avatar */}
      <Avatar
        sx={{ width: 38, height: 38, bgcolor: 'error.100', color: 'error.800', fontSize: '0.875rem', flexShrink: 0 }}
        aria-hidden="true"
      >
        {initials(user.fullName)}
      </Avatar>

      {/* Name + role */}
      <Box sx={{ flex: 1, minWidth: 0 }}>
        <Typography variant="body2" fontWeight={600} noWrap>{user.fullName}</Typography>
        <Typography variant="caption" color="text.secondary" noWrap>{user.roleSubtitle ?? user.role}</Typography>
      </Box>

      {/* Status badge */}
      <Chip
        label={user.status}
        size="small"
        color={isActive ? 'success' : 'default'}
        variant="outlined"
        sx={{ flexShrink: 0 }}
      />

      {/* Active/Inactive toggle */}
      <Tooltip title={isActive ? 'Deactivate user' : 'Reactivate user'}>
        <Switch
          checked={isActive}
          onChange={() => onToggle(user)}
          size="small"
          inputProps={{ 'aria-label': `${isActive ? 'Deactivate' : 'Reactivate'} ${user.fullName}` }}
        />
      </Tooltip>
    </Box>
  );
}

// ─── Main Panel ───────────────────────────────────────────────────────────────

export default function UserManagementPanel() {
  const { data, isLoading } = useAdminUsers();
  const { mutate: setStatus } = useSetUserStatus();

  const [pendingUser,  setPendingUser]  = useState<AdminUser | null>(null);   // UXR-102 confirm dialog
  const [inviteOpen,   setInviteOpen]   = useState(false);

  function handleToggle(user: AdminUser) {
    if (user.status === 'Active') {
      // Deactivation needs confirmation (UXR-102)
      setPendingUser(user);
    } else {
      // Reactivation is safe — no confirmation needed
      setStatus({ id: user.id, status: 'Active' as AdminUserStatus });
    }
  }

  function handleConfirmDeactivate() {
    if (pendingUser) {
      setStatus({ id: pendingUser.id, status: 'Inactive' as AdminUserStatus });
    }
    setPendingUser(null);
  }

  return (
    <Box>
      {/* Header */}
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
        <Typography variant="subtitle1" fontWeight={600}>
          User Management
        </Typography>
        <Button
          size="small"
          variant="contained"
          onClick={() => setInviteOpen(true)}
          aria-label="Invite new user"
        >
          + Invite User
        </Button>
      </Box>

      <Divider sx={{ mb: 1 }} />

      {/* List */}
      {isLoading ? (
        <Box>
          {[0, 1, 2, 3].map(i => (
            <Box key={i} sx={{ display: 'flex', alignItems: 'center', gap: 2, py: 1.5 }}>
              <Skeleton variant="circular" width={38} height={38} />
              <Skeleton variant="text" width={140} height={20} />
              <Box sx={{ flex: 1 }} />
              <Skeleton variant="rectangular" width={60} height={22} sx={{ borderRadius: 1 }} />
              <Skeleton variant="rectangular" width={36} height={22} sx={{ borderRadius: 1 }} />
            </Box>
          ))}
        </Box>
      ) : (
        (data?.users ?? []).map(u => (
          <UserRow key={u.id} user={u} onToggle={handleToggle} />
        ))
      )}

      {/* Deactivate confirmation dialog (UXR-102) */}
      <ConfirmDeactivateDialog
        user={pendingUser}
        onConfirm={handleConfirmDeactivate}
        onClose={() => setPendingUser(null)}
      />

      {/* Invite dialog */}
      <InviteDialog open={inviteOpen} onClose={() => setInviteOpen(false)} />
    </Box>
  );
}
