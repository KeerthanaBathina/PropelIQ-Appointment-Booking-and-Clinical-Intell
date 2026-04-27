/**
 * StaffAccountList — Staff data table for SCR-015 Users tab (US_061 AC-1 – AC-4).
 *
 * Columns: Name, Email, Role (Chip), Status (badge), Last Login, Creation Date.
 * Filters: full-text search (name/email) + Role Select + Status Select.
 * Actions: Deactivate (confirmation dialog) | Reactivate (immediate, UXR-102).
 * 5 screen states: Default, Loading (Skeleton), Empty, Error (retry), Validation.
 *
 * Responsive:
 *   ≥md: horizontal table layout
 *   <md: card-style rows (UXR-303)
 * UXR-403: error.main accent for admin UI.
 * EC-1: self-deactivation guard delegated to DeactivateConfirmDialog.
 * EC-2: last-admin guard surfaced via API error in DeactivateConfirmDialog.
 */

import Avatar from '@mui/material/Avatar';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Divider from '@mui/material/Divider';
import FormControl from '@mui/material/FormControl';
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
import TextField from '@mui/material/TextField';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import PersonOffOutlinedIcon from '@mui/icons-material/PersonOffOutlined';
import PersonAddAltOutlinedIcon from '@mui/icons-material/PersonAddAltOutlined';
import ReplayIcon from '@mui/icons-material/Replay';
import useMediaQuery from '@mui/material/useMediaQuery';
import { useTheme } from '@mui/material/styles';
import { useState, useMemo } from 'react';
import { useToast } from '@/components/common/ToastProvider';
import { useStaffAccounts, useReactivateStaff } from '@/hooks/useStaffAccounts';
import AddStaffDialog from './AddStaffDialog';
import DeactivateConfirmDialog from './DeactivateConfirmDialog';
import type { StaffAccount, AdminUserRole, AdminUserStatus, StaffListFilter } from '@/types/staff';

// ─── Helpers ──────────────────────────────────────────────────────────────────

function initials(name: string): string {
  return name
    .split(' ')
    .map(p => p[0] ?? '')
    .slice(0, 2)
    .join('')
    .toUpperCase();
}

function formatDate(iso: string | null): string {
  if (!iso) return '—';
  try {
    return new Intl.DateTimeFormat('en-US', { dateStyle: 'medium' }).format(new Date(iso));
  } catch {
    return '—';
  }
}

// ─── Loading skeleton ─────────────────────────────────────────────────────────

function LoadingSkeleton() {
  return (
    <TableBody aria-label="Loading staff accounts">
      {[0, 1, 2, 3, 4].map(i => (
        <TableRow key={i}>
          <TableCell>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
              <Skeleton variant="circular" width={36} height={36} />
              <Skeleton variant="text" width={130} height={18} />
            </Box>
          </TableCell>
          <TableCell><Skeleton variant="text" width={160} height={18} /></TableCell>
          <TableCell><Skeleton variant="rectangular" width={60} height={22} sx={{ borderRadius: 4 }} /></TableCell>
          <TableCell><Skeleton variant="rectangular" width={70} height={22} sx={{ borderRadius: 4 }} /></TableCell>
          <TableCell><Skeleton variant="text" width={100} height={18} /></TableCell>
          <TableCell><Skeleton variant="text" width={90}  height={18} /></TableCell>
          <TableCell><Skeleton variant="rectangular" width={80} height={28} sx={{ borderRadius: 1 }} /></TableCell>
        </TableRow>
      ))}
    </TableBody>
  );
}

// ─── Empty state ──────────────────────────────────────────────────────────────

interface EmptyStateProps { hasFilter: boolean; onClear: () => void; onAdd: () => void; }
function EmptyState({ hasFilter, onClear, onAdd }: EmptyStateProps) {
  return (
    <TableRow>
      <TableCell colSpan={7} align="center" sx={{ py: 6 }}>
        <Typography color="text.secondary" gutterBottom>
          {hasFilter ? 'No staff accounts match the current filters.' : 'No staff accounts found.'}
        </Typography>
        {hasFilter ? (
          <Button size="small" onClick={onClear}>Clear filters</Button>
        ) : (
          <Button size="small" variant="outlined" onClick={onAdd} aria-label="Create first staff account">
            Create Staff Account
          </Button>
        )}
      </TableCell>
    </TableRow>
  );
}

// ─── Error state ──────────────────────────────────────────────────────────────

interface ErrorStateProps { onRetry: () => void; }
function ErrorState({ onRetry }: ErrorStateProps) {
  return (
    <TableRow>
      <TableCell colSpan={7} align="center" sx={{ py: 6 }}>
        <Typography color="error" gutterBottom>
          Failed to load staff accounts.
        </Typography>
        <Button size="small" startIcon={<ReplayIcon />} onClick={onRetry}>
          Retry
        </Button>
      </TableCell>
    </TableRow>
  );
}

// ─── Staff row (desktop) ──────────────────────────────────────────────────────

interface StaffRowProps {
  account: StaffAccount;
  onDeactivate: (account: StaffAccount) => void;
  onReactivate: (id: string, name: string) => void;
  reactivating: boolean;
}

function StaffRow({ account, onDeactivate, onReactivate, reactivating }: StaffRowProps) {
  const isActive = account.status === 'Active';

  return (
    <TableRow
      sx={{ opacity: isActive ? 1 : 0.65 }}
      aria-label={`${account.fullName}, ${account.role}, ${account.status}`}
    >
      {/* Name + Avatar */}
      <TableCell>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
          <Avatar
            sx={{
              width: 36, height: 36,
              bgcolor: 'error.50', color: 'error.800', fontSize: '0.8rem',
              flexShrink: 0,
            }}
            aria-hidden="true"
          >
            {initials(account.fullName)}
          </Avatar>
          <Box sx={{ minWidth: 0 }}>
            <Typography variant="body2" fontWeight={600} noWrap>
              {account.fullName}
            </Typography>
            {account.roleSubtitle && (
              <Typography variant="caption" color="text.secondary" noWrap>
                {account.roleSubtitle}
              </Typography>
            )}
          </Box>
        </Box>
      </TableCell>

      {/* Email */}
      <TableCell>
        <Typography variant="body2" noWrap sx={{ maxWidth: 220 }}>
          {account.email}
        </Typography>
      </TableCell>

      {/* Role */}
      <TableCell>
        <Chip
          label={account.role}
          size="small"
          color={account.role === 'Admin' ? 'error' : 'default'}
          variant="outlined"
        />
      </TableCell>

      {/* Status */}
      <TableCell>
        <Chip
          label={account.status}
          size="small"
          color={isActive ? 'success' : 'default'}
          variant="filled"
        />
      </TableCell>

      {/* Last Login */}
      <TableCell>
        <Typography variant="body2" color="text.secondary">
          {formatDate(account.lastLoginAt)}
        </Typography>
      </TableCell>

      {/* Created */}
      <TableCell>
        <Typography variant="body2" color="text.secondary">
          {formatDate(account.createdAt)}
        </Typography>
      </TableCell>

      {/* Action */}
      <TableCell align="right">
        {isActive ? (
          <Tooltip title="Deactivate account">
            <Button
              size="small"
              color="error"
              variant="outlined"
              startIcon={<PersonOffOutlinedIcon />}
              onClick={() => onDeactivate(account)}
              aria-label={`Deactivate ${account.fullName}`}
            >
              Deactivate
            </Button>
          </Tooltip>
        ) : (
          <Tooltip title="Reactivate account">
            <Button
              size="small"
              color="success"
              variant="outlined"
              startIcon={<PersonAddAltOutlinedIcon />}
              onClick={() => onReactivate(account.id, account.fullName)}
              disabled={reactivating}
              aria-label={`Reactivate ${account.fullName}`}
            >
              Reactivate
            </Button>
          </Tooltip>
        )}
      </TableCell>
    </TableRow>
  );
}

// ─── Mobile card row ──────────────────────────────────────────────────────────

interface MobileCardProps {
  account: StaffAccount;
  onDeactivate: (account: StaffAccount) => void;
  onReactivate: (id: string, name: string) => void;
  reactivating: boolean;
}

function MobileCard({ account, onDeactivate, onReactivate, reactivating }: MobileCardProps) {
  const isActive = account.status === 'Active';

  return (
    <Paper
      variant="outlined"
      sx={{ p: 2, mb: 1.5, opacity: isActive ? 1 : 0.65 }}
      aria-label={`${account.fullName}, ${account.role}, ${account.status}`}
    >
      <Box sx={{ display: 'flex', alignItems: 'flex-start', gap: 1.5, mb: 1 }}>
        <Avatar
          sx={{ width: 36, height: 36, bgcolor: 'error.50', color: 'error.800', fontSize: '0.8rem', flexShrink: 0 }}
          aria-hidden="true"
        >
          {initials(account.fullName)}
        </Avatar>
        <Box sx={{ flex: 1, minWidth: 0 }}>
          <Typography variant="body2" fontWeight={600} noWrap>{account.fullName}</Typography>
          <Typography variant="caption" color="text.secondary" noWrap>{account.email}</Typography>
        </Box>
        <Chip label={account.status} size="small" color={isActive ? 'success' : 'default'} variant="filled" />
      </Box>

      <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap', mb: 1 }}>
        <Chip label={account.role} size="small" color={account.role === 'Admin' ? 'error' : 'default'} variant="outlined" />
        <Typography variant="caption" color="text.secondary">
          Last login: {formatDate(account.lastLoginAt)}
        </Typography>
        <Typography variant="caption" color="text.secondary">
          Created: {formatDate(account.createdAt)}
        </Typography>
      </Box>

      <Box sx={{ display: 'flex', justifyContent: 'flex-end' }}>
        {isActive ? (
          <Button
            size="small"
            color="error"
            variant="outlined"
            startIcon={<PersonOffOutlinedIcon />}
            onClick={() => onDeactivate(account)}
            aria-label={`Deactivate ${account.fullName}`}
          >
            Deactivate
          </Button>
        ) : (
          <Button
            size="small"
            color="success"
            variant="outlined"
            startIcon={<PersonAddAltOutlinedIcon />}
            onClick={() => onReactivate(account.id, account.fullName)}
            disabled={reactivating}
            aria-label={`Reactivate ${account.fullName}`}
          >
            Reactivate
          </Button>
        )}
      </Box>
    </Paper>
  );
}

// ─── Main component ───────────────────────────────────────────────────────────

export default function StaffAccountList() {
  const { data, isLoading, isError, refetch } = useStaffAccounts();
  const { mutate: reactivate, isLoading: reactivating } = useReactivateStaff();
  const { showToast } = useToast();

  const theme   = useTheme();
  const isMobile = useMediaQuery(theme.breakpoints.down('md'));

  // ── Dialog state ──────────────────────────────────────────────────────────
  const [addOpen, setAddOpen]           = useState(false);
  const [deactivateTarget, setDeactivateTarget] = useState<StaffAccount | null>(null);

  // ── Filter state ──────────────────────────────────────────────────────────
  const [filter, setFilter] = useState<StaffListFilter>({
    search: '',
    role:   '',
    status: '',
  });

  const hasFilter = !!(filter.search || filter.role || filter.status);

  // ── Client-side filtering ─────────────────────────────────────────────────
  const filtered = useMemo<StaffAccount[]>(() => {
    const accounts = data?.users ?? [];
    return accounts.filter(a => {
      if (filter.role   && a.role   !== filter.role)   return false;
      if (filter.status && a.status !== filter.status) return false;
      if (filter.search) {
        const q = filter.search.toLowerCase();
        if (!a.fullName.toLowerCase().includes(q) && !a.email.toLowerCase().includes(q)) return false;
      }
      return true;
    });
  }, [data, filter]);

  // ── Reactivate handler (AC-4 — no confirmation needed) ───────────────────
  function handleReactivate(id: string, name: string) {
    reactivate(id, {
      onSuccess: () => showToast({ message: `${name} has been reactivated.`, severity: 'success' }),
      onError:   () => showToast({ message: 'Failed to reactivate account. Please try again.', severity: 'error' }),
    });
  }

  function clearFilters() {
    setFilter({ search: '', role: '', status: '' });
  }

  // ── Filters bar ───────────────────────────────────────────────────────────
  const filtersBar = (
    <Box
      sx={{
        display: 'flex',
        gap: 1.5,
        flexWrap: 'wrap',
        alignItems: 'center',
        mb: 2,
      }}
    >
      <TextField
        label="Search name or email"
        size="small"
        value={filter.search}
        onChange={e => setFilter(p => ({ ...p, search: e.target.value }))}
        sx={{ minWidth: 220, flex: '1 1 220px' }}
        inputProps={{ 'aria-label': 'Search staff by name or email' }}
      />

      <FormControl size="small" sx={{ minWidth: 130 }}>
        <InputLabel id="staff-role-filter-label">Role</InputLabel>
        <Select
          labelId="staff-role-filter-label"
          value={filter.role}
          label="Role"
          onChange={e => setFilter(p => ({ ...p, role: e.target.value as AdminUserRole | '' }))}
          inputProps={{ 'aria-label': 'Filter by role' }}
        >
          <MenuItem value="">All roles</MenuItem>
          <MenuItem value="Staff">Staff</MenuItem>
          <MenuItem value="Admin">Admin</MenuItem>
        </Select>
      </FormControl>

      <FormControl size="small" sx={{ minWidth: 140 }}>
        <InputLabel id="staff-status-filter-label">Status</InputLabel>
        <Select
          labelId="staff-status-filter-label"
          value={filter.status}
          label="Status"
          onChange={e => setFilter(p => ({ ...p, status: e.target.value as AdminUserStatus | '' }))}
          inputProps={{ 'aria-label': 'Filter by status' }}
        >
          <MenuItem value="">All statuses</MenuItem>
          <MenuItem value="Active">Active</MenuItem>
          <MenuItem value="Inactive">Inactive</MenuItem>
        </Select>
      </FormControl>

      {hasFilter && (
        <Button size="small" onClick={clearFilters} aria-label="Clear all filters">
          Clear
        </Button>
      )}
    </Box>
  );

  // ── Breadcrumb ─────────────────────────────────────────────────────────────
  const breadcrumb = (
    <Typography variant="caption" color="text.secondary" sx={{ mb: 0.5, display: 'block' }}>
      Admin Dashboard &rsaquo; User Management
    </Typography>
  );

  // ── Header ─────────────────────────────────────────────────────────────────
  const header = (
    <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
      <Typography variant="subtitle1" fontWeight={600}>
        Staff Accounts
      </Typography>
      <Button
        size="small"
        variant="contained"
        onClick={() => setAddOpen(true)}
        aria-label="Create new staff account"
      >
        + Create Staff Account
      </Button>
    </Box>
  );

  return (
    <Box>
      {breadcrumb}
      {header}
      <Divider sx={{ mb: 2 }} />

      {filtersBar}

      {/* ── Mobile card layout (< md) ── */}
      {isMobile ? (
        <Box role="list" aria-label="Staff accounts">
          {isLoading ? (
            [0, 1, 2].map(i => (
              <Paper key={i} variant="outlined" sx={{ p: 2, mb: 1.5 }}>
                <Box sx={{ display: 'flex', gap: 1.5 }}>
                  <Skeleton variant="circular" width={36} height={36} />
                  <Box sx={{ flex: 1 }}>
                    <Skeleton variant="text" width="60%" height={18} />
                    <Skeleton variant="text" width="80%" height={14} />
                  </Box>
                </Box>
              </Paper>
            ))
          ) : isError ? (
            <Paper variant="outlined" sx={{ p: 4, textAlign: 'center' }}>
              <Typography color="error" gutterBottom>Failed to load staff accounts.</Typography>
              <Button size="small" startIcon={<ReplayIcon />} onClick={() => refetch()}>Retry</Button>
            </Paper>
          ) : filtered.length === 0 ? (
            <Paper variant="outlined" sx={{ p: 4, textAlign: 'center' }}>
              <Typography color="text.secondary" gutterBottom>
                {hasFilter ? 'No staff accounts match the current filters.' : 'No staff accounts found.'}
              </Typography>
              {hasFilter
                ? <Button size="small" onClick={clearFilters}>Clear filters</Button>
                : <Button size="small" variant="outlined" onClick={() => setAddOpen(true)}>Create Staff Account</Button>
              }
            </Paper>
          ) : (
            filtered.map(a => (
              <MobileCard
                key={a.id}
                account={a}
                onDeactivate={setDeactivateTarget}
                onReactivate={handleReactivate}
                reactivating={reactivating}
              />
            ))
          )}
        </Box>
      ) : (
        /* ── Desktop table layout (≥ md) ── */
        <TableContainer component={Paper} variant="outlined">
          <Table size="small" aria-label="Staff accounts table">
            <TableHead>
              <TableRow sx={{ '& th': { fontWeight: 600, bgcolor: 'grey.50' } }}>
                <TableCell>Name</TableCell>
                <TableCell>Email</TableCell>
                <TableCell>Role</TableCell>
                <TableCell>Status</TableCell>
                <TableCell>Last Login</TableCell>
                <TableCell>Created</TableCell>
                <TableCell align="right">Action</TableCell>
              </TableRow>
            </TableHead>

            {isLoading ? (
              <LoadingSkeleton />
            ) : isError ? (
              <TableBody>
                <ErrorState onRetry={() => refetch()} />
              </TableBody>
            ) : (
              <TableBody>
                {filtered.length === 0 ? (
                  <EmptyState
                    hasFilter={hasFilter}
                    onClear={clearFilters}
                    onAdd={() => setAddOpen(true)}
                  />
                ) : (
                  filtered.map(a => (
                    <StaffRow
                      key={a.id}
                      account={a}
                      onDeactivate={setDeactivateTarget}
                      onReactivate={handleReactivate}
                      reactivating={reactivating}
                    />
                  ))
                )}
              </TableBody>
            )}
          </Table>
        </TableContainer>
      )}

      {/* ── Dialogs ── */}
      <AddStaffDialog open={addOpen} onClose={() => setAddOpen(false)} />

      <DeactivateConfirmDialog
        target={deactivateTarget}
        onClose={() => setDeactivateTarget(null)}
      />
    </Box>
  );
}
