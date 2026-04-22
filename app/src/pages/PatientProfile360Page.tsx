/**
 * PatientProfile360Page — SCR-013 US_043 consolidated patient profile view.
 *
 * Route: /staff/patients/:patientId/profile
 *
 * Screen states (all 5 required):
 *   1. Loading   — skeleton header + tab skeletons while profile fetches
 *   2. Error     — Alert with retry button when fetch fails
 *   3. Empty     — "Profile not yet consolidated" with Upload Documents CTA
 *   4. Default   — Full profile: header, conflict banner, tabs, version history
 *   5. Validation — Conflict alert visible when conflictCount > 0 (inline in Default)
 *
 * Layout follows wireframe-SCR-013-patient-profile-360.html:
 *   Breadcrumb → ProfileHeader → ConflictAlertBanner → ClinicalDataTabs
 *   → VersionHistoryPanel → SourceCitationPanel (drawer, opened by row click)
 *
 * Navigation: UXR-003 breadcrumb (Staff Dashboard → Patients → patient name).
 * Toast: UXR-505 notification shown after background consolidation trigger.
 *
 * Responsive at 375px (xs), 768px (sm), 1440px (xl) per wireframe breakpoints.
 */

import { useCallback, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import Alert from '@mui/material/Alert';
import AppBar from '@mui/material/AppBar';
import Box from '@mui/material/Box';
import Breadcrumbs from '@mui/material/Breadcrumbs';
import Button from '@mui/material/Button';
import Container from '@mui/material/Container';
import Link from '@mui/material/Link';
import Toolbar from '@mui/material/Toolbar';
import Typography from '@mui/material/Typography';
import MedicalCodeIcon from '@mui/icons-material/LocalHospital';
import UploadFileIcon from '@mui/icons-material/UploadFile';

import ProfileHeader from '@/components/profile/ProfileHeader';
import ConflictAlertBanner from '@/components/profile/ConflictAlertBanner';
import ClinicalDataTabs from '@/components/profile/ClinicalDataTabs';
import SourceCitationPanel from '@/components/profile/SourceCitationPanel';
import VersionHistoryPanel from '@/components/profile/VersionHistoryPanel';
import ConflictResolutionModal from '@/components/conflict/ConflictResolutionModal';
import VerificationStatusBanner from '@/components/conflict/VerificationStatusBanner';
import AiUnavailableBanner from '@/features/clinical/AiUnavailableBanner';
import ManualReviewForm from '@/features/clinical/ManualReviewForm';
import { usePatientProfile } from '@/hooks/usePatientProfile';
import { useVersionHistory } from '@/hooks/useVersionHistory';
import { useAiHealthStatus, useLowConfidenceItems } from '@/hooks/useManualFallback';
import { useManualFallbackStore } from '@/stores/manualFallbackStore';
import { useToast } from '@/components/common/ToastProvider';

// ─── Component ────────────────────────────────────────────────────────────────

export default function PatientProfile360Page() {
  const { patientId = '' } = useParams<{ patientId: string }>();
  const navigate = useNavigate();
  const { showToast } = useToast();

  // ── Data ──────────────────────────────────────────────────────────────────
  const { profile, isLoading, isError, refetch } = usePatientProfile(patientId);
  const { versions, isLoading: versionsLoading } = useVersionHistory(patientId);

  // ── Citation drawer state ─────────────────────────────────────────────────
  const [citationOpen, setCitationOpen] = useState(false);
  const [selectedDataPointId, setSelectedDataPointId] = useState<string | null>(null);

  // ── Conflict modal state ──────────────────────────────────────────────────
  const [conflictModalOpen, setConflictModalOpen] = useState(false);
  const handleOpenConflictModal = useCallback(() => setConflictModalOpen(true), []);
  const handleCloseConflictModal = useCallback(() => setConflictModalOpen(false), []);

  // ── Manual fallback mode (US_046) ─────────────────────────────────────────
  const isAiUnavailable = useManualFallbackStore((s) => s.isAiUnavailable);
  const isManualFallbackMode = useManualFallbackStore((s) => s.isManualFallbackMode);
  const enterManualFallback = useManualFallbackStore((s) => s.enterManualFallback);
  const exitManualFallback = useManualFallbackStore((s) => s.exitManualFallback);

  // Poll AI health; syncs isAiUnavailable into store automatically.
  useAiHealthStatus();

  // Low-confidence items for the review form (AC-1).
  const {
    items: lowConfidenceItems,
    isLoading: lowConfLoading,
    isError: lowConfError,
  } = useLowConfidenceItems(patientId);

  const handleSwitchToManual = useCallback(() => {
    enterManualFallback(patientId);
  }, [patientId, enterManualFallback]);

  // Determine whether to show the manual review form: AI unavailable OR
  // staff explicitly switched, OR there are low-confidence items to review.
  const showManualReview =
    isManualFallbackMode ||
    (isAiUnavailable && !!patientId) ||
    lowConfidenceItems.length > 0;

  const handleDataPointClick = useCallback((extractedDataId: string) => {
    setSelectedDataPointId(extractedDataId);
    setCitationOpen(true);
  }, []);

  const handleCitationClose = useCallback(() => {
    setCitationOpen(false);
  }, []);

  // ── Consolidation trigger ─────────────────────────────────────────────────
  const handleTriggerConsolidation = useCallback(() => {
    showToast({
      message: 'Consolidation queued — the profile will update shortly.',
      severity: 'info',
    });
  }, [showToast]);

  // ── Missing patientId guard ───────────────────────────────────────────────
  if (!patientId) {
    return (
      <Container maxWidth="md" sx={{ py: 6 }}>
        <Alert severity="error">
          No patient ID provided. Please navigate from the patient search screen.
        </Alert>
      </Container>
    );
  }

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
            onClick={() => navigate(`/staff/documents/${patientId}`)}
            aria-label="Upload documents for this patient"
            sx={{ mr: 1 }}
          >
            Upload Documents
          </Button>
          <Button
            variant="contained"
            size="small"
            startIcon={<MedicalCodeIcon />}
            onClick={() => navigate(`/staff/patients/${patientId}/coding`)}
            aria-label="View medical coding for this patient"
          >
            Medical Coding
          </Button>
        </Toolbar>
      </AppBar>

      <Container
        maxWidth="xl"
        sx={{
          py: { xs: 2, sm: 3 },
          px: { xs: 2, sm: 3 },
          flexGrow: 1,
        }}
      >
        {/* Breadcrumb — UXR-003 */}
        <Breadcrumbs aria-label="Breadcrumb" sx={{ mb: 3 }}>
          <Link
            href="/staff/dashboard"
            underline="hover"
            color="inherit"
            aria-label="Go to Staff Dashboard"
          >
            Staff Dashboard
          </Link>
          <Link
            href="/staff/patients"
            underline="hover"
            color="inherit"
            aria-label="Go to Patients list"
          >
            Patients
          </Link>
          <Typography color="text.primary">
            {profile?.patientName ?? 'Patient Profile'}
          </Typography>
        </Breadcrumbs>

        {/* ── State 2: Error ─────────────────────────────────────────────── */}
        {isError && (
          <Alert
            severity="error"
            action={
              <Button color="inherit" size="small" onClick={() => refetch()} aria-label="Retry loading profile">
                Retry
              </Button>
            }
            sx={{ mb: 3 }}
          >
            Failed to load patient profile. Please try again.
          </Alert>
        )}

        {/* ── State 1: Loading ───────────────────────────────────────────── */}
        {!isError && (
          <>
            {/* Profile header (shows skeleton when loading) */}
            <ProfileHeader
              profile={profile}
              isLoading={isLoading}
              sourceDocumentCount={versions.reduce((sum: number, v) => sum + v.sourceDocumentCount, 0)}
            />

            {/* ── State 3: Empty ────────────────────────────────────────── */}
            {!isLoading && !profile && (
              <Box
                sx={{
                  display: 'flex',
                  flexDirection: 'column',
                  alignItems: 'center',
                  justifyContent: 'center',
                  py: 8,
                  gap: 2,
                  color: 'text.secondary',
                }}
                role="status"
                aria-label="No profile data"
              >
                <UploadFileIcon sx={{ fontSize: 64, opacity: 0.3 }} aria-hidden="true" />
                <Typography variant="h6" color="text.secondary">
                  No consolidated profile yet
                </Typography>
                <Typography variant="body2" color="text.disabled" align="center" sx={{ maxWidth: 400 }}>
                  Upload clinical documents for this patient and run consolidation to build
                  their 360° profile.
                </Typography>
                <Box sx={{ display: 'flex', gap: 1.5, mt: 1, flexWrap: 'wrap', justifyContent: 'center' }}>
                  <Button
                    variant="contained"
                    startIcon={<UploadFileIcon />}
                    onClick={() => navigate(`/staff/documents/${patientId}`)}
                    aria-label="Upload documents to start consolidation"
                  >
                    Upload Documents
                  </Button>
                  <Button
                    variant="outlined"
                    onClick={handleTriggerConsolidation}
                    aria-label="Trigger profile consolidation"
                  >
                    Run Consolidation
                  </Button>
                </Box>
              </Box>
            )}

            {/* ── States 4 & 5: Default + Validation ───────────────────── */}
            {!isLoading && profile && (
              <>
                {/* ── State 4: Default + Verification banner (US_045 AC-4) ── */}
                {/* Verification banner — shown only when profile is Verified */}
                <VerificationStatusBanner patientId={patientId} />

                {/* AI unavailable banner (US_046 AC-4, UXR-605) */}
                <AiUnavailableBanner
                  isAiUnavailable={isAiUnavailable}
                  onSwitchToManual={handleSwitchToManual}
                />

                {/* State 5: Validation — conflict banner (UXR-104) */}
                <ConflictAlertBanner
                  conflictCount={profile.conflictCount}
                  onReviewClick={handleOpenConflictModal}
                />

                {/* Clinical data tabs */}
                <ClinicalDataTabs
                  medications={profile.medications}
                  diagnoses={profile.diagnoses}
                  procedures={profile.procedures}
                  allergies={profile.allergies}
                  isLoading={false}
                  onDataPointClick={handleDataPointClick}
                />

                {/* Manual review / fallback form (US_046 AC-1, AC-3, AC-4) */}
                {showManualReview && (
                  <Box sx={{ mt: 3 }}>
                    <ManualReviewForm
                      patientId={patientId}
                      mode={isAiUnavailable && !lowConfidenceItems.length ? 'manual' : 'low-confidence'}
                      lowConfidenceItems={lowConfidenceItems}
                      isLoading={lowConfLoading}
                      isError={lowConfError}
                    />
                    {isManualFallbackMode && (
                      <Box sx={{ mt: 1, display: 'flex', justifyContent: 'flex-end' }}>
                        <Button
                          variant="text"
                          size="small"
                          onClick={exitManualFallback}
                          aria-label="Exit manual fallback mode and return to normal profile view"
                        >
                          Exit Manual Mode
                        </Button>
                      </Box>
                    )}
                  </Box>
                )}

                {/* Version history panel */}
                <Box sx={{ mt: 3 }}>
                  <VersionHistoryPanel
                    versions={versions}
                    isLoading={versionsLoading}
                  />
                </Box>
              </>
            )}

            {/* Loading state tabs skeleton */}
            {isLoading && (
              <ClinicalDataTabs
                medications={[]}
                diagnoses={[]}
                procedures={[]}
                allergies={[]}
                isLoading
                onDataPointClick={() => undefined}
              />
            )}
          </>
        )}
      </Container>

      {/* Source citation drawer (AC-3) */}
      <SourceCitationPanel
        open={citationOpen}
        onClose={handleCitationClose}
        patientId={patientId}
        extractedDataId={selectedDataPointId}
      />

      {/* Conflict resolution modal (US_044 AC-2, AC-3, UXR-104) */}
      <ConflictResolutionModal
        open={conflictModalOpen}
        patientId={patientId}
        onClose={handleCloseConflictModal}
      />
    </Box>
  );
}
