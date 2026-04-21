/**
 * useAppointmentSlots — React Query data-fetching hook for appointment slot availability (US_017).
 *
 * Fetches:
 *   GET /api/appointments/slots?startDate=&endDate=&providerId=
 *     → SlotAvailabilityResponse (list of slots + date availability summary)
 *
 * Design decisions:
 *   - staleTime: 5 minutes aligns with backend Redis cache TTL.
 *   - Query key includes all filter params so changing provider or date range
 *     triggers a new fetch automatically.
 *   - Date availability summary (which dates have ≥1 slot) is derived from the
 *     response so the calendar can render dots without a separate request.
 *
 * AC-1: Data must load within 2 seconds; React Query handles loading state.
 * EC-2: 90-day range restriction enforced in the query params by callers.
 */

import { useQuery } from '@tanstack/react-query';
import { apiGet } from '@/lib/apiClient';
import type { ApiError } from '@/lib/apiClient';

// ─── API response types ───────────────────────────────────────────────────────

/** A single bookable time slot returned by the API. */
export interface AppointmentSlot {
  /** Unique slot identifier. */
  slotId: string;
  /** ISO-8601 date string (YYYY-MM-DD). */
  date: string;
  /** Start time as "HH:MM" (24-hour). */
  startTime: string;
  /** End time as "HH:MM" (24-hour). */
  endTime: string;
  /** Provider full name. */
  providerName: string;
  /** Provider unique identifier. */
  providerId: string;
  /** Appointment type label (e.g. "General Checkup"). */
  appointmentType: string;
  /** Whether the slot is still bookable. */
  available: boolean;
}

/** Provider option for the filter dropdown. */
export interface ProviderOption {
  providerId: string;
  providerName: string;
}

/** Full API response shape. */
export interface SlotAvailabilityResponse {
  slots: AppointmentSlot[];
  /** Distinct providers present in the result set (for filter dropdown). */
  providers: ProviderOption[];
}

// ─── Query params ─────────────────────────────────────────────────────────────

export interface SlotQueryParams {
  /** ISO-8601 start date (inclusive). */
  startDate: string;
  /** ISO-8601 end date (inclusive). Max 90 days from today (FR-013). */
  endDate: string;
  /** Null/empty = all providers. */
  providerId?: string;
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

const STALE_TIME_MS = 5 * 60 * 1000; // 5 minutes — aligns with Redis TTL

export function useAppointmentSlots(params: SlotQueryParams) {
  const { startDate, endDate, providerId } = params;

  const queryString = new URLSearchParams({
    startDate,
    endDate,
    ...(providerId ? { providerId } : {}),
  }).toString();

  return useQuery<SlotAvailabilityResponse, ApiError>({
    queryKey: ['appointment-slots', startDate, endDate, providerId ?? ''],
    queryFn: () => apiGet<SlotAvailabilityResponse>(`/api/appointments/slots?${queryString}`),
    staleTime: STALE_TIME_MS,
    retry: 1,
    // Keep previous data visible while new params load (prevents flicker on filter change).
    keepPreviousData: true,
  });
}

// ─── Derived selector ─────────────────────────────────────────────────────────

/**
 * Returns a Set of date strings ("YYYY-MM-DD") that have at least one available slot.
 * Used by SlotCalendar to render availability dots.
 */
export function getAvailableDates(slots: AppointmentSlot[]): Set<string> {
  const result = new Set<string>();
  for (const s of slots) {
    if (s.available) result.add(s.date);
  }
  return result;
}

/**
 * Filters the full slot list to only those matching the selected date.
 */
export function getSlotsForDate(slots: AppointmentSlot[], date: string): AppointmentSlot[] {
  return slots.filter((s) => s.date === date);
}
