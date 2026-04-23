/**
 * PayerRuleAlert — MUI Alert component for payer rule violations and claim denial risks.
 * (US_051, AC-1, AC-2, SCR-014)
 *
 * Renders one collapsible alert per validation result, severity-based:
 *   error   → Denial Risk (red)    — high probability of claim denial
 *   warning → Review Needed (amber) — review recommended before submission
 *   info    → Advisory (blue)       — informational guidance
 *
 * Each alert shows:
 *   • Rule ID + description
 *   • Affected code values (chips)
 *   • Corrective actions list (AlternativeCode, AddModifier, Documentation, ManualReview)
 *   • "CMS Default" badge when payer-specific rules unavailable (edge case)
 *   • "Resolve Conflict" link → opens PayerConflictDialog when conflict detected
 *
 * Uses confidence color tokens from designsystem.md:
 *   error-500:   #D32F2F  (denial risk)
 *   warning-500: #ED6C02  (review required)
 *   info:        #0288D1  (advisory)
 */

import Alert from '@mui/material/Alert';
import AlertTitle from '@mui/material/AlertTitle';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Collapse from '@mui/material/Collapse';
import Divider from '@mui/material/Divider';
import List from '@mui/material/List';
import ListItem from '@mui/material/ListItem';
import ListItemIcon from '@mui/material/ListItemIcon';
import ListItemText from '@mui/material/ListItemText';
import Skeleton from '@mui/material/Skeleton';
import Typography from '@mui/material/Typography';
import AddCircleOutlineIcon from '@mui/icons-material/AddCircleOutline';
import ArticleOutlinedIcon from '@mui/icons-material/ArticleOutlined';
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined';
import SwapHorizIcon from '@mui/icons-material/SwapHoriz';
import WarningAmberIcon from '@mui/icons-material/WarningAmber';
import { useState } from 'react';

import type {
  PayerValidationResultDto,
  ClaimDenialRiskDto,
  CorrectiveActionDto,
} from '@/hooks/usePayerValidation';

// ─── Helpers ──────────────────────────────────────────────────────────────────

function correctiveActionIcon(actionType: string) {
  switch (actionType) {
    case 'AlternativeCode':      return <SwapHorizIcon fontSize="small" />;
    case 'AddModifier':          return <AddCircleOutlineIcon fontSize="small" />;
    case 'DocumentationRequired': return <ArticleOutlinedIcon fontSize="small" />;
    default:                      return <InfoOutlinedIcon fontSize="small" />;
  }
}

function riskChipColor(level: 'high' | 'medium' | 'low'): 'error' | 'warning' | 'success' {
  if (level === 'high')   return 'error';
  if (level === 'medium') return 'warning';
  return 'success';
}

// ─── Sub-component: single corrective action row ─────────────────────────────

function CorrectiveActionItem({ action }: { action: CorrectiveActionDto }) {
  return (
    <ListItem disableGutters sx={{ py: 0.25 }}>
      <ListItemIcon sx={{ minWidth: 28, color: 'text.secondary' }}>
        {correctiveActionIcon(action.action_type)}
      </ListItemIcon>
      <ListItemText
        primary={
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75, flexWrap: 'wrap' }}>
            <Typography variant="caption" sx={{ fontSize: '0.8125rem' }}>
              {action.description}
            </Typography>
            {action.suggested_code && (
              <Chip
                label={action.suggested_code}
                size="small"
                variant="outlined"
                sx={{ fontFamily: 'monospace', fontSize: '0.7rem', height: 18 }}
              />
            )}
            {action.suggested_modifier && (
              <Chip
                label={`Mod. ${action.suggested_modifier}`}
                size="small"
                color="primary"
                variant="outlined"
                sx={{ fontSize: '0.7rem', height: 18 }}
              />
            )}
          </Box>
        }
      />
    </ListItem>
  );
}

// ─── Sub-component: single validation result alert ───────────────────────────

interface ValidationAlertRowProps {
  result:          PayerValidationResultDto;
  onResolveConflict?: (result: PayerValidationResultDto) => void;
}

function ValidationAlertRow({ result, onResolveConflict }: ValidationAlertRowProps) {
  const [expanded, setExpanded] = useState(result.severity === 'error');

  const title = result.severity === 'error'   ? 'Claim Denial Risk'
              : result.severity === 'warning' ? 'Review Recommended'
              : 'Advisory';

  return (
    <Alert
      severity={result.severity}
      role={result.severity === 'error' ? 'alert' : 'status'}
      aria-live={result.severity === 'error' ? 'assertive' : 'polite'}
      icon={result.severity === 'warning' ? <WarningAmberIcon fontSize="inherit" /> : undefined}
      sx={{ mb: 1, alignItems: 'flex-start' }}
    >
      <AlertTitle sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
        {title}
        {result.is_cms_default && (
          <Chip
            label="CMS Default"
            size="small"
            color="default"
            variant="outlined"
            sx={{ fontSize: '0.65rem', height: 18 }}
            aria-label="CMS default rules applied — payer-specific rules unavailable"
          />
        )}
        <Typography
          component="span"
          variant="caption"
          color="text.secondary"
          sx={{ fontFamily: 'monospace' }}
        >
          Rule: {result.rule_id}
        </Typography>
      </AlertTitle>

      <Typography variant="body2" sx={{ mb: 1 }}>
        {result.description}
      </Typography>

      {/* Affected codes */}
      {result.affected_codes.length > 0 && (
        <Box sx={{ display: 'flex', gap: 0.5, flexWrap: 'wrap', mb: 1 }}>
          {result.affected_codes.map(code => (
            <Chip
              key={code}
              label={code}
              size="small"
              variant="filled"
              sx={{
                fontFamily: 'monospace',
                fontWeight:  700,
                fontSize:   '0.75rem',
                bgcolor:    result.severity === 'error'   ? 'error.50'
                          : result.severity === 'warning' ? 'warning.50'
                          : 'info.50',
              }}
              aria-label={`Affected code: ${code}`}
            />
          ))}
        </Box>
      )}

      {/* Corrective actions (collapsible) */}
      {result.corrective_actions.length > 0 && (
        <>
          <Button
            size="small"
            variant="text"
            onClick={() => setExpanded(e => !e)}
            aria-expanded={expanded}
            aria-controls={`actions-${result.rule_id}`}
            sx={{ p: 0, minWidth: 0, textTransform: 'none', fontSize: '0.8125rem' }}
          >
            {expanded ? 'Hide' : 'Show'} {result.corrective_actions.length} corrective action
            {result.corrective_actions.length === 1 ? '' : 's'}
          </Button>

          <Collapse in={expanded} id={`actions-${result.rule_id}`}>
            <Divider sx={{ my: 0.75 }} />
            <List dense disablePadding>
              {result.corrective_actions.map((a, i) => (
                <CorrectiveActionItem key={i} action={a} />
              ))}
            </List>
          </Collapse>
        </>
      )}

      {/* Resolve conflict link */}
      {onResolveConflict && (
        <Box sx={{ mt: 1 }}>
          <Button
            size="small"
            variant="outlined"
            color={result.severity === 'error' ? 'error' : 'warning'}
            onClick={() => onResolveConflict(result)}
            aria-label={`Resolve payer conflict for rule ${result.rule_id}`}
          >
            Resolve Conflict
          </Button>
        </Box>
      )}
    </Alert>
  );
}

// ─── Sub-component: claim denial risk alert ───────────────────────────────────

interface DenialRiskAlertProps {
  risk: ClaimDenialRiskDto;
}

function DenialRiskAlert({ risk }: DenialRiskAlertProps) {
  const [expanded, setExpanded] = useState(risk.risk_level === 'high');

  return (
    <Alert
      severity={risk.risk_level === 'high' ? 'error' : 'warning'}
      role={risk.risk_level === 'high' ? 'alert' : 'status'}
      sx={{ mb: 1, alignItems: 'flex-start' }}
    >
      <AlertTitle sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
        Claim Denial Risk
        <Chip
          label={`${risk.risk_level.charAt(0).toUpperCase() + risk.risk_level.slice(1)} Risk`}
          size="small"
          color={riskChipColor(risk.risk_level)}
          sx={{ fontSize: '0.65rem', height: 18 }}
        />
        {risk.historical_denial_rate != null && (
          <Typography variant="caption" color="text.secondary">
            {(risk.historical_denial_rate * 100).toFixed(0)}% historical denial rate
          </Typography>
        )}
      </AlertTitle>

      <Typography variant="body2" sx={{ mb: 1 }}>
        {risk.denial_reason}
      </Typography>

      <Box sx={{ display: 'flex', gap: 0.5, flexWrap: 'wrap', mb: 1 }}>
        {risk.code_pair.map(code => (
          <Chip
            key={code}
            label={code}
            size="small"
            sx={{ fontFamily: 'monospace', fontWeight: 700, fontSize: '0.75rem' }}
            aria-label={`Code in denial-risk pair: ${code}`}
          />
        ))}
      </Box>

      {risk.corrective_actions.length > 0 && (
        <>
          <Button
            size="small"
            variant="text"
            onClick={() => setExpanded(e => !e)}
            aria-expanded={expanded}
            sx={{ p: 0, minWidth: 0, textTransform: 'none', fontSize: '0.8125rem' }}
          >
            {expanded ? 'Hide' : 'Show'} corrective actions
          </Button>
          <Collapse in={expanded}>
            <List dense disablePadding sx={{ mt: 0.5 }}>
              {risk.corrective_actions.map((a, i) => (
                <CorrectiveActionItem key={i} action={a} />
              ))}
            </List>
          </Collapse>
        </>
      )}
    </Alert>
  );
}

// ─── Props ────────────────────────────────────────────────────────────────────

export interface PayerRuleAlertProps {
  validationResults:   PayerValidationResultDto[];
  denialRisks:         ClaimDenialRiskDto[];
  isManualReview?:     boolean;
  payerName?:          string;
  isLoading?:          boolean;
  /** Called when user clicks "Resolve Conflict" on a validation alert. */
  onResolveConflict?:  (result: PayerValidationResultDto) => void;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function PayerRuleAlert({
  validationResults,
  denialRisks,
  isManualReview = false,
  payerName,
  isLoading = false,
  onResolveConflict,
}: PayerRuleAlertProps) {
  if (isLoading) {
    return (
      <Box sx={{ mb: 2 }} aria-busy="true" aria-label="Loading payer validation">
        <Skeleton variant="rounded" height={72} sx={{ mb: 1, borderRadius: 2 }} />
        <Skeleton variant="rounded" height={56} sx={{ borderRadius: 2 }} />
      </Box>
    );
  }

  if (validationResults.length === 0 && denialRisks.length === 0 && !isManualReview) {
    return null;
  }

  return (
    <Box
      component="section"
      aria-labelledby="payer-alert-heading"
      sx={{ mb: 2 }}
    >
      <Typography
        id="payer-alert-heading"
        variant="subtitle2"
        fontWeight={600}
        sx={{ mb: 1 }}
      >
        Payer Rule Validation
        {payerName && (
          <Typography component="span" variant="caption" color="text.secondary" sx={{ ml: 1 }}>
            ({payerName})
          </Typography>
        )}
      </Typography>

      {/* Manual review required (unknown payer — CMS defaults applied) */}
      {isManualReview && (
        <Alert severity="info" role="status" sx={{ mb: 1 }}>
          Payer rules not found. CMS default rules applied. This encounter is flagged for
          manual payer rule verification before submission.
        </Alert>
      )}

      {/* Validation results (error then warning then info) */}
      {[...validationResults]
        .sort((a, b) => {
          const order = { error: 0, warning: 1, info: 2 };
          return order[a.severity] - order[b.severity];
        })
        .map(result => (
          <ValidationAlertRow
            key={result.rule_id}
            result={result}
            onResolveConflict={onResolveConflict}
          />
        ))}

      {/* Denial risks */}
      {[...denialRisks]
        .sort((a, b) => {
          const order = { high: 0, medium: 1, low: 2 };
          return order[a.risk_level] - order[b.risk_level];
        })
        .map((risk, i) => (
          <DenialRiskAlert key={i} risk={risk} />
        ))}
    </Box>
  );
}
