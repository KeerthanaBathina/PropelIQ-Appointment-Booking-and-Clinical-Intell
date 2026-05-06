/**
 * StaffPatientDocumentsPage — /staff/documents/:patientId
 *
 * Staff-facing document upload and list page for a specific patient.
 * Uses the same staff sidebar shell as other staff pages.
 */

import { useCallback, useState } from 'react';
import { useParams, useNavigate, Link as RouterLink } from 'react-router-dom';
import Alert from '@mui/material/Alert';
import Avatar from '@mui/material/Avatar';
import Box from '@mui/material/Box';
import Breadcrumbs from '@mui/material/Breadcrumbs';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Divider from '@mui/material/Divider';
import LinearProgress from '@mui/material/LinearProgress';
import Link from '@mui/material/Link';
import List from '@mui/material/List';
import ListItem from '@mui/material/ListItem';
import ListItemButton from '@mui/material/ListItemButton';
import ListItemIcon from '@mui/material/ListItemIcon';
import ListItemText from '@mui/material/ListItemText';
import MenuItem from '@mui/material/MenuItem';
import Paper from '@mui/material/Paper';
import Select from '@mui/material/Select';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';
import ArrowBackIcon from '@mui/icons-material/ArrowBack';
import ArticleOutlinedIcon from '@mui/icons-material/ArticleOutlined';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';
import CloudUploadOutlinedIcon from '@mui/icons-material/CloudUploadOutlined';
import DashboardIcon from '@mui/icons-material/Dashboard';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline';
import DescriptionOutlinedIcon from '@mui/icons-material/DescriptionOutlined';
import ErrorOutlineIcon from '@mui/icons-material/ErrorOutline';
import PeopleOutlinedIcon from '@mui/icons-material/PeopleOutlined';
import QueueOutlinedIcon from '@mui/icons-material/QueueOutlined';
import { useAuthStore } from '@/hooks/useAuth';
import { useClinicalDocumentUpload, DOCUMENT_CATEGORY_LABELS } from '@/hooks/useClinicalDocumentUpload';
import type { DocumentCategory } from '@/hooks/useClinicalDocumentUpload';
import { usePatientSearch } from '@/hooks/usePatientSearch';

// ─── Sidebar nav ──────────────────────────────────────────────────────────────

const NAV_ITEMS = [
  { label: 'Dashboard', icon: <DashboardIcon />,           href: '/staff/dashboard'       },
  { label: 'Queue',     icon: <QueueOutlinedIcon />,       href: '/staff/queue'           },
  { label: 'Documents', icon: <DescriptionOutlinedIcon />, href: '/staff/documents'       },
  { label: 'Patients',  icon: <PeopleOutlinedIcon />,      href: '/staff/patients/search' },
] as const;

const SIDEBAR_WIDTH = 220;

const CATEGORY_OPTIONS: { value: DocumentCategory; label: string }[] = [
  { value: 'LabResult',       label: DOCUMENT_CATEGORY_LABELS['LabResult'] },
  { value: 'Prescription',    label: DOCUMENT_CATEGORY_LABELS['Prescription'] },
  { value: 'ClinicalNote',    label: DOCUMENT_CATEGORY_LABELS['ClinicalNote'] },
  { value: 'ImagingReport',   label: DOCUMENT_CATEGORY_LABELS['ImagingReport'] },
];

function formatBytes(bytes: number): string {
  if (bytes === 0) return '0 B';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(1))} ${sizes[i]}`;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function StaffPatientDocumentsPage() {
  const { patientId = '' } = useParams<{ patientId: string }>();
  const navigate = useNavigate();
  const email = useAuthStore(s => s.email);
  const avatarInitials = email ? email.split('@')[0].slice(0, 2).toUpperCase() : 'ST';

  const [category, setCategory] = useState<DocumentCategory>('LabResult');
  const [isDragOver, setIsDragOver] = useState(false);

  const { entries, addFiles, retryEntry, removeEntry } = useClinicalDocumentUpload();

  // Fetch patient name for display
  const { data: searchData } = usePatientSearch({ term: '', page: 1, pageSize: 100 });
  const patient = searchData?.patients.find(p => p.patientId === patientId);
  const patientName = patient?.fullName ?? 'Patient';
  const initials = patientName.split(' ').map(n => n[0]).slice(0, 2).join('').toUpperCase();

  const handleFiles = useCallback((files: FileList | null) => {
    if (!files || files.length === 0) return;
    addFiles(Array.from(files), category, patientId, '');
  }, [addFiles, category, patientId]);

  const onDrop = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setIsDragOver(false);
    handleFiles(e.dataTransfer.files);
  }, [handleFiles]);

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh', bgcolor: 'grey.50' }}>

      {/* ── Sidebar ── */}
      <Box
        component="nav"
        aria-label="Staff navigation"
        sx={{
          width: SIDEBAR_WIDTH,
          flexShrink: 0,
          bgcolor: 'background.paper',
          borderRight: '1px solid',
          borderColor: 'divider',
          display: 'flex',
          flexDirection: 'column',
        }}
      >
        <Box sx={{ px: 2.5, py: 2.5 }}>
          <Typography variant="subtitle2" fontWeight={700} color="secondary.main" letterSpacing={0.5} textTransform="uppercase" fontSize="0.7rem">
            UPACIP
          </Typography>
          <Typography variant="caption" color="text.secondary">Staff Portal</Typography>
        </Box>
        <Divider />
        <List dense sx={{ mt: 1, flex: 1 }}>
          {NAV_ITEMS.map(({ label, icon, href }) => {
            const isActive = href === '/staff/documents';
            return (
              <ListItem key={label} disablePadding>
                <ListItemButton
                  component={RouterLink}
                  to={href}
                  selected={isActive}
                  sx={{
                    mx: 1, borderRadius: 1,
                    '&.Mui-selected': { bgcolor: 'secondary.50', color: 'secondary.main' },
                  }}
                >
                  <ListItemIcon sx={{ minWidth: 36, color: isActive ? 'secondary.main' : 'inherit' }}>{icon}</ListItemIcon>
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
          sx={{
            display: 'flex', alignItems: 'center', justifyContent: 'space-between',
            px: 3, py: 1.5, bgcolor: 'background.paper',
            borderBottom: '1px solid', borderColor: 'divider', gap: 2,
          }}
        >
          <Breadcrumbs aria-label="Breadcrumb">
            <Link component={RouterLink} to="/staff/dashboard" underline="hover" color="inherit" variant="body2">
              Dashboard
            </Link>
            <Link component={RouterLink} to="/staff/documents" underline="hover" color="inherit" variant="body2">
              Documents
            </Link>
            <Typography variant="body2" color="text.primary">{patientName}</Typography>
          </Breadcrumbs>
          <Avatar sx={{ bgcolor: 'secondary.100', color: 'secondary.800', width: 36, height: 36, fontSize: '0.85rem' }}>
            {avatarInitials}
          </Avatar>
        </Box>

        {/* Content */}
        <Box component="main" role="main" sx={{ flex: 1, overflowY: 'auto', p: 3 }}>

          {/* Back + Patient header */}
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, mb: 3 }}>
            <Button
              startIcon={<ArrowBackIcon />}
              variant="outlined"
              size="small"
              onClick={() => navigate('/staff/documents')}
            >
              Back
            </Button>
            <Avatar sx={{ bgcolor: 'secondary.main', width: 44, height: 44, fontSize: '1rem' }}>
              {initials}
            </Avatar>
            <Box>
              <Typography variant="h6" fontWeight={700}>{patientName}</Typography>
              {patient && (
                <Typography variant="caption" color="text.secondary">
                  MRN: {patient.mrn} · Provider: {patient.provider || '—'}
                </Typography>
              )}
            </Box>
          </Box>

          <Box sx={{ display: 'flex', gap: 3, flexWrap: 'wrap', alignItems: 'flex-start' }}>

            {/* ── Upload panel ── */}
            <Box sx={{ flex: 1, minWidth: 300 }}>
              <Paper variant="outlined" sx={{ p: 3, borderRadius: 2 }}>
                <Typography variant="subtitle1" fontWeight={600} sx={{ mb: 2 }}>
                  Upload Document
                </Typography>

                {/* Category picker */}
                <Select
                  fullWidth
                  size="small"
                  value={category}
                  onChange={e => setCategory(e.target.value as DocumentCategory)}
                  displayEmpty
                  sx={{ mb: 2 }}
                >
                  {CATEGORY_OPTIONS.map(opt => (
                    <MenuItem key={opt.value} value={opt.value}>{opt.label}</MenuItem>
                  ))}
                </Select>

                {/* Drop zone */}
                <Box
                  onDragOver={e => { e.preventDefault(); setIsDragOver(true); }}
                  onDragLeave={() => setIsDragOver(false)}
                  onDrop={onDrop}
                  onClick={() => document.getElementById('doc-file-input')?.click()}
                  tabIndex={0}
                  role="button"
                  aria-label="Drag and drop files here or click to browse"
                  onKeyDown={e => { if (e.key === 'Enter' || e.key === ' ') document.getElementById('doc-file-input')?.click(); }}
                  sx={{
                    border: '2px dashed',
                    borderColor: isDragOver ? 'secondary.main' : 'divider',
                    borderRadius: 2,
                    p: 4,
                    textAlign: 'center',
                    cursor: 'pointer',
                    bgcolor: isDragOver ? 'secondary.50' : 'grey.50',
                    transition: 'all 0.15s',
                    '&:hover': { borderColor: 'secondary.main', bgcolor: 'secondary.50' },
                    '&:focus-visible': { outline: '2px solid', outlineColor: 'secondary.main', outlineOffset: 2 },
                  }}
                >
                  <CloudUploadOutlinedIcon sx={{ fontSize: 48, color: 'secondary.light', mb: 1 }} />
                  <Typography variant="body2" fontWeight={500}>Drag &amp; drop files here</Typography>
                  <Typography variant="caption" color="text.secondary">or click to browse</Typography>
                  <Typography variant="caption" display="block" color="text.disabled" sx={{ mt: 0.5 }}>
                    PDF, JPEG, PNG, DOCX — max 25 MB
                  </Typography>
                </Box>

                <input
                  id="doc-file-input"
                  type="file"
                  multiple
                  accept=".pdf,.jpg,.jpeg,.png,.docx"
                  style={{ display: 'none' }}
                  onChange={e => handleFiles(e.target.files)}
                />
              </Paper>
            </Box>

            {/* ── Upload list ── */}
            <Box sx={{ flex: 1, minWidth: 300 }}>
              <Paper variant="outlined" sx={{ p: 3, borderRadius: 2 }}>
                <Typography variant="subtitle1" fontWeight={600} sx={{ mb: 2 }}>
                  Uploaded Files {entries.length > 0 && `(${entries.length})`}
                </Typography>

                {entries.length === 0 ? (
                  <Box sx={{ textAlign: 'center', py: 4 }}>
                    <ArticleOutlinedIcon sx={{ fontSize: 40, color: 'text.disabled', mb: 1 }} />
                    <Typography variant="body2" color="text.secondary">No files uploaded yet</Typography>
                  </Box>
                ) : (
                  <TableContainer>
                    <Table size="small">
                      <TableHead>
                        <TableRow>
                          <TableCell sx={{ fontWeight: 600 }}>File</TableCell>
                          <TableCell sx={{ fontWeight: 600 }}>Size</TableCell>
                          <TableCell sx={{ fontWeight: 600 }}>Status</TableCell>
                          <TableCell />
                        </TableRow>
                      </TableHead>
                      <TableBody>
                        {entries.map(entry => (
                          <TableRow key={entry.id}>
                            <TableCell>
                              <Typography variant="caption" noWrap sx={{ maxWidth: 180, display: 'block' }}>
                                {entry.file.name}
                              </Typography>
                              <Typography variant="caption" color="text.secondary">
                                {DOCUMENT_CATEGORY_LABELS[entry.category]}
                              </Typography>
                            </TableCell>
                            <TableCell>
                              <Typography variant="caption">{formatBytes(entry.file.size)}</Typography>
                            </TableCell>
                            <TableCell>
                              {entry.status === 'uploading' && (
                                <Box sx={{ minWidth: 80 }}>
                                  <LinearProgress variant="determinate" value={entry.progress} sx={{ height: 4, borderRadius: 2 }} />
                                  <Typography variant="caption" color="text.secondary">{entry.progress}%</Typography>
                                </Box>
                              )}
                              {entry.status === 'pending' && <Chip label="Pending" size="small" color="default" />}
                              {entry.status === 'success' && <Chip icon={<CheckCircleOutlineIcon />} label="Uploaded" size="small" color="success" variant="outlined" />}
                              {(entry.status === 'error' || entry.status === 'interrupted') && (
                                <Box>
                                  <Chip icon={<ErrorOutlineIcon />} label="Failed" size="small" color="error" variant="outlined" />
                                  {entry.status === 'error' && (
                                    <Button size="small" onClick={() => retryEntry(entry.id)} sx={{ ml: 0.5, fontSize: '0.7rem' }}>Retry</Button>
                                  )}
                                </Box>
                              )}
                            </TableCell>
                            <TableCell>
                              {entry.status !== 'uploading' && (
                                <Button size="small" color="error" onClick={() => removeEntry(entry.id)} sx={{ minWidth: 0, p: 0.5 }}>
                                  <DeleteOutlineIcon fontSize="small" />
                                </Button>
                              )}
                            </TableCell>
                          </TableRow>
                        ))}
                      </TableBody>
                    </Table>
                  </TableContainer>
                )}

                {entries.some(e => e.status === 'error') && (
                  <Alert severity="warning" sx={{ mt: 2 }}>
                    Some files failed to upload. Check your connection and retry.
                  </Alert>
                )}
              </Paper>
            </Box>

          </Box>
        </Box>
      </Box>
    </Box>
  );
}
