/**
 * useIntakeModeSwitch — shared hook for AI ↔ manual intake mode transitions (US_029, FR-028).
 *
 * Responsibilities:
 *   - Execute manual → AI switch by posting carried-over field values to the backend,
 *     retrieving the AI session context to resume from the next uncollected field (AC-2).
 *   - Detect AI service unavailability (503 / 502) and set `aiAvailable = false` so
 *     the calling page can disable the switch-to-AI action and show the required fallback
 *     alert text (EC-2, UXR-605).
 *   - Prevent duplicate switch actions during in-flight transitions via `switching` guard.
 *
 * AI → manual switching is handled by `useAIIntakeSession.switchToManual()` (US_027).
 * This hook owns the manual → AI direction only, plus the shared `aiAvailable` flag.
 *
 * API contract (task_002_be_intake_mode_switching_api):
 *   POST /api/intake/manual/switch-ai
 *     Body:   { fields: ManualIntakeFormValues }
 *     200:    { sessionId: string; nextField: string | null }
 *     503:    AI service unavailable — sets aiAvailable = false
 *
 * Graceful degradation: on non-503 API failure the hook still returns null but does NOT
 * set aiAvailable=false, allowing the caller to navigate to AI intake anyway (the session
 * resume in useAIIntakeSession will handle recovery).
 */

import { useState, useCallback } from 'react';
import { apiPost, ApiError } from '@/lib/apiClient';
import type { ManualIntakeFormValues } from './useManualIntakeForm';

// ─── Types ────────────────────────────────────────────────────────────────────

export interface SwitchToAIResult {
  /** Existing or new AI intake session ID to resume. */
  sessionId: string;
  /**
   * Key of the next field the AI should ask about so the conversation
   * picks up from the correct uncollected field (AC-2).
   */
  nextField: string | null;
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function useIntakeModeSwitch() {
  /** True while a switch API call is in flight — disables repeat triggers (EC-2). */
  const [switching, setSwitching] = useState(false);
  /**
   * False when the AI service returned 503 / 502.
   * Drives disabled state and UXR-605 fallback messaging (EC-2).
   */
  const [aiAvailable, setAiAvailable] = useState(true);

  /**
   * Switches from manual form to AI intake.
   *
   * Sends the current form field values to the backend so the AI session can
   * pre-populate already-collected fields and resume from the next uncollected
   * field (AC-2, AC-3). Returns null on API failure; sets `aiAvailable = false`
   * on 503 / 502.
   */
  const switchToAI = useCallback(
    async (fields: ManualIntakeFormValues): Promise<SwitchToAIResult | null> => {
      if (switching) return null;
      setSwitching(true);

      try {
        const response = await apiPost<SwitchToAIResult>(
          '/api/intake/manual/switch-ai',
          { fields },
        );
        return response;
      } catch (err) {
        const apiErr = err as ApiError;
        if (apiErr?.status === 503 || apiErr?.status === 502) {
          // AI service unavailable — disable switch-to-AI and surface EC-2 fallback (UXR-605)
          setAiAvailable(false);
        }
        // Non-503: graceful degradation — caller can navigate with startSession fallback
        return null;
      } finally {
        setSwitching(false);
      }
    },
    [switching],
  );

  /**
   * Explicitly marks AI as unavailable — called by AIIntakePage when startSession
   * receives a 503 so ManualIntakePage's switch-to-AI button stays disabled on
   * back-navigation within the same session (EC-2).
   */
  const markAiUnavailable = useCallback(() => {
    setAiAvailable(false);
  }, []);

  return {
    /** True while a switch API call is in flight. */
    switching,
    /** False when the AI service is unavailable — disables switch-to-AI. */
    aiAvailable,
    switchToAI,
    markAiUnavailable,
  };
}
