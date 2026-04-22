/**
 * PatientProfilePage — SCR-013 (US_041 AC-2–AC-4, EC-1, EC-2)
 *
 * Staff-facing 360° patient profile view. Renders the patient header, alert for
 * data conflicts, and the extracted clinical data tabs with confidence-review and
 * verification interactions (US_041).
 *
 * Layout follows wireframe-SCR-013-patient-profile-360.html:
 *   - Patient avatar + name + meta row
 *   - Conflict alert (shown when backend flags a conflict)
 *   - PatientProfileExtractedDataTabs (Medications / Diagnoses / Procedures / Allergies)
 *
 * After successful verification actions, PatientProfileExtractedDataTabs invalidates
 * its own React Query cache so counts and status chips refresh immediately (AC-4, EC-2).
 */

import { useParams } from 'react-router-dom';
import Alert from '@mui/material/Alert';
import AppBar from '@mui/material/AppBar';
import Box from '@mui/material/Box';
import Breadcrumbs from '@mui/material/Breadcrumbs';
import Button from '@mui/material/Button';
import Container from '@mui/material/Container';
import Link from '@mui/material/Link';
import Paper from '@mui/material/Paper';
import Toolbar from '@mui/material/Toolbar';
import Typography from '@mui/material/Typography';
import UploadFileIcon from '@mui/icons-material/UploadFile';

import PatientProfileExtractedDataTabs from '@/components/patient/PatientProfileExtractedDataTabs';

// ─── Component ────────────────────────────────────────────────────────────────

export default function PatientProfilePage() {
  const { patientId = '' } = useParams<{ patientId: string }>();

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', minHeight: '100vh' }}>
      {/* App bar */}
      <AppBar position="static" color="default" elevation={1}>
        <Toolbar>
          <Typography variant="h6" component="h2" sx={{ flexGrow: 1 }}>
            Patient Profile 360°
          </Typography>
          <Button
            variant="outlined"
            size="small"
            startIcon={<UploadFileIcon />}
            href={`/staff/documents/upload?patientId=${patientId}`}
            aria-label="Upload documents for this patient"
            sx={{ mr: 1 }}
          >
            Upload Documents
          </Button>
        </Toolbar>
      </AppBar>

      <Container maxWidth="xl" sx={{ py: 3, flexGrow: 1 }}>
        {/* Breadcrumb (SCR-013 wireframe) */}
        <Breadcrumbs aria-label="Breadcrumb" sx={{ mb: 3 }}>
          <Link href="/staff/dashboard" underline="hover" color="inherit">
            Staff Dashboard
          </Link>
          <Link href="/staff/patients" underline="hover" color="inherit">
            Patients
          </Link>
          <Typography color="text.primary">Patient Profile</Typography>
        </Breadcrumbs>

        {/* Patient header (wireframe profile-header) */}
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, mb: 3 }}>
          <Box
            sx={{
              width: 64,
              height: 64,
              borderRadius: '50%',
              bgcolor: 'primary.100',
              color: 'primary.700',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              fontSize: '1.5rem',
              fontWeight: 600,
              flexShrink: 0,
            }}
            aria-hidden="true"
          >
            {patientId.slice(0, 2).toUpperCase()}
          </Box>
          <Box>
            <Typography variant="h4" component="h1">
              Patient {patientId}
            </Typography>
            <Typography variant="body2" color="text.secondary">
              MRN: {patientId}
            </Typography>
          </Box>
        </Box>

        {/* Extracted data panel with confidence badges and verification (US_041) */}
        <Paper variant="outlined" sx={{ borderRadius: 2, overflow: 'hidden' }}>
          <Box sx={{ px: 3, pt: 2, pb: 1, borderBottom: 1, borderColor: 'divider' }}>
            <Typography variant="h6">Clinical Data — Extraction Review</Typography>
            <Typography variant="body2" color="text.secondary">
              Review AI-extracted clinical data. Items flagged in amber or red require
              verification before they can be used in coding or discharge workflows.
            </Typography>
          </Box>

          {/* Confidence-review and bulk-verification tabs (AC-2–AC-4, EC-1, EC-2) */}
          {patientId ? (
            <PatientProfileExtractedDataTabs patientId={patientId} />
          ) : (
            <Alert severity="warning" sx={{ m: 2 }}>
              No patient ID provided. Navigate from the patient search screen.
            </Alert>
          )}
        </Paper>
      </Container>
    </Box>
  );
}
