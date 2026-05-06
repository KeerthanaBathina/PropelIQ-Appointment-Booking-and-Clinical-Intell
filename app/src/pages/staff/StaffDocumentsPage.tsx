/**
 * StaffDocumentsPage — /staff/documents
 *
 * Patient-picker landing page for the Documents section (SCR-012).
 * Staff can search for a patient, then click to open the full document upload/list
 * page at /staff/documents/:patientId.
 *
 * Shares the same sidebar shell as StaffDashboard and PatientSearchPage.
 */

import { useDeferredValue, useState } from 'react';
import { Link as RouterLink, useNavigate } from 'react-router-dom';
import Avatar from '@mui/material/Avatar';
import Box from '@mui/material/Box';
import Breadcrumbs from '@mui/material/Breadcrumbs';
import Chip from '@mui/material/Chip';
import Divider from '@mui/material/Divider';
import InputAdornment from '@mui/material/InputAdornment';
import Link from '@mui/material/Link';
import List from '@mui/material/List';
import ListItem from '@mui/material/ListItem';
import ListItemButton from '@mui/material/ListItemButton';
import ListItemIcon from '@mui/material/ListItemIcon';
import ListItemText from '@mui/material/ListItemText';
import Paper from '@mui/material/Paper';
import Skeleton from '@mui/material/Skeleton';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import Alert from '@mui/material/Alert';
import DashboardIcon from '@mui/icons-material/Dashboard';
import DescriptionOutlinedIcon from '@mui/icons-material/DescriptionOutlined';
import FolderOpenIcon from '@mui/icons-material/FolderOpen';
import PeopleOutlinedIcon from '@mui/icons-material/PeopleOutlined';
import QueueOutlinedIcon from '@mui/icons-material/QueueOutlined';
import SearchIcon from '@mui/icons-material/Search';
import SpeedOutlinedIcon from '@mui/icons-material/SpeedOutlined';
import LastLoginBanner from '@/components/auth/LastLoginBanner';
import { usePatientSearch } from '@/hooks/usePatientSearch';
import { useAuthStore } from '@/hooks/useAuth';
import type { PatientSearchRow } from '@/hooks/usePatientSearch';

// ─── Sidebar nav ──────────────────────────────────────────────────────────────

const NAV_ITEMS = [
  { label: 'Dashboard', icon: <DashboardIcon />,            href: '/staff/dashboard'       },
  { label: 'Queue',     icon: <QueueOutlinedIcon />,        href: '/staff/queue'           },
  { label: 'Documents', icon: <DescriptionOutlinedIcon />,  href: '/staff/documents'       },
  { label: 'Patients',  icon: <PeopleOutlinedIcon />,       href: '/staff/patients/search' },
] as const;

const SIDEBAR_WIDTH = 220;

// ─── Helpers ──────────────────────────────────────────────────────────────────

function formatDob(dob: string): string {
  if (!dob) return '—';
  const d = new Date(dob);
  return isNaN(d.getTime())
    ? dob
    : new Intl.DateTimeFormat(undefined, { year: 'numeric', month: 'short', day: 'numeric' }).format(d);
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function StaffDocumentsPage() {
  const navigate = useNavigate();
  const email = useAuthStore(s => s.email);
  const avatarInitials = email
    ? email.split('@')[0].slice(0, 2).toUpperCase()
    : 'ST';

  const [term, setTerm] = useState('');
  const deferredTerm = useDeferredValue(term);

  const { data, isLoading, isError } = usePatientSearch({
    term:     deferredTerm,
    status:   'All',
    page:     1,
    pageSize: 30,
  });

  const patients: PatientSearchRow[] = data?.patients ?? [];

  function handleRowClick(patientId: string) {
    void navigate(`/staff/documents/${patientId}`);
  }

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh', bgcolor: 'grey.50' }}>

      {/* ── Sidebar ── */}
      <Box
        component="nav"
        aria-label="Staff navigation"
        sx={{
          width:         SIDEBAR_WIDTH,
          flexShrink:    0,
          bgcolor:       'background.paper',
          borderRight:   '1px solid',
          borderColor:   'divider',
          display:       { xs: 'none', md: 'flex' },
          flexDirection: 'column',
        }}
      >
        <Box sx={{ px: 3, py: 2.5, display: 'flex', alignItems: 'center', gap: 1 }}>
          <SpeedOutlinedIcon sx={{ color: 'secondary.main', fontSize: 22 }} />
          <Typography variant="h6" sx={{ color: 'secondary.main', fontWeight: 700 }}>
            UPACIP
          </Typography>
        </Box>
        <Divider />
        <List dense sx={{ pt: 1 }}>
          {NAV_ITEMS.map(({ label, icon, href }) => {
            const isActive = globalThis.location.pathname === href;
            return (
              <ListItem key={label} disablePadding>
                <ListItemButton
                  component={RouterLink}
                  to={href}
                  selected={isActive}
                  aria-current={isActive ? 'page' : undefined}
                  sx={{
                    mx: 1,
                    borderRadius: 1,
                    '&.Mui-selected': {
                      borderLeft:  '3px solid',
                      borderColor: 'secondary.main',
                      bgcolor:     'secondary.50',
                      color:       'secondary.main',
                      '& .MuiListItemIcon-root': { color: 'secondary.main' },
                    },
                  }}
                >
                  <ListItemIcon sx={{ minWidth: 36 }}>{icon}</ListItemIcon>
                  <ListItemText primary={label} />
                </ListItemButton>
              </ListItem>
            );
          })}
        </List>
      </Box>

      {/* ── Main ── */}
      <Box sx={{ flex: 1, display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>

        {/* Header */}
        <Box
          component="header"
          role="banner"
          sx={{
            display:        'flex',
            alignItems:     'center',
            justifyContent: 'space-between',
            px:             3,
            py:             1.5,
            bgcolor:        'background.paper',
            borderBottom:   '1px solid',
            borderColor:    'divider',
            gap:            2,
            flexWrap:       'wrap',
          }}
        >
          <Breadcrumbs aria-label="Breadcrumb navigation">
            <Link component={RouterLink} to="/dashboard" underline="hover" color="inherit" variant="body2">
              Home
            </Link>
            <Link component={RouterLink} to="/staff/dashboard" underline="hover" color="inherit" variant="body2">
              Dashboard
            </Link>
            <Typography variant="body2" color="text.primary" aria-current="page">
              Documents
            </Typography>
          </Breadcrumbs>

          <Avatar
            sx={{ bgcolor: 'secondary.100', color: 'secondary.800', width: 36, height: 36, fontSize: '0.85rem' }}
            aria-label={`User: ${email ?? 'Staff'}`}
          >
            {avatarInitials}
          </Avatar>
        </Box>

        <LastLoginBanner />

        {/* Content */}
        <Box component="main" role="main" sx={{ flex: 1, overflowY: 'auto', p: 3 }}>

          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, mb: 0.5 }}>
            <FolderOpenIcon sx={{ color: 'secondary.main', fontSize: 28 }} aria-hidden="true" />
            <Typography variant="h5" component="h1" fontWeight={600}>
              Patient Documents
            </Typography>
          </Box>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>
            Select a patient to view or upload clinical documents.
          </Typography>

          {/* Search bar */}
          <Paper variant="outlined" sx={{ p: 2, mb: 2 }}>
            <TextField
              fullWidth
              size="small"
              placeholder="Search by name, MRN, or phone…"
              value={term}
              onChange={e => setTerm(e.target.value)}
              InputProps={{
                startAdornment: (
                  <InputAdornment position="start">
                    <SearchIcon fontSize="small" color="action" />
                  </InputAdornment>
                ),
              }}
              inputProps={{ 'aria-label': 'Search patients' }}
            />
          </Paper>

          {/* Error */}
          {isError && (
            <Alert severity="error" sx={{ mb: 2 }}>
              Failed to load patients. Please try again.
            </Alert>
          )}

          {/* Results table */}
          <TableContainer component={Paper} variant="outlined">
            <Table size="small" aria-label="Patient list for document selection">
              <TableHead>
                <TableRow sx={{ bgcolor: 'grey.50' }}>
                  <TableCell sx={{ fontWeight: 600 }}>Patient</TableCell>
                  <TableCell sx={{ fontWeight: 600 }}>MRN</TableCell>
                  <TableCell sx={{ fontWeight: 600 }}>Date of Birth</TableCell>
                  <TableCell sx={{ fontWeight: 600 }}>Provider</TableCell>
                  <TableCell sx={{ fontWeight: 600 }}>Status</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {isLoading ? (
                  Array.from({ length: 6 }).map((_, i) => (
                    <TableRow key={i}>
                      {Array.from({ length: 5 }).map((__, j) => (
                        <TableCell key={j}><Skeleton variant="text" width={j === 0 ? 140 : 80} /></TableCell>
                      ))}
                    </TableRow>
                  ))
                ) : patients.length === 0 ? (
                  <TableRow>
                    <TableCell colSpan={5} align="center" sx={{ py: 4 }}>
                      <Typography variant="body2" color="text.secondary">
                        {deferredTerm.length >= 2
                          ? `No patients found matching "${deferredTerm}".`
                          : 'No patients found.'}
                      </Typography>
                    </TableCell>
                  </TableRow>
                ) : (
                  patients.map(p => (
                    <TableRow
                      key={p.patientId}
                      hover
                      onClick={() => handleRowClick(p.patientId)}
                      sx={{ cursor: 'pointer' }}
                      tabIndex={0}
                      onKeyDown={e => { if (e.key === 'Enter' || e.key === ' ') handleRowClick(p.patientId); }}
                      aria-label={`Open documents for ${p.fullName}`}
                    >
                      <TableCell>
                        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                          <Avatar sx={{ width: 28, height: 28, fontSize: '0.75rem', bgcolor: 'primary.100', color: 'primary.800' }}>
                            {p.fullName.split(' ').map(n => n[0]).slice(0, 2).join('').toUpperCase()}
                          </Avatar>
                          <Typography variant="body2" fontWeight={500}>{p.fullName}</Typography>
                        </Box>
                      </TableCell>
                      <TableCell>
                        <Typography variant="caption" color="text.secondary">{p.mrn}</Typography>
                      </TableCell>
                      <TableCell>
                        <Typography variant="body2">{formatDob(p.dateOfBirth)}</Typography>
                      </TableCell>
                      <TableCell>
                        <Typography variant="body2">{p.provider || '—'}</Typography>
                      </TableCell>
                      <TableCell>
                        <Chip
                          label={p.status}
                          size="small"
                          color={p.status === 'Active' ? 'success' : 'default'}
                          variant="outlined"
                          sx={{ height: 20, fontSize: '0.7rem' }}
                        />
                      </TableCell>
                    </TableRow>
                  ))
                )}
              </TableBody>
            </Table>
          </TableContainer>

          {!isLoading && patients.length > 0 && (
            <Typography variant="caption" color="text.secondary" sx={{ mt: 1, display: 'block' }}>
              {data?.total ?? patients.length} patient{(data?.total ?? patients.length) !== 1 ? 's' : ''} found
            </Typography>
          )}

        </Box>
      </Box>
    </Box>
  );
}
