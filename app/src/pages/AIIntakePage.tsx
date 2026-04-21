/**
 * AIIntakePage — SCR-008 AI Conversational Intake (US_027, FL-004).
 *
 * Layout (wireframe-SCR-008-ai-intake.html):
 *   - AppBar + breadcrumb (Dashboard › AI Intake)
 *   - Card containing:
 *       - Chat header: "AI Health Assistant" badge + "Switch to Manual Form" button
 *       - IntakeProgressHeader: progress bar + autosave
 *       - IntakeChatMessageList: scrollable conversation log (role="log")
 *       - Input composer: text field + Send button
 *   - Summary review state: collected fields table + Confirm/Edit back actions (AC-4)
 *   - UXR-605: "AI Unavailable" banner with switch-to-manual fallback
 *   - EC-2: session resume on re-entry (handled in useAIIntakeSession)
 *
 * Breakpoints: 375px (single-column), 768px, 1440px (maxWidth="lg" Container).
 * Accessibility: live region on message list, focus on input after AI reply (AC-2).
 */

import { useState, useEffect, useRef, useCallback, KeyboardEvent } from 'react';
import { Link as RouterLink, useNavigate, useLocation } from 'react-router-dom';
import Alert from '@mui/material/Alert';
import AppBar from '@mui/material/AppBar';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import Container from '@mui/material/Container';
import Divider from '@mui/material/Divider';
import IconButton from '@mui/material/IconButton';
import Paper from '@mui/material/Paper';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableRow from '@mui/material/TableRow';
import TextField from '@mui/material/TextField';
import Toolbar from '@mui/material/Toolbar';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import SendIcon from '@mui/icons-material/Send';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';
import IntakeChatMessageList from '@/components/intake/IntakeChatMessageList';
import IntakeConflictNotice from '@/components/intake/IntakeConflictNotice';
import IntakeModeSwitchBanner from '@/components/intake/IntakeModeSwitchBanner';
import IntakeProgressHeader from '@/components/intake/IntakeProgressHeader';
import { useAIIntakeSession } from '@/hooks/useAIIntakeSession';

// ─── Component ────────────────────────────────────────────────────────────────

// Location state passed when navigating back from ManualIntakePage (AC-2, AC-3)
interface AIIntakeLocationState {
  /** True when the patient switched back from the manual form to AI intake (US_029). */
  resumedFromManual?: boolean;
}

export default function AIIntakePage() {
  const navigate  = useNavigate();
  const location  = useLocation();
  const locState  = (location.state ?? {}) as AIIntakeLocationState;

  const [inputValue, setInputValue] = useState('');
  // Dismissal flag for the "resumed from manual" info banner
  const [resumedBannerDismissed, setResumedBannerDismissed] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  const {
    sessionState,
    messages,
    collectedCount,
    totalRequired,
    lastSavedAt,
    autosaveStatus,
    summary,
    errorMessage,
    startSession,
    sendMessage,
    completeIntake,
    switchToManual,
    backToChat,
    reset,
  } = useAIIntakeSession();

  // ── Switch-to-manual handler: call backend endpoint then navigate (FL-004) ──
  const handleSwitchToManual = useCallback(async () => {
    const prefilledFields = await switchToManual();
    navigate('/patient/intake/manual', { state: { prefilledFields } });
  }, [switchToManual, navigate]);

  // Start session on mount (EC-2: backend decides whether to start new or resume)
  useEffect(() => {
    startSession();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Focus input after AI replies (AC-2: respond within 1 second)
  useEffect(() => {
    if (sessionState === 'active') {
      inputRef.current?.focus();
    }
  }, [sessionState, messages.length]);

  // ── Send handler ──────────────────────────────────────────────────────────

  const handleSend = useCallback(async () => {
    const trimmed = inputValue.trim();
    if (!trimmed || sessionState !== 'active') return;
    setInputValue('');
    await sendMessage(trimmed);
  }, [inputValue, sessionState, sendMessage]);

  const handleKeyDown = useCallback(
    (e: KeyboardEvent<HTMLInputElement>) => {
      if (e.key === 'Enter' && !e.shiftKey) {
        e.preventDefault();
        handleSend();
      }
    },
    [handleSend],
  );

  // ── Completion handler ────────────────────────────────────────────────────

  const handleComplete = useCallback(async () => {
    await completeIntake();
  }, [completeIntake]);

  // ── Redirect on success ───────────────────────────────────────────────────

  useEffect(() => {
    if (sessionState === 'completed') {
      // Small delay so the user can see the completed state before navigating
      const timer = setTimeout(() => navigate('/patient/dashboard'), 1800);
      return () => clearTimeout(timer);
    }
  }, [sessionState, navigate]);

  // ── Derived booleans ──────────────────────────────────────────────────────

  const isAiTyping   = sessionState === 'ai_typing';
  const isStarting   = sessionState === 'starting' || sessionState === 'idle';
  const isSummary    = sessionState === 'summary';
  const isCompleted  = sessionState === 'completed';
  const isCompleting = sessionState === 'completing';
  const isUnavailable = sessionState === 'unavailable';
  const isActive     = sessionState === 'active';
  const inputDisabled = !isActive;

  // ─────────────────────────────────────────────────────────────────────────
  // Render
  // ─────────────────────────────────────────────────────────────────────────

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', minHeight: '100vh' }}>
      {/* Top AppBar */}
      <AppBar position="static" sx={{ bgcolor: 'primary.main' }}>
        <Toolbar>
          <Typography variant="h6" sx={{ flexGrow: 1 }}>
            UPACIP — Patient Portal
          </Typography>
        </Toolbar>
      </AppBar>

      <Container maxWidth="lg" sx={{ mt: 2, pb: 4, flex: 1, display: 'flex', flexDirection: 'column' }}>
        {/* Breadcrumb (UXR-003) */}
        <Box sx={{ display: 'flex', gap: 1, alignItems: 'center', mb: 2 }} aria-label="Breadcrumb">
          <Typography
            variant="body2"
            component={RouterLink}
            to="/patient/dashboard"
            id="breadcrumb-dash"
            sx={{ color: 'primary.main', textDecoration: 'none', '&:hover': { textDecoration: 'underline' } }}
          >
            Dashboard
          </Typography>
          <Typography variant="body2" color="text.secondary">/</Typography>
          <Typography variant="body2" color="text.secondary" aria-current="page">
            AI Intake
          </Typography>
        </Box>

        {/* AI Unavailable banner (UXR-605, EC-2) — shared component */}
        {isUnavailable && (
          <IntakeModeSwitchBanner
            variant="ai-unavailable"
            onSwitchToManual={handleSwitchToManual}
          />
        )}

        {/* Resumed-from-manual banner (US_029, AC-2) */}
        {locState.resumedFromManual && !resumedBannerDismissed && (
          <IntakeModeSwitchBanner
            variant="resumed-from-manual"
            onDismiss={() => setResumedBannerDismissed(true)}
          />
        )}

        {/* Error banner */}
        {sessionState === 'error' && errorMessage && (
          <Alert
            severity="error"
            sx={{ mb: 2 }}
            action={
              <Button color="inherit" size="small" onClick={reset}>
                Retry
              </Button>
            }
          >
            {errorMessage}
          </Alert>
        )}

        {/* Completed success message */}
        {isCompleted && (
          <Alert severity="success" icon={<CheckCircleOutlineIcon />} sx={{ mb: 2 }}>
            Intake complete! Redirecting to your dashboard…
          </Alert>
        )}

        {/* ── Main chat card ─────────────────────────────────────────────── */}
        <Paper
          variant="outlined"
          sx={{
            display: 'flex',
            flexDirection: 'column',
            flex: 1,
            minHeight: { xs: 480, sm: 560 },
            maxHeight: { xs: 'calc(100vh - 200px)', md: 'calc(100vh - 180px)' },
            overflow: 'hidden',
          }}
        >
          {/* Chat header */}
          <Box
            sx={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              px: 2,
              py: 1.5,
              borderBottom: '1px solid',
              borderColor: 'grey.200',
              flexWrap: 'wrap',
              gap: 1,
            }}
          >
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
              <Typography variant="subtitle1" fontWeight={600}>
                AI Health Assistant
              </Typography>
              {!isUnavailable && (
                <Chip
                  label={isStarting ? 'Connecting…' : 'Active'}
                  size="small"
                  color={isStarting ? 'default' : 'success'}
                  sx={{ height: 20, fontSize: '0.7rem' }}
                />
              )}
            </Box>

            {/* Switch to Manual Form — always visible (FL-004 requirement) */}
            <Button
              variant="outlined"
              size="small"
              onClick={handleSwitchToManual}
              id="switch-manual-header"
              aria-label="Switch to manual intake form"
            >
              Switch to Manual Form
            </Button>
          </Box>

          {/* Progress header (AC-5) */}
          <IntakeProgressHeader
            collectedCount={collectedCount}
            totalRequired={totalRequired}
            lastSavedAt={lastSavedAt}
            autosaveStatus={autosaveStatus}
            isStarting={isStarting}
          />

          {/* ── Summary review state (AC-4) ─────────────────────────────── */}
          {isSummary && summary ? (
            <Box sx={{ flex: 1, overflowY: 'auto', p: 2 }}>
              <Typography variant="h6" gutterBottom>
                Review Your Intake Information
              </Typography>
              <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                Please review the information collected. Click "Edit" to correct any field before
                submitting.
              </Typography>

              <Table size="small" aria-label="Collected intake fields">
                <TableBody>
                  {summary.fields.map((field) => (
                    <TableRow key={field.key}>
                      <TableCell
                        component="th"
                        scope="row"
                        sx={{ fontWeight: 600, width: '35%', verticalAlign: 'top' }}
                      >
                        {field.label}
                      </TableCell>
                      <TableCell sx={{ verticalAlign: 'top' }}>
                        {field.value}
                        {/* Conflict notice (US_029, EC-1): show when manual/AI values differ */}
                        {field.alternateValue && field.alternateSource && (
                          <IntakeConflictNotice
                            alternateValue={field.alternateValue}
                            overriddenSource={field.alternateSource}
                          />
                        )}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>

              <Divider sx={{ my: 2 }} />

              <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap' }}>
                <Button
                  variant="contained"
                  onClick={handleComplete}
                  disabled={isCompleting}
                  startIcon={isCompleting ? <CircularProgress size={16} /> : undefined}
                  id="confirm-intake"
                >
                  {isCompleting ? 'Submitting…' : 'Confirm & Submit'}
                </Button>
                <Button variant="outlined" onClick={backToChat}>
                  Edit Responses
                </Button>
              </Box>

              {errorMessage && (
                <Alert severity="error" sx={{ mt: 2 }}>
                  {errorMessage}
                </Alert>
              )}
            </Box>
          ) : (
            <>
              {/* ── Conversation log ──────────────────────────────────────── */}
              <IntakeChatMessageList messages={messages} isAiTyping={isAiTyping} />

              {/* ── Input composer ───────────────────────────────────────── */}
              <Box
                component="form"
                onSubmit={(e) => { e.preventDefault(); handleSend(); }}
                sx={{
                  display: 'flex',
                  gap: 1,
                  p: 2,
                  borderTop: '1px solid',
                  borderColor: 'grey.200',
                  alignItems: 'flex-end',
                }}
              >
                <TextField
                  inputRef={inputRef}
                  value={inputValue}
                  onChange={(e) => setInputValue(e.target.value)}
                  onKeyDown={handleKeyDown}
                  placeholder="Type your response…"
                  aria-label="Type your response"
                  disabled={inputDisabled || isCompleted}
                  fullWidth
                  size="small"
                  multiline
                  maxRows={4}
                  variant="outlined"
                  inputProps={{ 'aria-autocomplete': 'none' }}
                />
                <Tooltip title={inputDisabled ? 'Waiting for AI…' : 'Send (Enter)'}>
                  <span>
                    <IconButton
                      type="submit"
                      color="primary"
                      disabled={inputDisabled || !inputValue.trim() || isCompleted}
                      aria-label="Send message"
                      size="large"
                    >
                      {isAiTyping ? <CircularProgress size={22} /> : <SendIcon />}
                    </IconButton>
                  </span>
                </Tooltip>
              </Box>
            </>
          )}
        </Paper>
      </Container>
    </Box>
  );
}
