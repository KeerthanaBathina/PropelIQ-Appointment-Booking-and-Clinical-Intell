/**
 * MedicalCodingReviewPage — SCR-014 AI-assisted Medical Coding Review.
 *   - US_047: ICD-10 Diagnosis Codes (AI-generated)
 *   - US_048: CPT Procedure Codes (AI-generated, Approve/Override workflow)
 *   - US_049: Code Verification Workflow (Approve/Override queue, audit trail, progress)
 *
 * Route: /staff/patients/:patientId/coding
 *
 * Screen states (all 5 required):
 *   1. Loading    — skeleton header + table skeleton rows while codes fetch
 *   2. Error      — AI unavailable banner or generic error Alert + retry
 *   3. Empty      — "No pending codes for review" inside verification table
 *   4. Default    — Full layout: verification progress, queue, ICD-10 section, CPT section
 *   5. Validation — Inline field errors in CodeOverrideModal
 *
 * Layout follows wireframe SCR-014:
 *   AppBar breadcrumb → Verification progress bar → Verification queue →
 *   ICD-10 section → CPT section
 *   AiUnavailableBanner shown when CPT fetch errors (circuit-breaker / 503 scenario).
 *
 * Responsive:
 *   375px (xs)  — single-column card layout (table hidden, card list shown)
 *   768px (sm)  — condensed table (justification hidden via column display prop)
 *   1440px (xl) — full table with all columns
 *
 * Navigation: UXR-003 breadcrumb — Staff Dashboard → Patient Profile → Medical Coding
 */

import { useCallback, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import Alert from '@mui/material/Alert';
import AppBar from '@mui/material/AppBar';
import Box from '@mui/material/Box';
import Breadcrumbs from '@mui/material/Breadcrumbs';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Container from '@mui/material/Container';
import Divider from '@mui/material/Divider';
import Link from '@mui/material/Link';
import Skeleton from '@mui/material/Skeleton';
import Toolbar from '@mui/material/Toolbar';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import ArrowBackIcon from '@mui/icons-material/ArrowBack';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';
import HelpOutlineIcon from '@mui/icons-material/HelpOutline';
import PendingOutlinedIcon from '@mui/icons-material/PendingOutlined';
import ReviewsOutlinedIcon from '@mui/icons-material/ReviewsOutlined';

import Icd10CodeTable from '@/components/coding/Icd10CodeTable';
import AgreementRateAlert from '@/components/coding/AgreementRateAlert';
import AgreementRateSummary from '@/components/coding/AgreementRateSummary';
import CodingAiUnavailableBanner from '@/components/coding/CodingAiUnavailableBanner';
import CodingDiscrepancyTable from '@/components/coding/CodingDiscrepancyTable';
import ConfidenceBadge from '@/components/coding/ConfidenceBadge';
import MultiCodeAssignmentPanel from '@/components/coding/MultiCodeAssignmentPanel';
import PayerConflictDialog from '@/components/coding/PayerConflictDialog';
import PayerRuleAlert from '@/components/coding/PayerRuleAlert';
import VerificationProgressBar from '@/components/coding/VerificationProgressBar';
import VerificationQueueTable from '@/components/coding/VerificationQueueTable';
import type { Icd10CodeDto } from '@/hooks/useIcd10Codes';
import { useIcd10Codes } from '@/hooks/useIcd10Codes';
import {
  useLatestAgreementRate,
  useAgreementAlerts,
  useDiscrepancies,
} from '@/hooks/useAgreementRate';
import { usePayerValidation } from '@/hooks/usePayerValidation';
import type { PayerValidationResultDto } from '@/hooks/usePayerValidation';
import { useVerificationQueue } from '@/hooks/useVerificationQueue';
import { useVerificationProgress } from '@/hooks/useVerificationProgress';
import AiUnavailableBanner from '@/features/medical-coding/components/AiUnavailableBanner';
import CptCodeTable from '@/features/medical-coding/components/CptCodeTable';
import CptCodingSummary from '@/features/medical-coding/components/CptCodingSummary';
import { useCptCodes } from '@/features/medical-coding/hooks/useCptCodes';

// ─── Summary Stats ────────────────────────────────────────────────────────────

interface StatsBarProps {
  codes:     Icd10CodeDto[];
  isLoading: boolean;
}

function StatsBar({ codes, isLoading }: StatsBarProps) {
  if (isLoading) {
    return (
      <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap', mb: 3 }}>
        {Array.from({ length: 4 }).map((_, i) => (
          <Skeleton key={i} variant="rounded" width={120} height={72} sx={{ borderRadius: 2 }} />
        ))}
      </Box>
    );
  }

  const total      = codes.length;
  const approved   = codes.filter(c => !c.requiresReview && c.codeValue !== 'UNCODABLE').length;
  const pending    = codes.filter(c => c.requiresReview).length;
  const uncodable  = codes.filter(c => c.codeValue === 'UNCODABLE').length;

  const avgConfidence = total > 0
    ? codes.reduce((sum, c) => sum + c.confidenceScore, 0) / total
    : 0;

  const statItems = [
    {
      label:  'Total Codes',
      value:  total,
      icon:   <ReviewsOutlinedIcon fontSize="small" />,
      color:  'text.primary',
    },
    {
      label:  'Approved',
      value:  approved,
      icon:   <CheckCircleOutlineIcon fontSize="small" />,
      color:  'success.main',
    },
    {
      label:  'Needs Review',
      value:  pending,
      icon:   <PendingOutlinedIcon fontSize="small" />,
      color:  'warning.main',
    },
    {
      label:  'Uncodable',
      value:  uncodable,
      icon:   <HelpOutlineIcon fontSize="small" />,
      color:  uncodable > 0 ? 'error.main' : 'text.disabled',
    },
  ];

  return (
    <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap', mb: 3 }} role="region" aria-label="Coding summary statistics">
      {statItems.map(item => (
        <Card
          key={item.label}
          variant="outlined"
          sx={{ minWidth: 120, flex: '1 1 120px', borderRadius: 2 }}
        >
          <CardContent sx={{ p: '12px !important', display: 'flex', alignItems: 'center', gap: 1 }}>
            <Box sx={{ color: item.color }}>{item.icon}</Box>
            <Box>
              <Typography variant="h5" component="div" fontWeight={700} color={item.color}>
                {item.value}
              </Typography>
              <Typography variant="caption" color="text.secondary">
                {item.label}
              </Typography>
            </Box>
          </CardContent>
        </Card>
      ))}

      {/* AI-Human Agreement */}
      {total > 0 && (
        <Card variant="outlined" sx={{ minWidth: 160, flex: '1 1 160px', borderRadius: 2 }}>
          <CardContent sx={{ p: '12px !important' }}>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
              <Typography variant="caption" color="text.secondary" sx={{ flex: 1 }}>
                Avg AI Confidence
              </Typography>
              <Tooltip title="Average confidence across all AI code suggestions">
                <HelpOutlineIcon fontSize="inherit" sx={{ color: 'text.disabled', cursor: 'default' }} />
              </Tooltip>
            </Box>
            <Box sx={{ mt: 0.5 }}>
              <ConfidenceBadge score={avgConfidence} />
            </Box>
          </CardContent>
        </Card>
      )}
    </Box>
  );
}

// ─── Page ─────────────────────────────────────────────────────────────────────

export default function MedicalCodingReviewPage() {
  const { patientId = '' } = useParams<{ patientId: string }>();
  const navigate = useNavigate();

  // ── ICD-10 data ───────────────────────────────────────────────────────────
  const { data, isLoading, isError, refetch } = useIcd10Codes(patientId);

  // ── CPT data ──────────────────────────────────────────────────────────────
  const {
    data:       cptData,
    isLoading:  cptLoading,
    isError:    cptIsError,
    refetch:    cptRefetch,
  } = useCptCodes(patientId);

  const cptCodes = cptData?.codes ?? [];

  // ── US_049 Verification queue + progress ──────────────────────────────────
  const {
    items:     verificationItems,
    isLoading: verificationLoading,
    isError:   verificationError,
    refetch:   verificationRefetch,
  } = useVerificationQueue(patientId);

  const {
    progress:  verificationProgress,
    isLoading: progressLoading,
  } = useVerificationProgress(patientId);

  // ── US_050 Agreement rate data ───────────────────────────────────────────
  const {
    data:      agreementMetrics,
    isLoading: agreementLoading,
    isError:   agreementError,
    refetch:   agreementRefetch,
  } = useLatestAgreementRate();

  const {
    data:      agreementAlerts,
  } = useAgreementAlerts();

  const {
    data:      discrepancies,
    isLoading: discrepanciesLoading,
    isError:   discrepanciesError,
    refetch:   discrepanciesRefetch,
  } = useDiscrepancies();

  // ── US_051 Payer rule validation ──────────────────────────────────────────
  const {
    data:              payerValidation,
    isLoading:         payerLoading,
    isError:           payerError,
    refetch:           payerRefetch,
    payerStatusByCode,
  } = usePayerValidation(patientId);

  const [conflictDialog, setConflictDialog] = useState<{
    open:         boolean;
    result:       PayerValidationResultDto | null;
    clinicalCode: string;
  }>({ open: false, result: null, clinicalCode: '' });

  const handleResolveConflict = useCallback((result: PayerValidationResultDto) => {
    const firstCode = result.affected_codes[0] ?? '';
    setConflictDialog({ open: true, result, clinicalCode: firstCode });
  }, []);

  const handleConflictClose = useCallback(() => {
    setConflictDialog(prev => ({ ...prev, open: false }));
  }, []);

  // Scroll target for the discrepancy section (from AgreementRateAlert link)
  const scrollToDiscrepancies = useCallback(() => {
    document.getElementById('discrepancy-section-heading')?.scrollIntoView({ behavior: 'smooth' });
  }, []);

  // CPT AI unavailable banner — dismissed by staff (UXR-605)
  const [cptAiBannerDismissed, setCptAiBannerDismissed] = useState(false);

  // ICD-10 manual-coding mode (UXR-605 for ICD-10 path)
  const [manualMode, setManualMode] = useState(false);

  const handleSwitchToManual = useCallback(() => {
    setManualMode(true);
  }, []);

  const codes = data?.codes ?? [];

  // Determine if ICD-10 error is AI-specific (circuit breaker / 503)
  const isAiUnavailable = isError;

  // ── Breadcrumb back link ─────────────────────────────────────────────────
  const handleBack = useCallback(() => {
    navigate(`/staff/patients/${patientId}/profile`);
  }, [navigate, patientId]);

  return (
    <Box sx={{ minHeight: '100vh', bgcolor: 'grey.50' }}>
      {/* ── AppBar ── */}
      <AppBar position="sticky" color="default" elevation={1} sx={{ bgcolor: 'background.paper' }}>
        <Toolbar sx={{ gap: 1 }}>
          <Button
            startIcon={<ArrowBackIcon />}
            onClick={handleBack}
            size="small"
            aria-label="Back to patient profile"
            sx={{ mr: 1 }}
          >
            Back
          </Button>

          <Breadcrumbs aria-label="Page breadcrumb" sx={{ flex: 1 }}>
            <Link
              component="button"
              variant="body2"
              underline="hover"
              color="text.secondary"
              onClick={() => navigate('/staff/dashboard')}
            >
              Staff Dashboard
            </Link>
            <Link
              component="button"
              variant="body2"
              underline="hover"
              color="text.secondary"
              onClick={handleBack}
            >
              Patient Profile
            </Link>
            <Typography variant="body2" color="text.primary" aria-current="page">
              Medical Coding
            </Typography>
          </Breadcrumbs>
        </Toolbar>
      </AppBar>

      {/* ── Main content ── */}
      <Container maxWidth="xl" sx={{ py: 3 }}>
        {/* ── Page heading ── */}
        <Box sx={{ mb: 3 }}>
          {isLoading ? (
            <Skeleton variant="text" width={280} height={40} />
          ) : (
            <Typography variant="h5" component="h1" fontWeight={700}>
              AI-Assisted Medical Coding Review
            </Typography>
          )}
          {isLoading ? (
            <Skeleton variant="text" width={200} sx={{ mt: 0.5 }} />
          ) : data?.lastCodingRunAt ? (
            <Typography variant="body2" color="text.secondary">
              Last coding run:{' '}
              {new Date(data.lastCodingRunAt).toLocaleString(undefined, {
                dateStyle: 'medium',
                timeStyle: 'short',
              })}
            </Typography>
          ) : null}
        </Box>

        {/* ── AI Unavailable banner (state: Error — AI-specific) ── */}
        {!isLoading && (
          <CodingAiUnavailableBanner
            isAiUnavailable={isAiUnavailable && !manualMode}
            onSwitchToManual={handleSwitchToManual}
          />
        )}

        {/* ── Manual mode notice ── */}
        {manualMode && (
          <Alert
            severity="info"
            role="status"
            aria-live="polite"
            sx={{ mb: 2 }}
            onClose={() => setManualMode(false)}
          >
            Manual coding mode active — locate and enter ICD-10 codes manually using your coding reference.
          </Alert>
        )}

        {/* ── Summary stats bar ── */}
        <StatsBar codes={codes} isLoading={isLoading} />

        <Divider sx={{ mb: 3 }} />

        {/* ── US_050: Agreement Rate Dashboard section ── */}
        <Box component="section" aria-labelledby="agreement-rate-section-heading" sx={{ mb: 4 }}>
          <Typography
            id="agreement-rate-section-heading"
            variant="h6"
            component="h2"
            fontWeight={600}
            sx={{ mb: 2 }}
          >
            AI-Human Agreement Rate
          </Typography>

          {/* Alert banner: below-threshold warning, not-enough-data, AI unavailable */}
          <AgreementRateAlert
            metrics={agreementMetrics}
            latestAlert={agreementAlerts.length > 0 ? agreementAlerts[0] : null}
            aiUnavailable={agreementError}
            onViewDiscrepancies={scrollToDiscrepancies}
          />

          {/* 5-card summary grid */}
          <AgreementRateSummary
            data={agreementMetrics}
            isLoading={agreementLoading}
          />

          {/* Error state with retry (UXR-601) */}
          {!agreementLoading && agreementError && (
            <Alert
              severity="error"
              role="alert"
              action={
                <Button color="inherit" size="small" onClick={() => agreementRefetch()} aria-label="Retry loading agreement rate">
                  Retry
                </Button>
              }
              sx={{ mb: 2 }}
            >
              Failed to load agreement rate metrics.
            </Alert>
          )}

          {/* Discrepancy breakdown table */}
          <Box component="section" aria-labelledby="discrepancy-section-heading" sx={{ mt: 3 }}>
            <Typography
              id="discrepancy-section-heading"
              variant="subtitle1"
              component="h3"
              fontWeight={600}
              sx={{ mb: 1.5 }}
            >
              Coding Discrepancy Breakdown
            </Typography>
            <CodingDiscrepancyTable
              data={discrepancies}
              isLoading={discrepanciesLoading}
              isError={discrepanciesError}
              onRetry={discrepanciesRefetch}
            />
          </Box>
        </Box>

        <Divider sx={{ mb: 3 }} />

        {/* ── US_049: Verification Queue section ── */}
        <Box component="section" aria-labelledby="verification-section-heading" sx={{ mb: 4 }}>
          <Typography
            id="verification-section-heading"
            variant="h6"
            component="h2"
            fontWeight={600}
            sx={{ mb: 2 }}
          >
            Code Verification Queue
          </Typography>

          {/* Verification progress bar (EC-2) */}
          <VerificationProgressBar
            progress={verificationProgress}
            isLoading={progressLoading}
          />

          {/* Verification queue table */}
          <VerificationQueueTable
            patientId={patientId}
            items={verificationItems}
            isLoading={verificationLoading}
            isError={verificationError}
            onRetry={verificationRefetch}
          />
        </Box>

        <Divider sx={{ mb: 3 }} />

        {/* ── US_051: Payer Rule Validation section ── */}
        <Box component="section" aria-labelledby="payer-validation-section-heading" sx={{ mb: 4 }}>
          <Typography
            id="payer-validation-section-heading"
            variant="h6"
            component="h2"
            fontWeight={600}
            sx={{ mb: 2 }}
          >
            Payer Rule Validation
          </Typography>
          <PayerRuleAlert
            validationResults={payerValidation?.validation_results ?? []}
            denialRisks={payerValidation?.denial_risks ?? []}
            isManualReview={manualMode}
            payerName={payerValidation?.payer_name}
            isLoading={payerLoading}
            onResolveConflict={handleResolveConflict}
          />
          <MultiCodeAssignmentPanel
            patientId={patientId}
            onAssigned={() => void payerRefetch()}
          />
          {payerError && (
            <Alert severity="error" sx={{ mt: 2 }} role="alert">
              Failed to load payer validation rules.
            </Alert>
          )}
        </Box>

        <Divider sx={{ mb: 3 }} />

        {/* ── ICD-10 Code Table section ── */}
        <Box component="section" aria-labelledby="icd10-section-heading">
          <Typography
            id="icd10-section-heading"
            variant="h6"
            component="h2"
            fontWeight={600}
            sx={{ mb: 2 }}
          >
            ICD-10 Diagnosis Codes
          </Typography>

          {data?.unmappedDiagnosisIds && data.unmappedDiagnosisIds.length > 0 && (
            <Alert severity="info" sx={{ mb: 2 }} role="status">
              {data.unmappedDiagnosisIds.length === 1
                ? '1 diagnosis returned no code suggestions.'
                : `${data.unmappedDiagnosisIds.length} diagnoses returned no code suggestions.`}{' '}
              These may need manual review.
            </Alert>
          )}

          <Icd10CodeTable
            codes={codes}
            isLoading={isLoading}
            isError={isError}
            onRetry={refetch}
            payerStatusByCode={Object.keys(payerStatusByCode).length > 0 ? payerStatusByCode : undefined}
          />
        </Box>

        <Divider sx={{ my: 4 }} />

        {/* ── CPT Procedure Codes section (US_048) ── */}
        <Box component="section" aria-labelledby="cpt-section-heading">
          <Typography
            id="cpt-section-heading"
            variant="h6"
            component="h2"
            fontWeight={600}
            sx={{ mb: 2 }}
          >
            CPT Procedure Codes
          </Typography>

          {/* CPT AI unavailable banner (UXR-605) */}
          <AiUnavailableBanner
            isVisible={cptIsError && !cptAiBannerDismissed}
            onDismiss={() => setCptAiBannerDismissed(true)}
          />

          {/* CPT summary stats */}
          <CptCodingSummary codes={cptCodes} isLoading={cptLoading} />

          {/* CPT table */}
          <CptCodeTable
            codes={cptCodes}
            patientId={patientId}
            isLoading={cptLoading}
            isError={cptIsError}
            onRetry={cptRefetch}
            payerStatusByCode={Object.keys(payerStatusByCode).length > 0 ? payerStatusByCode : undefined}
          />
        </Box>

        {/* Payer conflict resolution dialog */}
        <PayerConflictDialog
          open={conflictDialog.open}
          result={conflictDialog.result}
          clinicalCode={conflictDialog.clinicalCode}
          patientId={patientId}
          onClose={handleConflictClose}
          onResolved={() => void payerRefetch()}
        />
      </Container>
    </Box>
  );
}
