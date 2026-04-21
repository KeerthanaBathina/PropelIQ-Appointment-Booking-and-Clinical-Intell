/**
 * ManualIntakePage — SCR-009 Patient Manual Intake Form (US_028, FL-004).
 *
 * Layout (wireframe-SCR-009-manual-intake.html):
 *   - AppBar with breadcrumb: Dashboard › AI Intake › Manual Form
 *   - "← Back to AI Intake" secondary button (top-right)
 *   - Info alert: mandatory fields notice + autosave every 30s (UXR-004)
 *   - ManualIntakeForm inside a Card: Personal Info, Medical History, Insurance, Consent
 *   - Submit + Save Draft button row
 *   - Success state: redirects to patient dashboard after 2s
 *
 * Pre-fill (AC-2):
 *   Navigation state `state.prefilledFields` (Record<string, string>) is consumed
 *   by useManualIntakeForm and visually annotated via PrefilledFieldIndicator.
 *
 * Validation (AC-3): inline errors shown within 200ms on blur/change (UXR-501).
 *
 * Autosave (UXR-004, EC-1): draft saved every 30s; last-saved timestamp displayed.
 *
 * Duplicate submission (EC-2): submit button disabled during in-flight request.
 *
 * Responsive: maxWidth="lg" Container, form fields transition 1-column → 2-column at sm.
 */

import { useCallback, useEffect, useState } from 'react';
import { Link as RouterLink, useLocation, useNavigate } from 'react-router-dom';
import Alert from '@mui/material/Alert';
import AppBar from '@mui/material/AppBar';
import Box from '@mui/material/Box';
import Breadcrumbs from '@mui/material/Breadcrumbs';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import Container from '@mui/material/Container';
import Divider from '@mui/material/Divider';
import Link from '@mui/material/Link';
import Paper from '@mui/material/Paper';
import Toolbar from '@mui/material/Toolbar';
import Typography from '@mui/material/Typography';
import SaveIcon from '@mui/icons-material/Save';
import SendIcon from '@mui/icons-material/Send';
import IntakeModeSwitchBanner from '@/components/intake/IntakeModeSwitchBanner';
import ManualIntakeForm from '@/components/intake/ManualIntakeForm';
import { useManualIntakeForm } from '@/hooks/useManualIntakeForm';
import { AUTOSAVE_FAILED_MESSAGE } from '@/hooks/useIntakeAutosave';
import { useIntakeModeSwitch } from '@/hooks/useIntakeModeSwitch';

// ─── Component ────────────────────────────────────────────────────────────────

interface LocationState {
  /** Pre-filled AI-collected fields passed from AIIntakePage switch-to-manual */
  prefilledFields?: Record<string, string>;
}

export default function ManualIntakePage() {
  const navigate = useNavigate();
  const location  = useLocation();
  const state     = (location.state ?? {}) as LocationState;

  const {
    values,
    errors,
    touched,
    prefilledKeys,
    status,
    lastSavedAt,
    autosaveStatus,
    errorMessage,
    isMinor,
    isGuardianAlsoMinor,
    isGuardianConsentRequired,
    insurancePrecheckStatus,
    insurancePrecheckMessage,
    handleChange,
    handleBlur,
    handleSubmit,
    handleSaveDraft,
  } = useManualIntakeForm({
    prefilledFields: state.prefilledFields,
  });

  // Mode-switch hook — manual → AI (US_029, AC-2, EC-2)
  const { switching, aiAvailable, switchToAI } = useIntakeModeSwitch();

  // Switch to AI intake: save draft first, then post to switch-ai endpoint, then navigate (AC-2)
  const handleSwitchToAI = useCallback(async () => {
    // Persist current draft so no data is lost during the transition (AC-3)
    await handleSaveDraft();
    // Call switch-ai; null return = graceful degradation (still navigate)
    await switchToAI(values);
    navigate('/patient/intake/ai', { state: { resumedFromManual: true } });
  }, [handleSaveDraft, switchToAI, values, navigate]);

  // Dismiss flag for the prefilled-from-AI banner
  const [prefilledBannerDismissed, setPrefilledBannerDismissed] = useState(false);

  // Redirect to dashboard after successful submission (AC-4)
  useEffect(() => {
    if (status === 'submitted') {
      const timer = setTimeout(() => navigate('/patient/dashboard'), 2000);
      return () => clearTimeout(timer);
    }
  }, [status, navigate]);

  const isSubmitting = status === 'submitting';
  const isLoading    = status === 'loading';
  const isSubmitted  = status === 'submitted';
  const isDisabled   = isLoading || isSubmitting || isSubmitted;

  const hasPrefill = prefilledKeys.size > 0;

  // ── Autosave relative timestamp (UXR-004) ──────────────────────────────────
  // When no transient status is active, show the last-saved wall-clock time.
  const autosaveLabel =
    autosaveStatus === 'saving'   ? 'Saving…' :
    autosaveStatus === 'retrying' ? 'Retrying save…' :
    autosaveStatus === 'saved'    ? 'Auto-saved' :
    lastSavedAt
      ? `Last saved ${new Date(lastSavedAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`
      : null;

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', minHeight: '100vh', bgcolor: 'grey.50' }}>
      {/* ── AppBar ──────────────────────────────────────────────────────────── */}
      <AppBar
        position="sticky"
        elevation={2}
        sx={{ bgcolor: 'background.paper', color: 'text.primary' }}
      >
        <Toolbar sx={{ gap: 2, justifyContent: 'space-between' }}>
          <Box>
            <Breadcrumbs aria-label="Breadcrumb" sx={{ fontSize: '0.875rem' }}>
              <Link
                component={RouterLink}
                to="/patient/dashboard"
                underline="hover"
                color="inherit"
                id="breadcrumb-dash"
              >
                Dashboard
              </Link>
              <Link
                component={RouterLink}
                to="/patient/intake/ai"
                underline="hover"
                color="inherit"
              >
                AI Intake
              </Link>
              <Typography color="text.primary" variant="body2">
                Manual Form
              </Typography>
            </Breadcrumbs>
            <Typography variant="h6" sx={{ fontWeight: 500 }}>
              Manual Intake Form
            </Typography>
          </Box>

          <Button
            id="back-ai"
            variant="outlined"
            size="small"
            disabled={switching || isDisabled}
            startIcon={<span aria-hidden="true">←</span>}
            onClick={handleSwitchToAI}
            aria-label="Switch back to AI intake"
          >
            {switching ? 'Switching…' : 'Switch to AI Intake'}
          </Button>
        </Toolbar>
      </AppBar>

      {/* ── Main content ────────────────────────────────────────────────────── */}
      <Container maxWidth="lg" sx={{ py: 4, flex: 1 }}>
        {/* Page title row */}
        <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 3 }}>
          <Typography variant="h5" component="h1">
            Patient Intake Form
          </Typography>
          {autosaveLabel && (
            <Typography variant="caption" color="text.secondary" aria-live="polite">
              {autosaveLabel}
            </Typography>
          )}
        </Box>

        {/* Info banners */}
        {!isSubmitted && (
          <Alert severity="info" sx={{ mb: 3 }} role="status">
            Fields marked with * are required. Your progress is auto-saved every 30 seconds.
          </Alert>
        )}

        {/* AI unavailable banner — shown when switch-to-AI returns 503 (EC-2, UXR-605) */}
        {!aiAvailable && (
          <IntakeModeSwitchBanner variant="ai-unavailable" />
        )}

        {/* EC-1 autosave failure banner — shown when both save attempts fail (US_030, EC-1) */}
        {autosaveStatus === 'failed' && (
          <Alert severity="warning" sx={{ mb: 3 }} role="alert">
            {AUTOSAVE_FAILED_MESSAGE}
          </Alert>
        )}

        {/* Pre-filled from AI intake banner (AC-2) — replaces inline success alert */}
        {hasPrefill && !isSubmitted && !prefilledBannerDismissed && (
          <IntakeModeSwitchBanner
            variant="prefilled-from-ai"
            onDismiss={() => setPrefilledBannerDismissed(true)}
          />
        )}

        {/* Minor booking-readiness warning (US_031 AC-1) */}
        {isMinor && isGuardianConsentRequired && !isSubmitted && (
          <Alert severity="warning" sx={{ mb: 3 }} role="alert">
            This patient is under 18. Please complete the Guardian Consent section before submitting.
          </Alert>
        )}

        {errorMessage && (
          <Alert severity="error" sx={{ mb: 3 }} role="alert">
            {errorMessage}
          </Alert>
        )}

        {/* Success banner */}
        {isSubmitted && (
          <Alert severity="success" sx={{ mb: 3 }} role="status">
            Your intake form has been submitted successfully. Redirecting to your dashboard…
          </Alert>
        )}

        {/* Loading skeleton */}
        {isLoading && (
          <Box sx={{ display: 'flex', justifyContent: 'center', mt: 8 }}>
            <CircularProgress aria-label="Loading intake form" />
          </Box>
        )}

        {/* Form card */}
        {!isLoading && (
          <Paper
            component="form"
            aria-label="Patient intake form"
            elevation={1}
            sx={{ p: { xs: 2, sm: 3 } }}
            onSubmit={(e) => {
              e.preventDefault();
              handleSubmit();
            }}
            noValidate
          >
            <ManualIntakeForm
              values={values}
              errors={errors}
              touched={touched}
              prefilledKeys={prefilledKeys}
              disabled={isDisabled}
              isMinor={isMinor}
              isGuardianAlsoMinor={isGuardianAlsoMinor}
              insurancePrecheckStatus={insurancePrecheckStatus}
              insurancePrecheckMessage={insurancePrecheckMessage}
              onChange={handleChange}
              onBlur={handleBlur}
            />

            <Divider sx={{ my: 3 }} />

            {/* Action buttons */}
            <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap' }}>
              <Button
                id="submit-intake"
                type="submit"
                variant="contained"
                size="large"
                disabled={isDisabled}
                startIcon={
                  isSubmitting ? (
                    <CircularProgress size={18} color="inherit" />
                  ) : (
                    <SendIcon />
                  )
                }
                aria-busy={isSubmitting}
              >
                {isSubmitting ? 'Submitting…' : 'Submit Intake Form'}
              </Button>

              <Button
                variant="outlined"
                size="large"
                disabled={isDisabled}
                startIcon={<SaveIcon />}
                onClick={handleSaveDraft}
              >
                Save Draft
              </Button>
            </Box>
          </Paper>
        )}
      </Container>
    </Box>
  );
}
