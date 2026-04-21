/**
 * useAIIntakeSession — session management hook for AI conversational intake (US_027, SCR-008).
 *
 * Responsibilities:
 *   - Start a new session or resume an existing one (EC-2: timeout recovery).
 *   - Send patient responses and receive next-question payloads from the backend.
 *   - Drive progress tracking (fields collected vs total required).
 *   - Expose summary-review state and final submission.
 *   - Surface AI-service-unavailable state for the UXR-605 fallback banner.
 *
 * API contract (backed by task_003_be_ai_intake_session_api):
 *   POST /api/intake/sessions           → start / resume session
 *   POST /api/intake/sessions/{id}/messages → send message, receive next prompt
 *   GET  /api/intake/sessions/{id}/summary  → summary of collected fields
 *   POST /api/intake/sessions/{id}/complete → submit collected data
 *
 * State machine: idle → starting → active → summary → completed | error
 */

import { useState, useCallback, useRef, useEffect } from 'react';
import { apiPost, apiGet, ApiError } from '@/lib/apiClient';
import {
  useIntakeAutosave,
  type AutosaveStatus,
  AUTOSAVE_FAILED_MESSAGE,
} from '@/hooks/useIntakeAutosave';

export { AUTOSAVE_FAILED_MESSAGE };
export type { AutosaveStatus };

// ─── Types ────────────────────────────────────────────────────────────────────

export type IntakeSessionState =
  | 'idle'
  | 'starting'
  | 'active'
  | 'ai_typing'
  | 'summary'
  | 'completing'
  | 'completed'
  | 'error'
  | 'unavailable';

export type MessageRole = 'ai' | 'user' | 'system';

export interface IntakeMessage {
  id: string;
  role: MessageRole;
  content: string;
  /** ISO timestamp for display */
  timestamp: string;
  /** When role=ai: hints shown to help with ambiguous medical terminology (EC-1) */
  clarificationExamples?: string[];
  /** When role=ai: which field this question collects */
  fieldKey?: string;
}

export interface IntakeSessionSummary {
  sessionId: string;
  fields: IntakeFieldSummary[];
  collectedCount: number;
  totalRequired: number;
}

export interface IntakeFieldSummary {
  key: string;
  label: string;
  value: string;
  isEditable: boolean;
  /**
   * Value from the overridden source (US_029, EC-1).
   * Populated by the backend when a later manual edit or AI update overrides an earlier value.
   * Drives IntakeConflictNotice rendering in the summary review table (AC-4).
   */
  alternateValue?: string;
  /**
   * Which source owns the value that was overridden: 'ai' or 'manual' (US_029, EC-1, AC-4).
   */
  alternateSource?: 'ai' | 'manual';
}

interface StartSessionResponse {
  sessionId: string;
  /** True when resuming a timed-out or abandoned session */
  isResumed: boolean;
  /** Initial/resumed message history */
  messages: IntakeMessage[];
  collectedCount: number;
  totalRequired: number;
  /** Last auto-save timestamp (ISO) — for UXR-004 autosave indicator */
  lastSavedAt: string | null;
}

interface SendMessageResponse {
  /** AI reply message */
  reply: IntakeMessage;
  collectedCount: number;
  totalRequired: number;
  /** True when all mandatory fields have been collected */
  summaryReady: boolean;
  lastSavedAt: string;
}

interface AutosaveDraftPayload {
  sessionId: string | null;
  messages: IntakeMessage[];
  collectedCount: number;
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function useAIIntakeSession() {
  const [sessionState, setSessionState] = useState<IntakeSessionState>('idle');
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [messages, setMessages] = useState<IntakeMessage[]>([]);
  const [collectedCount, setCollectedCount] = useState(0);
  const [totalRequired, setTotalRequired] = useState(8);
  const [summary, setSummary] = useState<IntakeSessionSummary | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  // Track sessionId in ref so autosave thunk always sees the current value
  const sessionIdRef = useRef<string | null>(null);

  // Shared autosave hook (US_030, AC-1, AC-3, EC-1, EC-2)
  const { autosaveStatus, lastSavedAt, notifyChange, flush } = useIntakeAutosave({
    cacheKey: 'intake-ai-draft',
    enabled:  sessionState === 'active' || sessionState === 'summary',
    saveThunk: async (snapshot) => {
      const draft = snapshot as AutosaveDraftPayload;
      const sid   = sessionIdRef.current ?? draft.sessionId;
      if (!sid) throw new Error('No session ID for autosave');
      // Use the existing session autosave endpoint (US_027 task_003)
      const r = await apiPost<{ lastSavedAt: string }>(
        `/api/intake/sessions/${sid}/autosave`,
        { collectedCount: draft.collectedCount },
      );
      return r.lastSavedAt;
    },
  });

  // ── Start or resume session ────────────────────────────────────────────────

  const startSession = useCallback(async () => {
    setSessionState('starting');
    setErrorMessage(null);

    try {
      const response = await apiPost<StartSessionResponse>('/api/intake/sessions', {});

      setSessionId(response.sessionId);
      sessionIdRef.current = response.sessionId;
      setMessages(response.messages);
      setCollectedCount(response.collectedCount);
      setTotalRequired(response.totalRequired);
      setSessionState('active');

      // Seed lastSavedAt from the resume response (AC-2 restore freshness display)
      if (response.lastSavedAt) {
        notifyChange({
          sessionId:      response.sessionId,
          messages:       response.messages,
          collectedCount: response.collectedCount,
        });
      }
    } catch (err) {
      const apiErr = err as ApiError;
      if (apiErr?.status === 503 || apiErr?.status === 502) {
        setSessionState('unavailable');
      } else {
        setSessionState('error');
        setErrorMessage('Unable to start intake session. Please try again or switch to manual form.');
      }
    }
  }, []);

  // ── Send patient message ───────────────────────────────────────────────────

  const sendMessage = useCallback(
    async (content: string) => {
      if (!sessionId || sessionState !== 'active') return;

      const trimmed = content.trim();
      if (!trimmed) return;

      // Optimistically append the patient message
      const userMsg: IntakeMessage = {
        id: crypto.randomUUID(),
        role: 'user',
        content: trimmed,
        timestamp: new Date().toISOString(),
      };

      setMessages((prev) => [...prev, userMsg]);
      setSessionState('ai_typing');

      try {
        const response = await apiPost<SendMessageResponse>(
          `/api/intake/sessions/${sessionId}/messages`,
          { content: trimmed },
        );

        setMessages((prev) => [...prev, response.reply]);
        setCollectedCount(response.collectedCount);

        // Notify autosave hook of the new snapshot (EC-2: captured at next 30-s boundary)
        notifyChange({
          sessionId:      sessionId,
          messages:       [...messages, response.reply],
          collectedCount: response.collectedCount,
        });

        if (response.summaryReady) {
          // Transition to summary review (AC-4)
          await loadSummary(sessionId);
        } else {
          setSessionState('active');
        }
      } catch (err) {
        const apiErr = err as ApiError;
        if (apiErr?.status === 503) {
          setSessionState('unavailable');
        } else {
          // Revert to active — message failed, keep conversation going
          setSessionState('active');
          const errMsg: IntakeMessage = {
            id: crypto.randomUUID(),
            role: 'system',
            content: 'Unable to process your response. Please try again.',
            timestamp: new Date().toISOString(),
          };
          setMessages((prev) => [...prev, errMsg]);
        }
      }
    },
    [sessionId, sessionState],
  );

  // ── Load summary for review (AC-4) ────────────────────────────────────────

  const loadSummary = useCallback(async (sid: string) => {
    try {
      const data = await apiGet<IntakeSessionSummary>(`/api/intake/sessions/${sid}/summary`);
      setSummary(data);
      setSessionState('summary');
    } catch {
      setSessionState('active'); // stay in conversation if summary fails
    }
  }, []);

  const requestSummary = useCallback(async () => {
    if (!sessionId) return;
    await loadSummary(sessionId);
  }, [sessionId, loadSummary]);

  // ── Complete intake ────────────────────────────────────────────────────────

  const completeIntake = useCallback(async () => {
    if (!sessionId) return;
    setSessionState('completing');

    try {
      await apiPost(`/api/intake/sessions/${sessionId}/complete`, {});
      setSessionState('completed');
    } catch {
      setSessionState('summary'); // allow retry
      setErrorMessage('Unable to submit. Please try again.');
    }
  }, [sessionId]);

  // ── Switch to manual form (FL-004) ───────────────────────────────────────

  /**
   * Calls the backend switch-manual endpoint to mark the session as transferred
   * and returns the pre-filled fields so ManualIntakePage can pre-populate the form.
   * Returns an empty dict if the session is not yet started (no sessionId).
   */
  /**
   * Flush any pending autosave before switching modes so no data is lost (AC-3).
   */
  const flushAutosave = useCallback(() => flush(), [flush]);

  const switchToManual = useCallback(async (): Promise<Record<string, string>> => {
    if (!sessionId) return {};
    // Persist current state before leaving (AC-3)
    await flush();

    try {
      const response = await apiPost<{ prefilledFields: Record<string, string> }>(
        `/api/intake/sessions/${sessionId}/switch-manual`,
        {},
      );
      return response.prefilledFields ?? {};
    } catch {
      // If the call fails, still navigate — just with no prefilled data
      return {};
    }
  }, [sessionId]);

  // ── Go back to editing from summary (AC-4) ────────────────────────────────

  const backToChat = useCallback(() => {
    setSummary(null);
    setSessionState('active');
  }, []);

  // ── Reset on error ─────────────────────────────────────────────────────────

  const reset = useCallback(() => {
    setSessionState('idle');
    setSessionId(null);
    sessionIdRef.current = null;
    setMessages([]);
    setCollectedCount(0);
    setSummary(null);
    setErrorMessage(null);
  }, []);

  return {
    sessionState,
    sessionId,
    messages,
    collectedCount,
    totalRequired,
    lastSavedAt,
    autosaveStatus,
    summary,
    errorMessage,
    startSession,
    sendMessage,
    requestSummary,
    completeIntake,
    switchToManual,
    flushAutosave,
    backToChat,
    reset,
  };
}
