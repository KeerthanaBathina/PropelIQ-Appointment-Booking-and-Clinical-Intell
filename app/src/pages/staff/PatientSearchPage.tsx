/**
 * PatientSearchPage — SCR-016 Patient Search (US_062 AC-1, AC-2).
 *
 * Route: /staff/patients/search  (query param ?q= pre-populates from StaffDashboard header search)
 *
 * Screen states (all 5):
 *   1. Default   — Empty form, no query yet; prompt staff to enter criteria
 *   2. Loading   — Skeleton rows while fetch is in-flight (UXR-502, >300 ms)
 *   3. Empty     — "No patients found" guidance card (edge case per task)
 *   4. Error     — Alert with retry action
 *   5. Validation — Inline field-level error (term too short < 2 chars)
 *
 * Layout follows wireframe-SCR-016-patient-search.html:
 *   Sidebar (UPACIP nav, Patients active) | Header (Patient Search title + avatar)
 *   Breadcrumb → "Find a Patient" heading → Search card → result count → Results table
 *   Pagination (Previous / Page N of N / Next)
 *   Empty state card (shown instead of table when results=[])
 *
 * Accessibility (WCAG 2.1 AA):
 *   - aria-label on search input, results table, pagination controls
 *   - aria-live="polite" region for result count announcements
 *   - aria-sort on sortable column headers
 *   - keyboard-navigable table rows (Enter/Space → navigate to profile)
 *   - Focus management: after search submit focus moves to result count
 *
 * Responsive (UXR-303):
 *   - Search form fields stack vertically on mobile (<768px)
 *   - Table hidden on xs; replaced by card list on xs/sm
 *   - Touch targets ≥ 44 px (UXR-304)
 *
 * Partial match highlighting (edge case):
 *   Matched characters in patient name cells are wrapped in <mark> with
 *   primary.main background so "Joh" highlights within "John" (AC-1).
 *
 * Navigation to profile (AC-2):
 *   Clicking a patient name navigates to /staff/patients/:patientId/profile
 *   (PatientProfile360Page) which renders the full 6-section consolidated profile.
 */

import {
  useCallback,
  useDeferredValue,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';
import { Link as RouterLink, useNavigate, useSearchParams } from 'react-router-dom';

import Alert from '@mui/material/Alert';
import Avatar from '@mui/material/Avatar';
import Box from '@mui/material/Box';
import Breadcrumbs from '@mui/material/Breadcrumbs';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Container from '@mui/material/Container';
import Divider from '@mui/material/Divider';
import FormControl from '@mui/material/FormControl';
import InputAdornment from '@mui/material/InputAdornment';
import InputLabel from '@mui/material/InputLabel';
import Link from '@mui/material/Link';
import List from '@mui/material/List';
import ListItem from '@mui/material/ListItem';
import ListItemButton from '@mui/material/ListItemButton';
import ListItemIcon from '@mui/material/ListItemIcon';
import ListItemText from '@mui/material/ListItemText';
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
import DashboardIcon from '@mui/icons-material/Dashboard';
import DescriptionOutlinedIcon from '@mui/icons-material/DescriptionOutlined';
import PeopleOutlinedIcon from '@mui/icons-material/PeopleOutlined';
import QueueOutlinedIcon from '@mui/icons-material/QueueOutlined';
import SearchIcon from '@mui/icons-material/Search';
import SortIcon from '@mui/icons-material/Sort';
import PersonSearchIcon from '@mui/icons-material/PersonSearch';
import ArrowUpwardIcon from '@mui/icons-material/ArrowUpward';
import ArrowDownwardIcon from '@mui/icons-material/ArrowDownward';

import { useAuthStore } from '@/hooks/useAuth';
import {
  usePatientSearch,
  useProviderList,
  type PatientSearchParams,
  type PatientSearchRow,
  type PatientStatusFilter,
} from '@/hooks/usePatientSearch';

// ─── Constants ────────────────────────────────────────────────────────────────

const SIDEBAR_WIDTH = 220;
const PAGE_SIZE     = 20;
const MIN_TERM_LEN  = 2;

const NAV_ITEMS = [
  { label: 'Dashboard', icon: <DashboardIcon />,           href: '/staff/dashboard'        },
  { label: 'Queue',     icon: <QueueOutlinedIcon />,       href: '/staff/queue'            },
  { label: 'Documents', icon: <DescriptionOutlinedIcon />, href: '/staff/documents'        },
  { label: 'Patients',  icon: <PeopleOutlinedIcon />,      href: '/staff/patients/search'  },
] as const;

// ─── Helpers ─────────────────────────────────────────────────────────────────

/** Wrap matching characters in a <mark> element for partial-match highlighting. */
function HighlightedText({ text, term }: { text: string; term: string }) {
  if (!term.trim()) return <>{text}</>;
  const idx = text.toLowerCase().indexOf(term.toLowerCase().trim());
  if (idx === -1) return <>{text}</>;
  return (
    <>
      {text.slice(0, idx)}
      <mark style={{ backgroundColor: 'rgba(33, 150, 243, 0.15)', borderRadius: 2 }}>
        {text.slice(idx, idx + term.trim().length)}
      </mark>
      {text.slice(idx + term.trim().length)}
    </>
  );
}

/** Format ISO datetime to "Jan 15, 2025" display. */
function formatDate(iso: string | null): string {
  if (!iso) return '—';
  return new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric', year: 'numeric' }).format(
    new Date(iso),
  );
}

// ─── Sub-components ───────────────────────────────────────────────────────────

/** Skeleton rows shown while the search is loading (UXR-502). */
function SkeletonRows({ count = 5 }: { count?: number }) {
  return (
    <>
      {Array.from({ length: count }).map((_, i) => (
        <TableRow key={i}>
          {Array.from({ length: 7 }).map((__, j) => (
            <TableCell key={j}>
              <Skeleton variant="text" width={j === 0 ? 140 : j === 6 ? 60 : 100} />
            </TableCell>
          ))}
        </TableRow>
      ))}
    </>
  );
}

/** Mobile-friendly card for a single patient result (shown on xs/sm). */
function PatientCard({
  row,
  term,
  onNavigate,
}: {
  row: PatientSearchRow;
  term: string;
  onNavigate: (id: string) => void;
}) {
  const isInactive = row.status === 'Inactive';
  return (
    <Paper
      variant="outlined"
      sx={{
        p: 2,
        mb: 1.5,
        opacity: isInactive ? 0.7 : 1,
        cursor: 'pointer',
        '&:hover': { boxShadow: 2 },
      }}
      onClick={() => onNavigate(row.patientId)}
      role="button"
      tabIndex={0}
      aria-label={`View profile for ${row.fullName}`}
      onKeyDown={e => {
        if (e.key === 'Enter' || e.key === ' ') onNavigate(row.patientId);
      }}
    >
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
        <Typography variant="subtitle2" color="primary.main" fontWeight={600}>
          <HighlightedText text={row.fullName} term={term} />
        </Typography>
        <Chip
          label={row.status}
          size="small"
          color={row.status === 'Active' ? 'success' : 'default'}
        />
      </Box>
      <Typography variant="caption" color="text.secondary" display="block">
        {row.mrn} · DOB {formatDate(row.dateOfBirth)} · {row.phone}
      </Typography>
      <Typography variant="caption" color="text.secondary" display="block">
        {row.provider} · Last visit {formatDate(row.lastVisitAt)}
      </Typography>
    </Paper>
  );
}

// ─── Main component ───────────────────────────────────────────────────────────

export default function PatientSearchPage() {
  const navigate        = useNavigate();
  const [searchParams]  = useSearchParams();
  const resultCountRef  = useRef<HTMLDivElement>(null);

  const email = useAuthStore(s => s.email);
  const avatarInitials = email
    ? email.split('@')[0].slice(0, 2).toUpperCase()
    : 'ST';

  // ── Form state ─────────────────────────────────────────────────────────────
  const [term,       setTerm]       = useState(() => searchParams.get('q') ?? '');
  const [provider,   setProvider]   = useState('');
  const [statusFilter, setStatus]   = useState<PatientStatusFilter>('All');
  const [page,       setPage]       = useState(1);
  const [submitted,  setSubmitted]  = useState(() => !!searchParams.get('q'));
  const [termError,  setTermError]  = useState('');

  // Sort state
  type SortCol = 'fullName' | 'dateOfBirth' | null;
  const [sortCol, setSortCol] = useState<SortCol>(null);
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>('asc');

  // Defer the search params so rapid typing doesn't fire concurrent queries
  const deferredTerm     = useDeferredValue(submitted ? term : '');
  const deferredProvider = useDeferredValue(submitted ? provider : '');
  const deferredStatus   = useDeferredValue(submitted ? statusFilter : 'All');

  const searchParamsObj: PatientSearchParams = useMemo(
    () => ({ term: deferredTerm, provider: deferredProvider, status: deferredStatus, page, pageSize: PAGE_SIZE }),
    [deferredTerm, deferredProvider, deferredStatus, page],
  );

  const { data, isLoading, isError, refetch, error } = usePatientSearch(searchParamsObj);
  const { data: providers = [] }                      = useProviderList();

  // Focus result count after query resolves (accessibility focus management)
  useEffect(() => {
    if (data && resultCountRef.current) {
      resultCountRef.current.focus();
    }
  }, [data]);

  // ── Handlers ───────────────────────────────────────────────────────────────

  const handleSubmit = useCallback(
    (e: React.FormEvent) => {
      e.preventDefault();
      setTermError('');
      if (term.trim().length > 0 && term.trim().length < MIN_TERM_LEN) {
        setTermError(`Enter at least ${MIN_TERM_LEN} characters or leave blank to browse.`);
        return;
      }
      setPage(1);
      setSubmitted(true);
    },
    [term],
  );

  const handleSort = useCallback(
    (col: SortCol) => {
      if (sortCol === col) {
        setSortDir(d => (d === 'asc' ? 'desc' : 'asc'));
      } else {
        setSortCol(col);
        setSortDir('asc');
      }
    },
    [sortCol],
  );

  const handleNavigateToProfile = useCallback(
    (patientId: string) => navigate(`/staff/patients/${patientId}/profile`),
    [navigate],
  );

  // Client-side sort of the current page results
  const sortedRows: PatientSearchRow[] = useMemo(() => {
    const rows = data?.patients ?? [];
    if (!sortCol) return rows;
    return [...rows].sort((a, b) => {
      const aVal = a[sortCol] ?? '';
      const bVal = b[sortCol] ?? '';
      const cmp = aVal < bVal ? -1 : aVal > bVal ? 1 : 0;
      return sortDir === 'asc' ? cmp : -cmp;
    });
  }, [data, sortCol, sortDir]);

  const totalPages = Math.max(1, Math.ceil((data?.total ?? 0) / PAGE_SIZE));

  // ── Sort icon helper ────────────────────────────────────────────────────────
  function SortButton({ col, label }: { col: SortCol; label: string }) {
    const active = sortCol === col;
    return (
      <Box
        component="button"
        onClick={() => handleSort(col)}
        aria-label={`Sort by ${label} ${active && sortDir === 'asc' ? 'descending' : 'ascending'}`}
        aria-sort={active ? (sortDir === 'asc' ? 'ascending' : 'descending') : 'none'}
        sx={{
          display: 'inline-flex', alignItems: 'center', gap: 0.5,
          background: 'none', border: 'none', cursor: 'pointer',
          color: active ? 'primary.main' : 'inherit',
          fontWeight: active ? 700 : 'inherit',
          p: 0,
          '&:focus-visible': { outline: '2px solid', outlineColor: 'primary.main', borderRadius: 1 },
        }}
      >
        {label}
        {active
          ? sortDir === 'asc'
            ? <ArrowUpwardIcon fontSize="inherit" />
            : <ArrowDownwardIcon fontSize="inherit" />
          : <SortIcon fontSize="inherit" sx={{ opacity: 0.4 }} />}
      </Box>
    );
  }

  // ── Render ──────────────────────────────────────────────────────────────────
  return (
    <Box sx={{ display: 'flex', minHeight: '100vh', bgcolor: 'grey.50' }}>

      {/* ── Sidebar (wireframe .staff-sidebar) ── */}
      <Box
        component="nav"
        aria-label="Staff navigation"
        sx={{
          width: SIDEBAR_WIDTH, flexShrink: 0,
          bgcolor: 'background.paper', borderRight: '1px solid', borderColor: 'divider',
          display: { xs: 'none', md: 'flex' }, flexDirection: 'column',
        }}
      >
        <Box sx={{ px: 3, py: 2.5 }}>
          <Typography variant="h6" color="secondary.main" fontWeight={700}>UPACIP</Typography>
        </Box>
        <Divider />
        <List dense sx={{ pt: 1 }}>
          {NAV_ITEMS.map(({ label, icon, href }) => {
            const isActive = window.location.pathname.startsWith(
              href === '/staff/patients/search' ? '/staff/patients' : href,
            );
            return (
              <ListItem key={label} disablePadding>
                <ListItemButton
                  component={RouterLink}
                  to={href}
                  selected={isActive}
                  aria-current={isActive ? 'page' : undefined}
                  sx={{
                    mx: 1, borderRadius: 1,
                    '&.Mui-selected': {
                      borderLeft: '3px solid', borderColor: 'secondary.main',
                      bgcolor: 'secondary.50', color: 'secondary.main',
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

      {/* ── Main area ── */}
      <Box sx={{ flex: 1, display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>

        {/* ── Header ── */}
        <Box
          component="header"
          role="banner"
          sx={{
            display: 'flex', alignItems: 'center', justifyContent: 'space-between',
            px: 3, py: 1.5, bgcolor: 'background.paper',
            borderBottom: '1px solid', borderColor: 'divider',
          }}
        >
          <Typography variant="h6" fontWeight={600}>Patient Search</Typography>
          <Avatar
            sx={{ bgcolor: 'secondary.50', color: 'secondary.700', width: 36, height: 36, fontSize: '0.85rem' }}
            aria-label={`User: ${email ?? 'Staff'}`}
          >
            {avatarInitials}
          </Avatar>
        </Box>

        {/* ── Body ── */}
        <Box sx={{ flex: 1, overflowY: 'auto', p: { xs: 2, md: 3 } }}>
          <Container maxWidth="xl" disableGutters>

            {/* Breadcrumb */}
            <Breadcrumbs aria-label="Breadcrumb" sx={{ mb: 2 }}>
              <Link component={RouterLink} to="/staff/dashboard" underline="hover" color="inherit" variant="body2">
                Staff Dashboard
              </Link>
              <Typography variant="body2" color="text.primary" aria-current="page">
                Patient Search
              </Typography>
            </Breadcrumbs>

            <Typography variant="h5" fontWeight={700} mb={3}>
              Find a Patient
            </Typography>

            {/* ── Search card ── */}
            <Paper variant="outlined" sx={{ p: 2.5, mb: 3 }}>
              <Box
                component="form"
                onSubmit={handleSubmit}
                sx={{ display: 'flex', gap: 2, flexWrap: 'wrap', alignItems: 'flex-end' }}
              >
                {/* Search input */}
                <Box sx={{ flex: 1, minWidth: 240 }}>
                  <TextField
                    id="patient-search-input"
                    label="Search"
                    type="search"
                    size="small"
                    fullWidth
                    placeholder="Name, MRN, DOB, phone, or email…"
                    value={term}
                    onChange={e => { setTerm(e.target.value); setTermError(''); }}
                    error={!!termError}
                    helperText={termError || undefined}
                    inputProps={{ 'aria-label': 'Search patients by name, MRN, DOB, phone or email' }}
                    InputProps={{
                      startAdornment: (
                        <InputAdornment position="start">
                          <SearchIcon fontSize="small" color="action" />
                        </InputAdornment>
                      ),
                    }}
                    autoComplete="off"
                  />
                </Box>

                {/* Provider filter */}
                <FormControl size="small" sx={{ minWidth: 160 }}>
                  <InputLabel id="provider-label">Provider</InputLabel>
                  <Select
                    labelId="provider-label"
                    id="search-provider"
                    value={provider}
                    label="Provider"
                    onChange={e => setProvider(e.target.value)}
                    aria-label="Filter by provider"
                  >
                    <MenuItem value="">All Providers</MenuItem>
                    {providers.map(p => (
                      <MenuItem key={p.id} value={p.id}>{p.displayName}</MenuItem>
                    ))}
                  </Select>
                </FormControl>

                {/* Status filter */}
                <FormControl size="small" sx={{ minWidth: 140 }}>
                  <InputLabel id="status-label">Status</InputLabel>
                  <Select
                    labelId="status-label"
                    id="search-status"
                    value={statusFilter}
                    label="Status"
                    onChange={e => setStatus(e.target.value as PatientStatusFilter)}
                    aria-label="Filter by patient status"
                  >
                    <MenuItem value="All">All</MenuItem>
                    <MenuItem value="Active">Active</MenuItem>
                    <MenuItem value="Inactive">Inactive</MenuItem>
                  </Select>
                </FormControl>

                <Button
                  type="submit"
                  variant="contained"
                  sx={{ height: 40, minWidth: 80 }}
                  aria-label="Search patients"
                >
                  Search
                </Button>
              </Box>
            </Paper>

            {/* ── Error state ── */}
            {isError && (
              <Alert
                severity="error"
                action={
                  <Button size="small" onClick={() => refetch()}>Retry</Button>
                }
                sx={{ mb: 3 }}
              >
                {(error as Error | null)?.message ?? 'Failed to load patients. Please try again.'}
              </Alert>
            )}

            {/* ── Result count (aria-live for accessibility) ── */}
            {submitted && !isLoading && !isError && data && (
              <Typography
                ref={resultCountRef}
                variant="body2"
                color="text.secondary"
                mb={1.5}
                aria-live="polite"
                tabIndex={-1}
                sx={{ outline: 'none' }}
              >
                {data.total === 0
                  ? `No results${term.trim() ? ` for "${term.trim()}"` : ''}`
                  : `Showing ${data.patients.length} of ${data.total} result${data.total !== 1 ? 's' : ''}${
                      term.trim() ? ` for "${term.trim()}"` : ''
                    }`}
              </Typography>
            )}

            {/* ── Default state (no search yet) ── */}
            {!submitted && !isLoading && !isError && (
              <Paper
                variant="outlined"
                sx={{ p: 6, textAlign: 'center', bgcolor: 'background.paper' }}
              >
                <PersonSearchIcon sx={{ fontSize: 56, color: 'action.disabled', mb: 2 }} />
                <Typography variant="body1" color="text.secondary" gutterBottom>
                  Enter a name, MRN, date of birth, phone number, or email to find a patient.
                </Typography>
                <Typography variant="caption" color="text.secondary">
                  Partial name search is supported — e.g. "Joh" will match "John" and "Johnston".
                </Typography>
              </Paper>
            )}

            {/* ── Empty state ── */}
            {submitted && !isLoading && !isError && data?.total === 0 && (
              <Paper
                variant="outlined"
                sx={{ p: 6, textAlign: 'center', bgcolor: 'background.paper' }}
              >
                <Typography variant="body1" color="text.secondary" gutterBottom>
                  No patients found matching your search criteria.
                </Typography>
                <Typography variant="caption" color="text.secondary">
                  Try adjusting your filters or search terms.
                </Typography>
              </Paper>
            )}

            {/* ── Results — desktop table (md+) ── */}
            {(isLoading || (submitted && !isError && (data?.total ?? 0) > 0)) && (
              <Paper variant="outlined" sx={{ display: { xs: 'none', md: 'block' } }}>
                <TableContainer>
                  <Table aria-label="Patient search results">
                    <TableHead>
                      <TableRow sx={{ '& th': { fontWeight: 700, bgcolor: 'grey.50' } }}>
                        <TableCell>
                          <SortButton col="fullName" label="Name" />
                        </TableCell>
                        <TableCell>MRN</TableCell>
                        <TableCell>
                          <SortButton col="dateOfBirth" label="DOB" />
                        </TableCell>
                        <TableCell>Phone</TableCell>
                        <TableCell>Provider</TableCell>
                        <TableCell>Last Visit</TableCell>
                        <TableCell>Status</TableCell>
                      </TableRow>
                    </TableHead>
                    <TableBody>
                      {isLoading ? (
                        <SkeletonRows count={5} />
                      ) : (
                        sortedRows.map(row => (
                          <TableRow
                            key={row.patientId}
                            hover
                            sx={{
                              opacity: row.status === 'Inactive' ? 0.7 : 1,
                              cursor: 'pointer',
                              '&:focus-within': { outline: '2px solid', outlineColor: 'primary.main' },
                            }}
                            onClick={() => handleNavigateToProfile(row.patientId)}
                            tabIndex={0}
                            aria-label={`Patient row: ${row.fullName}`}
                            onKeyDown={e => {
                              if (e.key === 'Enter' || e.key === ' ') handleNavigateToProfile(row.patientId);
                            }}
                          >
                            <TableCell>
                              <Tooltip title="View profile" placement="right" arrow>
                                <Link
                                  component={RouterLink}
                                  to={`/staff/patients/${row.patientId}/profile`}
                                  color="primary.main"
                                  fontWeight={500}
                                  underline="hover"
                                  onClick={e => e.stopPropagation()}
                                  aria-label={`View profile for ${row.fullName}`}
                                >
                                  <HighlightedText text={row.fullName} term={term} />
                                </Link>
                              </Tooltip>
                            </TableCell>
                            <TableCell sx={{ color: 'text.secondary' }}>{row.mrn}</TableCell>
                            <TableCell sx={{ color: 'text.secondary' }}>{formatDate(row.dateOfBirth)}</TableCell>
                            <TableCell sx={{ color: 'text.secondary' }}>{row.phone}</TableCell>
                            <TableCell sx={{ color: 'text.secondary' }}>{row.provider}</TableCell>
                            <TableCell sx={{ color: 'text.secondary' }}>{formatDate(row.lastVisitAt)}</TableCell>
                            <TableCell>
                              <Chip
                                label={row.status}
                                size="small"
                                color={row.status === 'Active' ? 'success' : 'default'}
                              />
                            </TableCell>
                          </TableRow>
                        ))
                      )}
                    </TableBody>
                  </Table>
                </TableContainer>

                {/* Pagination */}
                <Box
                  sx={{
                    display: 'flex', alignItems: 'center', justifyContent: 'flex-end',
                    gap: 2, px: 2, py: 1.5, borderTop: '1px solid', borderColor: 'divider',
                  }}
                >
                  <Button
                    size="small"
                    variant="outlined"
                    onClick={() => setPage(p => Math.max(1, p - 1))}
                    disabled={page <= 1 || isLoading}
                    aria-label="Previous page"
                  >
                    Previous
                  </Button>
                  <Typography variant="body2" color="text.secondary" aria-live="polite">
                    Page {page} of {totalPages}
                  </Typography>
                  <Button
                    size="small"
                    variant="outlined"
                    onClick={() => setPage(p => Math.min(totalPages, p + 1))}
                    disabled={page >= totalPages || isLoading}
                    aria-label="Next page"
                  >
                    Next
                  </Button>
                </Box>
              </Paper>
            )}

            {/* ── Results — mobile cards (xs/sm) ── */}
            {submitted && !isLoading && !isError && sortedRows.length > 0 && (
              <Box sx={{ display: { xs: 'block', md: 'none' } }}>
                {sortedRows.map(row => (
                  <PatientCard
                    key={row.patientId}
                    row={row}
                    term={term}
                    onNavigate={handleNavigateToProfile}
                  />
                ))}
                {/* Mobile pagination */}
                <Box sx={{ display: 'flex', justifyContent: 'center', gap: 2, mt: 2 }}>
                  <Button
                    size="small"
                    variant="outlined"
                    onClick={() => setPage(p => Math.max(1, p - 1))}
                    disabled={page <= 1}
                    aria-label="Previous page"
                    sx={{ minHeight: 44, minWidth: 80 }}
                  >
                    Previous
                  </Button>
                  <Typography variant="body2" color="text.secondary" sx={{ alignSelf: 'center' }}>
                    Page {page} of {totalPages}
                  </Typography>
                  <Button
                    size="small"
                    variant="outlined"
                    onClick={() => setPage(p => Math.min(totalPages, p + 1))}
                    disabled={page >= totalPages}
                    aria-label="Next page"
                    sx={{ minHeight: 44, minWidth: 80 }}
                  >
                    Next
                  </Button>
                </Box>
              </Box>
            )}

          </Container>
        </Box>
      </Box>
    </Box>
  );
}
