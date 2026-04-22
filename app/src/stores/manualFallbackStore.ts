/**
 * manualFallbackStore — Zustand store tracking manual fallback mode and AI health
 * state on the Patient Profile 360 screen (US_046 AC-3, AC-4, UXR-605).
 *
 * Fields:
 *   isAiUnavailable         — true when backend reports AI service is down (AC-4)
 *   isManualFallbackMode    — true when staff has explicitly switched to manual entry
 *   patientIdInFallback     — the patient currently in fallback mode (null when off)
 *
 * Actions:
 *   setAiUnavailable        — called when AI health poll returns unhealthy
 *   enterManualFallback     — explicitly activate manual mode for a patient
 *   exitManualFallback      — return to normal profile view
 */

import { create } from 'zustand';

// ─── State shape ──────────────────────────────────────────────────────────────

interface ManualFallbackState {
  isAiUnavailable: boolean;
  isManualFallbackMode: boolean;
  patientIdInFallback: string | null;

  setAiUnavailable: (unavailable: boolean) => void;
  enterManualFallback: (patientId: string) => void;
  exitManualFallback: () => void;
}

// ─── Store ────────────────────────────────────────────────────────────────────

export const useManualFallbackStore = create<ManualFallbackState>()((set) => ({
  isAiUnavailable: false,
  isManualFallbackMode: false,
  patientIdInFallback: null,

  setAiUnavailable(unavailable) {
    set({ isAiUnavailable: unavailable });
  },

  enterManualFallback(patientId) {
    set({
      isManualFallbackMode: true,
      patientIdInFallback: patientId,
    });
  },

  exitManualFallback() {
    set({
      isManualFallbackMode: false,
      patientIdInFallback: null,
    });
  },
}));
